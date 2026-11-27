using Microsoft.EntityFrameworkCore;
using Shush.Recipe;

namespace Shush.Data;

/// <summary>What a page needs to show under a rig's ID: its hostname, and whether the registry said it doesn't exist.</summary>
public sealed record RigHost(string? Hostname, bool Unresolved);

public sealed record GroupSummary(int Id, string Name, string? Description, int MachineCount, int DeploymentCount);

public sealed class GroupService(IDbContextFactory<ShushDbContext> factory)
{
    public async Task<List<GroupSummary>> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var deployments = await db.Deployments.GroupBy(d => d.GroupId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var groups = await db.Groups.Select(g => new { g.Id, g.Name, g.Description, Count = g.Members.Count }).ToListAsync();
        return groups
            .Select(g => new GroupSummary(g.Id, g.Name, g.Description, g.Count, deployments.GetValueOrDefault(g.Id)))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Group?> GetAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var group = await db.Groups.AsNoTracking().Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id);
        group?.Members.Sort((a, b) => a.Position.CompareTo(b.Position));
        return group;
    }

    public async Task<Group?> FindAsync(string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        var group = await db.Groups.AsNoTracking().Include(g => g.Members).FirstOrDefaultAsync(g => g.Name == name);
        group?.Members.Sort((a, b) => a.Position.CompareTo(b.Position));
        return group;
    }

    public async Task<Group> CreateAsync(string name, string? description = null, IEnumerable<string>? machines = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ShushDataException("Enter a name for the group.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Groups.AnyAsync(g => g.Name == name))
            throw new ShushDataException($"A group named '{name}' already exists.");

        var group = new Group { Name = name, Description = description, CreatedAt = Clock.UtcNow() };
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        if (machines is not null)
            await SetMembersAsync(group.Id, machines);
        return group;
    }

    public async Task UpdateAsync(int id, string name, string? description)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ShushDataException("Enter a name for the group.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Groups.AnyAsync(g => g.Name == name && g.Id != id))
            throw new ShushDataException($"A group named '{name}' already exists.");

        var group = await db.Groups.FirstAsync(g => g.Id == id);
        group.Name = name;
        group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Deployments.AnyAsync(d => d.GroupId == id))
            throw new ShushDataException("Deployments use this group. Delete them first.");

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
            return;
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
    }

    /// <summary>Replaces the group's machines with <paramref name="machines"/>, in that order (duplicates dropped).</summary>
    public async Task SetMembersAsync(int groupId, IEnumerable<string> machines)
    {
        var names = machines.Select(m => m.Trim()).Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await using var db = await factory.CreateDbContextAsync();
        db.GroupMembers.RemoveRange(db.GroupMembers.Where(m => m.GroupId == groupId));
        await db.SaveChangesAsync();

        for (var i = 0; i < names.Count; i++)
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, MachineName = names[i], Position = i });

        await MachineService.EnsureExistAsync(db, names);
        await db.SaveChangesAsync();
    }

    public async Task AddMembersAsync(int groupId, IEnumerable<string> machines)
    {
        var group = await GetAsync(groupId) ?? throw new ShushDataException("That group no longer exists.");
        await SetMembersAsync(groupId, group.Members.Select(m => m.MachineName).Concat(machines));
    }

    public async Task RemoveMemberAsync(int groupId, string machine)
    {
        var group = await GetAsync(groupId) ?? throw new ShushDataException("That group no longer exists.");
        await SetMembersAsync(groupId, group.Members.Select(m => m.MachineName)
            .Where(m => !string.Equals(m, machine, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The groups a machine belongs to.</summary>
    public async Task<List<Group>> GroupsOfAsync(string machine)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.GroupMembers.AsNoTracking().Where(m => m.MachineName == machine).Select(m => m.Group)
            .OrderBy(g => g.Name).ToListAsync();
    }
}

public sealed class MachineService(IDbContextFactory<ShushDbContext> factory)
{
    /// <summary>The probe error recorded when the machine registry doesn't know a machine.</summary>
    public const string NotInRegistry = "Not found in the machine registry.";

    public async Task<List<Machine>> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var machines = await db.Machines.AsNoTracking().ToListAsync();
        return machines.OrderBy(m => m.Name, MachineNameComparer.Instance).ToList();
    }

    public async Task<Machine?> GetAsync(string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Name == name);
    }

    public async Task<Dictionary<string, Machine>> GetManyAsync(IEnumerable<string> names)
    {
        var list = names.ToList();
        await using var db = await factory.CreateDbContextAsync();
        var machines = await db.Machines.AsNoTracking().Where(m => list.Contains(m.Name)).ToListAsync();
        return machines.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Hostname and registry status for each of <paramref name="names"/> (case-insensitive); unknown names are left out.</summary>
    public async Task<Dictionary<string, RigHost>> HostnamesAsync(IEnumerable<string> names)
    {
        var lower = names.Select(n => n.ToLowerInvariant()).Distinct().ToList();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Machines.AsNoTracking().Where(m => lower.Contains(m.Name.ToLower()))
            .Select(m => new { m.Name, m.Hostname, m.ProbeError }).ToListAsync();
        var map = new Dictionary<string, RigHost>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            map[row.Name] = new RigHost(row.Hostname, row.ProbeError == NotInRegistry);
        return map;
    }

    /// <summary>Records the hostname the machine registry resolved <paramref name="name"/> to, adding the machine if it's new.</summary>
    public async Task RecordHostnameAsync(string name, string? hostname)
    {
        name = name.Trim();
        if (name.Length == 0)
            return;
        hostname = string.IsNullOrWhiteSpace(hostname) ? null : hostname.Trim();

        await using var db = await factory.CreateDbContextAsync();
        await EnsureExistAsync(db, [name]);
        await db.SaveChangesAsync();

        var lower = name.ToLowerInvariant();
        var machine = await db.Machines.FirstAsync(m => m.Name.ToLower() == lower);
        var changed = false;
        if (hostname is not null && !string.Equals(machine.Hostname, hostname, StringComparison.OrdinalIgnoreCase))
        {
            machine.Hostname = hostname;
            changed = true;
        }
        if (machine.ProbeError == NotInRegistry)
        {
            // The registry knows it now; whether it answers is for the next probe to say.
            machine.ProbeError = null;
            machine.Reachable = null;
            changed = true;
        }
        if (changed)
            await db.SaveChangesAsync();
    }

    public async Task SetNotesAsync(string name, string? notes)
    {
        await using var db = await factory.CreateDbContextAsync();
        await EnsureExistAsync(db, [name]);
        await db.SaveChangesAsync();
        var machine = await db.Machines.FirstAsync(m => m.Name == name);
        machine.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync();
    }

    /// <summary>Records a reachability probe (no credentials needed).</summary>
    public async Task RecordProbeAsync(string name, HostProbeResult probe)
    {
        await using var db = await factory.CreateDbContextAsync();
        var machine = await db.Machines.FirstOrDefaultAsync(m => m.Name == name);
        if (machine is null)
            return;

        var now = Clock.UtcNow();
        machine.LastProbeAt = now;
        machine.Reachable = probe.ReachableAddress is not null;
        machine.ReachableAddress = probe.ReachableAddress;
        machine.ProbeError = probe.ReachableAddress is null ? probe.Describe() : null;
        if (machine.Reachable == true)
            machine.LastSeenAt = now;
        await db.SaveChangesAsync();
    }

    /// <summary>Records a machine that couldn't even be looked up (not in the registry).</summary>
    public async Task RecordUnresolvedAsync(string name, string error)
    {
        await using var db = await factory.CreateDbContextAsync();
        var machine = await db.Machines.FirstOrDefaultAsync(m => m.Name == name);
        if (machine is null)
            return;
        machine.LastProbeAt = Clock.UtcNow();
        machine.Reachable = false;
        machine.ReachableAddress = null;
        machine.ProbeError = error;
        await db.SaveChangesAsync();
    }

    /// <summary>Records what a pre-flight learned (needs SSH, so it only happens during a dry run or deploy).</summary>
    public async Task RecordFactsAsync(string name, MachineFacts facts)
    {
        await using var db = await factory.CreateDbContextAsync();
        await EnsureExistAsync(db, [name]);
        await db.SaveChangesAsync();

        var machine = await db.Machines.FirstAsync(m => m.Name == name);
        var now = Clock.UtcNow();
        machine.FactsAt = now;
        machine.LastSeenAt = now;
        machine.Reachable = true;
        machine.LoggedOnUser = facts.LoggedOnUser;
        machine.LastBoot = facts.LastBoot?.UtcDateTime;
        machine.OperatingSystem = facts.OperatingSystem;
        machine.SystemDrive = facts.SystemDrive;
        machine.FreeGb = facts.FreeGb;
        machine.SizeGb = facts.SizeGb;
        await db.SaveChangesAsync();
    }

    internal static async Task EnsureExistAsync(ShushDbContext db, IReadOnlyCollection<string> names)
    {
        var lower = names.Select(n => n.ToLowerInvariant()).ToList();
        var known = await db.Machines.Where(m => lower.Contains(m.Name.ToLower())).Select(m => m.Name).ToListAsync();
        var tracked = db.ChangeTracker.Entries<Machine>().Select(e => e.Entity.Name);
        var have = new HashSet<string>(known.Concat(tracked), StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Where(n => !have.Contains(n)))
        {
            db.Machines.Add(new Machine { Name = name });
            have.Add(name);
        }
    }
}

/// <summary>Sorts rig names naturally: FRG.4-A before FRG.12-A.</summary>
public sealed class MachineNameComparer : IComparer<string?>
{
    public static MachineNameComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var cmp = long.Parse(x[si..i]).CompareTo(long.Parse(y[sj..j]));
                if (cmp != 0)
                    return cmp;
                continue;
            }

            var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0)
                return c;
            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
