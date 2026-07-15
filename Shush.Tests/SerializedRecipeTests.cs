using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests;

public class SerializedRecipeTests
{
    private static SerializedRecipe Recipe(string yaml, Dictionary<string, string>? paramValues = null) =>
        new(
            YamlRecipeSerializer.Deserialize(yaml),
            new StepRegistry(typeof(IRecipeStep).Assembly),
            FunctionLibrary.Default,
            paramValues ?? new Dictionary<string, string>());

    [Fact]
    public void Plan_resolves_step_outputs_into_later_steps()
    {
        var recipe = Recipe("""
            name: T
            vars:
              repoRoot: C:/git
            steps:
              - id: clone
                type: GitClone
                with:
                  repositoryUrl: https://x/y
                  rootPath: ${vars.repoRoot}
                  folderName: y-dev
              - type: GitCheckout
                with:
                  path: ${clone.clonedPath}
                  reference: main
            """);

        using var e = recipe.CreatePlan().Steps().GetEnumerator();
        Assert.True(e.MoveNext());
        e.Current.CaptureOutputs(); // simulate clone having run
        Assert.True(e.MoveNext());
        var checkout = e.Current.Step;

        var path = checkout.GetType().GetProperty("Path")!.GetValue(checkout);
        Assert.Equal(@"C:/git\y-dev", path);
    }

    [Fact]
    public void Param_value_overrides_default()
    {
        var recipe = Recipe("""
            name: T
            params:
              tag:
                type: string
                default: v1.0.0
            steps:
              - id: clone
                type: GitClone
                with:
                  repositoryUrl: https://x/y
                  rootPath: C:/git
              - type: GitCheckout
                with:
                  path: ${clone.clonedPath}
                  reference: ${params.tag}
            """,
            paramValues: new() { ["tag"] = "v2.0.0" });

        using var e = recipe.CreatePlan().Steps().GetEnumerator();
        e.MoveNext();
        e.Current.CaptureOutputs();
        e.MoveNext();
        var reference = e.Current.Step.GetType().GetProperty("Reference")!.GetValue(e.Current.Step);
        Assert.Equal("v2.0.0", reference);
    }

    [Fact]
    public void StepNames_are_resolution_free()
    {
        var recipe = Recipe("""
            name: T
            steps:
              - id: clone
                type: GitClone
                with:
                  repositoryUrl: https://x/y
                  rootPath: C:/git
              - type: GitCheckout
                with:
                  path: ${clone.clonedPath}
                  reference: main
            """);
        Assert.Equal(new[] { "clone", "GitCheckout" }, recipe.StepNames);
    }
}
