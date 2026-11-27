using YamlDotNet.Serialization;

namespace Shush.Recipe.Serialization;

public sealed class StepSpec
{
    public string? Id { get; set; }

    /// <summary>Optional display label for progress and logs (e.g. "Checkout main"). Defaults to the id, else the type.</summary>
    public string? Name { get; set; }

    public string Type { get; set; } = "";

    /// <summary>
    /// Optional condition, resolved per machine: <c>a == b</c>, <c>a != b</c>, or a single true/false value. The step
    /// is skipped when it's false. See <see cref="ConditionEvaluator"/>.
    /// </summary>
    public string? When { get; set; }

    public Dictionary<string, object?> With { get; set; } = new();

    /// <summary>
    /// Null (the common case, omitted from YAML) or true means the step runs; false means it's
    /// skipped at deploy time but stays in the recipe. Kept nullable so an enabled step's YAML
    /// stays clean — only a disabled step gets an explicit <c>enabled: false</c> line.
    /// </summary>
    public bool? Enabled { get; set; }

    [YamlIgnore]
    public bool IsEnabled => Enabled ?? true;

    /// <summary>What progress and logs call this step.</summary>
    [YamlIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : !string.IsNullOrWhiteSpace(Id) ? Id : Type;
}
