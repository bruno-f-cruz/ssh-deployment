namespace Shush.Recipe.Steps;

[Step("GitCheckout", Description = "Fetch, clean, and hard-checkout a git reference (with submodules).")]
public class GitCheckoutStep : IRecipeStep
{
    [Input(Required = true, Description = "Path of the working tree to check out.")]
    public string Path { get; init; } = "";

    [Input(Required = true, Description = "Branch, tag, or commit to check out.")]
    public string Reference { get; init; } = "";

    [Input(Description = "Paths excluded from 'git clean' (e.g. cached state files).")]
    public IReadOnlyList<string> CleanExceptions { get; init; } = [];

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var exceptionArgs = string.Join(
            " ",
            CleanExceptions
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => $"-e '{EscapeSingleQuotedPowerShellString(value)}'"));

        var cleanCommand = string.IsNullOrWhiteSpace(exceptionArgs)
            ? "git clean -ffdx"
            : $"git clean -ffdx {exceptionArgs}";

        string[] commands =
        [
            $"Set-Location '{EscapeSingleQuotedPowerShellString(Path)}'",
            $"git config --global --add safe.directory '{EscapeSingleQuotedPowerShellString(Path)}'",
            "git fetch --all --tags --prune --force",
            "git reset --hard",
            cleanCommand,
            $"git checkout -f '{EscapeSingleQuotedPowerShellString(Reference)}'",
            "git submodule sync --recursive",
            "git submodule foreach --recursive git clean -ffdx",
            "git submodule foreach --recursive git reset --hard",
            "git submodule update --init --recursive --force",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }

    private static string EscapeSingleQuotedPowerShellString(string value)
    {
        return value.Replace("'", "''");
    }
}
