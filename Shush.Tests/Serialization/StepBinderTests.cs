using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

[Step("Fake", Description = "A fake step for tests.")]
public class FakeStep : IRecipeStep
{
    [Input(Required = true, Description = "The path.")] public string Path { get; init; } = "";
    [Input] public string? Optional { get; init; }
    [Input] public IReadOnlyList<string> Items { get; init; } = [];
    [Output(Description = "Echoes the inputs.")] public string Echo => $"{Path}:{Optional}";
    public Task ExecuteAsync(MachineContext context, CancellationToken ct = default) => Task.CompletedTask;
}

public class StepBinderTests
{
    [Fact]
    public void Registry_finds_step_by_type_name()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var descriptor = registry.Get("Fake");
        Assert.Equal(typeof(FakeStep), descriptor.StepType);
        Assert.Contains(descriptor.Inputs, i => i is { Name: "path", Required: true });
        Assert.Contains(descriptor.Outputs, o => o.Name == "echo");
    }

    [Fact]
    public void Binder_sets_inputs_including_collection()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var with = new Dictionary<string, object?>
        {
            ["path"] = "C:/x",
            ["items"] = new List<object?> { "a", "b" },
        };
        var step = (FakeStep)StepBinder.Bind(registry.Get("Fake"), with);
        Assert.Equal("C:/x", step.Path);
        Assert.Equal(new[] { "a", "b" }, step.Items);
    }

    [Fact]
    public void Binder_rejects_unknown_input_key()
    {
        var registry = new StepRegistry(typeof(FakeStep).Assembly);
        var with = new Dictionary<string, object?> { ["path"] = "x", ["bogus"] = "1" };
        Assert.Throws<RecipeValidationException>(() => StepBinder.Bind(registry.Get("Fake"), with));
    }
}
