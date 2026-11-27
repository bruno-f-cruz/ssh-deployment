using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Shush;
using Shush.Data;
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
var paths = new ShushPaths(builder.Environment.ContentRootPath, shushSettings);

builder.Host.UseWindowsService(options => options.ServiceName = "ShushDeployment");

var appLogPath = Path.Combine(paths.LogsDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
builder.Logging.AddProvider(new FileLoggerProvider(appLogPath, append: true));
builder.Services.AddSingleton(new AppLogPath(appLogPath));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(shushSettings);
builder.Services.AddSingleton(paths);

// Secrets (variable values) are encrypted with keys kept next to the database.
builder.Services.AddDataProtection()
    .SetApplicationName("Shush")
    .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDirectory));
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();

// The database: one SQLite file in the data folder.
builder.Services.AddDbContextFactory<ShushDbContext>(o =>
    o.UseSqlite($"Data Source={paths.DatabaseFile}", sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

// The recipe engine: step catalog, functions, and suggested values for inputs (e.g. clabe releases, git refs).
builder.Services.AddSingleton(new StepRegistry(typeof(IRecipe).Assembly));
builder.Services.AddSingleton(FunctionLibrary.Default);
builder.Services.AddSingleton(Shush.Recipe.Options.OptionSourceRegistry.Default);

builder.Services.AddSingleton<RecipeService>();
builder.Services.AddSingleton<GroupService>();
builder.Services.AddSingleton<MachineService>();
builder.Services.AddSingleton<VariableService>();
builder.Services.AddSingleton<DeploymentService>();
builder.Services.AddSingleton<RunService>();
builder.Services.AddSingleton<Seeder>();

builder.Services.AddSingleton<MachineRegistryService>();
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddSingleton<HealthChecker>();
builder.Services.AddHostedService<HealthProbeService>();

// Per browser tab: the operator's SSH credentials (memory only), and toasts.
builder.Services.AddScoped<CredentialStore>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<ClientTime>();

var app = builder.Build();

// Bring the database up to date before serving: migrations, shipped content, the one-time v1 import, and runs an
// earlier shutdown cut short.
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<Seeder>();
    await seeder.RunAsync(new SeedPaths(
        ShushPaths.Shipped("Recipes"), ShushPaths.Shipped("Groups"), ShushPaths.Shipped("Deployments"), paths.DataDirectory));

    var interrupted = await scope.ServiceProvider.GetRequiredService<RunService>().MarkInterruptedAsync();
    if (interrupted > 0)
        app.Logger.LogWarning("{Count} run(s) were still running when the app stopped; marked interrupted.", interrupted);
}

// Lets the app be served under a subpath behind a reverse proxy (e.g. Traefik PathPrefix
// routing without stripping the prefix) — <base href> is derived from this at render time.
// UseRouting must come right after UsePathBase, or routes (and antiforgery, which depends on
// the matched endpoint) see the pre-rewrite path: https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/app-base-path
if (!string.IsNullOrEmpty(shushSettings.BasePath))
{
    app.UsePathBase(shushSettings.BasePath);
    app.UseRouting();
}

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
