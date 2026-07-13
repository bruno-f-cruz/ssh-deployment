using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shush;
using Shush.Recipe;
using System.CommandLine;

var recipeOption = new Option<string>(
    aliases: ["--recipe", "-r"],
    description: "Name of the recipe to run (matched by IRecipe.Name, case-insensitive)")
{ IsRequired = true };

var machinesOption = new Option<string>(
    aliases: ["--machines", "-m"],
    description: "Path to a machines YAML file (list of machine names)")
{ IsRequired = true };

var envFileOption = new Option<string?>(
    aliases: ["--env-file", "-e"],
    description: "Path to a .env file to load. If omitted, settings come from real environment variables only.");

var rootCommand = new RootCommand("SSH Deployment Tool")
{
    recipeOption,
    machinesOption,
    envFileOption
};

rootCommand.SetHandler(async (string recipeName, string machinesPath, string? envFilePath) =>
{
    if (envFilePath is not null)
    {
        if (!File.Exists(envFilePath))
        {
            Console.Error.WriteLine($"--env-file '{envFilePath}' was not found.");
            Environment.Exit(1);
            return;
        }

        DotNetEnv.Env.Load(envFilePath);
    }

    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    var settings = configuration.Get<ShushSettings>() ?? new ShushSettings();

    var logFile = $"deploy_{DateTime.Now:yyyyMMdd_HHmmss}.log";

    await using var services = new ServiceCollection()
        .AddLogging(b => b
            .ClearProviders()
            .AddProvider(new Shush.FileLoggerProvider(logFile))
            .SetMinimumLevel(LogLevel.Debug))
        .BuildServiceProvider();

    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<Program>();

    var recipes = RecipeCatalog.Discover();

    var recipe = recipes.FirstOrDefault(r => r.Name.Equals(recipeName, StringComparison.OrdinalIgnoreCase));

    if (recipe is null)
    {
        var available = string.Join(", ", recipes.Select(r => $"'{r.Name}'"));
        Console.Error.WriteLine($"Recipe '{recipeName}' not found. Available recipes: {available}");
        Environment.Exit(1);
        return;
    }

    var machines = await MachineLoader.LoadAsync(machinesPath, settings);

    var secrets = settings.Credentials;

    Console.WriteLine($"Recipe '{recipe.Name}' — {machines.Count} machine(s)  [log: {logFile}]");
    Console.WriteLine();

    var display = new Shush.DeploymentDisplay(machines.Keys.ToList());

    var runner = new RecipeRunner(recipe, machines, secrets, loggerFactory, display);

    try
    {
        await runner.RunAsync();
    }
    catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
    {
        logger.LogError(ex, "One or more deployments failed.");
    }

    display.PrintSummary();

}, recipeOption, machinesOption, envFileOption);

return await rootCommand.InvokeAsync(args);
