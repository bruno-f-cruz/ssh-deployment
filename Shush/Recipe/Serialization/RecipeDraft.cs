namespace Shush.Recipe.Serialization;

/// <summary>
/// A recipe being edited: the structured document and its YAML, kept in step. Nothing is stored until the page
/// saves <see cref="ToYaml"/> as a new version.
/// </summary>
public sealed class RecipeDraft
{
    private readonly RecipeValidator _validator;

    public RecipeDraft(RecipeDocument document, RecipeValidator validator)
    {
        Document = document;
        _validator = validator;
    }

    public RecipeDocument Document { get; private set; }

    public static RecipeDraft FromYaml(string yaml, RecipeValidator validator) =>
        new(YamlRecipeSerializer.Deserialize(yaml), validator);

    public string ToYaml() => YamlRecipeSerializer.Serialize(Document);

    public IReadOnlyList<string> Validate()
    {
        try
        {
            _validator.Validate(Document);
            return [];
        }
        catch (RecipeValidationException ex)
        {
            return ex.Errors;
        }
    }

    /// <summary>Validates <paramref name="steps"/> in place of the current ones, without changing the draft (for reorders).</summary>
    public IReadOnlyList<string> ValidateWith(List<StepSpec> steps)
    {
        var probe = new RecipeDocument
        {
            Name = Document.Name,
            Params = Document.Params,
            Vars = Document.Vars,
            SharedAccess = Document.SharedAccess,
            Steps = steps,
        };
        try
        {
            _validator.Validate(probe);
            return [];
        }
        catch (RecipeValidationException ex)
        {
            return ex.Errors;
        }
    }

    public bool TryApplyRawYaml(string yaml, out IReadOnlyList<string> errors)
    {
        RecipeDocument document;
        try
        {
            document = YamlRecipeSerializer.Deserialize(yaml);
        }
        catch (Exception ex)
        {
            errors = [$"YAML parse error: {ex.Message}"];
            return false;
        }

        try
        {
            _validator.Validate(document);
        }
        catch (RecipeValidationException ex)
        {
            errors = ex.Errors;
            return false;
        }

        Document = document;
        errors = [];
        return true;
    }
}
