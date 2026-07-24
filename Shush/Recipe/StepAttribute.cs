namespace Shush.Recipe;

[AttributeUsage(AttributeTargets.Class)]
public sealed class StepAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
    public string? Description { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    public bool Required { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Hint for a multi-line string input (e.g. a file's whole contents): the editor renders a
    /// full-height, monospace, non-wrapping text area instead of a single-line field. Only
    /// meaningful for <see cref="string"/> inputs.
    /// </summary>
    public bool Multiline { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OutputAttribute : Attribute
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}
