using Microsoft.Extensions.Logging;
using Shush;

namespace Shush.Recipe;

public class RecipeRunner
{
    private const int MaxConcurrentDeployments = 16;

    private readonly IRecipe _recipe;
    private readonly Dictionary<string, MachineInfo> _machines;
    private readonly Secrets _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RecipeRunner> _logger;
    private readonly IDeploymentProgress? _display;
    private readonly IReadOnlyList<SharedAccessGrant> _machineWideAccess;

    /// <param name="machineWideAccess">
    /// Grants applied on every machine before any recipe-declared ones (from <see cref="ShushSettings.SharedAccess"/>).
    /// </param>
    public RecipeRunner(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        Secrets secrets,
        ILoggerFactory loggerFactory,
        IDeploymentProgress? display = null,
        IReadOnlyList<SharedAccessGrant>? machineWideAccess = null)
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
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var failures = new System.Collections.Concurrent.ConcurrentDictionary<string, Exception>();

        ThreadPool.GetMinThreads(out _, out var minIocp);
        ThreadPool.SetMinThreads(MaxConcurrentDeployments, minIocp);

        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = MaxConcurrentDeployments,
        };

        await Parallel.ForEachAsync(_machines, options, async (kv, cancellationToken) =>
        {
            var (boxId, machineInfo) = (kv.Key, kv.Value);

            _logger.LogInformation("[{BoxId}] Starting recipe '{Recipe}'.", boxId, _recipe.Name);

            try
            {
                await using var context = await MachineContext.ConnectAsync(boxId, machineInfo, _secrets, _loggerFactory, cancellationToken);

                var plan = _recipe.CreatePlan();

                // Grant before any step creates files: new files inherit the ACL of their parent.
                foreach (var grant in _machineWideAccess.Concat(plan.SharedAccess).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation(
                        "[{BoxId}] Granting {Principal}:{Rights} on '{Path}' (inherited).",
                        boxId, grant.Principal, grant.Rights, grant.Path);
                    await context.EnsureSharedAccessAsync(grant, cancellationToken);
                }

                foreach (var planned in plan.Steps())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var stepName = planned.DisplayName;

                    if (!planned.Enabled)
                    {
                        _logger.LogInformation("[{BoxId}] Skipping step: {Step} (disabled).", boxId, stepName);
                        _display?.ReportStep(boxId, success: true, $"{stepName} (skipped)");
                        continue;
                    }

                    _logger.LogInformation("[{BoxId}] Executing step: {Step}.", boxId, stepName);
                    _display?.ReportStepStart(boxId, stepName);

                    try
                    {
                        await planned.Step!.ExecuteAsync(context, cancellationToken);
                        planned.CaptureOutputs();
                        _logger.LogInformation("[{BoxId}] Step '{Step}' completed successfully.", boxId, stepName);
                        _display?.ReportStep(boxId, success: true, stepName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{BoxId}] Step '{Step}' failed.", boxId, stepName);
                        _display?.ReportStep(boxId, success: false, stepName);
                        throw;
                    }
                }

                _logger.LogInformation("[{BoxId}] Recipe '{Recipe}' finished.", boxId, _recipe.Name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures[boxId] = ex;
                _display?.ReportFailure(boxId, ex);
            }
        });

        if (!failures.IsEmpty)
        {
            var summary = string.Join(", ", failures.Keys);
            _logger.LogError("Recipe failed on {Count} machine(s): {Machines}", failures.Count, summary);
            foreach (var (boxId, ex) in failures)
            {
                _logger.LogError(ex, "[{BoxId}] Failure details.", boxId);
            }
            throw new AggregateException(
                $"Recipe '{_recipe.Name}' failed on {failures.Count} machine(s): {summary}",
                failures.Values);
        }
    }
}
