namespace Shush.Recipe;

/// <summary>
/// A command on the rig exited with an error. Its full output is already in the run log (written where it ran), so
/// handlers further up log only a summary.
/// </summary>
public sealed class RemoteCommandException(int exitCode, string error)
    : InvalidOperationException($"SSH command failed with exit code {exitCode}: {error}")
{
    public int ExitCode { get; } = exitCode;
    public string Error { get; } = error;
}
