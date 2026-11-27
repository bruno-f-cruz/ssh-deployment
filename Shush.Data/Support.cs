using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Shush.Data;

/// <summary>Encrypts secret variable values at rest. The web app backs it with ASP.NET Data Protection.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}

/// <summary>A request the operator can fix (a duplicate name, a missing recipe...). Its message is shown as is.</summary>
public sealed class ShushDataException(string message) : Exception(message);

public static class Clock
{
    /// <summary>Now, in UTC. Overridable in tests.</summary>
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
}

public static class Json
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static Dictionary<string, string> ReadParams(string? json) =>
        Read(json, new Dictionary<string, string>());
}

public static class Hashing
{
    /// <summary>Content hash of a YAML document, ignoring line-ending differences.</summary>
    public static string Of(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").TrimEnd()))).ToLowerInvariant();
}

public enum DiffKind { Unchanged, Added, Removed }

public sealed record DiffLine(DiffKind Kind, int? OldNumber, int? NewNumber, string Text);

public static class TextDiff
{
    /// <summary>A line diff of two texts, in the order a reader expects (removed lines before the lines that replace them).</summary>
    public static IReadOnlyList<DiffLine> Lines(string oldText, string newText)
    {
        var model = InlineDiffBuilder.Diff(oldText.Replace("\r\n", "\n"), newText.Replace("\r\n", "\n"), ignoreWhiteSpace: false);
        var lines = new List<DiffLine>();
        int oldNumber = 0, newNumber = 0;

        foreach (var line in model.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Deleted:
                    lines.Add(new DiffLine(DiffKind.Removed, ++oldNumber, null, line.Text));
                    break;
                case ChangeType.Inserted:
                    lines.Add(new DiffLine(DiffKind.Added, null, ++newNumber, line.Text));
                    break;
                default:
                    lines.Add(new DiffLine(DiffKind.Unchanged, ++oldNumber, ++newNumber, line.Text));
                    break;
            }
        }

        return lines;
    }

    /// <summary>Only the changed lines and <paramref name="context"/> lines around them; null entries mark skipped stretches.</summary>
    public static IReadOnlyList<DiffLine?> Hunks(IReadOnlyList<DiffLine> lines, int context = 3)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffKind.Unchanged)
                continue;
            for (var j = Math.Max(0, i - context); j <= Math.Min(lines.Count - 1, i + context); j++)
                keep[j] = true;
        }

        var result = new List<DiffLine?>();
        var skipped = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                result.Add(lines[i]);
                skipped = false;
            }
            else if (!skipped)
            {
                result.Add(null);
                skipped = true;
            }
        }

        return result;
    }

    /// <summary>The parameter values that differ between two runs, as (name, old, new).</summary>
    public static IReadOnlyList<(string Name, string? Old, string? New)> Params(
        IReadOnlyDictionary<string, string> oldValues, IReadOnlyDictionary<string, string> newValues) =>
        oldValues.Keys.Union(newValues.Keys, StringComparer.Ordinal)
            .Select(k => (k, oldValues.GetValueOrDefault(k), newValues.GetValueOrDefault(k)))
            .Where(x => !string.Equals(x.Item2 ?? "", x.Item3 ?? "", StringComparison.Ordinal))
            .OrderBy(x => x.k, StringComparer.Ordinal)
            .ToList();
}
