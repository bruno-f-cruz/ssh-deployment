using Shush.Data;

namespace Shush.Design.Services;

/// <summary>The browser's time zone, read once per tab, so times show in the operator's local time.</summary>
public sealed class ClientTime
{
    public int OffsetMinutes { get; set; }
    public bool Known { get; set; }

    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc.AddMinutes(-OffsetMinutes), DateTimeKind.Unspecified);
}

/// <summary>A status as the UI draws it: a tone (ok, run, fail, warn, idle) and a label.</summary>
public readonly record struct Tone(string Name, string Label)
{
    public string Dot => $"dot dot--{Name}";
    public string Text => $"t-{Name}";
}

public static class Ui
{
    /// <summary>An ARIA state ("true"/"false"). Blazor renders a bool attribute as present-or-absent, which ARIA doesn't read.</summary>
    public static string Aria(bool value) => value ? "true" : "false";

    public static string Aria(string value) => value;

    public static Tone Of(RunStatus status) => status switch
    {
        RunStatus.Running => new("run", "Running"),
        RunStatus.Succeeded => new("ok", "Succeeded"),
        RunStatus.Failed => new("fail", "Failed"),
        RunStatus.Cancelled => new("idle", "Cancelled"),
        _ => new("warn", "Interrupted"),
    };

    public static Tone Of(MachineStatus status) => status switch
    {
        MachineStatus.Waiting => new("idle", "Waiting"),
        MachineStatus.Running => new("run", "Running"),
        MachineStatus.Succeeded => new("ok", "Done"),
        MachineStatus.Failed => new("fail", "Failed"),
        MachineStatus.Cancelled => new("idle", "Cancelled"),
        _ => new("warn", "Interrupted"),
    };

    public static string Segment(StepStatus? status) => status switch
    {
        StepStatus.Succeeded => "seg-ok",
        StepStatus.Running => "seg-run",
        StepStatus.Failed => "seg-fail",
        StepStatus.Skipped => "seg-skip",
        _ => "seg-pend",
    };

    public static string Kind(RunKind kind) => kind switch
    {
        RunKind.Deploy => "Deploy",
        RunKind.DryRun => "Dry run",
        RunKind.Retry => "Retry",
        _ => "Rollback",
    };

    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span < TimeSpan.FromSeconds(45)) return "just now";
        if (span < TimeSpan.FromMinutes(1.5)) return "a minute ago";
        if (span < TimeSpan.FromHours(1)) return $"{(int)Math.Round(span.TotalMinutes)} min ago";
        if (span < TimeSpan.FromHours(1.5)) return "an hour ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)Math.Round(span.TotalHours)} hours ago";
        if (span < TimeSpan.FromDays(2)) return "yesterday";
        if (span < TimeSpan.FromDays(30)) return $"{(int)Math.Round(span.TotalDays)} days ago";
        return utc.ToString("d MMM yyyy");
    }

    public static string When(DateTime utc, ClientTime time)
    {
        var local = time.ToLocal(utc);
        var today = time.ToLocal(DateTime.UtcNow).Date;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return $"yesterday {local:HH:mm}";
        return local.Year == today.Year ? local.ToString("d MMM, HH:mm") : local.ToString("d MMM yyyy, HH:mm");
    }

    public static string Duration(DateTime? start, DateTime? end)
    {
        if (start is null) return "";
        var span = (end ?? DateTime.UtcNow) - start.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return $"{span.Seconds}s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m {span.Seconds:00}s";
        return $"{(int)span.TotalHours}h {span.Minutes:00}m";
    }

    public static string ShortCommit(string? commit) => string.IsNullOrEmpty(commit) ? "" : commit.Length > 8 ? commit[..8] : commit;

    public static string Plural(int count, string one, string? many = null) => count == 1 ? $"1 {one}" : $"{count} {many ?? one + "s"}";

    /// <summary>The room a rig sits in, from names like FRG.12-C (room FRG.12, tile "12-C"). Other names go under "Other".</summary>
    public static (string Room, string Short) Room(string machine)
    {
        var dash = machine.LastIndexOf('-');
        if (dash > 0 && dash < machine.Length - 1)
        {
            var room = machine[..dash];
            var dot = room.IndexOf('.');
            return (room, dot >= 0 ? machine[(dot + 1)..] : machine);
        }

        return ("Other", machine);
    }
}
