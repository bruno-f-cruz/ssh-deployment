using Shush;

namespace Shush.Design.Services;

public enum RowStatus
{
    Ready,
    Invalid,
    Running,
    Success,
    Failed,
}

public sealed class MachineRow
{
    private readonly object _stepsLock = new();
    private readonly List<(bool Success, string StepName)> _completedSteps = [];

    public required string Name { get; init; }
    public MachineInfo? Info { get; set; }
    public string? Error { get; set; }
    public RowStatus Status { get; set; } = RowStatus.Ready;
    public string CurrentStep { get; set; } = string.Empty;

    // Reachability is informational only — an unreachable row stays deployable and
    // the deployment itself reports the connection failure.
    public HostProbeResult? Probe { get; set; }
    public bool Probing { get; set; }

    public bool IsResolved => Info is not null;

    public int CompletedStepCount
    {
        get { lock (_stepsLock) return _completedSteps.Count; }
    }

    public void AddCompletedStep(bool success, string stepName)
    {
        lock (_stepsLock) _completedSteps.Add((success, stepName));
    }

    public (bool Success, string StepName)? LastCompletedStep()
    {
        lock (_stepsLock) return _completedSteps.Count > 0 ? _completedSteps[^1] : null;
    }
}
