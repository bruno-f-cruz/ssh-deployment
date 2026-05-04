namespace Shush.Recipe.Steps;

public class GitCheckoutStep : IRecipeStep
{
    private readonly string _remotePath;
    private readonly string _tag;

    public GitCheckoutStep(string remotePath, string tag)
    {
        _remotePath = remotePath;
        _tag = tag;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"cd {_remotePath}",
            "git fetch --all --tags --prune",
            "git clean -fd",
            "git reset --hard",
            $"git checkout tags/{_tag}",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
