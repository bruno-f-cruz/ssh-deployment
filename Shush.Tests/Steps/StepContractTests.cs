using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Steps;

public class StepContractTests
{
    private readonly StepRegistry _registry = new(typeof(IRecipeStep).Assembly);

    [Theory]
    [InlineData("GitClone")]
    [InlineData("GitCheckout")]
    [InlineData("RunScript")]
    [InlineData("WriteFile")]
    [InlineData("CreateBatchFile")]
    [InlineData("CopyFiles")]
    [InlineData("TemplatedCopyFiles")]
    [InlineData("CreateShortcut")]
    [InlineData("DeleteDirectory")]
    public void Step_is_registered(string typeName) =>
        Assert.NotNull(_registry.Get(typeName));

    [Fact]
    public void GitClone_exposes_clonedPath_output()
    {
        var with = new Dictionary<string, object?> { ["repositoryUrl"] = "https://x/y", ["rootPath"] = "C:/git", ["folderName"] = "y-dev" };
        var step = StepBinder.Bind(_registry.Get("GitClone"), with);
        var output = _registry.Get("GitClone").Outputs.Single(o => o.Name == "clonedPath");
        Assert.Equal(@"C:/git\y-dev", output.Property.GetValue(step));
    }
}
