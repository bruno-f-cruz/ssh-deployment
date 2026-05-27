namespace Shush.Recipe.Steps;

public class DeleteDirectoryStep : IRecipeStep
{
    private readonly string _path;

    public DeleteDirectoryStep(string path)
    {
        _path = path;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"if (Test-Path '{_path}') {{ Remove-Item '{_path}' -Recurse -Force }}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
