namespace Shush.Recipe.Serialization;

/// <summary>
/// Evaluates a step's <c>when:</c> after its <c>${...}</c> tokens are resolved. The grammar is deliberately small:
/// <list type="bullet">
/// <item><c>a == b</c> or <c>a != b</c>: each side trimmed, and unquoted when wrapped in double quotes; compared
/// ignoring case (so <c>Dev</c> equals <c>dev</c>).</item>
/// <item>a single value: true for <c>true</c>, <c>yes</c>, <c>1</c>, <c>on</c>; false for <c>false</c>, <c>no</c>,
/// <c>0</c>, <c>off</c> and empty.</item>
/// </list>
/// </summary>
public static class ConditionEvaluator
{
    private static readonly string[] TrueWords = ["true", "yes", "1", "on"];
    private static readonly string[] FalseWords = ["false", "no", "0", "off", ""];

    public static bool Evaluate(string resolved)
    {
        if (TrySplit(resolved, out var left, out var op, out var right))
        {
            var equal = string.Equals(Unquote(left), Unquote(right), StringComparison.OrdinalIgnoreCase);
            return op == "==" ? equal : !equal;
        }

        var value = Unquote(resolved);
        if (TrueWords.Contains(value, StringComparer.OrdinalIgnoreCase))
            return true;
        if (FalseWords.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        throw new RecipeValidationException(
            $"when: '{resolved}' is neither a comparison (a == b, a != b) nor a true/false value.");
    }

    /// <summary>
    /// Checks a <c>when:</c> before resolution: every <c>${...}</c> token is replaced by a placeholder, so only the
    /// shape is checked (a comparison, or a single token or true/false word). Returns the problem, or null.
    /// </summary>
    public static string? CheckSyntax(string template)
    {
        var shape = System.Text.RegularExpressions.Regex.Replace(template, @"\$\{\s*(.*?)\s*\}", "x");
        if (TrySplit(shape, out _, out _, out _))
            return null;

        var value = Unquote(shape);
        if (template.Contains("${") || TrueWords.Contains(value, StringComparer.OrdinalIgnoreCase)
            || FalseWords.Contains(value, StringComparer.OrdinalIgnoreCase))
            return null;

        return $"'{template}' is neither a comparison (a == b, a != b) nor a true/false value.";
    }

    private static bool TrySplit(string expression, out string left, out string op, out string right)
    {
        var inQuotes = false;
        for (var i = 0; i < expression.Length - 1; i++)
        {
            var c = expression[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes || expression[i + 1] != '=' || c is not ('=' or '!'))
                continue;

            left = expression[..i];
            op = c == '=' ? "==" : "!=";
            right = expression[(i + 2)..];
            return true;
        }

        left = op = right = string.Empty;
        return false;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
    }
}
