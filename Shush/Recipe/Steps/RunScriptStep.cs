namespace Shush.Recipe.Steps;

public class RunScriptStep : IRecipeStep
{
    private readonly string[] _commands;

    public RunScriptStep(params string[] commands)
    {
        _commands = commands;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        return context.RunCommandsAsync(_commands, cancellationToken);
    }
}
