using Microsoft.EntityFrameworkCore;
using Shush.Recipe.Serialization;

namespace Shush.Data;

public sealed record RecipeSummary(int Id, string Name, bool IsBuiltIn, int Version, DateTime UpdatedAt, bool ShippedUpdateAvailable, int DeploymentCount);

/// <summary>Recipes and their versions. Every save is a new immutable version; the newest is the current one.</summary>
public sealed class RecipeService(IDbContextFactory<ShushDbContext> factory, StepRegistry registry, FunctionLibrary functions)
{
    public RecipeValidator Validator { get; } = new(registry, functions);

    public async Task<List<RecipeSummary>> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployments = await db.Deployments.GroupBy(d => d.RecipeId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var recipes = await db.Recipes
            .Select(r => new
            {
                r.Id, r.Name, r.IsBuiltIn, r.UpdatedAt, r.ShippedHash,
                Current = r.Versions.OrderByDescending(v => v.Number).Select(v => new { v.Number, v.Hash }).First(),
            })
            .ToListAsync();

        return recipes
            .Select(r => new RecipeSummary(
                r.Id, r.Name, r.IsBuiltIn, r.Current.Number, r.UpdatedAt,
                r.IsBuiltIn && r.ShippedHash is not null && r.ShippedHash != r.Current.Hash,
                deployments.GetValueOrDefault(r.Id)))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Recipe?> GetAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Recipe?> FindAsync(string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Name == name);
    }

    public async Task<RecipeVersion> CurrentVersionAsync(int recipeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RecipeVersions.AsNoTracking().Where(v => v.RecipeId == recipeId).OrderByDescending(v => v.Number).FirstAsync();
    }

    public async Task<RecipeVersion?> GetVersionAsync(int versionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RecipeVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId);
    }

    public async Task<List<RecipeVersion>> VersionsAsync(int recipeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RecipeVersions.AsNoTracking().Where(v => v.RecipeId == recipeId).OrderByDescending(v => v.Number).ToListAsync();
    }

    /// <summary>Parses and validates YAML; throws <see cref="RecipeValidationException"/> with every problem.</summary>
    public RecipeDocument Parse(string yaml)
    {
        RecipeDocument document;
        try
        {
            document = YamlRecipeSerializer.Deserialize(yaml);
        }
        catch (Exception ex) when (ex is not RecipeValidationException)
        {
            throw new RecipeValidationException($"YAML parse error: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(document.Name))
            throw new RecipeValidationException("The recipe needs a name.");

        Validator.Validate(document);
        return document;
    }

    /// <summary>Creates a recipe from YAML. Throws when the name is taken.</summary>
    public async Task<Recipe> CreateAsync(string yaml, VersionSource source, string? user)
    {
        var document = Parse(yaml);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Recipes.AnyAsync(r => r.Name == document.Name))
            throw new ShushDataException($"A recipe named '{document.Name}' already exists.");

        var now = Clock.UtcNow();
        var recipe = new Recipe { Name = document.Name, CreatedAt = now, UpdatedAt = now };
        recipe.Versions.Add(NewVersion(1, yaml, source, user, now));
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe;
    }

    /// <summary>
    /// Saves YAML as the recipe's next version (unchanged content saves nothing). The name inside the YAML must stay
    /// the recipe's name; rename with <see cref="RenameAsync"/>.
    /// </summary>
    public async Task<RecipeVersion> SaveAsync(int recipeId, string yaml, VersionSource source, string? user)
    {
        var document = Parse(yaml);
        await using var db = await factory.CreateDbContextAsync();
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId)
            ?? throw new ShushDataException("That recipe no longer exists.");

        if (!string.Equals(document.Name, recipe.Name, StringComparison.OrdinalIgnoreCase))
            throw new ShushDataException($"The YAML names the recipe '{document.Name}', but this is '{recipe.Name}'. Keep the name, or rename the recipe.");

        var current = await db.RecipeVersions.Where(v => v.RecipeId == recipeId).OrderByDescending(v => v.Number).FirstAsync();
        if (current.Hash == Hashing.Of(yaml))
            return current;

        var now = Clock.UtcNow();
        var version = NewVersion(current.Number + 1, yaml, source, user, now);
        version.RecipeId = recipeId;
        db.RecipeVersions.Add(version);
        recipe.UpdatedAt = now;
        await db.SaveChangesAsync();
        return version;
    }

    /// <summary>Imports a YAML file: a new recipe, or a new version of the recipe with that name.</summary>
    public async Task<Recipe> ImportAsync(string yaml, string? user)
    {
        var document = Parse(yaml);
        var existing = await FindAsync(document.Name);
        if (existing is null)
            return await CreateAsync(yaml, VersionSource.Import, user);

        await SaveAsync(existing.Id, yaml, VersionSource.Import, user);
        return existing;
    }

    /// <summary>A built-in recipe goes back to the YAML shipped with the app (as a new version, so nothing is lost).</summary>
    public async Task<RecipeVersion> ResetToShippedAsync(int recipeId, string? user)
    {
        var recipe = await GetAsync(recipeId) ?? throw new ShushDataException("That recipe no longer exists.");
        if (!recipe.IsBuiltIn || recipe.ShippedYaml is null)
            throw new ShushDataException($"'{recipe.Name}' doesn't ship with the app, so there's no default to reset to.");
        return await SaveAsync(recipeId, recipe.ShippedYaml, VersionSource.Reset, user);
    }

    /// <summary>Restores an older version, as a new version.</summary>
    public async Task<RecipeVersion> RestoreAsync(int versionId, string? user)
    {
        var version = await GetVersionAsync(versionId) ?? throw new ShushDataException("That version no longer exists.");
        return await SaveAsync(version.RecipeId, version.Yaml, VersionSource.Restore, user);
    }

    public async Task RenameAsync(int recipeId, string newName, string? user)
    {
        newName = RequireName(newName);

        await using var db = await factory.CreateDbContextAsync();
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId)
            ?? throw new ShushDataException("That recipe no longer exists.");
        if (recipe.IsBuiltIn)
            throw new ShushDataException("A recipe that ships with the app keeps its name.");
        await EnsureNameFreeAsync(db, newName, recipeId);

        var current = await db.RecipeVersions.Where(v => v.RecipeId == recipeId).OrderByDescending(v => v.Number).FirstAsync();
        var document = YamlRecipeSerializer.Deserialize(current.Yaml);
        document.Name = newName;
        var yaml = YamlRecipeSerializer.Serialize(document);

        var now = Clock.UtcNow();
        recipe.Name = newName;
        recipe.UpdatedAt = now;
        var version = NewVersion(current.Number + 1, yaml, VersionSource.Edit, user, now);
        version.RecipeId = recipeId;
        db.RecipeVersions.Add(version);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Copies a recipe's current version into a new recipe of your own, named <paramref name="newName"/> (the name
    /// inside the YAML follows). <paramref name="transform"/> can change the copied document first, e.g. to bake a
    /// deployment's values into it; the name is set again after it. The copy starts at v1 and remembers its source.
    /// </summary>
    public async Task<Recipe> DuplicateAsync(int recipeId, string newName, string? user, Func<RecipeDocument, RecipeDocument>? transform = null)
    {
        newName = RequireName(newName);

        await using var db = await factory.CreateDbContextAsync();
        var source = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipeId)
            ?? throw new ShushDataException("That recipe no longer exists.");
        await EnsureNameFreeAsync(db, newName, exceptId: null);

        var current = await db.RecipeVersions.AsNoTracking().Where(v => v.RecipeId == recipeId).OrderByDescending(v => v.Number).FirstAsync();
        var document = YamlRecipeSerializer.Deserialize(current.Yaml);
        document.Name = newName;
        if (transform is not null)
        {
            document = transform(document);
            document.Name = newName;
        }

        var yaml = YamlRecipeSerializer.Serialize(document);
        Parse(yaml);

        var now = Clock.UtcNow();
        var recipe = new Recipe { Name = newName, CopiedFrom = $"{source.Name} v{current.Number}", CreatedAt = now, UpdatedAt = now };
        recipe.Versions.Add(NewVersion(1, yaml, VersionSource.Duplicate, user, now));
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe;
    }

    /// <summary>Deletes a recipe of your own. Refused for a built-in one, or one deployments or runs use.</summary>
    public async Task DeleteAsync(int recipeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId);
        if (recipe is null)
            return;
        if (recipe.IsBuiltIn)
            throw new ShushDataException("A recipe that ships with the app can't be deleted. Reset it to its default instead.");
        if (await db.Deployments.AnyAsync(d => d.RecipeId == recipeId))
            throw new ShushDataException("Deployments use this recipe. Delete them first.");
        if (await db.Runs.AnyAsync(r => r.RecipeId == recipeId))
            throw new ShushDataException("Runs in the history used this recipe, so it's kept. Rename it if it's in the way.");

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
    }

    private static string RequireName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ShushDataException("Enter a name.");
        return name;
    }

    /// <summary>Names are unique, ignoring case (the column is NOCASE).</summary>
    private static async Task EnsureNameFreeAsync(ShushDbContext db, string name, int? exceptId)
    {
        if (await db.Recipes.AnyAsync(r => r.Name == name && r.Id != exceptId))
            throw new ShushDataException($"A recipe named '{name}' already exists.");
    }

    internal static RecipeVersion NewVersion(int number, string yaml, VersionSource source, string? user, DateTime now) =>
        new() { Number = number, Yaml = yaml, Hash = Hashing.Of(yaml), Source = source, CreatedBy = user, CreatedAt = now };
}
