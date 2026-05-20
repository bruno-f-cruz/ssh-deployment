namespace Shush.Recipe.Steps;

public class CreateShortcutStep : IRecipeStep
{
    private readonly string _cmdPath;
    private readonly string _shortcutDirectory;
    private readonly string _shortName;

    public CreateShortcutStep(string cmdPath, string shortcutDirectory, string shortName)
    {
        _cmdPath = cmdPath;
        _shortcutDirectory = shortcutDirectory;
        _shortName = shortName;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var shortcutPath = $@"{_shortcutDirectory}\{_shortName}.lnk";
        string[] commands =
        [
            $"$sh = New-Object -ComObject WScript.Shell; $sc = $sh.CreateShortcut('{shortcutPath}'); $sc.TargetPath = '{_cmdPath}'; $sc.Save()",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
