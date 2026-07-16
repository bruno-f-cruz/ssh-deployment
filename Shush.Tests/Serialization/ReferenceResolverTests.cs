using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class ReferenceResolverTests
{
    private static ResolutionScope Scope()
    {
        var scope = new ResolutionScope();
        scope.SetParam("tag", "v1.2.3");
        scope.SetVar("repoRoot", "C:/git");
        scope.SetStepOutputs("clone", new Dictionary<string, string> { ["clonedPath"] = @"C:/git\repo" });
        return scope;
    }

    [Theory]
    [InlineData("${params.tag}", "v1.2.3")]
    [InlineData("${vars.repoRoot}", "C:/git")]
    [InlineData("${clone.clonedPath}/local/clabe.yml", @"C:/git\repo/local/clabe.yml")]
    [InlineData("no tokens here", "no tokens here")]
    public void Resolves_strings(string input, string expected) =>
        Assert.Equal(expected, ReferenceResolver.ResolveString(input, Scope(), FunctionLibrary.Empty));

    [Fact]
    public void Unknown_reference_throws()
    {
        Assert.Throws<RecipeValidationException>(
            () => ReferenceResolver.ResolveString("${vars.nope}", Scope(), FunctionLibrary.Empty));
    }

    [Fact]
    public void Resolves_object_graph_recursively()
    {
        var with = new Dictionary<string, object?>
        {
            ["path"] = "${clone.clonedPath}",
            ["list"] = new List<object?> { "${params.tag}", "static" },
        };
        var resolved = ReferenceResolver.ResolveWith(with, Scope(), FunctionLibrary.Empty);
        Assert.Equal(@"C:/git\repo", resolved["path"]);
        Assert.Equal(new[] { "v1.2.3", "static" }, ((IEnumerable<object?>)resolved["list"]!).Cast<string>());
    }
}
