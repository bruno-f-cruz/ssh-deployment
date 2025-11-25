using Microsoft.Extensions.Logging;
using System.CommandLine;

var tag = new Option<string>(
    aliases: new[] { "--tag", "-t" },
    description: "Semantic version of the repository to deploy")
{ IsRequired = true };

var repoPathOption = new Option<string>(
    aliases: new[] { "--repository-path", "-r" },
    description: "Remote repository path")
{ IsRequired = true };

var rootCommand = new RootCommand("SSH Deployment Tool")
{
    tag,
    repoPathOption
};

rootCommand.SetHandler(async (string tag, string remotePath) =>
{
    using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
    ILogger logger = factory.CreateLogger("Shush");

    logger.LogInformation("Starting deployment for tag {tag}.", tag);

    var machines = await MachineManager.GetComputers();
    var secrets = Secrets.Load();

    await Parallel.ForEachAsync(machines, async (kv, cancellationToken) =>
    {
        try
        {
            var deployer = new MachineDeployer(kv.Key, kv.Value, secrets, tag, logger, remotePath);
            await deployer.Deploy(cancellationToken);
            logger.LogInformation("Successfully deployed to machine {Machine}.", kv.Key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deploying to machine {Machine}.", kv.Key);
        }
    });
}, tag, repoPathOption);

return await rootCommand.InvokeAsync(args);


