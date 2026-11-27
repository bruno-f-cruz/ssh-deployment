namespace Shush.Recipe.Serialization;

/// <summary>
/// An <see cref="IRecipe"/> backed by a <see cref="RecipeDocument"/>. Each <see cref="CreatePlan"/>
/// call builds a fresh scope (the machine's group variables, params from supplied values/defaults, vars
/// resolved once) and lazily resolves + binds each step against the outputs captured from earlier steps.
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
        _document.Steps.Select(s => s.DisplayName).ToList();

    public IRecipeExecutionPlan CreatePlan(PlanContext? context = null)
    {
        var scope = new ResolutionScope();

        foreach (var (name, value) in context?.GroupVariables ?? new Dictionary<string, string>())
            scope.SetGroupVariable(name, value);

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

        // Vars resolve once, before any step runs (params, group variables and functions only, enforced by validation).
        foreach (var (name, expr) in _document.Vars)
            scope.SetVar(name, ReferenceResolver.ResolveString(expr, scope, _functions));

        return new Plan(_document, _registry, _functions, scope, ResolveSharedAccess(scope));
    }

    private IReadOnlyList<SharedAccessGrant> ResolveSharedAccess(ResolutionScope scope)
    {
        if (_document.SharedAccess is not { } spec)
            return [];

        var grants = spec.ToGrants(p => ReferenceResolver.ResolveString(p, scope, _functions));

        var errors = grants.SelectMany(g => g.Validate()).ToList();
        if (errors.Count > 0)
            throw new RecipeValidationException(errors);

        return grants;
    }

    private static readonly IReadOnlyDictionary<string, string> NoOutputs = new Dictionary<string, string>();

    private sealed class Plan : IRecipeExecutionPlan
    {
        private readonly RecipeDocument _document;
        private readonly StepRegistry _registry;
        private readonly FunctionLibrary _functions;
        private readonly ResolutionScope _scope;

        public Plan(
            RecipeDocument document, StepRegistry registry, FunctionLibrary functions, ResolutionScope scope,
            IReadOnlyList<SharedAccessGrant> sharedAccess)
        {
            _document = document;
            _registry = registry;
            _functions = functions;
            _scope = scope;
            SharedAccess = sharedAccess;
        }

        public IReadOnlyList<SharedAccessGrant> SharedAccess { get; }

        public IEnumerable<PlannedStep> Steps()
        {
            foreach (var spec in _document.Steps)
            {
                var displayName = spec.DisplayName;

                // A step that doesn't run is never resolved or bound — it can't reference outputs that
                // will never be captured, and there's nothing useful to construct.
                if (!spec.IsEnabled)
                {
                    yield return new PlannedStep(displayName, spec.Id, Step: null, Enabled: false, () => NoOutputs, spec.DisabledReason ?? "disabled");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(spec.When))
                {
                    var condition = ReferenceResolver.ResolveString(spec.When, _scope, _functions);
                    if (!ConditionEvaluator.Evaluate(condition))
                    {
                        yield return new PlannedStep(
                            displayName, spec.Id, Step: null, Enabled: false, () => NoOutputs, $"when: {spec.When.Trim()} is false");
                        continue;
                    }
                }

                var descriptor = _registry.Get(spec.Type);
                var resolvedWith = ReferenceResolver.ResolveWith(spec.With, _scope, _functions);
                var step = StepBinder.Bind(descriptor, resolvedWith);
                var id = spec.Id;

                IReadOnlyDictionary<string, string> CaptureOutputs()
                {
                    var outputs = new Dictionary<string, string>();
                    foreach (var output in descriptor.Outputs)
                        outputs[output.Name] = output.Property.GetValue(step)?.ToString() ?? string.Empty;

                    if (id is not null)
                        _scope.SetStepOutputs(id, outputs);

                    return outputs;
                }

                yield return new PlannedStep(displayName, id, step, Enabled: true, CaptureOutputs, ResolvedInputs: resolvedWith);
            }
        }
    }
}
