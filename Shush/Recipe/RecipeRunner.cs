using Microsoft.Extensions.Logging;
using Shush;

namespace Shush.Recipe;

public class RecipeRunner
{
    private readonly IRecipe _recipe;
    private readonly Dictionary<string, MachineInfo> _machines;
    private readonly Secrets _secrets;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RecipeRunner> _logger;
    private readonly DeploymentDisplay? _display;

    public RecipeRunner(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        Secrets secrets,
        ILoggerFactory loggerFactory,
        DeploymentDisplay? display = null)
    {
        _recipe = recipe;
        _machines = machines;
        _secrets = secrets;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RecipeRunner>();
        _display = display;
    }

    public Task RunAsync(CancellationToken ct = default)
    {
        return Parallel.ForEachAsync(_machines, ct, async (kv, cancellationToken) =>
        {
            var (boxId, machineInfo) = (kv.Key, kv.Value);

            _logger.LogInformation("[{BoxId}] Starting recipe '{Recipe}'.", boxId, _recipe.Name);

            await using var context = await MachineContext.ConnectAsync(boxId, machineInfo, _secrets, _loggerFactory, cancellationToken);

            foreach (var step in _recipe.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepName = step.GetType().Name;
                _logger.LogInformation("[{BoxId}] Executing step: {Step}.", boxId, stepName);

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
        });
    }
}
