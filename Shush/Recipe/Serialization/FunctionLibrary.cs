namespace Shush.Recipe.Serialization;

/// <summary>
/// The set of <c>${fn(...)}</c> helpers a recipe can call. Functions take positional
/// string arguments and return a string.
/// </summary>
public sealed class FunctionLibrary
{
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<string>, string>> _functions;

    public FunctionLibrary(IReadOnlyDictionary<string, Func<IReadOnlyList<string>, string>> functions)
    {
        _functions = functions;
    }

    /// <summary>A library with no functions — used where only references are expected.</summary>
    public static FunctionLibrary Empty { get; } =
        new(new Dictionary<string, Func<IReadOnlyList<string>, string>>(StringComparer.OrdinalIgnoreCase));

    public bool Contains(string name) => _functions.ContainsKey(name);

    public string Invoke(string name, IReadOnlyList<string> args) =>
        _functions.TryGetValue(name, out var fn)
            ? fn(args)
            : throw new RecipeValidationException($"Unknown function '{name}'.");
}
