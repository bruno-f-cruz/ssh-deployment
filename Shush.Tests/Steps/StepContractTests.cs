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
    [InlineData("CreateShortcut")]
    [InlineData("DeleteDirectory")]
    [InlineData("SetEnvironmentVariable")]
    [InlineData("Reboot")]
    public void Step_is_registered(string typeName) =>
        Assert.NotNull(_registry.Get(typeName));

    [Fact]
    public void Reboot_defaults_delay_and_binds_override()
    {
        var descriptor = _registry.Get("Reboot");

        var defaulted = (Shush.Recipe.Steps.RebootStep)StepBinder.Bind(descriptor, new Dictionary<string, object?>());
        Assert.Equal(5, defaulted.DelaySeconds);

        var overridden = (Shush.Recipe.Steps.RebootStep)StepBinder.Bind(
            descriptor, new Dictionary<string, object?> { ["delaySeconds"] = 30 });
        Assert.Equal(30, overridden.DelaySeconds);
    }

    [Fact]
    public void WriteFile_content_is_a_multiline_text_input()
    {
        var content = _registry.Get("WriteFile").Inputs.Single(i => i.Name == "content");
        Assert.True(content.Multiline);
        Assert.Equal(InputShape.Text, content.Shape);
    }

    [Fact]
    public void SetEnvironmentVariable_binds_scope_and_secret()
    {
        var with = new Dictionary<string, object?>
        {
            ["name"] = "OPENOBSERVE_AUTH",
            ["value"] = "Basic abc==",
            ["secret"] = true,
        };
        var descriptor = _registry.Get("SetEnvironmentVariable");
        var step = (Shush.Recipe.Steps.SetEnvironmentVariableStep)StepBinder.Bind(descriptor, with);

        Assert.Equal("OPENOBSERVE_AUTH", step.Name);
        Assert.Equal("Basic abc==", step.Value);
        Assert.True(step.Secret);
        Assert.Equal("Machine", step.Scope); // default applied when not supplied
    }

    [Fact]
    public void GitClone_exposes_clonedPath_output()
    {
        var with = new Dictionary<string, object?> { ["repositoryUrl"] = "https://x/y", ["rootPath"] = "C:/git", ["folderName"] = "y-dev" };
        var step = StepBinder.Bind(_registry.Get("GitClone"), with);
        var output = _registry.Get("GitClone").Outputs.Single(o => o.Name == "clonedPath");
        Assert.Equal(@"C:/git\y-dev", output.Property.GetValue(step));
    }
}
