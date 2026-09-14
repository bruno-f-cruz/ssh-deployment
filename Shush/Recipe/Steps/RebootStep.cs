namespace Shush.Recipe.Steps;

[Step("Reboot", Description = "Restart the remote machine (applies any pending Windows updates, like a normal restart). " +
    "The SSH connection drops when it fires, so this should be the last step in a recipe.")]
public class RebootStep : IRecipeStep
{
    [Input(Description = "Seconds to wait before restarting (default 5). Gives shutdown.exe time to return " +
        "cleanly over SSH before the connection drops, unlike Restart-Computer.")]
    public int DelaySeconds { get; init; } = 5;

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        string[] commands = [$"shutdown.exe /r /t {DelaySeconds}"];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
