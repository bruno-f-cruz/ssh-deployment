namespace Shush.Recipe.Serialization;

/// <summary>
/// An <see cref="IRecipe"/> backed by a <see cref="RecipeDocument"/>. Each <see cref="CreatePlan"/>
/// call builds a fresh scope (params from supplied values/defaults, vars resolved once) and lazily
/// resolves + binds each step against the outputs captured from earlier steps.
/// </summary>
public sealed class SerializedRecipe : IRecipe
{
    private readonly RecipeDocument _document;
    private readonly StepRegistry _registry;
    private readonly FunctionLibrary _functions;
    private readonly IReadOnlyDictionary<string, string> _paramValues;

    public SerializedRecipe(
        RecipeDocument document,
        StepRegistry registry,
        FunctionLibrary functions,
        IReadOnlyDictionary<string, string> paramValues)
    {
        _document = document;
        _registry = registry;
        _functions = functions;
        _paramValues = paramValues;
    }

    public RecipeDocument Document => _document;

    public string Name => _document.Name;

    public IReadOnlyList<string> StepNames =>
        _document.Steps.Select(s => s.Id ?? s.Type).ToList();

    public IRecipeExecutionPlan CreatePlan()
    {
        var scope = new ResolutionScope();

        foreach (var (name, decl) in _document.Params)
        {
            string value;
            if (_paramValues.TryGetValue(name, out var supplied) && !string.IsNullOrEmpty(supplied))
                value = supplied;
            else if (decl.Default is not null)
                value = decl.Default;
            else
                throw new RecipeValidationException($"Parameter '{name}' is required but no value was provided.");

            scope.SetParam(name, value);
        }

        // Vars resolve once, before any step runs (params + functions only, enforced by validation).
        foreach (var (name, expr) in _document.Vars)
            scope.SetVar(name, ReferenceResolver.ResolveString(expr, scope, _functions));

        return new Plan(_document, _registry, _functions, scope);
    }

    private sealed class Plan : IRecipeExecutionPlan
    {
        private readonly RecipeDocument _document;
        private readonly StepRegistry _registry;
        private readonly FunctionLibrary _functions;
        private readonly ResolutionScope _scope;

        public Plan(RecipeDocument document, StepRegistry registry, FunctionLibrary functions, ResolutionScope scope)
        {
            _document = document;
            _registry = registry;
            _functions = functions;
            _scope = scope;
        }

        public IEnumerable<PlannedStep> Steps()
        {
            foreach (var spec in _document.Steps)
            {
                var descriptor = _registry.Get(spec.Type);
                var resolvedWith = ReferenceResolver.ResolveWith(spec.With, _scope, _functions);
                var step = StepBinder.Bind(descriptor, resolvedWith);
                var id = spec.Id;

                void CaptureOutputs()
                {
                    if (id is null)
                        return;

                    var outputs = new Dictionary<string, string>();
                    foreach (var output in descriptor.Outputs)
                        outputs[output.Name] = output.Property.GetValue(step)?.ToString() ?? string.Empty;

                    _scope.SetStepOutputs(id, outputs);
                }

                yield return new PlannedStep(spec.Id ?? spec.Type, id, step, CaptureOutputs);
            }
        }
    }
}
