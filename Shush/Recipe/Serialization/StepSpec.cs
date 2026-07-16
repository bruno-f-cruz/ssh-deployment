namespace Shush.Recipe.Serialization;

public sealed class StepSpec
{
    public string? Id { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, object?> With { get; set; } = new();
}
