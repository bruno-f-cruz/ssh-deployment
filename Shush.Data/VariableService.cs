using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shush.Recipe;

namespace Shush.Data;

/// <summary>A variable as the UI shows it. A secret's value is never included.</summary>
public sealed record VariableView(
    int Id, VariableScope Scope, int? GroupId, string? MachineName, string Name, string? Value, bool IsSecret,
    bool ApplyAsEnv, string EnvScope, string? Description, DateTime UpdatedAt, bool IsSharedFolder = false)
{
    public string Where => Scope switch
    {
        VariableScope.Global => "Global",
        VariableScope.Group => "Group",
        _ => MachineName ?? "Machine",
    };
}

/// <summary>A variable to create or update. A secret left with a null <see cref="Value"/> keeps its current value.</summary>
public sealed record VariableInput(
    VariableScope Scope, int? GroupId, string? MachineName, string Name, string? Value, bool IsSecret,
    bool ApplyAsEnv, string EnvScope = "Machine", string? Description = null, bool IsSharedFolder = false);

/// <summary>The value a machine gets for one name, where it came from, and what it replaced.</summary>
public sealed record ResolvedVariable(
    string Name, string Value, VariableScope Source, string SourceLabel, bool IsSecret, bool ApplyAsEnv, string EnvScope,
    int VariableId, IReadOnlyList<(VariableScope Scope, string Label)> Overrides, bool IsSharedFolder = false);

public sealed partial class VariableService(IDbContextFactory<ShushDbContext> factory, ISecretProtector protector)
{
    public async Task<List<VariableView>> ListAsync(VariableScope scope, int? groupId = null, string? machine = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Variables.AsNoTracking().Where(v => v.Scope == scope);
        query = scope switch
        {
            VariableScope.Group => query.Where(v => v.GroupId == groupId),
            VariableScope.Machine => machine is null ? query : query.Where(v => v.MachineName == machine),
            _ => query,
        };
        var variables = await query.ToListAsync();
        return variables.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.MachineName).Select(View).ToList();
    }

    /// <summary>Every machine override of the group's machines.</summary>
    public async Task<List<VariableView>> MachineOverridesAsync(IReadOnlyCollection<string> machines)
    {
        await using var db = await factory.CreateDbContextAsync();
        var list = machines.ToList();
        var variables = await db.Variables.AsNoTracking()
            .Where(v => v.Scope == VariableScope.Machine && v.MachineName != null && list.Contains(v.MachineName))
            .ToListAsync();
        return variables.OrderBy(v => v.MachineName, MachineNameComparer.Instance).ThenBy(v => v.Name).Select(View).ToList();
    }

    public async Task<VariableView> SaveAsync(int? id, VariableInput input)
    {
        var name = input.Name.Trim();
        if (!NamePattern().IsMatch(name))
            throw new ShushDataException("A variable name starts with a letter or _, then letters, digits, _ or - (e.g. repoRoot, HUB_URL).");
        if (input.Scope == VariableScope.Group && input.GroupId is null)
            throw new ShushDataException("A group variable needs its group.");
        if (input.Scope == VariableScope.Machine && string.IsNullOrWhiteSpace(input.MachineName))
            throw new ShushDataException("A machine variable needs its machine.");
        if (input.EnvScope is not ("Machine" or "User"))
            throw new ShushDataException("An environment variable is set for the whole machine (Machine) or the SSH user (User).");
        if (input.IsSharedFolder && input.IsSecret)
            throw new ShushDataException("A shared folder can't be secret: its path is shown in the deploy log.");
        if (input.IsSharedFolder && input.Value is { } folder && !FolderPattern().IsMatch(folder.Trim()))
            throw new ShushDataException("A shared folder is a full path on the rig, e.g. C:/uv/uv_tool_dir.");

        await using var db = await factory.CreateDbContextAsync();
        var groupId = input.Scope == VariableScope.Group ? input.GroupId : null;
        var machine = input.Scope == VariableScope.Machine ? input.MachineName!.Trim() : null;

        var clash = await db.Variables.AnyAsync(v =>
            v.Id != (id ?? 0) && v.Scope == input.Scope && v.GroupId == groupId && v.MachineName == machine && v.Name == name);
        if (clash)
            throw new ShushDataException($"'{name}' is already set here.");

        Variable variable;
        if (id is { } existingId)
        {
            variable = await db.Variables.FirstOrDefaultAsync(v => v.Id == existingId)
                ?? throw new ShushDataException("That variable no longer exists.");
        }
        else
        {
            variable = new Variable();
            db.Variables.Add(variable);
            if (input.IsSecret && input.Value is null)
                throw new ShushDataException("Enter the secret's value.");
        }

        if (machine is not null)
            await MachineService.EnsureExistAsync(db, [machine]);

        variable.Scope = input.Scope;
        variable.GroupId = groupId;
        variable.MachineName = machine;
        variable.Name = name;
        variable.ApplyAsEnv = input.ApplyAsEnv;
        variable.EnvScope = input.EnvScope;
        variable.IsSharedFolder = input.IsSharedFolder;
        variable.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

        if (input.IsSecret)
        {
            if (input.Value is not null)
                variable.ProtectedValue = protector.Protect(input.Value);
            else if (!variable.IsSecret && variable.Value is not null)
                variable.ProtectedValue = protector.Protect(variable.Value); // turning an existing value into a secret
            variable.Value = null;
        }
        else
        {
            if (input.Value is null && variable.IsSecret)
                throw new ShushDataException("Enter the value: a secret's value can't be shown, so it can't be kept as plain text.");
            variable.Value = input.Value ?? variable.Value ?? "";
            variable.ProtectedValue = null;
        }

        variable.IsSecret = input.IsSecret;
        variable.UpdatedAt = Clock.UtcNow();
        await db.SaveChangesAsync();
        return View(variable);
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var variable = await db.Variables.FirstOrDefaultAsync(v => v.Id == id);
        if (variable is null)
            return;
        db.Variables.Remove(variable);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The variables <paramref name="machine"/> gets when deployed as part of <paramref name="groupId"/>: its own
    /// value wins over the group's, which wins over the global one. Secrets are decrypted.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedVariable>> ResolveAsync(int? groupId, string? machine)
    {
        await using var db = await factory.CreateDbContextAsync();
        var candidates = await db.Variables.AsNoTracking()
            .Where(v => v.Scope == VariableScope.Global
                        || (v.Scope == VariableScope.Group && v.GroupId == groupId)
                        || (v.Scope == VariableScope.Machine && machine != null && v.MachineName == machine))
            .ToListAsync();

        return candidates
            .GroupBy(v => v.Name, StringComparer.Ordinal)
            .Select(g =>
            {
                var layers = g.OrderByDescending(v => v.Scope).ToList(); // Machine, Group, Global
                var winner = layers[0];
                return new ResolvedVariable(
                    winner.Name, Plain(winner), winner.Scope, Label(winner), winner.IsSecret, winner.ApplyAsEnv, winner.EnvScope,
                    winner.Id, layers.Skip(1).Select(v => (v.Scope, Label(v))).ToList(), winner.IsSharedFolder);
            })
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The variable names any machine of the group could read (for validating <c>${group.NAME}</c>).</summary>
    public async Task<IReadOnlyCollection<string>> NamesAsync(int? groupId, string machine)
    {
        var resolved = await ResolveAsync(groupId, machine);
        return resolved.Select(v => v.Name).ToList();
    }

    /// <summary>What the runner needs for one machine: the variables by name, and the environment variables to set.</summary>
    public static MachineRunSettings ToRunSettings(IReadOnlyList<ResolvedVariable> resolved) => new()
    {
        Variables = resolved.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal),
        Environment = resolved.Where(v => v.ApplyAsEnv)
            .Select(v => new EnvironmentAssignment(v.Name, v.Value, v.EnvScope, v.IsSecret))
            .ToList(),
        SharedFolders = resolved.Where(v => v.IsSharedFolder && !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => new SharedAccessGrant(v.Value.Trim()))
            .Distinct()
            .ToList(),
    };

    /// <summary>Replaces every secret value in a text with dots (for previews and stored output).</summary>
    public static Func<string, string> Masker(IEnumerable<ResolvedVariable> resolved)
    {
        var secrets = resolved.Where(v => v.IsSecret && v.Value.Length > 0).Select(v => v.Value).OrderByDescending(s => s.Length).ToList();
        return text => secrets.Aggregate(text, (current, secret) => current.Replace(secret, "••••••••", StringComparison.Ordinal));
    }

    private string Plain(Variable v) => v.IsSecret ? (v.ProtectedValue is null ? "" : protector.Unprotect(v.ProtectedValue)) : v.Value ?? "";

    private static string Label(Variable v) => v.Scope switch
    {
        VariableScope.Global => "Global",
        VariableScope.Group => "Group",
        _ => v.MachineName ?? "Machine",
    };

    private static VariableView View(Variable v) =>
        new(v.Id, v.Scope, v.GroupId, v.MachineName, v.Name, v.IsSecret ? null : v.Value, v.IsSecret, v.ApplyAsEnv,
            v.EnvScope, v.Description, v.UpdatedAt, v.IsSharedFolder);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]*$")]
    private static partial Regex NamePattern();

    /// <summary>An absolute Windows path: C:/… or C:\….</summary>
    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex FolderPattern();
}
