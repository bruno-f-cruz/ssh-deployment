using Microsoft.Extensions.Hosting.WindowsServices;
using Shush;
using Shush.Design.Components;
using Shush.Design.Services;
using Shush.Recipe;
using Shush.Recipe.Serialization;

// A Windows Service's default working directory is %SystemRoot%\System32, not wherever the
// exe lives — resolve the real app directory once and use it for ContentRootPath.
var isWindowsService = WindowsServiceHelpers.IsWindowsService();
var appDirectory = isWindowsService ? AppContext.BaseDirectory : Directory.GetCurrentDirectory();

var envFileArg = GetArgValue(args, "--env-file") ?? GetArgValue(args, "-e");
if (envFileArg is not null)
{
    if (!File.Exists(envFileArg))
        throw new FileNotFoundException($"--env-file '{envFileArg}' was not found.");

    DotNetEnv.Env.Load(envFileArg);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = isWindowsService ? appDirectory : default,
});

var shushSettings = builder.Configuration.Get<ShushSettings>() ?? new ShushSettings();

builder.Host.UseWindowsService(options => options.ServiceName = "ShushDeployment");

var appLogDirectory = Path.Combine(ShushPaths.GetShushDirectory(builder.Environment, shushSettings), "logs");
Directory.CreateDirectory(appLogDirectory);
var appLogPath = Path.Combine(appLogDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
builder.Logging.AddProvider(new FileLoggerProvider(appLogPath, append: true));
builder.Services.AddSingleton(new AppLogPath(appLogPath));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(shushSettings);
builder.Services.AddSingleton<MachineRegistryService>();
builder.Services.AddSingleton<DeploymentOrchestrator>();

// SSH credentials are entered per session by the operator (never shipped with the app).
builder.Services.AddScoped<CredentialStore>();

// Recipe engine wiring: step catalog, functions, YAML discovery, and per-recipe deploy state.
builder.Services.AddSingleton(new StepRegistry(typeof(IRecipe).Assembly));
builder.Services.AddSingleton(FunctionLibrary.Default);
builder.Services.AddSingleton<RecipeStore>();
builder.Services.AddSingleton(new RecipePaths(
    BaseDir: Path.Combine(AppContext.BaseDirectory, "Recipes"),
    UserDir: ShushPaths.GetUserRecipesDirectory(builder.Environment, shushSettings)));
builder.Services.AddSingleton(new DeployStateStore(
    Path.Combine(ShushPaths.GetShushDirectory(builder.Environment, shushSettings), "state")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length)
            return args[i + 1];
        if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
            return args[i][(name.Length + 1)..];
    }
    return null;
}
