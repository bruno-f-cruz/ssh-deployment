namespace Shush.Recipe;

public interface IRecipe
{
    string Name { get; }

    /// <summary>Display names of the steps, in order — resolution-free (for progress UI).</summary>
    IReadOnlyList<string> StepNames { get; }

    /// <summary>
    /// Builds a fresh execution plan with its own resolution scope (one per machine). <paramref name="context"/>
    /// supplies what differs per machine: its name and the variables <c>${group.NAME}</c> reads.
    /// </summary>
    IRecipeExecutionPlan CreatePlan(PlanContext? context = null);
}

/// <summary>What one machine's plan resolves against, beyond the recipe's own params and vars.</summary>
/// <param name="MachineName">The machine the plan is for (informational; shown in errors).</param>
/// <param name="GroupVariables">The machine's variables (machine override, else group, else global), by name.</param>
public sealed record PlanContext(string? MachineName = null, IReadOnlyDictionary<string, string>? GroupVariables = null);

public interface IRecipeExecutionPlan
{
    /// <summary>
    /// ACL grants the runner must apply, in order, before executing the first step — files
    /// created by any step then inherit the access.
    /// </summary>
    IReadOnlyList<SharedAccessGrant> SharedAccess { get; }

    /// <summary>
    /// Yields steps in order. Each step is resolved and bound lazily against the outputs
    /// captured from earlier steps, so callers must invoke <see cref="PlannedStep.CaptureOutputs"/>
    /// after executing a step before advancing to the next.
    /// </summary>
    IEnumerable<PlannedStep> Steps();
}

/// <param name="Step">Null when the step doesn't run — disabled, or its <c>when:</c> is false — so it's never bound.</param>
/// <param name="CaptureOutputs">
/// Call after the step ran: records its outputs for later references (when it has an id) and returns them, so the
/// caller can keep them (e.g. the commit a checkout left).
/// </param>
/// <param name="SkipReason">Why a step that doesn't run is skipped ("disabled", or the <c>when:</c> condition).</param>
/// <param name="ResolvedInputs">The step's inputs with every <c>${...}</c> resolved (null when it doesn't run).</param>
public sealed record PlannedStep(
    string DisplayName,
    string? Id,
    IRecipeStep? Step,
    bool Enabled,
    Func<IReadOnlyDictionary<string, string>> CaptureOutputs,
    string? SkipReason = null,
    IReadOnlyDictionary<string, object?>? ResolvedInputs = null);
