using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Semver;

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
    logger.LogDebug("Found machine {Machine} with rig ID {RigId} and hostname {Hostname}.", kv.Key, kv.Value.rig_id, kv.Value.hostname);
    using(var sshClient = new SshClient(kv.Value.hostname, secrets.Username, secrets.Password))
    {
        await sshClient.ConnectAsync(cancellationToken);

        string[] shellCommand = {
            "cd " + remotePath,
            "git fetch --all --tags --prune",
            "git clean -fd",
            "git reset --hard",
            "git checkout " + "tags/v" + semVersion.ToString(),
        };
        ExecuteCommand(sshClient, shellCommand, logger);
        sshClient.Disconnect();
    }

    using (var scpClient = new ScpClient(kv.Value.hostname, secrets.Username, secrets.Password))
    {
        await scpClient.ConnectAsync(cancellationToken);

        var sourceDir = new DirectoryInfo("./FilesToTransfer");
        foreach (var file in sourceDir.GetFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
            var remoteFilePath = Path.Combine(remotePath, relativePath).Replace('\\', '/');

            var dirName = Path.GetDirectoryName(remoteFilePath);
            var remoteDir = dirName != null ? dirName.Replace('\\', '/') : string.Empty;
            scpClient.Upload(new DirectoryInfo(remoteDir), remoteDir);

            using (var fileStream = File.OpenRead(file.FullName))
            {
                scpClient.Upload(fileStream, remoteFilePath);
            }
        }

        scpClient.Disconnect();
    }
});

static void ExecuteCommand(SshClient client, string[] commands, ILogger logger)
{
    string joined = string.Join("; ", commands);
    string escaped = joined.Replace("\"", "\\\"");
    string psCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"";

    var cmd = client.CreateCommand(psCommand);
    cmd.CommandTimeout = TimeSpan.FromMinutes(2);

    string result = cmd.Execute();
    string error = cmd.Error;

    if (cmd.ExitStatus != 0)
    {
        logger.LogError(
            "SSH command failed.\nCommands: {Commands}\nWrapped: {Wrapped}\nExitCode: {ExitCode}\nError: {Error}",
            string.Join(" | ", commands),
            psCommand,
            cmd.ExitStatus,
            error
        );
    }
    else
    {
        logger.LogInformation(
            "SSH command succeeded.\nCommands: {Commands}\nResult: {Result}",
            string.Join(" | ", commands),
            result
        );
    }
}

