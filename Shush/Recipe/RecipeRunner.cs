using Microsoft.Extensions.Logging;
using Shush.Recipe.Steps;

namespace Shush.Recipe;

/// <summary>An environment variable the runner sets on a machine before the recipe's steps.</summary>
/// <param name="Scope">Machine (default) or User.</param>
/// <param name="Secret">Redact the value from logs.</param>
public sealed record EnvironmentAssignment(string Name, string Value, string Scope = "Machine", bool Secret = false);

/// <summary>What differs per machine in a run: the variables <c>${group.NAME}</c> reads, and environment variables to set.</summary>
public sealed class MachineRunSettings
{
    public static MachineRunSettings Empty { get; } = new();

    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<EnvironmentAssignment> Environment { get; init; } = [];
}

public class RecipeRunner
{
    /// <summary>The name the environment-variables phase reports under, as if it were a step.</summary>
    public const string EnvironmentStepName = "Environment variables";

    private readonly IRecipe _recipe;
    private readonly Dictionary<string, MachineInfo> _machines;
    private readonly Secrets _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RecipeRunner> _logger;
    private readonly IDeploymentProgress? _display;
    private readonly IReadOnlyList<SharedAccessGrant> _machineWideAccess;
    private readonly Func<string, MachineRunSettings> _settingsFor;
    private readonly int _maxConcurrency;

    /// <param name="machineWideAccess">
    /// Grants applied on every machine before any recipe-declared ones (from <see cref="ShushSettings.SharedAccess"/>).
    /// </param>
    /// <param name="settingsFor">Each machine's variables and environment variables; none when null.</param>
    public RecipeRunner(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        Secrets secrets,
        ILoggerFactory loggerFactory,
        IDeploymentProgress? display = null,
        IReadOnlyList<SharedAccessGrant>? machineWideAccess = null,
        Func<string, MachineRunSettings>? settingsFor = null,
        int maxConcurrency = 16)
    {
        _machineWideAccess = machineWideAccess ?? [];
        var errors = _machineWideAccess.SelectMany(g => g.Validate()).ToList();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(machineWideAccess));

        _recipe = recipe;
        _machines = machines;
        _secrets = secrets;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RecipeRunner>();
        _display = display;
        _settingsFor = settingsFor ?? (_ => MachineRunSettings.Empty);
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    /// <summary>The step names a machine reports, in order: the environment phase (when it has any), then the recipe's steps.</summary>
    public static IReadOnlyList<string> PlannedStepNames(IRecipe recipe, MachineRunSettings settings) =>
        settings.Environment.Count > 0 ? [EnvironmentStepName, .. recipe.StepNames] : recipe.StepNames;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var failures = new System.Collections.Concurrent.ConcurrentDictionary<string, Exception>();

        ThreadPool.GetMinThreads(out _, out var minIocp);
        ThreadPool.SetMinThreads(_maxConcurrency, minIocp);

        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = _maxConcurrency,
        };

        await Parallel.ForEachAsync(_machines, options, async (kv, cancellationToken) =>
        {
            var (boxId, machineInfo) = (kv.Key, kv.Value);
            var stepFailureLogged = false;
            var settings = _settingsFor(boxId);
            _display?.ReportPlan(boxId, PlannedStepNames(_recipe, settings));

            _logger.LogInformation("[{BoxId}] Starting recipe '{Recipe}'.", boxId, _recipe.Name);

            try
            {
                await using var context = await MachineContext.ConnectAsync(boxId, machineInfo, _secrets, _loggerFactory, cancellationToken);

                var plan = _recipe.CreatePlan(new PlanContext(boxId, settings.Variables));

                // Grant before any step creates files: new files inherit the ACL of their parent.
                foreach (var grant in _machineWideAccess.Concat(plan.SharedAccess).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation(
                        "[{BoxId}] Granting {Principal}:{Rights} on '{Path}' (inherited).",
                        boxId, grant.Principal, grant.Rights, grant.Path);
                    await context.EnsureSharedAccessAsync(grant, cancellationToken);
                }

                if (settings.Environment.Count > 0)
                    await ApplyEnvironmentAsync(boxId, context, settings.Environment, cancellationToken);

                foreach (var planned in plan.Steps())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var stepName = planned.DisplayName;

                    if (!planned.Enabled)
                    {
                        var reason = planned.SkipReason ?? "disabled";
                        _logger.LogInformation("[{BoxId}] Skipping step: {Step} ({Reason}).", boxId, stepName, reason);
                        _display?.ReportSkipped(boxId, stepName, reason);
                        continue;
                    }

                    _logger.LogInformation("[{BoxId}] Executing step: {Step}.", boxId, stepName);
                    _display?.ReportStepStart(boxId, stepName);

                    try
                    {
                        await planned.Step!.ExecuteAsync(context, cancellationToken);
                        var outputs = planned.CaptureOutputs();
                        _logger.LogInformation("[{BoxId}] Step '{Step}' completed successfully.", boxId, stepName);
                        _display?.ReportStep(boxId, success: true, stepName);
                        if (outputs.Count > 0)
                            _display?.ReportOutputs(boxId, stepName, outputs);
                    }
                    catch (Exception ex)
                    {
                        stepFailureLogged = true;
                        // A failed command's output is already in the log; say what failed in a few lines instead of repeating it.
                        if (ex is RemoteCommandException)
                            _logger.LogError("[{BoxId}] Step '{Step}' failed: {Reason}", boxId, stepName, ErrorSummary.Summarize(ex.Message).Replace("\n", " | "));
                        else
                            _logger.LogError(ex, "[{BoxId}] Step '{Step}' failed.", boxId, stepName);
                        _display?.ReportStep(boxId, success: false, stepName);
                        throw;
                    }
                }

                _logger.LogInformation("[{BoxId}] Recipe '{Recipe}' finished.", boxId, _recipe.Name);
                _display?.ReportSucceeded(boxId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures[boxId] = ex;
                if (!stepFailureLogged)
                    _logger.LogError(ex, "[{BoxId}] Failed.", boxId);
                _display?.ReportFailure(boxId, ex);
            }
        });

        if (!failures.IsEmpty)
        {
            var summary = string.Join(", ", failures.Keys);
            _logger.LogError("Recipe failed on {Count} machine(s): {Machines}", failures.Count, summary);
            throw new AggregateException(
                $"Recipe '{_recipe.Name}' failed on {failures.Count} machine(s): {summary}",
                failures.Values);
        }
    }

    private async Task ApplyEnvironmentAsync(
        string boxId, MachineContext context, IReadOnlyList<EnvironmentAssignment> environment, CancellationToken ct)
    {
        _logger.LogInformation("[{BoxId}] Executing step: {Step}.", boxId, EnvironmentStepName);
        _display?.ReportStepStart(boxId, EnvironmentStepName);
        try
        {
            foreach (var assignment in environment)
            {
                ct.ThrowIfCancellationRequested();
                var step = new SetEnvironmentVariableStep
                {
                    Name = assignment.Name,
                    Value = assignment.Value,
                    Scope = assignment.Scope,
                    Secret = assignment.Secret,
                };
                await step.ExecuteAsync(context, ct);
            }

            _display?.ReportStep(boxId, success: true, EnvironmentStepName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[{BoxId}] Step '{Step}' failed.", boxId, EnvironmentStepName);
            _display?.ReportStep(boxId, success: false, EnvironmentStepName);
            throw;
        }
    }
}
