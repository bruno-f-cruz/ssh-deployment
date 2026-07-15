using Shush.Recipe;

namespace Shush.Design.Services;

public sealed class BlazorDeploymentProgress : IDeploymentProgress
{
    private readonly Dictionary<string, MachineRow> _rows;

    public BlazorDeploymentProgress(Dictionary<string, MachineRow> rows, int totalSteps)
    {
        _rows = rows;
        TotalSteps = totalSteps;
    }

    public event Action? Changed;

    public int TotalSteps { get; }

    public string? LogFilePath { get; set; }

    public void ReportStepStart(string boxId, string stepName)
    {
        var row = _rows[boxId];
        row.Status = RowStatus.Running;
        row.CurrentStep = stepName;
        Changed?.Invoke();
    }

    public void ReportStep(string boxId, bool success, string stepName)
    {
        var row = _rows[boxId];
        row.CurrentStep = string.Empty;
        row.AddCompletedStep(success, stepName);
        if (!success) row.Status = RowStatus.Failed;
        Changed?.Invoke();
    }

    public void ReportFailure(string boxId, Exception ex)
    {
        var row = _rows[boxId];
        row.CurrentStep = string.Empty;
        row.Status = RowStatus.Failed;
        row.Error = ex.Message;
        Changed?.Invoke();
    }

    public void MarkRemainingAsSucceeded()
    {
        foreach (var row in _rows.Values)
            if (row.Status != RowStatus.Failed)
                row.Status = RowStatus.Success;

        Changed?.Invoke();
    }
}
