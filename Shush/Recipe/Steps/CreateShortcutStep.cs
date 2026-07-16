namespace Shush.Recipe.Steps;

[Step("CreateShortcut", Description = "Create a Windows .lnk shortcut pointing at a target file.")]
public class CreateShortcutStep : IRecipeStep
{
    [Input(Required = true, Description = "Target the shortcut points at.")]
    public string CmdPath { get; init; } = "";

    [Input(Required = true, Description = "Directory the shortcut is created in.")]
    public string ShortcutDirectory { get; init; } = "";

    [Input(Required = true, Description = "Shortcut file name (without extension).")]
    public string ShortName { get; init; } = "";

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var shortcutPath = $@"{ShortcutDirectory}\{ShortName}.lnk";
        string[] commands =
        [
            $"$sh = New-Object -ComObject WScript.Shell; $sc = $sh.CreateShortcut('{shortcutPath}'); $sc.TargetPath = '{CmdPath}'; $sc.Save()",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
