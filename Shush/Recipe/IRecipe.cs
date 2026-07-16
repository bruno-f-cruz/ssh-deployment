namespace Shush.Recipe;

public interface IRecipe
{
    string Name { get; }

    /// <summary>Display names of the steps, in order — resolution-free (for progress UI).</summary>
    IReadOnlyList<string> StepNames { get; }

    /// <summary>Builds a fresh execution plan with its own resolution scope (one per machine).</summary>
    IRecipeExecutionPlan CreatePlan();
}

public interface IRecipeExecutionPlan
{
    /// <summary>
    /// Yields steps in order. Each step is resolved and bound lazily against the outputs
    /// captured from earlier steps, so callers must invoke <see cref="PlannedStep.CaptureOutputs"/>
    /// after executing a step before advancing to the next.
    /// </summary>
    IEnumerable<PlannedStep> Steps();
}

public sealed record PlannedStep(string DisplayName, string? Id, IRecipeStep Step, Action CaptureOutputs);
