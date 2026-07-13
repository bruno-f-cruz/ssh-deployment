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
    public required string Name { get; init; }
    public MachineInfo? Info { get; set; }
    public string? Error { get; set; }
    public RowStatus Status { get; set; } = RowStatus.Ready;
    public string CurrentStep { get; set; } = string.Empty;
    public List<(bool Success, string StepName)> CompletedSteps { get; } = [];

    public bool IsResolved => Info is not null;
}
