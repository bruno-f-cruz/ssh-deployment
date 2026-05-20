namespace Shush.Recipe.Steps;

public class GitCloneStep : IRecipeStep
{
    private readonly string _repositoryUrl;

    public string ClonedPath { get; }

    public GitCloneStep(string repositoryUrl, string rootPath)
    {
        _repositoryUrl = repositoryUrl;

        var lastSegment = repositoryUrl.TrimEnd('/').Split('/').Last();
        var repoName = lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? lastSegment[..^4]
            : lastSegment;
        ClonedPath = $"{rootPath.TrimEnd('/', '\\')}\\{repoName}";
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"if (-not (Test-Path '{ClonedPath}')) {{ git clone {_repositoryUrl} '{ClonedPath}' }} else {{ Write-Host 'Repository already cloned, skipping.' }}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
