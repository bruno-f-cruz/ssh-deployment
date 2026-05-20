namespace Shush;

public sealed class DeploymentDisplay
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _rowIndex = new();
    private readonly Dictionary<string, List<(bool Success, string StepName)>> _steps = new();
    private readonly int _labelWidth;
    private readonly int _startRow;

    public DeploymentDisplay(IReadOnlyList<string> boxIds)
    {
        _labelWidth = boxIds.Max(id => id.Length) + 2;
        _startRow = Console.CursorTop;

        for (int i = 0; i < boxIds.Count; i++)
        {
            var id = boxIds[i];
            _rowIndex[id] = i;
            _steps[id] = [];
            Console.WriteLine(id.PadRight(_labelWidth));
        }
    }

    public void ReportStep(string boxId, bool success, string stepName)
    {
        lock (_lock)
        {
            _steps[boxId].Add((success, stepName));
            Redraw(boxId);
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

    private void Redraw(string boxId)
    {
        var savedTop = Console.CursorTop;
        var savedLeft = Console.CursorLeft;

        Console.SetCursorPosition(_labelWidth, _startRow + _rowIndex[boxId]);
        foreach (var (success, _) in _steps[boxId])
        {
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(success ? "." : "F");
        }
        Console.ResetColor();

        Console.SetCursorPosition(savedLeft, savedTop);
    }
}
