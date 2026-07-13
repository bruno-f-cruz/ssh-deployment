using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shush;
using Shush.Recipe;

namespace Shush.Design.Services;

public sealed class DeploymentOrchestrator
{
    private readonly IWebHostEnvironment _env;

    public DeploymentOrchestrator(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task RunAsync(
        IRecipe recipe,
        Dictionary<string, MachineInfo> machines,
        BlazorDeploymentProgress progress,
        CancellationToken ct = default)
    {
        // dotnet run sets the process's working directory to this project's folder, not the
        // repo root, so relative paths like Secrets.Load()'s default "secrets.json" won't
        // resolve — anchor explicitly to the repo root (Shush.Design's parent), where
        // secrets.json and frg-machines.yaml already live for the CLI.
        var repoRoot = ShushPaths.GetRepoRoot(_env);
        var secrets = Secrets.Load(Path.Combine(repoRoot, "secrets.json"));

        var logDirectory = Path.Combine(ShushPaths.GetShushDirectory(_env), "logs");
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
