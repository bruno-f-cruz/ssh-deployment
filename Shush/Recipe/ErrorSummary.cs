using System.Text.RegularExpressions;

namespace Shush.Recipe;

/// <summary>
/// Turns a failed command's output (often pages of installer progress and PowerShell's error decoration) into the few
/// lines that say what went wrong, plus a plain-English hint for Windows errors we recognise. The full output stays in
/// the run log.
/// </summary>
/// <param name="Hint">A plain-English explanation, when the error is one we recognise.</param>
/// <param name="Lines">The lines of the output that say what failed.</param>
public sealed record ErrorExplanation(string? Hint, IReadOnlyList<string> Lines, string? ExitCode);

public static partial class ErrorSummary
{
    private const int MaxLines = 3;

    /// <summary>Plain-English hints for error codes that show up on the rigs.</summary>
    private static readonly (Regex Pattern, string Hint)[] Hints =
    [
        (new Regex(@"os error 1260\b|error 1260\b|blocked by group policy", RegexOptions.IgnoreCase),
            "Windows blocked a program from starting (error 1260: blocked by group policy, e.g. AppLocker). Check which folders the rig allows programs to run from."),
        (new Regex(@"os error 5\b|Access is denied", RegexOptions.IgnoreCase),
            "Windows denied access (error 5): the SSH account lacks permission, or a file is in use."),
        (new Regex(@"os error 32\b|being used by another process", RegexOptions.IgnoreCase),
            "A file is in use by another process (error 32), e.g. a running experiment or daemon."),
    ];

    [GeneratedRegex(@"^SSH command failed with exit code (-?\d+):\s*", RegexOptions.Singleline)]
    private static partial Regex SshPrefix();

    [GeneratedRegex(@"\b(error|failed|failure|denied|not found|cannot|can't|couldn't|exception|refused|blocked|fatal|timed out)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Interesting();

    /// <summary>True for lines that are decoration or progress, not information.</summary>
    private static bool IsNoise(string line) =>
        line.StartsWith("At line:", StringComparison.Ordinal)
        || line.StartsWith("+ ", StringComparison.Ordinal) // PowerShell's caret line, and uv's "+ package==1.2" list
        || line.StartsWith("+~", StringComparison.Ordinal)
        || line.TrimStart('~', ' ').Length == 0
        || line.StartsWith("CategoryInfo", StringComparison.Ordinal)
        || line.StartsWith("FullyQualifiedErrorId", StringComparison.Ordinal)
        || line.StartsWith("Downloading", StringComparison.Ordinal)
        || line.StartsWith("Resolved ", StringComparison.Ordinal)
        || line.StartsWith("Prepared ", StringComparison.Ordinal)
        || line.StartsWith("Installed ", StringComparison.Ordinal)
        || line.StartsWith("Uninstalled ", StringComparison.Ordinal)
        || line.StartsWith("Audited ", StringComparison.Ordinal);

    /// <summary>The lines worth reading, at most a few, plus a hint when one applies. Short messages come back as they are.</summary>
    public static string Summarize(string? error) =>
        Explain(error) is { } e ? Compose(e.ExitCode, e.Lines.ToList(), e.Hint) : string.Empty;

    /// <summary>The same as <see cref="Summarize"/>, in parts (for display).</summary>
    public static ErrorExplanation? Explain(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        var text = error.Trim();
        string? exitCode = null;
        if (SshPrefix().Match(text) is { Success: true } prefix)
        {
            exitCode = prefix.Groups[1].Value;
            text = text[prefix.Length..];
        }

        var lines = text.Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (lines.Count <= MaxLines && text.Length <= 300)
            return new ErrorExplanation(Hint(text), lines, exitCode);

        var useful = lines.Where(l => !IsNoise(l)).ToList();
        var picked = useful.Where(l => Interesting().IsMatch(l)).Distinct().TakeLast(MaxLines).ToList();
        if (picked.Count == 0)
            picked = useful.TakeLast(MaxLines).ToList();
        if (picked.Count == 0)
            picked = lines.TakeLast(MaxLines).ToList();

        return new ErrorExplanation(Hint(text), picked.Select(l => l.Length > 240 ? l[..237] + "…" : l).ToList(), exitCode);
    }

    /// <summary>True when <see cref="Summarize"/> drops something (so the full text is worth offering).</summary>
    public static bool IsLong(string? error) =>
        !string.IsNullOrWhiteSpace(error) && (error.Length > 300 || error.Count(c => c == '\n') >= MaxLines);

    private static string? Hint(string text) => Hints.FirstOrDefault(h => h.Pattern.IsMatch(text)).Hint;

    private static string Compose(string? exitCode, List<string> lines, string? hint)
    {
        var parts = new List<string>();
        if (hint is not null)
            parts.Add(hint);
        parts.AddRange(lines);
        if (exitCode is not null)
            parts.Add($"(exit code {exitCode})");
        return string.Join("\n", parts);
    }
}
