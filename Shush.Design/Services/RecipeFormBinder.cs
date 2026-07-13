using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Shush.Recipe;

namespace Shush.Design.Services;

public enum PropertyEditorKind
{
    Text,
    Dropdown,
    Collection,
}

public sealed class RecipeProperty
{
    public required PropertyInfo Info { get; init; }
    public required string Label { get; init; }
    public required PropertyEditorKind Kind { get; init; }

    public Type ElementType =>
        Info.PropertyType.IsGenericType
            ? Info.PropertyType.GetGenericArguments()[0]
            : Info.PropertyType.GetElementType() ?? typeof(string);

    public string GetValueAsString(IRecipe recipe)
    {
        var value = Info.GetValue(recipe);
        if (value is null) return string.Empty;

        var converter = TypeDescriptor.GetConverter(Info.PropertyType);
        return converter.CanConvertTo(typeof(string))
            ? converter.ConvertToString(value) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    public void SetValueFromString(IRecipe recipe, string text)
    {
        var converter = TypeDescriptor.GetConverter(Info.PropertyType);
        Info.SetValue(recipe, converter.ConvertFromString(text));
    }

    public IReadOnlyList<string> GetDropdownOptions() => Enum.GetNames(Info.PropertyType);

    public IList GetOrCreateList(IRecipe recipe)
    {
        var value = Info.GetValue(recipe) as IList;
        if (value is not null) return value;

        value = (IList)Activator.CreateInstance(Info.PropertyType)!;
        Info.SetValue(recipe, value);
        return value;
    }

    public string ConvertElementToString(object element)
    {
        var converter = TypeDescriptor.GetConverter(ElementType);
        return converter.CanConvertTo(typeof(string))
            ? converter.ConvertToString(element) ?? string.Empty
            : element.ToString() ?? string.Empty;
    }

    public object? ConvertElementFromString(string text) =>
        TypeDescriptor.GetConverter(ElementType).ConvertFromString(text);
}

public static class RecipeFormBinder
{
    public static List<RecipeProperty> GetSettableProperties(IRecipe recipe) =>
        recipe.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Select(p => new RecipeProperty
            {
                Info = p,
                Label = Humanize(p.Name),
                Kind = ResolveKind(p.PropertyType),
            })
            .ToList();

    private static PropertyEditorKind ResolveKind(Type propertyType)
    {
        if (propertyType.IsEnum)
            return PropertyEditorKind.Dropdown;

        if (propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType))
            return PropertyEditorKind.Collection;

        return PropertyEditorKind.Text;
    }

    private static string Humanize(string name) => Regex.Replace(name, "(?<!^)([A-Z])", " $1");
}
