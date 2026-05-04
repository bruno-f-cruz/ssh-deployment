using Shush.Recipe;
using Shush.Recipe.Steps;

namespace Shush.Recipes;

public class ExampleRecipe : IRecipe
{
    public string Name => "example";

    public IEnumerable<IRecipeStep> Steps =>
    [
        new GitCheckoutStep(remotePath: "C:/some/repo", tag: "v1.0.0"),
        new RunScriptStep(".\\scripts\\deploy.cmd"),
        new CopyFilesStep(sourceDirectory: "./FilesToTransfer/example", remoteBaseDirectory: "C:/some/repo"),
    ];
}
