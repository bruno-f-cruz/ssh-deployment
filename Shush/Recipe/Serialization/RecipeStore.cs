namespace Shush.Recipe.Serialization;

/// <summary>
/// Discovers YAML recipe documents from one or more directories. Later directories override
/// earlier ones by recipe name (so a user directory overlays the shipped base directory).
/// Each file is parsed and validated on load; a bad file surfaces with its path.
/// </summary>
public sealed class RecipeStore
{
    private readonly StepRegistry _registry;
    private readonly FunctionLibrary _functions;
    private readonly RecipeValidator _validator;

    public RecipeStore(StepRegistry registry, FunctionLibrary functions)
    {
        _registry = registry;
        _functions = functions;
        _validator = new RecipeValidator(registry, functions);
    }

    public List<IRecipe> Discover(IReadOnlyList<string> directories)
    {
        var byName = new Dictionary<string, IRecipe>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.yml").OrderBy(f => f, StringComparer.Ordinal))
            {
                var recipe = Load(file);
                byName[recipe.Name] = recipe;
            }
        }

        return byName.Values.ToList();
    }

    public SerializedRecipe Load(string file)
    {
        RecipeDocument document;
        try
        {
            document = YamlRecipeSerializer.Deserialize(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is not RecipeValidationException)
        {
            throw new RecipeValidationException($"Failed to parse recipe '{file}': {ex.Message}");
        }

        _validator.Validate(document);
        return new SerializedRecipe(document, _registry, _functions, new Dictionary<string, string>());
    }
}
