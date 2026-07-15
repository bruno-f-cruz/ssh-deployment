namespace Shush.Recipe.Steps;

[Step("RunScript", Description = "Run a sequence of PowerShell commands on the remote machine.")]
public class RunScriptStep : IRecipeStep
{
    [Input(Required = true, Description = "PowerShell commands to run in order.")]
    public string[] Commands { get; init; } = [];

    [Input(Description = "Directory to run the commands in (Set-Location before running).")]
    public string? WorkingDirectory { get; init; }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var commands = WorkingDirectory is null
            ? Commands
            : [$"Set-Location '{WorkingDirectory}'", .. Commands];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
