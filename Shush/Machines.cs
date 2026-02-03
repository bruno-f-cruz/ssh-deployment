using System.Text.Json;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Semver;


public record MachineInfo(string hostname, string rig_id);

public class MachineManager
{

    private readonly static List<string> Include = new List<string>
    {
        //"FRG.4",
        //"FRG.5",
        //"FRG.12",
        //"FRG.13",
        "FRG.0"
    };

    public async static Task<Dictionary<string, MachineInfo>> GetComputers(string url = "http://mpe-computers/v2.0")
    {
        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetStringAsync(url);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            var jsonDocument = JsonSerializer.Deserialize<JsonDocument>(response, options);
            var machines = jsonDocument?.RootElement.GetProperty("comp_ids").Deserialize<Dictionary<string, MachineInfo>>(options) ?? new Dictionary<string, MachineInfo>();
            return machines
                .Where(kv => Include.Any(include => kv.Key.StartsWith(include)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}


public class MachineDeployer
{
    MachineInfo machine;
    Secrets secrets;
    string tag;
    ILogger logger;

    string boxId;
    string remotePath;

    public MachineDeployer(string boxId, MachineInfo machine, Secrets secrets, string tag, ILogger logger, string remotePath)
    {
        this.machine = machine;
        this.secrets = secrets;
        this.tag = tag;
        this.logger = logger;
        this.boxId = boxId;
        this.remotePath = remotePath;
    }

    public async Task RunCommand(string[] command, CancellationToken cancellationToken)
    {
        using (var sshClient = new SshClient(machine.hostname, secrets.Username, secrets.Password))
        {
            await sshClient.ConnectAsync(cancellationToken);
            logger.LogInformation("Running command on machine {Machine}: {Command}", boxId, string.Join(" ", command));
            ExecuteCommand(sshClient, command);
            sshClient.Disconnect();
        }
    }

    public async Task Deploy(CancellationToken cancellationToken)
    {
        logger.LogInformation("Found machine {Machine} with rig ID {RigId} and hostname {Hostname}.", boxId, machine.rig_id, machine.hostname);
        using (var sshClient = new SshClient(machine.hostname, secrets.Username, secrets.Password))
        {
            using (var scpClient = new ScpClient(machine.hostname, secrets.Username, secrets.Password))
            {
                await sshClient.ConnectAsync(cancellationToken);
                string[] shellCommand = {
                    "cd " + remotePath,
                    "git fetch --all --tags --prune",
                    "git clean -fd",
                    "git reset --hard",
                    "git checkout " + "tags/" + tag,
                    "& .\\scripts\\deploy.cmd",
                };
                ExecuteCommand(sshClient, shellCommand);

                await scpClient.ConnectAsync(cancellationToken);

                var sourceDir = new DirectoryInfo("./FilesToTransfer");
                foreach (var file in sourceDir.GetFiles("*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
                    var remoteFilePath = Path.Combine(remotePath, relativePath).Replace('\\', '/');

                    var dirName = Path.GetDirectoryName(remoteFilePath);
                    var remoteDir = dirName != null ? dirName.Replace('\\', '/') : string.Empty;

                    string[] mkdir = {
                    $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path '{remoteDir}'\""
                };
                    ExecuteCommand(sshClient, mkdir);

                    logger.LogDebug("Uploading file {LocalFile} to {RemoteFile}.", file.FullName, remoteFilePath);
                    scpClient.Upload(new FileInfo(file.FullName), remoteFilePath);

                }

                scpClient.Disconnect();
            }
            sshClient.Disconnect();
        }
    }
    void ExecuteCommand(SshClient client, string[] commands)
    {
        string joined = string.Join("; ", commands);
        string escaped = joined.Replace("\"", "\\\"");
        string psCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"";

        var cmd = client.CreateCommand(psCommand);
        cmd.CommandTimeout = TimeSpan.FromSeconds(60);

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
            throw new Exception($"SSH command failed with exit code {cmd.ExitStatus}: {error}");
        }
        else
        {
            logger.LogDebug(
                "SSH command succeeded.\nCommands: {Commands}\nResult: {Result}",
                string.Join(" | ", commands),
                result
            );
        }
    }

}

