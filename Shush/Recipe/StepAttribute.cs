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
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OutputAttribute : Attribute
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}
