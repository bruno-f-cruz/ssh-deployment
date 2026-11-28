using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shush.Recipe.Options;

/// <summary>
/// A clabe release that has builds: a GitHub release of AllenNeuralDynamics/clabe with its wheel attached. Or, with a
/// <see cref="Commit"/>, a build of a commit of clabe's main branch: <see cref="Version"/> is then its short commit, the
/// value the daemon is deployed with (the installer's <c>-Commit</c>), and <see cref="Tag"/> the release it follows.
/// </summary>
public sealed record ClabeRelease(string Tag, string Version, bool Prerelease, DateTimeOffset? PublishedAt, string? Commit = null)
{
    public bool IsBuild => Commit is not null;
}

/// <summary>
/// The clabe releases with builds, from GitHub (cached for <see cref="CacheFor"/>).
///
/// clabe's CI publishes a release to PyPI and then attaches the same wheel to the GitHub release, in one job: a
/// release with an <c>aind_clabe-*.whl</c> asset is one PyPI has, which is where the daemon installs it from. It also
/// builds every commit pushed to main into one pre-release, <see cref="BuildsTag"/>: each wheel there
/// (<c>aind_clabe-0.18.0+g&lt;commit&gt;-py3-none-any.whl</c>) is a build, deployed by its commit.
/// </summary>
public sealed class ClabeReleases(HttpClient http)
{
    public const string Repository = "AllenNeuralDynamics/clabe";

    /// <summary>The pre-release that holds a build of every commit pushed to clabe's main branch.</summary>
    public const string BuildsTag = "builds";

    private static readonly Regex BuildWheel = new(
        @"^aind_clabe-(?<version>[^-+]+)\+g(?<commit>[0-9a-f]{7,40})-.*\.whl$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CommitPattern = new("^[0-9a-f]{7,40}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="value"/> names a commit (7 to 40 hex characters), i.e. a build of main.</summary>
    public static bool IsCommit(string? value) => value is not null && CommitPattern.IsMatch(value.Trim());

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

    /// <summary>
    /// The published (not draft) releases of GitHub's releases JSON that have the clabe wheel attached, newest first;
    /// then the builds of main (the wheels of <see cref="BuildsTag"/>), newest first.
    /// </summary>
    public static IReadOnlyList<ClabeRelease> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ClabeRelease>();
        var builds = new List<ClabeRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;
            if (release.GetProperty("tag_name").GetString() == BuildsTag)
            {
                builds.AddRange(ParseBuilds(release));
                continue;
            }
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
        return
        [
            .. releases.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue),
            .. builds.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue),
        ];
    }

    /// <summary>The builds in the builds release: one per wheel whose version carries its commit.</summary>
    private static IEnumerable<ClabeRelease> ParseBuilds(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
            yield break;
        foreach (var asset in assets.EnumerateArray())
        {
            var match = BuildWheel.Match(asset.GetProperty("name").GetString() ?? "");
            if (!match.Success)
                continue;
            var commit = match.Groups["commit"].Value.ToLowerInvariant();
            DateTimeOffset? created = asset.TryGetProperty("created_at", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetDateTimeOffset()
                : null;
            yield return new ClabeRelease(match.Groups["version"].Value, commit[..7], true, created, commit);
        }
    }

    /// <summary>The build of <paramref name="commit"/> in <paramref name="releases"/>, matched by prefix, if exactly one.</summary>
    public static ClabeRelease? FindBuild(IEnumerable<ClabeRelease> releases, string commit)
    {
        var prefix = commit.Trim().ToLowerInvariant();
        var found = releases.Where(r => r.Commit?.StartsWith(prefix, StringComparison.Ordinal) == true).Take(2).ToList();
        return found.Count == 1 ? found[0] : null;
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

/// <summary>
/// <c>clabe.versions</c>: what the daemon can run — <c>lock</c>, <c>latest</c>, each release with builds, then the most
/// recent builds of main (by commit).
/// </summary>
public sealed class ClabeVersionOptions(ClabeReleases releases) : IOptionSource
{
    /// <summary>How many builds of main are offered (the newest).</summary>
    public int Builds { get; init; } = 15;

    public string Name => "clabe.versions";

    public async Task<IReadOnlyList<InputOption>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var found = await releases.GetAsync(cancellationToken);
        return
        [
            new InputOption("lock", "lock — the clabe the repository's uv.lock pins (a release or a build)"),
            new InputOption("latest", "latest — the newest release (not a pre-release)"),
            .. found.Where(r => !r.IsBuild).Select(r => new InputOption(r.Version, Describe(r))),
            .. found.Where(r => r.IsBuild).Take(Builds).Select(r => new InputOption(r.Version, Describe(r))),
        ];
    }

    internal static string Describe(ClabeRelease release)
    {
        if (release.IsBuild)
        {
            var build = $"{release.Version} — build of main after {release.Tag}";
            return release.PublishedAt is { } built ? $"{build} · {built:yyyy-MM-dd}" : build;
        }
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
        var recent = (await releases.GetAsync(cancellationToken)).Where(r => !r.IsBuild).Take(Checked).ToList();
        var checks = await Task.WhenAll(recent.Select(r => releases.InstallerDeploysAsync(r.Tag, cancellationToken)));
        return
        [
            new InputOption("main", "main — the latest installer"),
            .. recent.Where((_, i) => checks[i] == true).Select(r => new InputOption(r.Tag, ClabeVersionOptions.Describe(r))),
        ];
    }
}
