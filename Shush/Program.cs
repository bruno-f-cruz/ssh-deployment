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
    await using var services = new ServiceCollection()
        .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug))
        .BuildServiceProvider();

    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<Program>();

    // Discover all IRecipe implementations in this assembly via reflection
    var recipeTypes = typeof(IRecipe).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IRecipe)));

    var recipes = recipeTypes
        .Select(t => (IRecipe?)Activator.CreateInstance(t))
        .OfType<IRecipe>()
        .ToList();

    var recipe = recipes.FirstOrDefault(r => r.Name.Equals(recipeName, StringComparison.OrdinalIgnoreCase));

    if (recipe is null)
    {
        var available = string.Join(", ", recipes.Select(r => $"'{r.Name}'"));
        logger.LogError("Recipe '{RecipeName}' not found. Available recipes: {Available}", recipeName, available);
        Environment.Exit(1);
        return;
    }

    logger.LogInformation("Using recipe '{Recipe}'.", recipe.Name);

    var machines = await MachineLoader.LoadAsync(machinesPath);
    logger.LogInformation("Loaded {Count} machine(s) from '{Path}'.", machines.Count, machinesPath);

    var secrets = Secrets.Load();

    var runner = new RecipeRunner(recipe, machines, secrets, loggerFactory);
    await runner.RunAsync();

}, recipeOption, machinesOption);

return await rootCommand.InvokeAsync(args);
