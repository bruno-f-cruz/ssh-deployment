using Microsoft.EntityFrameworkCore;
using Shush.Recipe;

namespace Shush.Data;

public sealed record NewRun(
    RunKind Kind, int? DeploymentId, string DeploymentName, int RecipeId, int RecipeVersionId, int? GroupId, string? GroupName,
    IReadOnlyDictionary<string, string> Params, string? StartedBy, IReadOnlyList<string> Machines, string? LogFile, int? SourceRunId = null);

public sealed record RunListItem(
    int Id, RunKind Kind, RunStatus Status, string DeploymentName, int? DeploymentId, string? GroupName, string RecipeName,
    int RecipeVersion, string? StartedBy, DateTime StartedAt, DateTime? FinishedAt, int Machines, int Succeeded, int Failed);

public sealed class RunService(IDbContextFactory<ShushDbContext> factory)
{
    /// <summary>Creates the run and one waiting row per machine. Returns the run with its machines.</summary>
    public async Task<Run> CreateAsync(NewRun request)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = new Run
        {
            Kind = request.Kind,
            Status = RunStatus.Running,
            DeploymentId = request.DeploymentId,
            DeploymentName = request.DeploymentName,
            RecipeId = request.RecipeId,
            RecipeVersionId = request.RecipeVersionId,
            GroupId = request.GroupId,
            GroupName = request.GroupName,
            ParamValuesJson = Json.Write(request.Params),
            StartedBy = request.StartedBy,
            StartedAt = Clock.UtcNow(),
            LogFile = request.LogFile,
            SourceRunId = request.SourceRunId,
        };
        foreach (var machine in request.Machines)
            run.Machines.Add(new RunMachine { MachineName = machine, Status = MachineStatus.Waiting });

        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    public async Task<Run?> GetAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.Runs.AsNoTracking()
            .Include(r => r.Recipe)
            .Include(r => r.RecipeVersion)
            .Include(r => r.Machines).ThenInclude(m => m.Steps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run is not null)
        {
            run.Machines.Sort((a, b) => MachineNameComparer.Instance.Compare(a.MachineName, b.MachineName));
            foreach (var machine in run.Machines)
                machine.Steps.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        return run;
    }

    public async Task<List<RunListItem>> ListAsync(int take = 50, int? deploymentId = null, string? machine = null, bool includeDryRuns = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Runs.AsNoTracking();
        if (deploymentId is not null) query = query.Where(r => r.DeploymentId == deploymentId);
        if (machine is not null) query = query.Where(r => r.Machines.Any(m => m.MachineName == machine));
        if (!includeDryRuns) query = query.Where(r => r.Kind != RunKind.DryRun);

        return await query.OrderByDescending(r => r.Id).Take(take)
            .Select(r => new RunListItem(
                r.Id, r.Kind, r.Status, r.DeploymentName, r.DeploymentId, r.GroupName, r.Recipe.Name, r.RecipeVersion.Number,
                r.StartedBy, r.StartedAt, r.FinishedAt, r.Machines.Count,
                r.Machines.Count(m => m.Status == MachineStatus.Succeeded),
                r.Machines.Count(m => m.Status == MachineStatus.Failed)))
            .ToListAsync();
    }

    /// <summary>The run before <paramref name="runId"/> of the same deployment that succeeded everywhere it ran (for "what changed").</summary>
    public async Task<Run?> PreviousSuccessfulAsync(int deploymentId, int beforeRunId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Runs.AsNoTracking().Include(r => r.RecipeVersion)
            .Where(r => r.DeploymentId == deploymentId && r.Id < beforeRunId && r.Kind != RunKind.DryRun && r.Status == RunStatus.Succeeded)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();
    }

    /// <summary>The newest run of the deployment that succeeded everywhere it ran (the default rollback target).</summary>
    public async Task<List<Run>> SuccessfulRunsAsync(int deploymentId, int take = 20)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Runs.AsNoTracking().Include(r => r.RecipeVersion)
            .Where(r => r.DeploymentId == deploymentId && r.Kind != RunKind.DryRun && r.Status == RunStatus.Succeeded)
            .OrderByDescending(r => r.Id).Take(take)
            .ToListAsync();
    }

    public async Task<List<Run>> ActiveAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Runs.AsNoTracking().Include(r => r.Machines)
            .Where(r => r.Status == RunStatus.Running).OrderByDescending(r => r.Id).ToListAsync();
    }

    /// <summary>Machines whose last real run failed (newest first), for "needs attention".</summary>
    public async Task<List<(string Machine, int RunId, string DeploymentName, string? Error, DateTime At)>> RecentFailuresAsync(int take = 10)
    {
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.RunMachines.AsNoTracking()
            .Where(m => m.Run.Kind != RunKind.DryRun && (m.Status == MachineStatus.Failed || m.Status == MachineStatus.Succeeded))
            .Select(m => new { m.MachineName, m.RunId, m.Run.DeploymentId, m.Run.DeploymentName, m.Status, m.Error, At = m.FinishedAt ?? m.Run.StartedAt })
            .ToListAsync();

        // The latest outcome per machine and deployment: a later success clears an earlier failure.
        return rows.GroupBy(r => (r.MachineName.ToLowerInvariant(), r.DeploymentId))
            .Select(g => g.OrderByDescending(r => r.RunId).First())
            .Where(r => r.Status == MachineStatus.Failed)
            .OrderByDescending(r => r.At)
            .Take(take)
            .Select(r => (r.MachineName, r.RunId, r.DeploymentName, r.Error, r.At))
            .ToList();
    }

    /// <summary>Runs an app restart cut short: marked interrupted, with their unfinished machines.</summary>
    public async Task<int> MarkInterruptedAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var runs = await db.Runs.Include(r => r.Machines).ThenInclude(m => m.Steps).Where(r => r.Status == RunStatus.Running).ToListAsync();
        var now = Clock.UtcNow();
        foreach (var run in runs)
        {
            run.Status = RunStatus.Interrupted;
            run.FinishedAt ??= now;
            foreach (var machine in run.Machines.Where(m => m.Status is MachineStatus.Waiting or MachineStatus.Running))
            {
                machine.Status = MachineStatus.Interrupted;
                machine.FinishedAt ??= now;
                machine.Error ??= "The app restarted during the run.";
                foreach (var step in machine.Steps.Where(s => s.Status == StepStatus.Running))
                {
                    step.Status = StepStatus.Failed;
                    step.FinishedAt = now;
                }
            }
        }

        await db.SaveChangesAsync();
        return runs.Count;
    }

    public async Task RecordPreflightAsync(int runMachineId, PreflightResult result, bool failMachineOnError)
    {
        await using var db = await factory.CreateDbContextAsync();
        var machine = await db.RunMachines.FirstAsync(m => m.Id == runMachineId);
        machine.PreflightJson = Json.Write(result);
        if (failMachineOnError && result.HasErrors)
        {
            machine.Status = MachineStatus.Failed;
            machine.Error = string.Join(" ", result.Checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Message));
            machine.FinishedAt = Clock.UtcNow();
        }
        await db.SaveChangesAsync();
    }

    public async Task RecordPreviewAsync(int runMachineId, MachinePreview preview)
    {
        await using var db = await factory.CreateDbContextAsync();
        var machine = await db.RunMachines.FirstAsync(m => m.Id == runMachineId);
        machine.PreviewJson = Json.Write(preview);
        machine.PlannedStepsJson = Json.Write(preview.Steps.Select(s => s.Name).ToList());
        await db.SaveChangesAsync();
    }

    public async Task SetMachineStatusAsync(int runMachineId, MachineStatus status, string? error = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var machine = await db.RunMachines.FirstAsync(m => m.Id == runMachineId);
        machine.Status = status;
        if (error is not null) machine.Error = error;
        if (status is MachineStatus.Succeeded or MachineStatus.Failed or MachineStatus.Cancelled)
            machine.FinishedAt ??= Clock.UtcNow();
        await db.SaveChangesAsync();
    }

    /// <summary>Closes the run: unfinished machines become cancelled (or failed), and the run's status follows its machines.</summary>
    public async Task<RunStatus> FinishAsync(int runId, bool cancelled)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.Runs.Include(r => r.Machines).ThenInclude(m => m.Steps).FirstAsync(r => r.Id == runId);
        var now = Clock.UtcNow();

        foreach (var machine in run.Machines.Where(m => m.Status is MachineStatus.Waiting or MachineStatus.Running))
        {
            machine.Status = cancelled ? MachineStatus.Cancelled : run.Kind == RunKind.DryRun ? MachineStatus.Succeeded : MachineStatus.Failed;
            machine.FinishedAt ??= now;
            if (!cancelled && run.Kind != RunKind.DryRun)
                machine.Error ??= "The run ended before this machine finished.";
            foreach (var step in machine.Steps.Where(s => s.Status == StepStatus.Running))
            {
                step.Status = StepStatus.Failed;
                step.FinishedAt = now;
            }
        }

        run.FinishedAt = now;
        run.Status = cancelled
            ? RunStatus.Cancelled
            : run.Machines.Any(m => m.Status == MachineStatus.Failed) ? RunStatus.Failed : RunStatus.Succeeded;
        await db.SaveChangesAsync();
        return run.Status;
    }
}

/// <summary>
/// Writes one run's progress to the database as the runner reports it, and raises <see cref="Changed"/> so live
/// pages refresh. The runner reports from many machines at once; each machine's reports arrive in order.
/// </summary>
public sealed class RunRecorder : IDeploymentProgress
{
    private readonly IDbContextFactory<ShushDbContext> _factory;
    private readonly IReadOnlyDictionary<string, int> _machineIds;
    private readonly Func<string, string> _mask;
    private readonly Dictionary<string, int> _nextIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <param name="machineIds">Machine name → its <see cref="RunMachine"/> row.</param>
    /// <param name="mask">Applied to errors and outputs before they're stored (hides secrets).</param>
    public RunRecorder(IDbContextFactory<ShushDbContext> factory, IReadOnlyDictionary<string, int> machineIds, Func<string, string>? mask = null)
    {
        _factory = factory;
        _machineIds = machineIds;
        _mask = mask ?? (s => s);
    }

    /// <summary>A machine's row changed (its name).</summary>
    public event Action<string>? Changed;

    public void ReportPlan(string boxId, IReadOnlyList<string> stepNames) =>
        Update(boxId, (db, machine) =>
        {
            machine.PlannedStepsJson = Json.Write(stepNames);
            machine.Status = MachineStatus.Running;
            machine.StartedAt ??= Clock.UtcNow();
        });

    public void ReportStepStart(string boxId, string stepName) =>
        Update(boxId, (db, machine) =>
        {
            var index = _nextIndex.GetValueOrDefault(boxId);
            db.RunSteps.Add(new RunStep
            {
                RunMachineId = machine.Id, Index = index, Name = stepName, Status = StepStatus.Running, StartedAt = Clock.UtcNow(),
            });
        });

    public void ReportStep(string boxId, bool success, string stepName) =>
        Update(boxId, (db, machine) =>
        {
            var index = _nextIndex.GetValueOrDefault(boxId);
            var step = db.RunSteps.FirstOrDefault(s => s.RunMachineId == machine.Id && s.Index == index);
            if (step is null)
            {
                step = new RunStep { RunMachineId = machine.Id, Index = index, Name = stepName, StartedAt = Clock.UtcNow() };
                db.RunSteps.Add(step);
            }

            step.Status = success ? StepStatus.Succeeded : StepStatus.Failed;
            step.FinishedAt = Clock.UtcNow();
            _nextIndex[boxId] = index + 1;
        });

    public void ReportSkipped(string boxId, string stepName, string reason) =>
        Update(boxId, (db, machine) =>
        {
            var index = _nextIndex.GetValueOrDefault(boxId);
            var now = Clock.UtcNow();
            db.RunSteps.Add(new RunStep
            {
                RunMachineId = machine.Id, Index = index, Name = stepName, Status = StepStatus.Skipped, SkipReason = reason,
                StartedAt = now, FinishedAt = now,
            });
            _nextIndex[boxId] = index + 1;
        });

    public void ReportOutputs(string boxId, string stepName, IReadOnlyDictionary<string, string> outputs) =>
        Update(boxId, (db, machine) =>
        {
            var index = _nextIndex.GetValueOrDefault(boxId) - 1;
            var masked = outputs.ToDictionary(kv => kv.Key, kv => _mask(kv.Value));
            var step = db.RunSteps.FirstOrDefault(s => s.RunMachineId == machine.Id && s.Index == index);
            if (step is not null)
                step.OutputsJson = Json.Write(masked);
            if (masked.TryGetValue("commit", out var commit) && !string.IsNullOrWhiteSpace(commit))
                machine.Commit = commit;
        });

    public void ReportSucceeded(string boxId) =>
        Update(boxId, (db, machine) =>
        {
            machine.Status = MachineStatus.Succeeded;
            machine.FinishedAt = Clock.UtcNow();
        });

    public void ReportFailure(string boxId, Exception ex) =>
        Update(boxId, (db, machine) =>
        {
            machine.Status = MachineStatus.Failed;
            machine.FinishedAt = Clock.UtcNow();
            machine.Error = _mask(Describe(ex));
        });

    private static string Describe(Exception ex) => ex is AggregateException agg && agg.InnerExceptions.Count == 1
        ? agg.InnerExceptions[0].Message
        : ex.Message;

    private void Update(string boxId, Action<ShushDbContext, RunMachine> change)
    {
        if (!_machineIds.TryGetValue(boxId, out var id))
            return;

        lock (_sync)
        {
            using var db = _factory.CreateDbContext();
            var machine = db.RunMachines.First(m => m.Id == id);
            change(db, machine);
            db.SaveChanges();
        }

        Changed?.Invoke(boxId);
    }
}
