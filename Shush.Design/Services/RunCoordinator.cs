using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shush;
using Shush.Data;
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Design.Services;

/// <param name="Machines">A subset of the group's machines (e.g. retrying the failed ones). Null: the whole group.</param>
/// <param name="Params">The values to deploy with. Null: what the deployment (or, for a rollback or retry, the source run) used.</param>
/// <param name="SourceRunId">The run a retry or rollback repeats: its recipe version and parameter values are used.</param>
public sealed record StartRunRequest(
    int DeploymentId, RunKind Kind, IReadOnlyList<string>? Machines = null, IReadOnlyDictionary<string, string>? Params = null,
    int? SourceRunId = null);

/// <summary>
/// Owns runs on the server, so they don't depend on a browser tab: closing or refreshing the page doesn't stop a
/// run, and anyone can watch it. The SSH credentials are held only for the run's duration.
///
/// A run: look every machine up in the registry, resolve its variables, run the pre-flight checks (machines with an
/// error are left out), then either preview the plan (dry run) or run the recipe.
/// </summary>
public sealed class RunCoordinator(
    IDbContextFactory<ShushDbContext> factory,
    DeploymentService deployments,
    RecipeService recipes,
    VariableService variables,
    RunService runs,
    MachineService machines,
    MachineRegistryService registry,
    StepRegistry stepRegistry,
    FunctionLibrary functions,
    ShushSettings settings,
    ShushPaths paths,
    ILogger<RunCoordinator> logger)
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _active = new();

    /// <summary>A run's rows changed (its id). Raised from background threads.</summary>
    public event Action<int>? RunChanged;

    public bool IsActive(int runId) => _active.ContainsKey(runId);

    public IReadOnlyCollection<int> ActiveRunIds => _active.Keys.ToList();

    public void Cancel(int runId)
    {
        if (_active.TryGetValue(runId, out var cts))
            cts.Cancel();
    }

    /// <summary>
    /// Validates the request, records the run, and starts it in the background. Returns the run's id. Throws
    /// <see cref="RecipeValidationException"/> or <see cref="ShushDataException"/> with a message for the operator.
    /// </summary>
    public async Task<int> StartAsync(StartRunRequest request, Secrets secrets, string? user)
    {
        if (string.IsNullOrWhiteSpace(secrets.Username) || string.IsNullOrEmpty(secrets.Password))
            throw new ShushDataException("Sign in with your SSH username and password first.");

        var deployment = await deployments.GetAsync(request.DeploymentId)
            ?? throw new ShushDataException("That deployment no longer exists.");

        Run? source = null;
        if (request.SourceRunId is { } sourceId)
            source = await runs.GetAsync(sourceId) ?? throw new ShushDataException("The run to repeat no longer exists.");

        var version = source is not null && request.Kind is RunKind.Rollback or RunKind.Retry
            ? source.RecipeVersion
            : await recipes.CurrentVersionAsync(deployment.RecipeId);

        var document = YamlRecipeSerializer.Deserialize(version.Yaml);
        recipes.Validator.Validate(document);

        var requested = request.Params
            ?? (source is not null ? Json.ReadParams(source.ParamValuesJson) : Json.ReadParams(deployment.ParamValuesJson));
        var paramValues = DeploymentService.EffectiveParams(document, requested);

        var missing = document.Params.Where(p => p.Value.Default is null && string.IsNullOrEmpty(paramValues.GetValueOrDefault(p.Key))).Select(p => p.Key).ToList();
        if (missing.Count > 0)
            throw new ShushDataException($"Fill in {string.Join(", ", missing)} first.");

        var targets = (request.Machines ?? deployment.Group.Members.OrderBy(m => m.Position).Select(m => m.MachineName).ToList())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targets.Count == 0)
            throw new ShushDataException($"'{deployment.Group.Name}' has no machines. Add some to the group first.");

        if (request.Kind is RunKind.Deploy or RunKind.DryRun)
            await deployments.SaveParamValuesAsync(deployment.Id, paramValues);

        var logFile = Path.Combine(paths.LogsDirectory, $"run_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.log");
        var run = await runs.CreateAsync(new NewRun(
            request.Kind, deployment.Id, deployment.Name, deployment.RecipeId, version.Id, deployment.GroupId, deployment.Group.Name,
            paramValues, user, targets, logFile, request.SourceRunId));

        var cts = new CancellationTokenSource();
        _active[run.Id] = cts;
        var recipe = new SerializedRecipe(document, stepRegistry, functions, paramValues);
        var credentials = new Secrets { Username = secrets.Username, Password = secrets.Password };
        var machineIds = run.Machines.ToDictionary(m => m.MachineName, m => m.Id, StringComparer.OrdinalIgnoreCase);

        _ = Task.Run(() => ExecuteAsync(run.Id, request.Kind, deployment.GroupId, recipe, machineIds, credentials, logFile, cts));
        Raise(run.Id);
        return run.Id;
    }

    private async Task ExecuteAsync(
        int runId, RunKind kind, int groupId, SerializedRecipe recipe, IReadOnlyDictionary<string, int> machineIds,
        Secrets secrets, string logFile, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        using var logs = LoggerFactory.Create(b => b.AddProvider(new FileLoggerProvider(logFile)).SetMinimumLevel(LogLevel.Debug));
        var runLog = logs.CreateLogger("Shush.Run");

        try
        {
            runLog.LogInformation("Run {RunId}: {Kind} of '{Recipe}' to {Count} machine(s).", runId, kind, recipe.Name, machineIds.Count);

            // 1. Look the machines up and resolve their variables.
            var infos = new ConcurrentDictionary<string, MachineInfo>(StringComparer.OrdinalIgnoreCase);
            var runSettings = new ConcurrentDictionary<string, MachineRunSettings>(StringComparer.OrdinalIgnoreCase);
            var allResolved = new ConcurrentBag<ResolvedVariable>();

            await Parallel.ForEachAsync(machineIds, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (kv, token) =>
            {
                var (machine, id) = kv;
                MachineInfo? info;
                try
                {
                    info = await registry.ResolveAsync(machine);
                }
                catch (Exception ex)
                {
                    await FailMachineAsync(runId, id, $"Couldn't reach the machine registry: {ex.Message}");
                    return;
                }

                if (info is null)
                {
                    await FailMachineAsync(runId, id, "Not found in the machine registry.");
                    await machines.RecordUnresolvedAsync(machine, "Not found in the machine registry.");
                    return;
                }

                var resolved = await variables.ResolveAsync(groupId, machine);
                foreach (var variable in resolved)
                    allResolved.Add(variable);
                infos[machine] = info;
                runSettings[machine] = VariableService.ToRunSettings(resolved);
            });

            var mask = VariableService.Masker(allResolved);

            // 2. Pre-flight: a machine with an error is left out (it's reported, not deployed).
            var ready = new ConcurrentDictionary<string, MachineInfo>(StringComparer.OrdinalIgnoreCase);
            await Parallel.ForEachAsync(infos, new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentMachines, CancellationToken = ct }, async (kv, token) =>
            {
                var (machine, info) = kv;
                var result = await Preflight.CheckAsync(machine, info, secrets, logs, token);
                await runs.RecordPreflightAsync(machineIds[machine], result, failMachineOnError: true);
                if (result.Facts is not null)
                    await machines.RecordFactsAsync(machine, result.Facts);
                if (!result.HasErrors)
                    ready[machine] = info;
                Raise(runId);
            });

            // 3a. Dry run: the plan each machine would follow. Nothing changes on the rigs.
            if (kind == RunKind.DryRun)
            {
                foreach (var machine in infos.Keys)
                {
                    ct.ThrowIfCancellationRequested();
                    var preview = PlanPreview.For(machine, recipe, runSettings[machine], mask);
                    await runs.RecordPreviewAsync(machineIds[machine], preview);
                    if (ready.ContainsKey(machine))
                        await runs.SetMachineStatusAsync(machineIds[machine],
                            preview.Error is null ? MachineStatus.Succeeded : MachineStatus.Failed, preview.Error);
                    Raise(runId);
                }

                return;
            }

            // 3b. Deploy.
            if (ready.IsEmpty)
                return;

            var recorder = new RunRecorder(factory, machineIds, mask);
            recorder.Changed += _ => Raise(runId);

            var runner = new RecipeRunner(
                recipe,
                ready.ToDictionary(kv => kv.Key, kv => kv.Value),
                secrets,
                logs,
                recorder,
                settings.SharedAccess.ToGrants(),
                machine => runSettings.GetValueOrDefault(machine) ?? MachineRunSettings.Empty,
                settings.MaxConcurrentMachines);

            try
            {
                await runner.RunAsync(ct);
            }
            catch (AggregateException)
            {
                // Failures are already recorded per machine.
            }
        }
        catch (OperationCanceledException)
        {
            runLog.LogWarning("Run {RunId} was cancelled.", runId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} stopped unexpectedly.", runId);
            runLog.LogError(ex, "Run {RunId} stopped unexpectedly.", runId);
        }
        finally
        {
            try
            {
                var status = await runs.FinishAsync(runId, cancelled: cts.IsCancellationRequested);
                runLog.LogInformation("Run {RunId} finished: {Status}.", runId, status);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't record the end of run {RunId}.", runId);
            }

            _active.TryRemove(runId, out _);
            cts.Dispose();
            Raise(runId);
        }
    }

    private async Task FailMachineAsync(int runId, int runMachineId, string error)
    {
        await runs.SetMachineStatusAsync(runMachineId, MachineStatus.Failed, error);
        Raise(runId);
    }

    private void Raise(int runId)
    {
        try
        {
            RunChanged?.Invoke(runId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A run listener failed.");
        }
    }
}
