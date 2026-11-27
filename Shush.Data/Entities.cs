namespace Shush.Data;

// All times are UTC (see ShushDbContext's DateTime convention).

public enum VersionSource { BuiltIn, Edit, Import, Reset, Legacy, Restore, Duplicate }

/// <summary>A recipe. Its content lives in immutable <see cref="RecipeVersion"/>s; the newest is the current one.</summary>
public sealed class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Ships with the app (in the Recipes folder). Can't be deleted; can be reset to the shipped YAML.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>The newest shipped YAML, for "Reset to default" (a built-in recipe only).</summary>
    public string? ShippedYaml { get; set; }
    public string? ShippedHash { get; set; }

    /// <summary>When the recipe was made by duplicating another: which one and which version (e.g. "VrForagingExperiment v3").</summary>
    public string? CopiedFrom { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<RecipeVersion> Versions { get; set; } = [];
}

public sealed class RecipeVersion
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    /// <summary>1, 2, 3… per recipe.</summary>
    public int Number { get; set; }
    public string Yaml { get; set; } = "";
    public string Hash { get; set; } = "";
    public VersionSource Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GroupMember> Members { get; set; } = [];
}

public sealed class GroupMember
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public string MachineName { get; set; } = "";

    /// <summary>Order in the group, as the operator listed them.</summary>
    public int Position { get; set; }
}

/// <summary>A machine Shush knows about, and what the health probe and the last pre-flight learned about it.</summary>
public sealed class Machine
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>The hostname (or IP) the machine registry last resolved the name to. Kept so it shows when the registry is down.</summary>
    public string? Hostname { get; set; }

    public DateTime? LastProbeAt { get; set; }
    public bool? Reachable { get; set; }
    public string? ReachableAddress { get; set; }
    public string? ProbeError { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public DateTime? FactsAt { get; set; }
    public string? LoggedOnUser { get; set; }
    public DateTime? LastBoot { get; set; }
    public string? OperatingSystem { get; set; }
    public string? SystemDrive { get; set; }
    public double? FreeGb { get; set; }
    public double? SizeGb { get; set; }
}

public enum VariableScope { Global, Group, Machine }

/// <summary>
/// A value recipes read as <c>${group.NAME}</c>. Layers: a machine's own value wins over its group's, which wins
/// over the global one. Optionally set on the rig as an environment variable, optionally secret.
/// </summary>
public sealed class Variable
{
    public int Id { get; set; }
    public VariableScope Scope { get; set; }
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    public string? MachineName { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The value, when not secret.</summary>
    public string? Value { get; set; }

    /// <summary>The encrypted value, when secret.</summary>
    public string? ProtectedValue { get; set; }

    public bool IsSecret { get; set; }

    /// <summary>Also set it on the rig as an environment variable, before the recipe's steps.</summary>
    public bool ApplyAsEnv { get; set; }

    /// <summary>Machine or User.</summary>
    public string EnvScope { get; set; } = "Machine";

    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A recipe deployed to a group. Remembers the last parameter values used.</summary>
public sealed class Deployment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    /// <summary>JSON object of param name → value.</summary>
    public string ParamValuesJson { get; set; } = "{}";

    /// <summary>JSON of the <see cref="Shush.Recipe.Serialization.RecipeOverrides"/>: the recipe steps this deployment turns off (the recipe's later versions still reach it).</summary>
    public string OverridesJson { get; set; } = "{}";

    /// <summary>The SSH account this deployment connects as. Null: whoever is signed in.</summary>
    public string? SshUser { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum RunKind { Deploy, DryRun, Retry, Rollback }

public enum RunStatus { Running, Succeeded, Failed, Cancelled, Interrupted }

public enum MachineStatus { Waiting, Running, Succeeded, Failed, Cancelled, Interrupted }

public enum StepStatus { Running, Succeeded, Failed, Skipped }

public sealed class Run
{
    public int Id { get; set; }
    public RunKind Kind { get; set; }
    public RunStatus Status { get; set; }

    public int? DeploymentId { get; set; }
    public Deployment? Deployment { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public int RecipeVersionId { get; set; }
    public RecipeVersion RecipeVersion { get; set; } = null!;
    public int? GroupId { get; set; }

    /// <summary>Kept even if the deployment is renamed or deleted.</summary>
    public string DeploymentName { get; set; } = "";
    public string? GroupName { get; set; }

    public string ParamValuesJson { get; set; } = "{}";

    /// <summary>The deployment's overrides when the run started, so a retry or rollback repeats them.</summary>
    public string OverridesJson { get; set; } = "{}";
    public string? StartedBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? LogFile { get; set; }

    /// <summary>E.g. which run a rollback or retry came from.</summary>
    public int? SourceRunId { get; set; }

    public List<RunMachine> Machines { get; set; } = [];
}

public sealed class RunMachine
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public Run Run { get; set; } = null!;
    public string MachineName { get; set; } = "";
    public MachineStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }

    /// <summary>JSON array of the step names this machine goes through.</summary>
    public string PlannedStepsJson { get; set; } = "[]";

    /// <summary>JSON of the pre-flight result.</summary>
    public string? PreflightJson { get; set; }

    /// <summary>JSON of the dry-run plan preview.</summary>
    public string? PreviewJson { get; set; }

    /// <summary>The last commit a checkout step reported.</summary>
    public string? Commit { get; set; }

    public List<RunStep> Steps { get; set; } = [];
}

public sealed class RunStep
{
    public int Id { get; set; }
    public int RunMachineId { get; set; }
    public RunMachine RunMachine { get; set; } = null!;
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public StepStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? SkipReason { get; set; }
    public string? OutputsJson { get; set; }
}

public sealed class MetaEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
