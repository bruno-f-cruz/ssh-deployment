namespace Shush.Recipe;

public interface IDeploymentProgress
{
    void ReportStepStart(string boxId, string stepName);
    void ReportStep(string boxId, bool success, string stepName);
}
