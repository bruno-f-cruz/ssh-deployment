using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace Shush.Recipe.Serialization;

/// <summary>
/// Expands <c>${...}</c> tokens in recipe input strings. A token is either a function
/// call (<c>name(args)</c>) or a dotted reference (<c>namespace.name</c>) resolved against
/// a <see cref="ResolutionScope"/>.
/// </summary>
public static partial class ReferenceResolver
{
    public static string ResolveString(string input, ResolutionScope scope, FunctionLibrary functions) =>
        TokenPattern().Replace(input, m => ResolveExpression(m.Groups[1].Value.Trim(), scope, functions));

    /// <summary>Recursively resolves every string in a <c>with:</c> map (scalars, lists, nested maps).</summary>
    public static Dictionary<string, object?> ResolveWith(
        IReadOnlyDictionary<string, object?> with, ResolutionScope scope, FunctionLibrary functions)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in with)
            result[key] = ResolveValue(value, scope, functions);
        return result;
    }

    private static object? ResolveValue(object? value, ResolutionScope scope, FunctionLibrary functions) =>
        value switch
        {
            string s => ResolveString(s, scope, functions),
            IDictionary dict => ResolveDictionary(dict, scope, functions),
            IEnumerable list => list.Cast<object?>().Select(v => ResolveValue(v, scope, functions)).ToList(),
            _ => value,
        };

    private static Dictionary<object, object?> ResolveDictionary(IDictionary dict, ResolutionScope scope, FunctionLibrary functions)
    {
        var result = new Dictionary<object, object?>();
        foreach (DictionaryEntry entry in dict)
            result[entry.Key] = ResolveValue(entry.Value, scope, functions);
        return result;
    }

    private static string ResolveExpression(string expr, ResolutionScope scope, FunctionLibrary functions)
    {
        var open = expr.IndexOf('(');
        if (open >= 0 && expr.EndsWith(')'))
        {
            var name = expr[..open].Trim();
            var args = ParseArguments(expr[(open + 1)..^1]);
            return functions.Invoke(name, args);
        }

        var dot = expr.IndexOf('.');
        if (dot <= 0 || dot == expr.Length - 1)
            throw new RecipeValidationException($"Invalid reference '${{{expr}}}': expected 'namespace.name'.");

        var first = expr[..dot];
        var rest = expr[(dot + 1)..];
        if (scope.TryLookup(first, rest, out var resolved))
            return resolved;

        throw new RecipeValidationException($"Unknown reference '${{{expr}}}'.");
    }

    /// <summary>Splits a function argument list on top-level commas and strips surrounding double quotes.</summary>
    private static IReadOnlyList<string> ParseArguments(string argsText)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            return [];

        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in argsText)
        {
            switch (c)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inQuotes:
                    args.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        args.Add(current.ToString().Trim());
        return args;
    }

    [GeneratedRegex(@"\$\{\s*(.*?)\s*\}")]
    private static partial Regex TokenPattern();
}
