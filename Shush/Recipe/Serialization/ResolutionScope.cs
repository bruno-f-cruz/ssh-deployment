namespace Shush.Recipe.Serialization;

/// <summary>
/// The name scope a <see cref="ReferenceResolver"/> resolves <c>${...}</c> tokens against:
/// recipe params, vars, the machine's group variables, and the outputs captured from earlier steps.
/// </summary>
public sealed class ResolutionScope
{
    public const string ParamsNamespace = "params";
    public const string VarsNamespace = "vars";

    /// <summary>Variables set outside the recipe, per machine: machine override, else group, else global.</summary>
    public const string GroupNamespace = "group";

    private readonly Dictionary<string, string> _params = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _vars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _group = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _stepOutputs = new(StringComparer.Ordinal);

    public void SetParam(string name, string value) => _params[name] = value;

    public void SetVar(string name, string value) => _vars[name] = value;

    public void SetGroupVariable(string name, string value) => _group[name] = value;

    public void SetStepOutputs(string stepId, IReadOnlyDictionary<string, string> outputs) =>
        _stepOutputs[stepId] = new Dictionary<string, string>(outputs, StringComparer.Ordinal);

    public bool TryLookup(string first, string rest, out string value)
    {
        switch (first)
        {
            case ParamsNamespace:
                return _params.TryGetValue(rest, out value!);
            case VarsNamespace:
                return _vars.TryGetValue(rest, out value!);
            case GroupNamespace:
                return _group.TryGetValue(rest, out value!);
            default:
                if (_stepOutputs.TryGetValue(first, out var outputs))
                    return outputs.TryGetValue(rest, out value!);
                value = string.Empty;
                return false;
        }
    }
}
