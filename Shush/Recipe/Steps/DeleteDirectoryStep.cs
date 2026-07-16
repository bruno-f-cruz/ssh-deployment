namespace Shush.Recipe.Steps;

[Step("DeleteDirectory", Description = "Recursively delete a directory on the remote machine if it exists.")]
public class DeleteDirectoryStep : IRecipeStep
{
    [Input(Required = true, Description = "Directory to delete.")]
    public string Path { get; init; } = "";

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"if (Test-Path '{Path}') {{ Remove-Item '{Path}' -Recurse -Force }}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
