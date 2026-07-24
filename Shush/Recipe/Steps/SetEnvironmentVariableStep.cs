namespace Shush.Recipe.Steps;

[Step("SetEnvironmentVariable", Description = "Persist an environment variable on the remote machine (Machine scope by default).")]
public class SetEnvironmentVariableStep : IRecipeStep
{
    [Input(Required = true, Description = "Environment variable name.")]
    public string Name { get; init; } = "";

    [Input(Required = true, Description = "Value to assign.")]
    public string Value { get; init; } = "";

    [Input(Description = "Target scope: Machine (default), User, or Process.")]
    public string Scope { get; init; } = "Machine";

    [Input(Description = "Redact the value from deploy logs — use for secrets (auth tokens, passwords).")]
    public bool Secret { get; init; }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        // Written via .NET rather than setx: no 1024-char truncation, no cmd.exe quoting quirks.
        // Machine scope writes HKLM, so the SSH user must be an administrator.
        var name = Escape(Name);
        var value = Escape(Value);
        var scope = Escape(Scope);

        string[] commands = [$"[Environment]::SetEnvironmentVariable('{name}', '{value}', '{scope}')"];

        var logAs = Secret
            ? $"[Environment]::SetEnvironmentVariable('{name}', '<redacted>', '{scope}')"
            : null;

        return context.RunCommandsAsync(commands, cancellationToken, logAs);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
