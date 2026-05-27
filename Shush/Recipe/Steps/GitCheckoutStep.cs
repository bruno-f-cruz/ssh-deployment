namespace Shush.Recipe.Steps;

public class GitCheckoutStep : IRecipeStep
{
    private readonly string _path;
    private readonly string _tag;

    public GitCheckoutStep(string path, string tag)
    {
        _path = path;
        _tag = tag;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands =
        [
            $"cd {_path}",
            "git fetch --all --tags --prune --force",
            "git clean -fd",
            "git reset --hard",
            $"git checkout tags/{_tag}",
            "git submodule update --init --recursive",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
