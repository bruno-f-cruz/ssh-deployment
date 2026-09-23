using System.Text.Json;

namespace Shush.Recipe.Steps;

[Step("CleanDesktop", Description = "Delete every item from the interactively logged-in user's desktop and the shared Public desktop, except items matching an exclusion pattern.")]
public class CleanDesktopStep : IRecipeStep
{
    [Input(Description = "Wildcard patterns (PowerShell -like syntax, e.g. \"DEV-*\", \"*.ico\") for items to keep.")]
    public string[] Exclude { get; init; } = [];

    [Input(Description = "When true (default), nothing is deleted — only logs what would be kept/deleted.")]
    public bool DryRun { get; init; } = true;

    public async Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var patterns = string.Join(", ", Exclude.Select(p => $"'{Escape(p)}'"));
        var dryRun = DryRun ? "$true" : "$false";

        string script = $$"""
            $owner = (Get-CimInstance -ClassName Win32_ComputerSystem).UserName
            if (-not $owner) { throw 'No interactive user is currently logged in on this machine.' }
            $username = $owner.Split('\')[-1]
            $desktopPaths = @("C:\Users\$username\Desktop", 'C:\Users\Public\Desktop') | Where-Object { Test-Path $_ }
            if ($desktopPaths.Count -eq 0) { throw "No desktop path found for user '$username'." }

            $excludePatterns = @({{patterns}})
            $dryRun = {{dryRun}}

            $report = @(foreach ($desktopPath in $desktopPaths) {
                foreach ($item in Get-ChildItem -Path $desktopPath) {
                    $type = if ($item.PSIsContainer) { 'Directory' } else { 'File' }
                    $excluded = $false
                    foreach ($pattern in $excludePatterns) {
                        if ($item.Name -like $pattern) { $excluded = $true; break }
                    }

                    if ($excluded) {
                        [pscustomobject]@{ Name = $item.Name; Type = $type; Action = 'Kept'; Error = $null }
                    } elseif ($dryRun) {
                        [pscustomobject]@{ Name = $item.Name; Type = $type; Action = 'WouldDelete'; Error = $null }
                    } else {
                        try {
                            Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
                            [pscustomobject]@{ Name = $item.Name; Type = $type; Action = 'Deleted'; Error = $null }
                        } catch {
                            [pscustomobject]@{ Name = $item.Name; Type = $type; Action = 'Failed'; Error = $_.Exception.Message }
                        }
                    }
                }
            })

            if ($report.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $report -Compress }
            """;

        string[] commands = [script.ReplaceLineEndings("; ")];

        var output = await context.RunCommandsWithOutputAsync(commands, cancellationToken);
        var items = ParseReport(output);

        foreach (var item in items)
            context.Log(item.Error is null
                ? $"[{item.Action}] {item.Type} '{item.Name}'"
                : $"[{item.Action}] {item.Type} '{item.Name}' — {item.Error}");

        var failed = items.Where(i => i.Action == "Failed").ToList();
        if (failed.Count > 0)
            throw new InvalidOperationException(
                $"Failed to delete {failed.Count} desktop item(s): {string.Join(", ", failed.Select(f => f.Name))}");
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static List<CleanDesktopItemResult> ParseReport(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var trimmed = json.Trim();
        // ConvertTo-Json collapses a single-element array to a bare object.
        if (trimmed.StartsWith('{'))
            trimmed = $"[{trimmed}]";

        return JsonSerializer.Deserialize<List<CleanDesktopItemResult>>(
            trimmed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private sealed record CleanDesktopItemResult(string Name, string Type, string Action, string? Error);
}
