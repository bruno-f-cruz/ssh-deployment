using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shush.Recipe.Options;

/// <summary>
/// <c>git.refs(https://github.com/owner/repo)</c>: a GitHub repository's tags (newest first, as GitHub lists them),
/// then its branches. Cached for ten minutes per repository. A repository that isn't on GitHub, or GitHub not
/// answering, gives no suggestions; any ref can still be typed.
/// </summary>
public sealed partial class GitRefOptions(string name, string owner, string repository, HttpClient http) : IOptionSource
{
    public const string Prefix = "git.refs";

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
                var tags = await NamesAsync($"https://api.github.com/repos/{owner}/{repository}/tags?per_page=100", cancellationToken);
                var branches = await NamesAsync($"https://api.github.com/repos/{owner}/{repository}/branches?per_page=100", cancellationToken);
                _cached = tags.Select(t => new InputOption(t, $"{t} (tag)"))
                    .Concat(branches.Select(b => new InputOption(b, $"{b} (branch)")))
                    .ToList();
                _cachedAt = DateTimeOffset.UtcNow;
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

    private async Task<IReadOnlyList<string>> NamesAsync(string url, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return json.RootElement.EnumerateArray()
            .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
    }

    [GeneratedRegex(@"^git\.refs\(\s*https://github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+?)(\.git)?/?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SourcePattern();
}
