using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shush;
using Shush.Recipe;

namespace Shush.Design.Services;

public sealed class DeploymentOrchestrator
{
    private readonly IWebHostEnvironment _env;
    private readonly ShushSettings _settings;

    public DeploymentOrchestrator(IWebHostEnvironment env, ShushSettings settings)
    {
        _env = env;
        _settings = settings;
    }

    public async Task RunAsync(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        Secrets secrets,
        BlazorDeploymentProgress progress,
        CancellationToken ct = default)
    {
        var logDirectory = Path.Combine(ShushPaths.GetShushDirectory(_env, _settings), "logs");
        Directory.CreateDirectory(logDirectory);
        var logFile = Path.Combine(logDirectory, $"deploy_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        progress.LogFilePath = logFile;

        await using var services = new ServiceCollection()
            .AddLogging(b => b
                .ClearProviders()
                .AddProvider(new FileLoggerProvider(logFile))
                .SetMinimumLevel(LogLevel.Debug))
            .BuildServiceProvider();

        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var runner = new RecipeRunner(recipe, machines, secrets, loggerFactory, progress);

        try
        {
            await runner.RunAsync(ct);
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Failures are already reflected per-machine via progress reports.
        }
        finally
        {
            progress.MarkRemainingAsSucceeded();
        }
    }
}
