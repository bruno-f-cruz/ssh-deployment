using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Semver;
using System.CommandLine;

using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
ILogger logger = factory.CreateLogger("Shush");

if (args.Length != 1)
{
    throw new ArgumentException("Expected one argument with the semantic version of the repository to deploy.");
}

var version = args[0];
const string remotePath = "C:/git/Aind.Behavior.VrForaging";

var semVersion = SemVersion.Parse(version);
logger.LogInformation("Starting deployment for version {Version}.", semVersion);

var machines = await MachineManager.GetComputers();
machines = machines.Where(kv => kv.Key.Contains("FRG.0")).ToDictionary(kv => kv.Key, kv => kv.Value);
var secrets = Secrets.Load();

await Parallel.ForEachAsync(machines, async (kv, cancellationToken) =>
    {
        var deployer = new MachineDeployer(kv.Key, kv.Value, secrets, semVersion, logger, remotePath);
        deployer.Deploy(cancellationToken);
    }
);


