using System.Reflection;

namespace Shush.Recipe.Serialization;

public sealed class StepRegistry
{
    private readonly Dictionary<string, StepDescriptor> _descriptors;

    public StepRegistry(Assembly assembly)
    {
        _descriptors = assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.IsAssignableTo(typeof(IRecipeStep))
                        && t.GetCustomAttribute<StepAttribute>() is not null)
            .Select(StepDescriptor.FromType)
            .ToDictionary(d => d.TypeName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<StepDescriptor> Descriptors => _descriptors.Values;

    public bool Contains(string typeName) => _descriptors.ContainsKey(typeName);

    public bool TryGet(string typeName, out StepDescriptor descriptor) =>
        _descriptors.TryGetValue(typeName, out descriptor!);

    public StepDescriptor Get(string typeName) =>
        _descriptors.TryGetValue(typeName, out var descriptor)
            ? descriptor
            : throw new RecipeValidationException($"Unknown step type '{typeName}'.");
}
