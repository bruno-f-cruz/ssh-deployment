using Microsoft.EntityFrameworkCore;
using Shush.Recipe.Serialization;

namespace Shush.Data;

public sealed record LastRunSummary(int RunId, RunKind Kind, RunStatus Status, DateTime StartedAt, DateTime? FinishedAt, int Machines, int Succeeded, int Failed, int Running);

/// <param name="Fixed">The recipe's fixed params (label, value): what tells deployments of one recipe apart (install folder, shortcut).</param>
/// <param name="OverrideCount">Steps turned off plus inputs changed for this deployment.</param>
public sealed record DeploymentSummary(
    int Id, string Name, string? Description, int RecipeId, string RecipeName, int GroupId, string GroupName, int MachineCount,
    LastRunSummary? LastRun, IReadOnlyList<KeyValuePair<string, string>>? Fixed = null, int OverrideCount = 0, string? SshUser = null);

/// <summary>A machine's most recent successful deploy of one deployment.</summary>
public sealed record LastDeployed(
    string MachineName, int DeploymentId, string DeploymentName, int RunId, DateTime At, int RecipeVersion,
    IReadOnlyDictionary<string, string> Params, string? Commit);

public sealed class DeploymentService(IDbContextFactory<ShushDbContext> factory)
{
    public async Task<List<DeploymentSummary>> ListAsync(int? groupId = null, int? recipeId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Deployments.AsNoTracking();
        if (groupId is not null) query = query.Where(d => d.GroupId == groupId);
        if (recipeId is not null) query = query.Where(d => d.RecipeId == recipeId);

        var deployments = await query
            .Select(d => new
            {
                d.Id, d.Name, d.Description, d.RecipeId, RecipeName = d.Recipe.Name, d.GroupId, GroupName = d.Group.Name,
                Count = d.Group.Members.Count, d.ParamValuesJson, d.OverridesJson, d.SshUser,
            })
            .ToListAsync();

        var ids = deployments.Select(d => d.Id).ToList();
        var lastRuns = await LastRunsAsync(db, ids, includeDryRuns: false);

        // The fixed params come from each recipe's current version.
        var recipeIds = deployments.Select(d => d.RecipeId).Distinct().ToList();
        var currentYaml = await db.RecipeVersions.AsNoTracking()
            .Where(v => recipeIds.Contains(v.RecipeId))
            .GroupBy(v => v.RecipeId)
            .Select(g => g.OrderByDescending(v => v.Number).First())
            .ToDictionaryAsync(v => v.RecipeId, v => v.Yaml);
        var documents = new Dictionary<int, RecipeDocument?>();
        foreach (var (id, yaml) in currentYaml)
        {
            try { documents[id] = YamlRecipeSerializer.Deserialize(yaml); }
            catch (Exception) { documents[id] = null; }
        }

        return deployments
            .Select(d => new DeploymentSummary(d.Id, d.Name, d.Description, d.RecipeId, d.RecipeName, d.GroupId, d.GroupName, d.Count,
                lastRuns.GetValueOrDefault(d.Id),
                documents.GetValueOrDefault(d.RecipeId) is { } doc ? FixedValues(doc, Json.ReadParams(d.ParamValuesJson)) : [],
                RecipeOverrides.FromJson(d.OverridesJson).Count, d.SshUser))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Deployment?> GetAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Deployments.AsNoTracking()
            .Include(d => d.Recipe)
            .Include(d => d.Group).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Deployment> CreateAsync(string name, int recipeId, int groupId, IReadOnlyDictionary<string, string> paramValues, string? description = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ShushDataException("Enter a name for the deployment.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Deployments.AnyAsync(d => d.Name == name))
            throw new ShushDataException($"A deployment named '{name}' already exists.");
        if (!await db.Recipes.AnyAsync(r => r.Id == recipeId))
            throw new ShushDataException("Pick a recipe.");
        if (!await db.Groups.AnyAsync(g => g.Id == groupId))
            throw new ShushDataException("Pick a group.");

        var now = Clock.UtcNow();
        var deployment = new Deployment
        {
            Name = name, Description = description, RecipeId = recipeId, GroupId = groupId,
            ParamValuesJson = Json.Write(paramValues), CreatedAt = now, UpdatedAt = now,
        };
        db.Deployments.Add(deployment);
        await db.SaveChangesAsync();
        return deployment;
    }

    public async Task UpdateAsync(int id, string name, string? description, int groupId)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ShushDataException("Enter a name for the deployment.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Deployments.AnyAsync(d => d.Name == name && d.Id != id))
            throw new ShushDataException($"A deployment named '{name}' already exists.");

        var deployment = await db.Deployments.FirstAsync(d => d.Id == id);
        deployment.Name = name;
        deployment.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        deployment.GroupId = groupId;
        deployment.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A copy of deployment <paramref name="id"/>: same group, description, SSH account and overrides, with
    /// <paramref name="paramValues"/> (a new deployment needs its own fixed values). With <paramref name="recipeId"/>
    /// (a detached copy, deploying its own duplicate of the recipe) the overrides aren't copied: the new recipe has them built in.
    /// </summary>
    public async Task<Deployment> DuplicateAsync(int id, string name, IReadOnlyDictionary<string, string> paramValues, int? recipeId = null)
    {
        Deployment source;
        await using (var db = await factory.CreateDbContextAsync())
            source = await db.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id)
                ?? throw new ShushDataException("That deployment no longer exists.");

        var copy = await CreateAsync(name, recipeId ?? source.RecipeId, source.GroupId, paramValues, source.Description);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var created = await db.Deployments.FirstAsync(d => d.Id == copy.Id);
            created.SshUser = source.SshUser;
            created.OverridesJson = recipeId is null ? source.OverridesJson : "{}";
            await db.SaveChangesAsync();
            return created;
        }
    }

    /// <summary>Points the deployment at another recipe (e.g. its own detached copy) and clears its overrides.</summary>
    public async Task SwitchRecipeAsync(int id, int recipeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Recipes.AnyAsync(r => r.Id == recipeId))
            throw new ShushDataException("That recipe no longer exists.");
        var deployment = await db.Deployments.FirstAsync(d => d.Id == id);
        deployment.RecipeId = recipeId;
        deployment.OverridesJson = "{}";
        deployment.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>Saves the steps turned off and the inputs changed for this deployment.</summary>
    public async Task SaveOverridesAsync(int id, RecipeOverrides overrides)
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployment = await db.Deployments.FirstAsync(d => d.Id == id);
        deployment.OverridesJson = overrides.IsEmpty ? "{}" : overrides.ToJson();
        deployment.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>The SSH account the deployment connects as; null or blank: whoever is signed in.</summary>
    public async Task SetSshUserAsync(int id, string? user)
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployment = await db.Deployments.FirstAsync(d => d.Id == id);
        deployment.SshUser = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        deployment.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>The recipe's fixed params with this deployment's values, labelled.</summary>
    public static List<KeyValuePair<string, string>> FixedValues(RecipeDocument recipe, IReadOnlyDictionary<string, string> saved) =>
        EffectiveParams(recipe, saved)
            .Where(kv => recipe.Params[kv.Key].IsFixed)
            .Select(kv => new KeyValuePair<string, string>(recipe.Params[kv.Key].Label ?? kv.Key, kv.Value))
            .ToList();

    /// <summary>Remembers the parameter values last used (the deploy screen starts from them next time).</summary>
    public async Task SaveParamValuesAsync(int id, IReadOnlyDictionary<string, string> values)
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployment = await db.Deployments.FirstOrDefaultAsync(d => d.Id == id);
        if (deployment is null)
            return;
        deployment.ParamValuesJson = Json.Write(values);
        deployment.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes the deployment. Its runs stay in the history.</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployment = await db.Deployments.FirstOrDefaultAsync(d => d.Id == id);
        if (deployment is null)
            return;
        db.Deployments.Remove(deployment);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The parameter values a deploy starts from: the recipe's defaults, overlaid by what was last used for the
    /// params the recipe still declares.
    /// </summary>
    public static Dictionary<string, string> EffectiveParams(RecipeDocument recipe, IReadOnlyDictionary<string, string> saved)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, decl) in recipe.Params)
            values[name] = saved.TryGetValue(name, out var v) ? v : decl.Default ?? "";
        return values;
    }

    /// <summary>Per machine of the deployment, its last successful deploy (null when it never had one).</summary>
    public async Task<List<LastDeployed>> LastDeployedAsync(int deploymentId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.RunMachines.AsNoTracking()
            .Where(m => m.Status == MachineStatus.Succeeded && m.Run.DeploymentId == deploymentId && m.Run.Kind != RunKind.DryRun)
            .Select(m => new
            {
                m.MachineName, m.Run.DeploymentId, m.Run.DeploymentName, m.RunId, At = m.FinishedAt ?? m.Run.StartedAt,
                Version = m.Run.RecipeVersion.Number, m.Run.ParamValuesJson, m.Commit,
            })
            .ToListAsync();

        return rows
            .GroupBy(r => r.MachineName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.At).First())
            .Select(r => new LastDeployed(r.MachineName, r.DeploymentId!.Value, r.DeploymentName, r.RunId, r.At, r.Version,
                Json.ReadParams(r.ParamValuesJson), r.Commit))
            .ToList();
    }

    /// <summary>Per deployment that ever reached <paramref name="machine"/>, its last successful deploy there.</summary>
    public async Task<List<LastDeployed>> LastDeployedOnMachineAsync(string machine)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.RunMachines.AsNoTracking()
            .Where(m => m.MachineName == machine && m.Status == MachineStatus.Succeeded && m.Run.DeploymentId != null && m.Run.Kind != RunKind.DryRun)
            .Select(m => new
            {
                m.MachineName, m.Run.DeploymentId, m.Run.DeploymentName, m.RunId, At = m.FinishedAt ?? m.Run.StartedAt,
                Version = m.Run.RecipeVersion.Number, m.Run.ParamValuesJson, m.Commit,
            })
            .ToListAsync();

        return rows
            .GroupBy(r => r.DeploymentId)
            .Select(g => g.OrderByDescending(r => r.At).First())
            .Select(r => new LastDeployed(r.MachineName, r.DeploymentId!.Value, r.DeploymentName, r.RunId, r.At, r.Version,
                Json.ReadParams(r.ParamValuesJson), r.Commit))
            .OrderBy(r => r.DeploymentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static async Task<Dictionary<int, LastRunSummary>> LastRunsAsync(ShushDbContext db, List<int> deploymentIds, bool includeDryRuns)
    {
        var runs = await db.Runs.AsNoTracking()
            .Where(r => r.DeploymentId != null && deploymentIds.Contains(r.DeploymentId.Value) && (includeDryRuns || r.Kind != RunKind.DryRun))
            .GroupBy(r => r.DeploymentId!.Value)
            .Select(g => g.OrderByDescending(r => r.Id).First().Id)
            .ToListAsync();

        var summaries = await db.Runs.AsNoTracking().Where(r => runs.Contains(r.Id))
            .Select(r => new
            {
                r.DeploymentId,
                Summary = new LastRunSummary(r.Id, r.Kind, r.Status, r.StartedAt, r.FinishedAt, r.Machines.Count,
                    r.Machines.Count(m => m.Status == MachineStatus.Succeeded),
                    r.Machines.Count(m => m.Status == MachineStatus.Failed),
                    r.Machines.Count(m => m.Status == MachineStatus.Running)),
            })
            .ToListAsync();

        return summaries.ToDictionary(s => s.DeploymentId!.Value, s => s.Summary);
    }
}
