namespace Shush.Recipe.Steps;

public class RunScriptStep : IRecipeStep
{
    private readonly string[] _commands;
    private readonly string? _workingDirectory;

    public RunScriptStep(string[] commands, string? workingDirectory = null)
    {
        _commands = commands;
        _workingDirectory = workingDirectory;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var commands = _workingDirectory is null
            ? _commands
            : [$"Set-Location '{_workingDirectory}'", .. _commands];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
