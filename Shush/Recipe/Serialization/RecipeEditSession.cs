namespace Shush.Recipe.Serialization;

/// <summary>
/// A working copy of a recipe being edited in the UI. Edits to a built-in recipe are saved as a
/// user-directory copy (the base file stays pristine); "reset to default" deletes the user copy.
/// </summary>
public sealed class RecipeEditSession
{
    private readonly string _baseDir;
    private readonly string _userDir;
    private readonly RecipeValidator _validator;

    public string RecipeName { get; }
    public RecipeDocument Document { get; private set; }

    private RecipeEditSession(string name, RecipeDocument document, string baseDir, string userDir, RecipeValidator validator)
    {
        RecipeName = name;
        Document = document;
        _baseDir = baseDir;
        _userDir = userDir;
        _validator = validator;
    }

    public static RecipeEditSession OpenFromBase(
        string name, string baseDir, string userDir, StepRegistry registry, FunctionLibrary functions)
    {
        var document = LoadByName(name, userDir) ?? LoadByName(name, baseDir)
            ?? throw new RecipeValidationException($"Recipe '{name}' was not found.");

        return new RecipeEditSession(name, document, baseDir, userDir, new RecipeValidator(registry, functions));
    }

    /// <summary>
    /// Starts a brand-new recipe — nothing is written to disk until <see cref="Save"/> is called.
    /// Callers should check <see cref="Exists"/> first to avoid silently shadowing another recipe
    /// of the same name.
    /// </summary>
    public static RecipeEditSession CreateNew(
        string name, string baseDir, string userDir, StepRegistry registry, FunctionLibrary functions) =>
        new(name, new RecipeDocument { Name = name }, baseDir, userDir, new RecipeValidator(registry, functions));

    /// <summary>True if a recipe (built-in or user) already claims this name.</summary>
    public static bool Exists(string name, string baseDir, string userDir) =>
        LoadByName(name, userDir) is not null || LoadByName(name, baseDir) is not null;

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

    public void Save()
    {
        Directory.CreateDirectory(_userDir);
        File.WriteAllText(UserPath(), ToYaml());
    }

    public void ResetToDefault()
    {
        var userPath = UserPath();
        if (File.Exists(userPath))
            File.Delete(userPath);

        Document = LoadByName(RecipeName, _baseDir)
            ?? throw new RecipeValidationException($"No base recipe named '{RecipeName}' to reset to.");
    }

    public bool HasUserCopy() => File.Exists(UserPath());

    /// <summary>False for a recipe created via <see cref="CreateNew"/> — there's nothing to reset to.</summary>
    public bool HasBaseCopy() => LoadByName(RecipeName, _baseDir) is not null;

    private string UserPath() =>
        Path.Combine(_userDir, string.Concat(RecipeName.Split(Path.GetInvalidFileNameChars())) + ".yml");

    private static RecipeDocument? LoadByName(string name, string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        foreach (var file in Directory.EnumerateFiles(directory, "*.yml"))
        {
            RecipeDocument document;
            try
            {
                document = YamlRecipeSerializer.Deserialize(File.ReadAllText(file));
            }
            catch
            {
                continue;
            }

            if (string.Equals(document.Name, name, StringComparison.OrdinalIgnoreCase))
                return document;
        }

        return null;
    }
}
