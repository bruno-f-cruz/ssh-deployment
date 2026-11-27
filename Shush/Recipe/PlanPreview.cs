using System.Collections;
using Shush.Recipe.Serialization;

namespace Shush.Recipe;

/// <summary>One step as a dry run sees it: whether it would run, and its inputs with every reference resolved.</summary>
/// <param name="Inputs">Resolved inputs, flattened to text (lists one item per line, maps as key: value). Empty when skipped.</param>
public sealed record PreviewStep(string Name, bool Runs, string? SkipReason, IReadOnlyList<KeyValuePair<string, string>> Inputs);

/// <summary>A machine's whole plan, or why it can't be built.</summary>
public sealed record MachinePreview(string Machine, IReadOnlyList<PreviewStep> Steps, string? Error);

/// <summary>
/// Resolves a recipe's plan for one machine without running anything. Outputs a step only knows after running
/// (e.g. a checkout's commit) resolve to empty; outputs computed from inputs (e.g. a clone's path) are exact.
/// </summary>
public static class PlanPreview
{
    /// <param name="mask">Applied to every resolved value, e.g. to hide secrets.</param>
    public static MachinePreview For(string machine, IRecipe recipe, MachineRunSettings settings, Func<string, string>? mask = null)
    {
        mask ??= v => v;
        var steps = new List<PreviewStep>();

        if (settings.Environment.Count > 0)
            steps.Add(new PreviewStep(
                RecipeRunner.EnvironmentStepName, Runs: true, SkipReason: null,
                settings.Environment
                    .Select(e => new KeyValuePair<string, string>(e.Name, e.Secret ? "••••••••" : mask(e.Value)))
                    .ToList()));

        try
        {
            var plan = recipe.CreatePlan(new PlanContext(machine, settings.Variables));
            foreach (var planned in plan.Steps())
            {
                if (!planned.Enabled)
                {
                    steps.Add(new PreviewStep(planned.DisplayName, Runs: false, planned.SkipReason, []));
                    continue;
                }

                var inputs = (planned.ResolvedInputs ?? new Dictionary<string, object?>())
                    .Select(kv => new KeyValuePair<string, string>(kv.Key, mask(Flatten(kv.Value))))
                    .ToList();
                steps.Add(new PreviewStep(planned.DisplayName, Runs: true, SkipReason: null, inputs));
                planned.CaptureOutputs();
            }

            return new MachinePreview(machine, steps, Error: null);
        }
        catch (RecipeValidationException ex)
        {
            return new MachinePreview(machine, steps, string.Join(" ", ex.Errors));
        }
        catch (Exception ex)
        {
            return new MachinePreview(machine, steps, ex.Message);
        }
    }

    private static string Flatten(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IDictionary map => string.Join("\n", map.Cast<DictionaryEntry>().Select(e => $"{e.Key}: {Flatten(e.Value)}")),
        IEnumerable list => string.Join("\n", list.Cast<object?>().Select(Flatten)),
        _ => value.ToString() ?? string.Empty,
    };
}
