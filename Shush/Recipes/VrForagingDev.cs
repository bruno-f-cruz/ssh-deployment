using Shush.Recipe;
using Shush.Recipe.Steps;

namespace Shush.Recipes;

public class VrForagingDevRecipe : IRecipe
{
    public string Name => "VrForagingDev";
    const string TAG = "v1.1.1rc2";

    const string CLABE_FILE = """
# In order to get picked up, move this file to the root of the project or ./local

allow_dirty: false
skip_hardware_validation: false

watchdog:
  project_name: "Cognitive flexibility in patch foraging"
  destination: '\\allen\\aind\\stage\\vr-foraging\\data'
  delete_modalities_source_after_success: true
  job_type: "vr_foraging_v2"
  transfer_endpoint: "http://aind-data-transfer-service/api/v2/submit_jobs"
  s3_bucket: "default"
  schedule_time: 18:00:00

dataverse:
  tenant_id: "32669cd6-737f-4b39-8bdd-d6951120d3fc"
  client_id: "df37356e-3316-484a-b732-319b6b4ad464"
  org: "org5d93e08d"
""";

    public IEnumerable<IRecipeStep> Steps
    {
        get
        {
            var clone = new GitCloneStep($"https://github.com/AllenNeuralDynamics/Aind.Behavior.VrForaging", "C:/git", folderName: "Aind.Behavior.VrForaging-dev");
            return
            [
                clone,
                new GitCheckoutStep(clone.ClonedPath, tag: TAG, cleanExceptions: [".cache_manager.json"]),
                new RunScriptStep(new[] { "$ErrorActionPreference = 'Stop'", "$ProgressPreference = 'SilentlyContinue'", ".\\scripts\\deploy.ps1" }, workingDirectory: clone.ClonedPath),
                new CreateBatchFileStep(
                    @"C:\Users\Public\Desktop\DEV-VrForaging.cmd",
                    $"cd /d {clone.ClonedPath}",
                    $"uv run .\\scripts\\aind.py",
                    "pause"),
                new WriteFileStep(CLABE_FILE, $"{clone.ClonedPath}/local/clabe.yml"),
            ];
        }
    }
}
