using System.Net.Http.Headers;
using System.Text.Json;

namespace Shush.Recipe.Options;

/// <summary>A clabe release that has builds: a GitHub release of AllenNeuralDynamics/clabe with its wheel attached.</summary>
public sealed record ClabeRelease(string Tag, string Version, bool Prerelease, DateTimeOffset? PublishedAt);

/// <summary>
/// The clabe releases with builds, from GitHub (cached for <see cref="CacheFor"/>).
///
/// clabe's CI publishes a release to PyPI and then attaches the same wheel to the GitHub release, in one job: a
/// release with an <c>aind_clabe-*.whl</c> asset is one PyPI has, which is where the daemon installs it from.
/// </summary>
public sealed class ClabeReleases(HttpClient http)
{
    public const string Repository = "AllenNeuralDynamics/clabe";

    public static ClabeReleases Shared { get; } = new(CreateClient());

    public TimeSpan CacheFor { get; init; } = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<ClabeRelease>? _cached;
    private DateTimeOffset _cachedAt;
    private readonly Dictionary<string, bool> _installerChecks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The releases with builds, newest first, for suggestions: the cached list while it is recent, else a new one;
    /// the last known list (or none) when GitHub cannot be read. Never throws (but for the caller's cancellation).
    /// </summary>
    public async Task<IReadOnlyList<ClabeRelease>> GetAsync(CancellationToken cancellationToken = default) =>
        (await FetchAsync(refresh: false, cancellationToken)).Releases ?? [];

    /// <summary>
    /// The releases just read from GitHub, ignoring the cache, or <c>null</c> when GitHub cannot be read (unreachable,
    /// rate-limited, an unexpected answer). Only such a list can tell that a version is not a release.
    /// </summary>
    public async Task<IReadOnlyList<ClabeRelease>?> GetFreshAsync(CancellationToken cancellationToken = default)
    {
        var (releases, fresh) = await FetchAsync(refresh: true, cancellationToken);
        return fresh ? releases : null;
    }

    private async Task<(IReadOnlyList<ClabeRelease>? Releases, bool Fresh)> FetchAsync(bool refresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
                return (_cached, false);
            try
            {
                var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=100", cancellationToken);
                _cached = Parse(json);
                _cachedAt = DateTimeOffset.UtcNow;
                return (_cached, true);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Whatever went wrong (network, rate limit, a changed API): keep the last list, retry on the next call.
                return (_cached, false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The published (not draft) releases of GitHub's releases JSON that have the clabe wheel attached.</summary>
    public static IReadOnlyList<ClabeRelease> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ClabeRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            var hasWheel = release.TryGetProperty("assets", out var assets) && assets.EnumerateArray().Any(a =>
                a.GetProperty("name").GetString() is { } name
                && name.StartsWith("aind_clabe-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".whl", StringComparison.OrdinalIgnoreCase));
            if (!hasWheel)
                continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            DateTimeOffset? published = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetDateTimeOffset()
                : null;
            var prerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            releases.Add(new ClabeRelease(tag, tag.TrimStart('v', 'V'), prerelease, published));
        }
        return releases.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue).ToList();
    }

    /// <summary>
    /// Whether clabe's installer at <paramref name="tag"/> deploys the daemon with <c>clabe daemon deploy</c> (takes
    /// <c>-StopDaemon</c>); earlier installers take other arguments. <c>null</c> when it cannot be read.
    /// </summary>
    public async Task<bool?> InstallerDeploysAsync(string tag, CancellationToken cancellationToken = default)
    {
        lock (_installerChecks)
        {
            if (_installerChecks.TryGetValue(tag, out var known))
                return known;
        }
        try
        {
            var script = await http.GetStringAsync(InstallerUrl(tag), cancellationToken);
            var deploys = script.Contains("StopDaemon", StringComparison.Ordinal);
            lock (_installerChecks)
                _installerChecks[tag] = deploys;
            return deploys;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            lock (_installerChecks)
                _installerChecks[tag] = false;
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // unknown: not offered, not refused
        }
    }

    /// <summary>clabe's installer (scripts/install.ps1) at a git ref: a release tag, or a branch such as main.</summary>
    public static string InstallerUrl(string reference) =>
        $"https://raw.githubusercontent.com/{Repository}/{Uri.EscapeDataString(reference)}/scripts/install.ps1";

    internal static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Shush", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // Unauthenticated, GitHub allows 60 requests an hour per address; a token raises that.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary><c>clabe.versions</c>: what the daemon can run — <c>lock</c>, <c>latest</c>, then each release with builds.</summary>
public sealed class ClabeVersionOptions(ClabeReleases releases) : IOptionSource
{
    public string Name => "clabe.versions";

    public async Task<IReadOnlyList<InputOption>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var found = await releases.GetAsync(cancellationToken);
        return
        [
            new InputOption("lock", "lock — the release the repository's uv.lock pins"),
            new InputOption("latest", "latest — the newest release (not a pre-release)"),
            .. found.Select(r => new InputOption(r.Version, Describe(r))),
        ];
    }

    internal static string Describe(ClabeRelease release)
    {
        var parts = new List<string> { release.Tag };
        if (release.Prerelease)
            parts.Add("pre-release");
        if (release.PublishedAt is { } published)
            parts.Add(published.ToString("yyyy-MM-dd"));
        return string.Join(" · ", parts);
    }
}

/// <summary><c>clabe.installers</c>: the installer versions that deploy the daemon — <c>main</c>, then the recent release tags whose installer does.</summary>
public sealed class ClabeInstallerOptions(ClabeReleases releases) : IOptionSource
{
    /// <summary>How many recent releases are checked (one small download each, once).</summary>
    public int Checked { get; init; } = 10;

    public string Name => "clabe.installers";

    public async Task<IReadOnlyList<InputOption>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var recent = (await releases.GetAsync(cancellationToken)).Take(Checked).ToList();
        var checks = await Task.WhenAll(recent.Select(r => releases.InstallerDeploysAsync(r.Tag, cancellationToken)));
        return
        [
            new InputOption("main", "main — the latest installer"),
            .. recent.Where((_, i) => checks[i] == true).Select(r => new InputOption(r.Tag, ClabeVersionOptions.Describe(r))),
        ];
    }
}
