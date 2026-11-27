using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shush.Recipe.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shush.Data;

/// <summary>Where the shipped content lives, and where v1 kept its files.</summary>
/// <param name="LegacyDirectory">The v1 data directory (<c>.shush</c>): recipes/, groups/ and state/ are imported once.</param>
public sealed record SeedPaths(string RecipesDirectory, string GroupsDirectory, string DeploymentsDirectory, string? LegacyDirectory);

/// <summary>
/// Brings the database up to date on startup: migrations, the recipes, groups and deployments shipped with the app,
/// and (once) the files v1 kept in the data directory.
/// </summary>
public sealed class Seeder(IDbContextFactory<ShushDbContext> factory, RecipeService recipes, GroupService groups, DeploymentService deployments, ILogger<Seeder> logger)
{
    private const string LegacyImportedKey = "legacy-imported";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task RunAsync(SeedPaths paths)
    {
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.MigrateAsync();

        await SeedRecipesAsync(paths.RecipesDirectory);
        await SeedGroupsAsync(paths.GroupsDirectory);
        if (paths.LegacyDirectory is not null)
            await ImportLegacyAsync(paths.LegacyDirectory);
        await SeedDeploymentsAsync(paths.DeploymentsDirectory);
    }

    /// <summary>
    /// Every shipped recipe exists and is marked built-in. When the shipped YAML changed: if nobody edited the
    /// recipe since the last shipped version, the new one becomes the current version; otherwise it's only kept
    /// for "Reset to default", and the recipe list says an update is available.
    /// </summary>
    private async Task SeedRecipesAsync(string directory)
    {
        foreach (var file in YamlFiles(directory))
        {
            var yaml = await File.ReadAllTextAsync(file);
            RecipeDocument document;
            try
            {
                document = recipes.Parse(yaml);
            }
            catch (RecipeValidationException ex)
            {
                logger.LogError("Shipped recipe '{File}' is invalid and was skipped: {Errors}", file, string.Join("; ", ex.Errors));
                continue;
            }

            var hash = Hashing.Of(yaml);
            await using var db = await factory.CreateDbContextAsync();
            var recipe = await db.Recipes.Include(r => r.Versions).FirstOrDefaultAsync(r => r.Name == document.Name);
            var now = Clock.UtcNow();

            if (recipe is null)
            {
                recipe = new Recipe { Name = document.Name, IsBuiltIn = true, ShippedYaml = yaml, ShippedHash = hash, CreatedAt = now, UpdatedAt = now };
                recipe.Versions.Add(RecipeService.NewVersion(1, yaml, VersionSource.BuiltIn, null, now));
                db.Recipes.Add(recipe);
                await db.SaveChangesAsync();
                continue;
            }

            var current = recipe.Versions.OrderByDescending(v => v.Number).First();
            var untouched = current.Hash == recipe.ShippedHash || recipe.ShippedHash is null && current.Source == VersionSource.BuiltIn;

            recipe.IsBuiltIn = true;
            if (recipe.ShippedHash != hash && untouched && current.Hash != hash)
            {
                recipe.Versions.Add(RecipeService.NewVersion(current.Number + 1, yaml, VersionSource.BuiltIn, null, now));
                recipe.UpdatedAt = now;
            }

            recipe.ShippedYaml = yaml;
            recipe.ShippedHash = hash;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Shipped groups are created once; after that they're the operators' to change or delete.</summary>
    private async Task SeedGroupsAsync(string directory)
    {
        foreach (var file in YamlFiles(directory))
        {
            var group = Yaml.Deserialize<GroupFile>(await File.ReadAllTextAsync(file));
            if (group is null || string.IsNullOrWhiteSpace(group.Name))
                continue;
            if (!await ClaimOnceAsync($"seeded-group:{group.Name.ToLowerInvariant()}"))
                continue;
            if (await groups.FindAsync(group.Name) is null)
                await groups.CreateAsync(group.Name, machines: group.Machines);
        }
    }

    /// <summary>Shipped deployments are created once, when their recipe and group exist.</summary>
    private async Task SeedDeploymentsAsync(string directory)
    {
        foreach (var file in YamlFiles(directory))
        {
            var list = Yaml.Deserialize<List<DeploymentFile>>(await File.ReadAllTextAsync(file)) ?? [];
            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                    continue;
                var recipe = await recipes.FindAsync(item.Recipe);
                var group = await groups.FindAsync(item.Group);
                if (recipe is null || group is null)
                    continue;

                // A deployment whose values the recipe doesn't declare would silently lose them (e.g. a dev install
                // folder on a recipe edited in v1 without it), and install where it shouldn't. Skip it instead.
                var current = YamlRecipeSerializer.Deserialize((await recipes.CurrentVersionAsync(recipe.Id)).Yaml);
                var undeclared = (item.Params ?? new()).Keys.Where(k => !current.Params.ContainsKey(k)).ToList();
                if (undeclared.Count > 0)
                {
                    logger.LogWarning(
                        "Didn't create the shipped deployment '{Deployment}': recipe '{Recipe}' doesn't declare {Params}. Reset the recipe to its default, then restart.",
                        item.Name, recipe.Name, string.Join(", ", undeclared));
                    continue;
                }

                if (!await ClaimOnceAsync($"seeded-deployment:{item.Name.ToLowerInvariant()}"))
                    continue;

                await using var db = await factory.CreateDbContextAsync();
                if (await db.Deployments.AnyAsync(d => d.Name == item.Name))
                    continue;
                await deployments.CreateAsync(item.Name, recipe.Id, group.Id, item.Params ?? new());
            }
        }
    }

    /// <summary>
    /// v1 kept user recipes, user groups and per-recipe state as YAML files. They're imported once: a user recipe
    /// becomes a new version (it overrode the built-in in v1), a user group replaces the shipped one's machines, and
    /// each recipe's saved machines and parameter values become a deployment. The files are left in place.
    /// </summary>
    private async Task ImportLegacyAsync(string legacyDirectory)
    {
        if (!await ClaimOnceAsync(LegacyImportedKey))
            return;

        foreach (var file in YamlFiles(Path.Combine(legacyDirectory, "recipes")))
        {
            try
            {
                var yaml = await File.ReadAllTextAsync(file);
                var existing = await recipes.FindAsync(recipes.Parse(yaml).Name);
                if (existing is null)
                    await recipes.CreateAsync(yaml, VersionSource.Legacy, null);
                else
                    await recipes.SaveAsync(existing.Id, yaml, VersionSource.Legacy, null);
            }
            catch (Exception ex) when (ex is RecipeValidationException or ShushDataException or YamlDotNet.Core.YamlException)
            {
                logger.LogWarning("Skipped v1 recipe '{File}': {Message}", file, ex is RecipeValidationException rv ? string.Join("; ", rv.Errors) : ex.Message);
            }
        }

        foreach (var file in YamlFiles(Path.Combine(legacyDirectory, "groups")))
        {
            try
            {
                var group = Yaml.Deserialize<GroupFile>(await File.ReadAllTextAsync(file));
                if (group is null || string.IsNullOrWhiteSpace(group.Name))
                    continue;
                var existing = await groups.FindAsync(group.Name);
                if (existing is null)
                    await groups.CreateAsync(group.Name, machines: group.Machines);
                else
                    await groups.SetMembersAsync(existing.Id, group.Machines);
            }
            catch (Exception ex) when (ex is ShushDataException or YamlDotNet.Core.YamlException)
            {
                logger.LogWarning("Skipped v1 group '{File}': {Message}", file, ex.Message);
            }
        }

        foreach (var file in YamlFiles(Path.Combine(legacyDirectory, "state")))
        {
            try
            {
                var recipeName = Path.GetFileNameWithoutExtension(file);
                var state = Yaml.Deserialize<StateFile>(await File.ReadAllTextAsync(file));
                var recipe = await recipes.FindAsync(recipeName);
                if (state is null || recipe is null)
                    continue;

                var group = state.Group is null ? null : await groups.FindAsync(state.Group);
                if (group is null)
                {
                    if (state.Machines.Count == 0)
                        continue;
                    var name = await FreeNameAsync($"{recipe.Name} machines", n => groups.FindAsync(n));
                    group = await groups.CreateAsync(name, "Imported from the machines saved for this recipe in v1.", state.Machines);
                }

                var deploymentName = await FreeNameAsync($"{recipe.Name} on {group.Name}", async n =>
                {
                    await using var db = await factory.CreateDbContextAsync();
                    return await db.Deployments.AnyAsync(d => d.Name == n) ? n : null;
                });
                await deployments.CreateAsync(deploymentName, recipe.Id, group.Id, state.ParamOverrides,
                    "Imported from the v1 saved state of this recipe.");
            }
            catch (Exception ex) when (ex is ShushDataException or YamlDotNet.Core.YamlException)
            {
                logger.LogWarning("Skipped v1 state '{File}': {Message}", file, ex.Message);
            }
        }
    }

    private static async Task<string> FreeNameAsync<T>(string wanted, Func<string, Task<T?>> find) where T : class
    {
        var name = wanted;
        for (var i = 2; await find(name) is not null; i++)
            name = $"{wanted} ({i})";
        return name;
    }

    /// <summary>True the first time a key is claimed, false ever after.</summary>
    private async Task<bool> ClaimOnceAsync(string key)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Meta.AnyAsync(m => m.Key == key))
            return false;
        db.Meta.Add(new MetaEntry { Key = key, Value = Clock.UtcNow().ToString("o") });
        await db.SaveChangesAsync();
        return true;
    }

    private static IEnumerable<string> YamlFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.yml").Concat(Directory.EnumerateFiles(directory, "*.yaml")).OrderBy(f => f, StringComparer.Ordinal)
            : [];

    private sealed class GroupFile
    {
        public string Name { get; set; } = "";
        public List<string> Machines { get; set; } = [];
    }

    private sealed class DeploymentFile
    {
        public string Name { get; set; } = "";
        public string Recipe { get; set; } = "";
        public string Group { get; set; } = "";
        public Dictionary<string, string>? Params { get; set; }
    }

    private sealed class StateFile
    {
        public string? Group { get; set; }
        public List<string> Machines { get; set; } = [];
        public Dictionary<string, string> ParamOverrides { get; set; } = new();
    }
}
