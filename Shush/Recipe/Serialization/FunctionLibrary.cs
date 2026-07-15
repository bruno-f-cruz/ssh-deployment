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

    /// <summary>The functions available to recipes.</summary>
    public static FunctionLibrary Default { get; } =
        new(new Dictionary<string, Func<IReadOnlyList<string>, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["random.time"] = RandomTime,
            ["guid"] = _ => Guid.NewGuid().ToString(),
            ["env"] = args => Environment.GetEnvironmentVariable(Arg(args, 0, "env")) ?? string.Empty,
        });

    public bool Contains(string name) => _functions.ContainsKey(name);

    public string Invoke(string name, IReadOnlyList<string> args) =>
        _functions.TryGetValue(name, out var fn)
            ? fn(args)
            : throw new RecipeValidationException($"Unknown function '{name}'.");

    /// <summary>random.time("HH:mm", "HH:mm") → a random "HH:mm:00" within the inclusive range.</summary>
    private static string RandomTime(IReadOnlyList<string> args)
    {
        var from = ParseMinutes(Arg(args, 0, "random.time"));
        var to = ParseMinutes(Arg(args, 1, "random.time"));
        if (to < from)
            throw new RecipeValidationException("random.time: the second argument must not be earlier than the first.");

        var minutes = Random.Shared.Next(from, to + 1);
        return $"{minutes / 60:D2}:{minutes % 60:D2}:00";
    }

    private static int ParseMinutes(string value)
    {
        if (!TimeOnly.TryParse(value, out var time))
            throw new RecipeValidationException($"random.time: '{value}' is not a valid HH:mm time.");
        return time.Hour * 60 + time.Minute;
    }

    private static string Arg(IReadOnlyList<string> args, int index, string function) =>
        index < args.Count
            ? args[index]
            : throw new RecipeValidationException($"{function}: expected at least {index + 1} argument(s).");
}
