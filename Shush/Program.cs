using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

var rootCommand = new RootCommand("SSH Deployment Tool")
{
    recipeOption,
    machinesOption
};

rootCommand.SetHandler(async (string recipeName, string machinesPath) =>
{
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

    var machines = await MachineLoader.LoadAsync(machinesPath);

    var secrets = Secrets.Load();

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

}, recipeOption, machinesOption);

return await rootCommand.InvokeAsync(args);
