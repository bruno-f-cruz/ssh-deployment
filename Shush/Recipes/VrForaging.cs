using Shush.Recipe;
using Shush.Recipe.Steps;

namespace Shush.Recipes;

public class VrForagingRecipe : IRecipe
{
    public string Name => "VrForaging";

    const string LOCAL_REPO_NAME = "Aind.Behavior.VrForaging";
    const string TAG = "v1.0.0";

    public IEnumerable<IRecipeStep> Steps
    {
        get
        {
            var clone = new GitCloneStep($"https://github.com/AllenNeuralDynamics/{LOCAL_REPO_NAME}", "C:/git");
            return
            [
                clone,
                new DeleteDirectoryStep($"{clone.ClonedPath}\\bonsai"),
                new GitCheckoutStep(clone.ClonedPath, tag: TAG),
                new RunScriptStep(new[] { "$ErrorActionPreference = 'Stop'", "$ProgressPreference = 'SilentlyContinue'", ".\\scripts\\deploy.ps1" }, workingDirectory: clone.ClonedPath),
                new CreateBatchFileStep(
                    @"C:\Users\Public\Desktop\VrForaging.cmd",
                    $"cd /d {clone.ClonedPath}",
                    $"uv run .\\scripts\\aind.py",
                    "pause"),
                new TemplatedCopyFilesStep(
                    sourceDirectory: "./FilesToTransfer",
                    remoteBaseDirectory: clone.ClonedPath,
                    variables: new()
                    {
                        ["schedule_time"] = RandomTime(fromMinutes: 17 * 60 + 50, toMinutes: 18 * 60 + 10),
                    }),
            ];
        }
    }

    private static string RandomTime(int fromMinutes, int toMinutes)
    {
        var minutes = Random.Shared.Next(fromMinutes, toMinutes + 1);
        return $"{minutes / 60:D2}:{minutes % 60:D2}";
    }
}
