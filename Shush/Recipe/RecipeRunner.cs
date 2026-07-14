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

    public RecipeRunner(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        Secrets secrets,
        ILoggerFactory loggerFactory,
        IDeploymentProgress? display = null)
    {
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

                foreach (var step in _recipe.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var stepName = step.GetType().Name;
                    _logger.LogInformation("[{BoxId}] Executing step: {Step}.", boxId, stepName);
                    _display?.ReportStepStart(boxId, stepName);

                    try
                    {
                        await step.ExecuteAsync(context, cancellationToken);
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
