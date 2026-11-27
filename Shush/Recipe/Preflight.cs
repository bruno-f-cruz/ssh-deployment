using System.Text.Json;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Common;

namespace Shush.Recipe;

public enum CheckLevel { Ok, Warning, Error }

public sealed record PreflightCheck(string Name, CheckLevel Level, string Message);

/// <summary>What the rig reported about itself. Any field can be missing when the rig didn't say.</summary>
public sealed record MachineFacts(
    string? LoggedOnUser,
    DateTimeOffset? LastBoot,
    string? OperatingSystem,
    string? SystemDrive,
    double? FreeGb,
    double? SizeGb);

public sealed record PreflightResult(string Machine, IReadOnlyList<PreflightCheck> Checks, MachineFacts? Facts, string? ReachableAddress)
{
    public bool HasErrors => Checks.Any(c => c.Level == CheckLevel.Error);
    public bool HasWarnings => Checks.Any(c => c.Level == CheckLevel.Warning);
}

/// <summary>
/// Checks a machine before anything changes on it: it answers on port 22, the SSH sign-in works, who is logged on
/// (a recipe that runs in the logged-on user's session needs that to be the SSH user) and how much disk is free.
/// Read-only: nothing on the rig changes.
/// </summary>
public static class Preflight
{
    /// <summary>Below this much free space on the system drive, the check warns.</summary>
    public const double LowDiskGb = 5;

    // One PowerShell line, no double quotes (MachineContext wraps the command in them). Prints one JSON object.
    private const string FactsCommand =
        "$cs = Get-CimInstance Win32_ComputerSystem; $os = Get-CimInstance Win32_OperatingSystem; " +
        "$d = Get-CimInstance Win32_LogicalDisk -Filter ('DeviceID=''' + $env:SystemDrive + ''''); " +
        "[pscustomobject]@{ user = $cs.UserName; boot = $os.LastBootUpTime.ToUniversalTime().ToString('o'); os = $os.Caption; " +
        "drive = $env:SystemDrive; freeGb = [math]::Round($d.FreeSpace / 1GB, 1); sizeGb = [math]::Round($d.Size / 1GB, 1) } | ConvertTo-Json -Compress";

    public static async Task<PreflightResult> CheckAsync(
        string machine, MachineInfo info, Secrets secrets, ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        var checks = new List<PreflightCheck>();

        var probe = await HostProber.ProbeAsync(info.hostname, ct: ct);
        if (probe.ReachableAddress is null)
        {
            checks.Add(new("Reachable", CheckLevel.Error, $"Nothing answers on port {HostProber.SshPort}. {probe.Describe()}"));
            return new PreflightResult(machine, checks, null, null);
        }

        checks.Add(new("Reachable", CheckLevel.Ok, $"Answers on port {HostProber.SshPort} at {probe.ReachableAddress}."));

        MachineContext context;
        try
        {
            context = await MachineContext.ConnectAsync(machine, info, secrets, loggerFactory, ct);
        }
        catch (SshAuthenticationException)
        {
            checks.Add(new("SSH sign-in", CheckLevel.Error, $"The rig refused the sign-in for '{secrets.Username}'. Check the username and password."));
            return new PreflightResult(machine, checks, null, probe.ReachableAddress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            checks.Add(new("SSH sign-in", CheckLevel.Error, $"Couldn't open an SSH session: {ex.Message}"));
            return new PreflightResult(machine, checks, null, probe.ReachableAddress);
        }

        await using (context)
        {
            checks.Add(new("SSH sign-in", CheckLevel.Ok, $"Signed in as '{secrets.Username}'."));

            MachineFacts? facts;
            try
            {
                facts = ParseFacts(await context.RunCommandsWithOutputAsync([FactsCommand], ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                checks.Add(new("Rig facts", CheckLevel.Warning, $"Couldn't read the rig's facts: {ex.Message}"));
                return new PreflightResult(machine, checks, null, probe.ReachableAddress);
            }

            checks.AddRange(Evaluate(facts, secrets.Username));
            return new PreflightResult(machine, checks, facts, probe.ReachableAddress);
        }
    }

    /// <summary>The checks the rig's facts lead to. Public for tests.</summary>
    public static IEnumerable<PreflightCheck> Evaluate(MachineFacts facts, string sshUser)
    {
        if (string.IsNullOrWhiteSpace(facts.LoggedOnUser))
            yield return new("Logged-on user", CheckLevel.Warning, "No one is logged on to the rig.");
        else if (!SameAccount(facts.LoggedOnUser, sshUser))
            yield return new("Logged-on user", CheckLevel.Warning,
                $"'{facts.LoggedOnUser}' is logged on, not '{sshUser}'. Steps that run in the logged-on user's session may fail.");
        else
            yield return new("Logged-on user", CheckLevel.Ok, $"'{facts.LoggedOnUser}' is logged on.");

        if (facts.FreeGb is { } free)
        {
            var drive = facts.SystemDrive ?? "The system drive";
            yield return free < LowDiskGb
                ? new("Free disk", CheckLevel.Warning, $"{drive} has only {free:0.#} GB free.")
                : new("Free disk", CheckLevel.Ok, $"{drive} has {free:0.#} GB free.");
        }
    }

    /// <summary>Compares account names ignoring the domain (<c>CORP\jane</c> is <c>jane</c>) and case.</summary>
    public static bool SameAccount(string a, string b) =>
        string.Equals(AccountName(a), AccountName(b), StringComparison.OrdinalIgnoreCase);

    private static string AccountName(string account)
    {
        var trimmed = account.Trim();
        var slash = trimmed.LastIndexOf('\\');
        if (slash >= 0)
            return trimmed[(slash + 1)..];
        var at = trimmed.IndexOf('@');
        return at > 0 ? trimmed[..at] : trimmed;
    }

    /// <summary>Reads the JSON object <see cref="FactsCommand"/> prints (the last line that is one). Public for tests.</summary>
    public static MachineFacts ParseFacts(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith('{'))
            ?? throw new InvalidOperationException("The rig printed no facts.");

        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;

        return new MachineFacts(
            LoggedOnUser: String(root, "user"),
            LastBoot: DateTimeOffset.TryParse(String(root, "boot"), out var boot) ? boot : null,
            OperatingSystem: String(root, "os"),
            SystemDrive: String(root, "drive"),
            FreeGb: Number(root, "freeGb"),
            SizeGb: Number(root, "sizeGb"));
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
