using System.Collections;
using System.ComponentModel;

namespace Shush.Recipe.Serialization;

public static class StepBinder
{
    public static IRecipeStep Bind(StepDescriptor descriptor, IReadOnlyDictionary<string, object?> with)
    {
        var byName = descriptor.Inputs.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        var unknown = with.Keys.Where(k => !byName.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new RecipeValidationException(
                unknown.Select(k => $"Step '{descriptor.TypeName}' has no input named '{k}'.").ToList());
        }

        var step = (IRecipeStep)Activator.CreateInstance(descriptor.StepType)!;

        foreach (var input in descriptor.Inputs)
        {
            if (!with.TryGetValue(input.Name, out var raw) || raw is null)
                continue;

            var value = Coerce(raw, input.PropertyType, descriptor.TypeName, input.Name);
            input.Property.SetValue(step, value);
        }

        return step;
    }

    private static object? Coerce(object raw, Type target, string stepName, string inputName)
    {
        if (target == typeof(string))
            return raw.ToString();

        if (TryStringCollection(raw, target, out var collection))
            return collection;

        if (TryStringDictionary(raw, target, out var dictionary))
            return dictionary;

        var converter = TypeDescriptor.GetConverter(target);
        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFromInvariantString(raw.ToString() ?? string.Empty);

        throw new RecipeValidationException(
            $"Cannot bind input '{inputName}' of step '{stepName}' to type '{target.Name}'.");
    }

    private static bool TryStringCollection(object raw, Type target, out object? result)
    {
        result = null;

        var elementType = ElementType(target);
        if (elementType != typeof(string))
            return false;

        var items = (raw as IEnumerable ?? new[] { raw })
            .Cast<object?>()
            .Select(x => x?.ToString() ?? string.Empty)
            .ToList();

        if (target.IsArray)
        {
            result = items.ToArray();
            return true;
        }

        // List<string>, IReadOnlyList<string>, IList<string>, IEnumerable<string>, ICollection<string>
        if (target.IsAssignableFrom(typeof(List<string>)))
        {
            result = items;
            return true;
        }

        return false;
    }

    private static Type? ElementType(Type target)
    {
        if (target == typeof(string))
            return null;
        if (target.IsArray)
            return target.GetElementType();
        if (target.IsGenericType)
        {
            var args = target.GetGenericArguments();
            if (args.Length == 1)
                return args[0];
        }
        return null;
    }

    private static bool TryStringDictionary(object raw, Type target, out object? result)
    {
        result = null;

        if (!target.IsAssignableFrom(typeof(Dictionary<string, string>)))
            return false;
        if (raw is not IDictionary source)
            return false;

        var dict = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in source)
            dict[entry.Key?.ToString() ?? string.Empty] = entry.Value?.ToString() ?? string.Empty;

        result = dict;
        return true;
    }
}
