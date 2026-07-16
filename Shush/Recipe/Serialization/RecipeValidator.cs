using System.Collections;

namespace Shush.Recipe.Serialization;

/// <summary>
/// "Compiles" a <see cref="RecipeDocument"/> before it runs: unknown step types, unknown or
/// missing inputs, unknown/forward references, unknown functions, and duplicate step ids all
/// surface here — before any machine is touched.
/// </summary>
public sealed class RecipeValidator
{
    private readonly StepRegistry _registry;
    private readonly FunctionLibrary _functions;

    public RecipeValidator(StepRegistry registry, FunctionLibrary functions)
    {
        _registry = registry;
        _functions = functions;
    }

    public void Validate(RecipeDocument document)
    {
        var errors = new List<string>();

        // vars resolve before any step runs, so they may reference params + functions only.
        foreach (var (name, value) in document.Vars)
            ValidateString(value, errors, document, earlierSteps: null, context: $"vars.{name}");

        // Step ids referenceable so far, mapped to their descriptor (null when the type is unknown).
        var earlierSteps = new Dictionary<string, StepDescriptor?>(StringComparer.Ordinal);

        for (var i = 0; i < document.Steps.Count; i++)
        {
            var step = document.Steps[i];
            var label = step.Id is null ? $"steps[{i}] ({step.Type})" : $"step '{step.Id}'";

            if (step.Id is not null && earlierSteps.ContainsKey(step.Id))
                errors.Add($"Duplicate step id '{step.Id}'.");

            _registry.TryGet(step.Type, out var descriptor);
            if (descriptor is null)
                errors.Add($"{label}: unknown step type '{step.Type}'.");
            else
                ValidateInputs(step, descriptor, errors, label);

            foreach (var value in step.With.Values)
                ValidateValue(value, errors, document, earlierSteps, label);

            if (step.Id is not null)
                earlierSteps[step.Id] = descriptor;
        }

        if (errors.Count > 0)
            throw new RecipeValidationException(errors);
    }

    private static void ValidateInputs(StepSpec step, StepDescriptor descriptor, List<string> errors, string label)
    {
        var inputNames = descriptor.Inputs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in step.With.Keys)
            if (!inputNames.Contains(key))
                errors.Add($"{label}: no input named '{key}'.");

        foreach (var input in descriptor.Inputs)
            if (input.Required && !step.With.ContainsKey(input.Name))
                errors.Add($"{label}: missing required input '{input.Name}'.");
    }

    private void ValidateValue(
        object? value, List<string> errors, RecipeDocument document,
        IReadOnlyDictionary<string, StepDescriptor?> earlierSteps, string context)
    {
        switch (value)
        {
            case string s:
                ValidateString(s, errors, document, earlierSteps, context);
                break;
            case IDictionary dict:
                foreach (DictionaryEntry entry in dict)
                    ValidateValue(entry.Value, errors, document, earlierSteps, context);
                break;
            case IEnumerable list:
                foreach (var item in list)
                    ValidateValue(item, errors, document, earlierSteps, context);
                break;
        }
    }

    private void ValidateString(
        string value, List<string> errors, RecipeDocument document,
        IReadOnlyDictionary<string, StepDescriptor?>? earlierSteps, string context)
    {
        IReadOnlyList<ReferenceResolver.ReferenceToken> tokens;
        try
        {
            tokens = ReferenceResolver.ParseTokens(value);
        }
        catch (RecipeValidationException ex)
        {
            errors.AddRange(ex.Errors.Select(e => $"{context}: {e}"));
            return;
        }

        var inVars = earlierSteps is null;

        foreach (var token in tokens)
        {
            if (token.IsFunction)
            {
                if (!_functions.Contains(token.Name))
                    errors.Add($"{context}: unknown function '{token.Name}'.");
                continue;
            }

            switch (token.Namespace)
            {
                case ResolutionScope.ParamsNamespace:
                    if (!document.Params.ContainsKey(token.Member))
                        errors.Add($"{context}: unknown param '{token.Member}'.");
                    break;

                case ResolutionScope.VarsNamespace:
                    if (inVars)
                        errors.Add($"{context}: a var cannot reference another var ('{token.Member}').");
                    else if (!document.Vars.ContainsKey(token.Member))
                        errors.Add($"{context}: unknown var '{token.Member}'.");
                    break;

                default:
                    if (inVars)
                        errors.Add($"{context}: a var cannot reference step output '{token.Namespace}.{token.Member}'.");
                    else if (!earlierSteps!.TryGetValue(token.Namespace, out var stepDescriptor))
                        errors.Add($"{context}: unknown or forward reference to step '{token.Namespace}'.");
                    else if (stepDescriptor is not null && stepDescriptor.Outputs.All(o => o.Name != token.Member))
                        errors.Add($"{context}: step '{token.Namespace}' has no output '{token.Member}'.");
                    break;
            }
        }
    }
}
