using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shush.Recipe.Options;

/// <summary>
/// <c>git.refs(https://github.com/owner/repo)</c>: what a GitHub repository can be deployed at. Its releases first
/// (newest first, with their title and date; drafts left out), then its branches (the default one first), then any tags
/// that have no release. Cached for ten minutes per repository. A repository that isn't on GitHub, or GitHub not
/// answering, gives no suggestions; any ref can still be typed.
/// </summary>
public sealed partial class GitRefOptions(string name, string owner, string repository, HttpClient http) : IOptionSource
{
    public const string Prefix = "git.refs";

    private const int MaxReleases = 30;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<InputOption>? _cached;
    private DateTimeOffset _cachedAt;

    public string Name => name;

    /// <summary>Recognises <c>git.refs(url)</c> and returns the source for it, or null.</summary>
    public static GitRefOptions? TryCreate(string name, HttpClient http)
    {
        var match = SourcePattern().Match(name.Trim());
        return match.Success ? new GitRefOptions(name, match.Groups["owner"].Value, match.Groups["repo"].Value, http) : null;
    }

    public async Task<IReadOnlyList<InputOption>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
                return _cached;

            try
            {
                var api = $"https://api.github.com/repos/{owner}/{repository}";
                var releases = await GetJsonAsync($"{api}/releases?per_page={MaxReleases}", cancellationToken);
                var branches = await GetJsonAsync($"{api}/branches?per_page=100", cancellationToken);
                var tags = await GetJsonAsync($"{api}/tags?per_page=100", cancellationToken);
                string? defaultBranch = null;
                try
                {
                    using var repo = await GetJsonAsync(api, cancellationToken);
                    defaultBranch = repo.RootElement.TryGetProperty("default_branch", out var d) ? d.GetString() : null;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Only used to put the default branch first.
                }

                _cached = Build(releases.RootElement, branches.RootElement, tags.RootElement, defaultBranch);
                _cachedAt = DateTimeOffset.UtcNow;
                releases.Dispose();
                branches.Dispose();
                tags.Dispose();
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Unreachable, rate-limited or not a public repository: keep what we had.
            }

            return _cached ?? [];
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The suggestions from GitHub's answers (arrays of releases, branches and tags). Public for tests.</summary>
    public static IReadOnlyList<InputOption> Build(JsonElement releases, JsonElement branches, JsonElement tags, string? defaultBranch)
    {
        var options = new List<InputOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var first = true;
        foreach (var release in releases.EnumerateArray())
        {
            if (Bool(release, "draft") || Str(release, "tag_name") is not { Length: > 0 } tag || !seen.Add(tag))
                continue;

            var parts = new List<string> { tag };
            var title = Str(release, "name")?.Trim();
            if (!string.IsNullOrEmpty(title) && title != tag)
                parts.Add(title.StartsWith(tag + " ", StringComparison.Ordinal) ? title[(tag.Length + 1)..].Trim() : title);
            if (Bool(release, "prerelease"))
                parts.Add("pre-release");
            else if (first)
                parts.Add("latest release");
            if (Str(release, "published_at") is { } published
                && DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                parts.Add(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (!Bool(release, "prerelease"))
                first = false;
            options.Add(new InputOption(tag, string.Join(" · ", parts)));
        }

        var branchNames = branches.EnumerateArray().Select(b => Str(b, "name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)
            .OrderBy(n => n == defaultBranch ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var branch in branchNames)
            if (seen.Add(branch))
                options.Add(new InputOption(branch, branch == defaultBranch ? $"{branch} · default branch" : $"{branch} · branch"));

        foreach (var tag in tags.EnumerateArray().Select(t => Str(t, "name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!))
            if (seen.Add(tag))
                options.Add(new InputOption(tag, $"{tag} · tag"));

        return options;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) =>
        JsonDocument.Parse(await http.GetStringAsync(url, ct));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^git\.refs\(\s*https://github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+?)(\.git)?/?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SourcePattern();
}
