using System.Text.RegularExpressions;

namespace Shush.Design.Services;

/// <summary>Reads a run's log file, or one machine's lines of it (entries are tagged [MachineName]).</summary>
public static partial class RunLogReader
{
    /// <summary>One machine's whole log.</summary>
    public static string ForMachine(string? logFile, string machine) =>
        string.Join(Environment.NewLine, MachineLines(logFile, machine));

    /// <summary>The last <paramref name="maxLines"/> lines of one machine's log, and how many earlier lines were left out.</summary>
    public static (string Text, int Hidden) ForMachineTail(string? logFile, string machine, int maxLines = 2000)
    {
        var lines = MachineLines(logFile, machine);
        var hidden = Math.Max(0, lines.Count - maxLines);
        return (string.Join(Environment.NewLine, lines.Skip(hidden)), hidden);
    }

    private static List<string> MachineLines(string? logFile, string machine)
    {
        var output = new List<string>();
        if (logFile is null || !File.Exists(logFile))
            return output;

        var tag = $"[{machine}]";
        var include = false;
        foreach (var line in ReadLines(logFile))
        {
            if (EntryHeader().IsMatch(line))
                include = line.Contains(tag, StringComparison.OrdinalIgnoreCase);
            if (include)
                output.Add(line);
        }
        return output;
    }
    public static string Whole(string? path)
    {
        if (path is null || !File.Exists(path))
            return string.Empty;
        return string.Join(Environment.NewLine, ReadLines(path));
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        // The logger still holds the file open for writing, so the reader must tolerate that.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    [GeneratedRegex(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]")]
    private static partial Regex EntryHeader();
}
