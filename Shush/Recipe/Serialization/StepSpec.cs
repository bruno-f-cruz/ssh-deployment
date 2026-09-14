namespace Shush.Recipe.Serialization;

public sealed class StepSpec
{
    public string? Id { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, object?> With { get; set; } = new();

    /// <summary>
    /// Null (the common case, omitted from YAML) or true means the step runs; false means it's
    /// skipped at deploy time but stays in the recipe. Kept nullable so an enabled step's YAML
    /// stays clean — only a disabled step gets an explicit <c>enabled: false</c> line.
    /// </summary>
    public bool? Enabled { get; set; }

    public bool IsEnabled => Enabled ?? true;
}
