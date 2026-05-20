using Shush.Recipe;
using Shush.Recipe.Steps;

namespace Shush.Recipes;

public class VrForagingRecipe : IRecipe
{
    public string Name => "VrForaging";

    const string LOCAL_REPO_NAME = "Aind.Behavior.VrForaging";
    const string TAG = "v1.0.0rc4";

    public IEnumerable<IRecipeStep> Steps
    {
        get
        {
            var clone = new GitCloneStep($"https://github.com/AllenNeuralDynamics/{LOCAL_REPO_NAME}", "C:/git");
            return
            [
                clone,
                new GitCheckoutStep(clone.ClonedPath, tag: TAG),
                new RunScriptStep(new[] { ".\\scripts\\deploy.cmd" }, workingDirectory: clone.ClonedPath),
                new CreateShortcutStep(cmdPath: $"{clone.ClonedPath}\\scripts\\launcher.cmd", shortcutDirectory: @"C:\Users\Public\Desktop", shortName: "VrForaging"),
                new CopyFilesStep(sourceDirectory: "./FilesToTransfer", remoteBaseDirectory: clone.ClonedPath),
            ];
        }
    }
}
