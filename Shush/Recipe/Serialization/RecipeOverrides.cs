using System.Text.Json;

namespace Shush.Recipe.Serialization;

/// <summary>
/// One deployment's changes to its recipe, kept apart from the recipe so the recipe's later versions still reach the
/// deployment: steps turned off, and step inputs given another value. Steps are named by their <see cref="StepSpec.Key"/>.
/// </summary>
public sealed class RecipeOverrides
{
    public const string DisabledReason = "turned off for this deployment";

    /// <summary>Keys of the steps this deployment skips.</summary>
    public List<string> DisabledSteps { get; set; } = [];

    /// <summary>Step key → input name → the value used instead of the recipe's (may hold <c>${...}</c> references).</summary>
    public Dictionary<string, Dictionary<string, string>> Inputs { get; set; } = new();

    public bool IsEmpty => DisabledSteps.Count == 0 && Inputs.All(kv => kv.Value.Count == 0);

    public int Count => DisabledSteps.Count + Inputs.Sum(kv => kv.Value.Count);

    public bool IsDisabled(string stepKey) => DisabledSteps.Contains(stepKey, StringComparer.Ordinal);

    public string? InputOverride(string stepKey, string input) =>
        Inputs.TryGetValue(stepKey, out var inputs) && inputs.TryGetValue(input, out var value) ? value : null;

    public void SetDisabled(string stepKey, bool disabled)
    {
        DisabledSteps.RemoveAll(k => k == stepKey);
        if (disabled)
            DisabledSteps.Add(stepKey);
    }

    /// <summary>Sets (or, with null, clears) an input's override.</summary>
    public void SetInput(string stepKey, string input, string? value)
    {
        if (value is null)
        {
            if (Inputs.TryGetValue(stepKey, out var existing))
            {
                existing.Remove(input);
                if (existing.Count == 0)
                    Inputs.Remove(stepKey);
            }
            return;
        }

        if (!Inputs.TryGetValue(stepKey, out var inputs))
            Inputs[stepKey] = inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        inputs[input] = value;
    }

    /// <summary>Overrides that no longer match a step (or, for inputs, a step that still exists) of <paramref name="document"/>.</summary>
    public IReadOnlyList<string> Stale(RecipeDocument document)
    {
        var keys = document.Steps.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        return DisabledSteps.Where(k => !keys.Contains(k)).Select(k => $"Turned-off step '{k}'")
            .Concat(Inputs.Keys.Where(k => !keys.Contains(k)).Select(k => $"Changed inputs of step '{k}'"))
            .ToList();
    }

    /// <summary>
    /// A copy of <paramref name="document"/> with these overrides applied. The original isn't changed. Overrides for
    /// steps the document no longer has are ignored (see <see cref="Stale"/>).
    /// </summary>
    public RecipeDocument Apply(RecipeDocument document)
    {
        var copy = YamlRecipeSerializer.Deserialize(YamlRecipeSerializer.Serialize(document));
        if (IsEmpty)
            return copy;

        foreach (var step in copy.Steps)
        {
            if (IsDisabled(step.Key) && step.IsEnabled)
            {
                step.Enabled = false;
                step.DisabledReason = DisabledReason;
            }

            if (Inputs.TryGetValue(step.Key, out var inputs))
                foreach (var (name, value) in inputs)
                    step.With[name] = value;
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
