using System.Text.RegularExpressions;

namespace Shush.Recipe.Serialization;

public enum PropertyEditorKind
{
    Text,
    Dropdown,
    Collection,
}

/// <summary>A single editable recipe parameter, projected from a <see cref="ParamDecl"/> for the UI.</summary>
public sealed class RecipeField
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required PropertyEditorKind Kind { get; init; }
    public string? Default { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>
/// Projects a <see cref="RecipeDocument"/>'s declared params into UI form fields. Replaces the
/// old reflection-over-recipe-properties binder — params are now data, not CLR properties.
/// </summary>
public static partial class RecipeFormBinder
{
    public static List<RecipeField> GetFields(RecipeDocument document) =>
        document.Params
            .Select(kv => new RecipeField
            {
                Name = kv.Key,
                Label = string.IsNullOrWhiteSpace(kv.Value.Label) ? Humanize(kv.Key) : kv.Value.Label!,
                Kind = kv.Value.Type switch
                {
                    ParamType.Dropdown => PropertyEditorKind.Dropdown,
                    ParamType.Collection => PropertyEditorKind.Collection,
                    _ => PropertyEditorKind.Text,
                },
                Default = kv.Value.Default,
                Options = kv.Value.Options ?? [],
            })
            .ToList();

    private static string Humanize(string name)
    {
        var spaced = SpacingPattern().Replace(name, " $1");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex SpacingPattern();
}
