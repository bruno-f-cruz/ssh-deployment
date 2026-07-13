using Shush.Recipe;

namespace Shush;

public sealed class DeploymentDisplay : IDeploymentProgress
{
    private const int DotsColumnWidth = 20;
    private const string Esc = "\x1B";

    private readonly object _lock = new();
    private readonly List<string> _boxIds;
    private readonly Dictionary<string, List<(bool Success, string StepName)>> _steps = new();
    private readonly Dictionary<string, string> _currentStep = new();
    private readonly int _col1Width;

    public DeploymentDisplay(IReadOnlyList<string> boxIds)
    {
        _boxIds = [.. boxIds];
        _col1Width = boxIds.Max(id => id.Length) + 2;

        foreach (var id in boxIds)
        {
            _steps[id] = [];
            _currentStep[id] = string.Empty;
            Console.WriteLine(RenderRow(id));
        }
        // cursor is now _boxIds.Count lines below the first row
    }

    public void ReportStepStart(string boxId, string stepName)
    {
        lock (_lock)
        {
            _currentStep[boxId] = stepName;
            RedrawRow(boxId);
        }
    }

    public void ReportStep(string boxId, bool success, string stepName)
    {
        lock (_lock)
        {
            _currentStep[boxId] = string.Empty;
            _steps[boxId].Add((success, stepName));
            RedrawRow(boxId);
        }
    }

    public void PrintSummary()
    {
        var failures = _steps
            .SelectMany(kv => kv.Value
                .Where(s => !s.Success)
                .Select(s => (BoxId: kv.Key, s.StepName)))
            .ToList();

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All steps completed successfully.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{failures.Count} failure(s):");
            foreach (var (boxId, stepName) in failures)
                Console.WriteLine($"  [{boxId}] {stepName}");
        }
        Console.ResetColor();
    }

    private void RedrawRow(string boxId)
    {
        var linesUp = _boxIds.Count - _boxIds.IndexOf(boxId);

        Console.Write($"{Esc}[{linesUp}A\r");  // move up, go to start of line

        Console.ResetColor();
        Console.Write(boxId.PadRight(_col1Width));
        Console.Write("| ");

        foreach (var (success, _) in _steps[boxId])
        {
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(success ? "." : "F");
        }
        Console.ResetColor();
        Console.Write(new string(' ', Math.Max(0, DotsColumnWidth - _steps[boxId].Count)));

        Console.Write(" | ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(_currentStep[boxId]);
        Console.ResetColor();
        Console.Write($"{Esc}[K");             // erase to end of line

        Console.Write($"{Esc}[{linesUp}B\r"); // move back down
    }

    private string RenderRow(string boxId) =>
        $"{boxId.PadRight(_col1Width)}| {"".PadRight(DotsColumnWidth)} | ";
}
