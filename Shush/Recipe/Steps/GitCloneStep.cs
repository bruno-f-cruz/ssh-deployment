namespace Shush.Recipe.Steps;

[Step("GitClone", Description = "Clone a git repository if it isn't already present.")]
public class GitCloneStep : IRecipeStep
{
    [Input(Required = true, Description = "HTTPS URL of the repository to clone.")]
    public string RepositoryUrl { get; init; } = "";

    [Input(Required = true, Description = "Parent directory the repo is cloned into.")]
    public string RootPath { get; init; } = "";

    [Input(Description = "Override the folder name (defaults to the repo name).")]
    public string? FolderName { get; init; }

    [Output(Description = "Absolute path of the cloned working tree.")]
    public string ClonedPath => $"{RootPath.TrimEnd('/', '\\')}\\{FolderName ?? DeriveFolder(RepositoryUrl)}";

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var clonedPath = ClonedPath;
        string[] commands =
        [
            $"git config --global --add safe.directory '{clonedPath}'",
            $"if (-not (Test-Path '{clonedPath}')) {{ git clone {RepositoryUrl} '{clonedPath}' }} else {{ Write-Host 'Repository already cloned, skipping.' }}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }

    private static string DeriveFolder(string repositoryUrl)
    {
        var lastSegment = repositoryUrl.TrimEnd('/').Split('/').Last();
        return lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? lastSegment[..^4]
            : lastSegment;
    }
}
