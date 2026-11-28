using Shush.Recipe.Options;

namespace Shush.Recipe.Steps;

/// <summary>
/// Deploys the clabe daemon with clabe's installer (scripts/install.ps1): installs uv and the clabe command if needed,
/// then <c>clabe daemon deploy</c> — installs the requested release next to the one running, switches the daemon to it
/// and starts it (or only starts it, or nothing, when it already runs that release with these hubs). Refused while a
/// session runs, unless <see cref="Force"/>. Runs as the SSH user, who must be the user logged on to the rig: the
/// daemon runs in their session.
/// </summary>
[Step("DeployClabeDaemon", Description = "Deploy clabe-daemon: install, update or roll back the clabe daemon to a clabe release (from PyPI) or a build of a commit of clabe's main branch, enrolled with the hubs, and start it. With stopOnly, only stop it (e.g. before rebuilding an experiment's environment).")]
public class DeployClabeDaemonStep : IRecipeStep
{
    [Input(OptionsFrom = "clabe.versions", Description = "The clabe the daemon runs: a release, e.g. 0.19.0, or a commit of clabe's main branch (its build), e.g. cf17fa8. \"lock\": the clabe lockRepository's uv.lock pins. \"latest\" (or empty): the newest release.")]
    public string Version { get; init; } = "latest";

    [Input(Description = "The experiment repository whose uv.lock \"lock\" follows, e.g. ${clone.clonedPath}.")]
    public string? LockRepository { get; init; }

    [Input(Description = "The hubs the daemon enrolls with, ';'-separated; \"none\": no hub. Empty: leave them as they are.")]
    public string? Hubs { get; init; }

    [Input(Description = "Stop or restart the daemon even while it runs a session (the session is interrupted).")]
    public bool Force { get; init; }

    [Input(Description = "Only install the clabe command and stop the daemon; a later DeployClabeDaemon starts it again.")]
    public bool StopOnly { get; init; }

    [Input(OptionsFrom = "clabe.installers", Description = "The version of clabe's installer: a clabe release tag (e.g. v0.19.0), a commit, or main. Builds (a commit as version or cliVersion, or a lock on one) need an installer from cf17fa8 on.")]
    public string Installer { get; init; } = "main";

    [Input(OptionsFrom = "clabe.versions", Description = "The clabe the `clabe` command is installed at: a release, e.g. 0.19.0, or a commit (its build). Empty, \"lock\" or \"latest\": the installer's default (the build -Lock or -Commit names, else the newest release).")]
    public string? CliVersion { get; init; }

    /// <summary>The extras the `clabe` command is installed with (the installer's own default).</summary>
    public const string CliExtras = "frontend,otel";

    public async Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        await CheckVersionAsync(ClabeReleases.Shared, cancellationToken);
        await context.RunCommandsAsync(BuildCommands(), cancellationToken);
    }

    /// <summary>The PowerShell run on the rig: clabe's installer at <see cref="Installer"/>, with this step's options.</summary>
    public string[] BuildCommands()
    {
        var arguments = new List<string>();
        if (StopOnly)
        {
            arguments.Add("-StopDaemon");
        }
        else
        {
            var version = (Version ?? "").Trim();
            if (version.Equals("lock", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(LockRepository))
                    throw new InvalidOperationException("version \"lock\" needs lockRepository: the repository whose uv.lock pins clabe.");
                arguments.Add($"-Lock {Quote(LockRepository)}");
            }
            else if (ClabeReleases.IsCommit(version))
            {
                arguments.Add($"-Commit {Quote(version.ToLowerInvariant())}");
            }
            else if (version.Length > 0 && !version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add($"-Version {Quote(version)}");
            }
            if (!string.IsNullOrWhiteSpace(Hubs))
                arguments.Add($"-Hubs {Quote(Hubs.Trim())}");
        }
        var cli = (CliVersion ?? "").Trim();
        if (ClabeReleases.IsCommit(cli))
        {
            // The installer installs a build as the `clabe` command from -Commit (a build has no PyPI version to pin).
            var commit = $"-Commit {Quote(cli.ToLowerInvariant())}";
            if (StopOnly)
                arguments.Add(commit);
            else if (!arguments.Contains(commit))
                throw new InvalidOperationException($"cliVersion {cli} is a build: version must be the same commit (the installer installs one build for both).");
        }
        else
        {
            cli = cli.TrimStart('v', 'V');
            if (cli.Length > 0 && !cli.Equals("lock", StringComparison.OrdinalIgnoreCase) && !cli.Equals("latest", StringComparison.OrdinalIgnoreCase))
                arguments.Add($"-CliPackage {Quote($"aind-clabe[{CliExtras}]=={cli}")}");
        }
        arguments.Add(Force ? "-Force" : "-Force:$false");

        var installer = string.IsNullOrWhiteSpace(Installer) ? "main" : Installer.Trim();
        return
        [
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'",
            $"$install = [scriptblock]::Create((Invoke-RestMethod -UseBasicParsing {Quote(ClabeReleases.InstallerUrl(installer))}))",
            $"& $install {string.Join(' ', arguments)}",
        ];
    }

    /// <summary>
    /// Refuses a release with no builds before touching the rig — only on a list just read from GitHub, so a version
    /// typed by hand is never refused on stale or missing data: a release published minutes ago, or GitHub unreachable
    /// or rate-limited, lets the deploy go on, and the rig's install then says whether the release exists.
    /// </summary>
    public async Task CheckVersionAsync(ClabeReleases releases, CancellationToken cancellationToken = default)
    {
        var version = (Version ?? "").Trim().TrimStart('v', 'V');
        if (StopOnly || version.Length == 0
            || version.Equals("lock", StringComparison.OrdinalIgnoreCase)
            || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return;
        if (ClabeReleases.IsCommit(version))
        {
            await CheckBuildAsync(releases, version, cancellationToken);
            return;
        }
        if ((await releases.GetAsync(cancellationToken)).Any(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            return; // known: no need to ask GitHub again
        var known = await releases.GetFreshAsync(cancellationToken);
        if (known is null || known.Count == 0 || known.Any(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            return;
        var recent = string.Join(", ", known.Take(5).Select(r => r.Version));
        throw new InvalidOperationException(
            $"clabe {version} is not a release with builds (https://github.com/{ClabeReleases.Repository}/releases). Recent ones: {recent}.");
    }

    /// <summary>Refuses a commit with no build, on a list just read from GitHub only (as for releases).</summary>
    private static async Task CheckBuildAsync(ClabeReleases releases, string commit, CancellationToken cancellationToken)
    {
        if (ClabeReleases.FindBuild(await releases.GetAsync(cancellationToken), commit) is not null)
            return;
        var known = await releases.GetFreshAsync(cancellationToken);
        if (known is null || known.Count == 0 || ClabeReleases.FindBuild(known, commit) is not null)
            return;
        throw new InvalidOperationException(
            $"clabe has no build of commit {commit} (one per commit pushed to main, the newest 100): https://github.com/{ClabeReleases.Repository}/releases/tag/{ClabeReleases.BuildsTag}.");
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
