using System.Text.Json;

namespace Shush.Recipe.Serialization;

/// <summary>
/// One deployment's changes to its recipe: the steps it turns off. Kept apart from the recipe so the recipe's later
/// versions still reach the deployment. Steps are named by their <see cref="StepSpec.Key"/>. A value that should differ
/// between deployments belongs in a recipe parameter, not here.
/// </summary>
public sealed class RecipeOverrides
{
    public const string DisabledReason = "turned off for this deployment";

    /// <summary>Keys of the steps this deployment skips.</summary>
    public List<string> DisabledSteps { get; set; } = [];

    public bool IsEmpty => DisabledSteps.Count == 0;

    public int Count => DisabledSteps.Count;

    public bool IsDisabled(string stepKey) => DisabledSteps.Contains(stepKey, StringComparer.Ordinal);

    public void SetDisabled(string stepKey, bool disabled)
    {
        DisabledSteps.RemoveAll(k => k == stepKey);
        if (disabled)
            DisabledSteps.Add(stepKey);
    }

    /// <summary>Turned-off steps that no longer match a step of <paramref name="document"/> (renamed or removed).</summary>
    public IReadOnlyList<string> Stale(RecipeDocument document)
    {
        var keys = document.Steps.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        return DisabledSteps.Where(k => !keys.Contains(k)).ToList();
    }

    /// <summary>Forgets turned-off steps the recipe no longer has.</summary>
    public void RemoveStale(RecipeDocument document)
    {
        var stale = Stale(document);
        DisabledSteps.RemoveAll(stale.Contains);
    }

    /// <summary>
    /// A copy of <paramref name="document"/> with these steps turned off. The original isn't changed. Steps the document
    /// no longer has are ignored (see <see cref="Stale"/>).
    /// </summary>
    public RecipeDocument Apply(RecipeDocument document)
    {
        var copy = YamlRecipeSerializer.Deserialize(YamlRecipeSerializer.Serialize(document));
        foreach (var step in copy.Steps)
        {
            if (IsDisabled(step.Key) && step.IsEnabled)
            {
                step.Enabled = false;
                step.DisabledReason = DisabledReason;
            }
        }
        return copy;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RecipeOverrides FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new RecipeOverrides();
        try
        {
            return JsonSerializer.Deserialize<RecipeOverrides>(json, JsonOptions) ?? new RecipeOverrides();
        }
        catch (JsonException)
        {
            return new RecipeOverrides();
        }
    }

    public RecipeOverrides Clone() => FromJson(ToJson());
}
