namespace Shush.Recipe;

/// <summary>
/// Receives a run's progress. Per machine, steps report strictly in order (start, then done or skipped), so an
/// implementation can track the step index by counting.
/// </summary>
public interface IDeploymentProgress
{
    /// <summary>The steps this machine will go through, in order, before the first one starts.</summary>
    void ReportPlan(string boxId, IReadOnlyList<string> stepNames) { }

    void ReportStepStart(string boxId, string stepName);
    void ReportStep(string boxId, bool success, string stepName);

    /// <summary>A step that doesn't run: disabled, or its <c>when:</c> condition is false.</summary>
    void ReportSkipped(string boxId, string stepName, string reason) => ReportStep(boxId, success: true, $"{stepName} (skipped)");

    /// <summary>The outputs a step left (e.g. the commit a checkout left), right after it succeeded.</summary>
    void ReportOutputs(string boxId, string stepName, IReadOnlyDictionary<string, string> outputs) { }

    /// <summary>Every step of this machine succeeded (or was skipped).</summary>
    void ReportSucceeded(string boxId) { }

    void ReportFailure(string boxId, Exception ex);
}
