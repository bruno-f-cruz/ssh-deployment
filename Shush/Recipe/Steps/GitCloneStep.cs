namespace Shush.Recipe.Steps;

public class GitCloneStep : IRecipeStep
{
    private readonly string _repositoryUrl;

    public string ClonedPath { get; }

    public GitCloneStep(string repositoryUrl, string rootPath, string? folderName = null)
    {
        _repositoryUrl = repositoryUrl;

        if (folderName is null)
        {
            var lastSegment = repositoryUrl.TrimEnd('/').Split('/').Last();
            folderName = lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? lastSegment[..^4]
                : lastSegment;
        }

        ClonedPath = $"{rootPath.TrimEnd('/', '\\')}\\{folderName}";
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"git config --global --add safe.directory '{ClonedPath}'",
            $"if (-not (Test-Path '{ClonedPath}')) {{ git clone {_repositoryUrl} '{ClonedPath}' }} else {{ Write-Host 'Repository already cloned, skipping.' }}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
