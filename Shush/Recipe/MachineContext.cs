using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Shush.Recipe;

public class MachineContext : IAsyncDisposable
{
    private readonly SshClient _sshClient;
    private readonly ScpClient _scpClient;
    private readonly ILogger<MachineContext> _logger;

    public string BoxId { get; }
    public MachineInfo MachineInfo { get; }

    private MachineContext(string boxId, MachineInfo machineInfo, SshClient sshClient, ScpClient scpClient, ILogger<MachineContext> logger)
    {
        BoxId = boxId;
        MachineInfo = machineInfo;
        _sshClient = sshClient;
        _scpClient = scpClient;
        _logger = logger;
    }

    public static async Task<MachineContext> ConnectAsync(
        string boxId,
        MachineInfo machine,
        Secrets secrets,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger<MachineContext>();
        var sshClient = new SshClient(machine.hostname, secrets.Username, secrets.Password);
        var scpClient = new ScpClient(machine.hostname, secrets.Username, secrets.Password);

        logger.LogInformation("[{BoxId}] Connecting SSH and SCP clients to {Hostname}.", boxId, machine.hostname);
        await sshClient.ConnectAsync(ct);
        await scpClient.ConnectAsync(ct);
        logger.LogInformation("[{BoxId}] Connected.", boxId);

        return new MachineContext(boxId, machine, sshClient, scpClient, logger);
    }

    public Task RunCommandsAsync(string[] commands, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string joined = string.Join("; ", commands);
        string escaped = joined.Replace("\"", "\\\"");
        string psCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"";

        _logger.LogInformation("[{BoxId}] Running commands: {Commands}", BoxId, joined);

        var cmd = _sshClient.CreateCommand(psCommand);
        cmd.CommandTimeout = TimeSpan.FromMinutes(10);

        string result = cmd.Execute();
        string error = cmd.Error;

        if (cmd.ExitStatus != 0)
        {
            _logger.LogError(
                "[{BoxId}] SSH command failed.\nCommands: {Commands}\nWrapped: {Wrapped}\nExitCode: {ExitCode}\nError: {Error}",
                BoxId,
                joined,
                psCommand,
                cmd.ExitStatus,
                error);
            throw new InvalidOperationException($"SSH command failed with exit code {cmd.ExitStatus}: {error}");
        }

        _logger.LogDebug(
            "[{BoxId}] SSH command succeeded.\nCommands: {Commands}\nOutput: {Result}",
            BoxId,
            joined,
            result);

        return Task.CompletedTask;
    }

    public async Task UploadFileAsync(FileInfo localFile, string remotePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string remoteDir = (Path.GetDirectoryName(remotePath) ?? string.Empty).Replace('\\', '/');

        if (!string.IsNullOrEmpty(remoteDir))
        {
            await RunCommandsAsync(
                [$"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path '{remoteDir}'\""],
                ct);
        }

        _logger.LogDebug("[{BoxId}] Uploading {LocalFile} -> {RemotePath}.", BoxId, localFile.FullName, remotePath);
        _scpClient.Upload(localFile, remotePath);
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("[{BoxId}] Disconnecting clients.", BoxId);

        if (_scpClient.IsConnected)
            _scpClient.Disconnect();
        _scpClient.Dispose();

        if (_sshClient.IsConnected)
            _sshClient.Disconnect();
        _sshClient.Dispose();

        await Task.CompletedTask;
    }
}
