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

        // Multi-homed machines can register several DNS records and SSH.NET only tries the
        // first, so probe every resolved address and connect to one that actually answers.
        var probe = await HostProber.ProbeAsync(machine.hostname, ct: ct);
        var address = probe.ReachableAddress
            ?? throw new InvalidOperationException(
                $"No address of '{machine.hostname}' accepted a connection on port {HostProber.SshPort}. {probe.Describe()}");

        if (probe.Addresses.Count > 1)
            logger.LogWarning("[{BoxId}] {Details} — using {Address}.", boxId, probe.Describe(), address);

        var sshClient = new SshClient(address, secrets.Username, secrets.Password);
        var scpClient = new ScpClient(address, secrets.Username, secrets.Password);

        logger.LogInformation("[{BoxId}] Connecting SSH and SCP clients to {Hostname} ({Address}).", boxId, machine.hostname, address);
        await sshClient.ConnectAsync(ct);
        await scpClient.ConnectAsync(ct);
        logger.LogInformation("[{BoxId}] Connected.", boxId);

        return new MachineContext(boxId, machine, sshClient, scpClient, logger);
    }

    /// <param name="logAs">
    /// When set, this text is logged in place of the raw commands (and the wrapped command is
    /// omitted from the failure log) so secrets never reach the deploy log. When null, the
    /// commands are logged verbatim as before.
    /// </param>
    public Task RunCommandsAsync(string[] commands, CancellationToken ct = default, string? logAs = null)
    {
        ct.ThrowIfCancellationRequested();

        string joined = string.Join(" ; if ($LASTEXITCODE) { exit $LASTEXITCODE } ; ", commands);
        string escaped = joined.Replace("\"", "\\\"");
        string psCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"";
        string display = logAs ?? joined;

        _logger.LogInformation("[{BoxId}] Running commands: {Commands}", BoxId, display);

        var cmd = _sshClient.CreateCommand(psCommand);
        cmd.CommandTimeout = TimeSpan.FromMinutes(10);

        string result = cmd.Execute();
        string error = cmd.Error;

        if (cmd.ExitStatus != 0)
        {
            if (logAs is null)
                _logger.LogError(
                    "[{BoxId}] SSH command failed.\nCommands: {Commands}\nWrapped: {Wrapped}\nExitCode: {ExitCode}\nError: {Error}",
                    BoxId,
                    joined,
                    psCommand,
                    cmd.ExitStatus,
                    error);
            else
                _logger.LogError(
                    "[{BoxId}] SSH command failed.\nCommands: {Commands}\nExitCode: {ExitCode}\nError: {Error}",
                    BoxId,
                    display,
                    cmd.ExitStatus,
                    error);

            throw new InvalidOperationException($"SSH command failed with exit code {cmd.ExitStatus}: {error}");
        }

        _logger.LogDebug(
            "[{BoxId}] SSH command succeeded.\nCommands: {Commands}\nOutput: {Result}",
            BoxId,
            display,
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

    public async Task UploadContentAsync(string content, string remotePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string remoteDir = (Path.GetDirectoryName(remotePath) ?? string.Empty).Replace('\\', '/');

        if (!string.IsNullOrEmpty(remoteDir))
        {
            await RunCommandsAsync(
                [$"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"New-Item -ItemType Directory -Force -Path '{remoteDir}'\""],
                ct);
        }

        _logger.LogDebug("[{BoxId}] Uploading content -> {RemotePath}.", BoxId, remotePath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);
        _scpClient.Upload(stream, remotePath);
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
