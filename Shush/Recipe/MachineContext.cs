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

    /// <summary>
    /// Environment variables set during this run. Windows' OpenSSH gives a session the environment it had at sign-in,
    /// so a variable a step saves (e.g. machine-wide) only reaches new sessions; every later command of this run gets
    /// them set explicitly, so the next step already sees them.
    /// </summary>
    private readonly Dictionary<string, (string Value, bool Secret)> _sessionEnvironment = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes every later command of this run see <paramref name="name"/> = <paramref name="value"/>.</summary>
    public void SetSessionVariable(string name, string value, bool secret = false) => _sessionEnvironment[name] = (value, secret);

    /// <summary>The PowerShell that sets the session variables, ahead of a command ("" when there are none).</summary>
    internal string SessionPrefix() => BuildSessionPrefix(_sessionEnvironment.Select(kv => (kv.Key, kv.Value.Value)));

    /// <summary><c>$env:NAME = 'value'; </c> for each variable, single quotes doubled. Public for tests.</summary>
    public static string BuildSessionPrefix(IEnumerable<(string Name, string Value)> variables) =>
        string.Concat(variables.Select(v => $"$env:{v.Name} = '{v.Value.Replace("'", "''")}'; "));

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
        // The rigs run Windows OpenSSH, whose scp sink is invoked through cmd.exe, so remote paths
        // are double-quoted (the only quoting cmd.exe understands). Paths are also checked with
        // EnsureSafeRemotePath, since a double quote is the one character that escapes it.
        var scpClient = new ScpClient(address, secrets.Username, secrets.Password, RemotePathTransformation.DoubleQuote);

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
    public Task RunCommandsAsync(string[] commands, CancellationToken ct = default, string? logAs = null) =>
        ExecuteAsync(commands, ct, logAs);

    /// <summary>
    /// Same as <see cref="RunCommandsAsync"/>, but returns the command's stdout so the caller can
    /// parse and log a structured report (e.g. a dry-run preview) instead of the raw text.
    /// </summary>
    public Task<string> RunCommandsWithOutputAsync(string[] commands, CancellationToken ct = default, string? logAs = null) =>
        ExecuteAsync(commands, ct, logAs);

    /// <summary>
    /// Creates <paramref name="grant"/>'s directory if needed and adds an inheritable ACL entry
    /// (object + container inherit) so files and folders created beneath it later are accessible
    /// to the principal. Idempotent. Files that are moved in (rather than created or copied) keep
    /// their old ACL, and a subfolder with inheritance disabled breaks the flow.
    /// </summary>
    public Task EnsureSharedAccessAsync(SharedAccessGrant grant, CancellationToken ct = default)
    {
        var errors = grant.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var recurse = grant.ApplyToExisting ? " /T" : "";
        string[] commands =
        [
            $"New-Item -ItemType Directory -Force -Path '{grant.Path}' | Out-Null",
            $"icacls '{grant.Path}' /grant '{grant.Principal}:(OI)(CI){grant.Rights.ToUpperInvariant()}'{recurse}",
        ];

        return RunCommandsAsync(commands, ct);
    }

    /// <summary>Lets a step add its own line to the deploy log (e.g. a dry-run report entry).</summary>
    public void Log(string message) => _logger.LogInformation("[{BoxId}] {Message}", BoxId, message);

    private Task<string> ExecuteAsync(string[] commands, CancellationToken ct, string? logAs)
    {
        ct.ThrowIfCancellationRequested();

        string joined = string.Join(" ; if ($LASTEXITCODE) { exit $LASTEXITCODE } ; ", commands);
        string escaped = (SessionPrefix() + joined).Replace("\"", "\\\"");
        string psCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"";
        string display = logAs ?? joined;

        _logger.LogInformation("[{BoxId}] Running commands: {Commands}", BoxId, display);

        var cmd = _sshClient.CreateCommand(psCommand);
        cmd.CommandTimeout = TimeSpan.FromMinutes(10);

        string result = cmd.Execute();
        string error = cmd.Error;

        if (cmd.ExitStatus != 0)
        {
            // The wrapped command carries the session variables; leave it out when one of them is secret.
            if (logAs is null && !_sessionEnvironment.Values.Any(v => v.Secret))
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

            throw new RemoteCommandException(cmd.ExitStatus ?? -1, error);
        }

        _logger.LogDebug(
            "[{BoxId}] SSH command succeeded.\nCommands: {Commands}\nOutput: {Result}",
            BoxId,
            display,
            result);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Rejects remote paths containing a double quote. Such a path is never a valid Windows path,
    /// and it would close the quoting around the path in both the scp command and the
    /// New-Item command below, letting the rest of the path run as a command on the rig.
    /// </summary>
    public static void EnsureSafeRemotePath(string remotePath)
    {
        if (remotePath.Contains('"'))
            throw new ArgumentException($"Remote path must not contain a double quote: {remotePath}", nameof(remotePath));
    }

    public async Task UploadFileAsync(FileInfo localFile, string remotePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureSafeRemotePath(remotePath);

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
        EnsureSafeRemotePath(remotePath);

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
