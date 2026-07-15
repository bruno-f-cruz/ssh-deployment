namespace Shush.Recipe.Serialization;

public sealed class RecipeValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public RecipeValidationException(string error)
        : this([error])
    {
    }

    public RecipeValidationException(IReadOnlyList<string> errors)
        : base(Compose(errors))
    {
        Errors = errors;
    }

    private static string Compose(IReadOnlyList<string> errors) =>
        errors.Count == 1
            ? errors[0]
            : $"Recipe is invalid ({errors.Count} errors):{Environment.NewLine}" +
              string.Join(Environment.NewLine, errors.Select(e => $"  - {e}"));
}
