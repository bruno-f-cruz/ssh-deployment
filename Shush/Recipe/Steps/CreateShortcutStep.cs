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

    [Input(Description = "Optional remote path to an .ico file used as the shortcut's icon (e.g. a file inside a cloned repo).")]
    public string? IconPath { get; init; }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var shortcutPath = $@"{ShortcutDirectory}\{ShortName}.lnk";
        var iconAssignment = string.IsNullOrWhiteSpace(IconPath) ? "" : $" $sc.IconLocation = '{IconPath},0';";

        string[] commands =
        [
            $"$sh = New-Object -ComObject WScript.Shell; $sc = $sh.CreateShortcut('{shortcutPath}'); $sc.TargetPath = '{CmdPath}';{iconAssignment} $sc.Save()",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
