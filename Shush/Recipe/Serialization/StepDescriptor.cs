using System.Collections;
using System.Reflection;

namespace Shush.Recipe.Serialization;

/// <summary>How an input is edited in the UI, derived from its CLR type.</summary>
public enum InputShape { Scalar, Collection, Dictionary }

public sealed class InputDescriptor
{
    public required string Name { get; init; }
    public required PropertyInfo Property { get; init; }
    public required bool Required { get; init; }
    public string? Description { get; init; }
    public Type PropertyType => Property.PropertyType;

    public InputShape Shape
    {
        get
        {
            var type = PropertyType;
            if (type == typeof(string))
                return InputShape.Scalar;
            if (type.IsAssignableFrom(typeof(Dictionary<string, string>)) && typeof(IEnumerable).IsAssignableFrom(type))
                return InputShape.Dictionary;
            if (typeof(IEnumerable).IsAssignableFrom(type))
                return InputShape.Collection;
            return InputShape.Scalar;
        }
    }
}

public sealed class OutputDescriptor
{
    public required string Name { get; init; }
    public required PropertyInfo Property { get; init; }
    public string? Description { get; init; }
}

public sealed class StepDescriptor
{
    public required Type StepType { get; init; }
    public required string TypeName { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<InputDescriptor> Inputs { get; init; }
    public required IReadOnlyList<OutputDescriptor> Outputs { get; init; }

    public static StepDescriptor FromType(Type stepType)
    {
        var stepAttr = stepType.GetCustomAttribute<StepAttribute>()
            ?? throw new InvalidOperationException($"Type '{stepType.Name}' is missing [Step].");

        var properties = stepType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var inputs = properties
            .Select(p => (p, attr: p.GetCustomAttribute<InputAttribute>()))
            .Where(x => x.attr is not null)
            .Select(x => new InputDescriptor
            {
                Name = x.attr!.Name ?? CamelCase(x.p.Name),
                Property = x.p,
                Required = x.attr.Required,
                Description = x.attr.Description,
            })
            .ToList();

        var outputs = properties
            .Select(p => (p, attr: p.GetCustomAttribute<OutputAttribute>()))
            .Where(x => x.attr is not null)
            .Select(x => new OutputDescriptor
            {
                Name = x.attr!.Name ?? CamelCase(x.p.Name),
                Property = x.p,
                Description = x.attr.Description,
            })
            .ToList();

        return new StepDescriptor
        {
            StepType = stepType,
            TypeName = stepAttr.TypeName,
            Description = stepAttr.Description,
            Inputs = inputs,
            Outputs = outputs,
        };
    }

    internal static string CamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
