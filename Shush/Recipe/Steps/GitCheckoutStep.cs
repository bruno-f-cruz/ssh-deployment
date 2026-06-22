namespace Shush.Recipe.Steps;

public class GitCheckoutStep : IRecipeStep
{
    private readonly string _path;
    private readonly string _tag;
    private readonly IReadOnlyList<string> _cleanExceptions;

    public GitCheckoutStep(string path, string tag, IEnumerable<string>? cleanExceptions = null)
    {
        _path = path;
        _tag = tag;
        _cleanExceptions = cleanExceptions?.ToArray() ?? [];
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var exceptionArgs = string.Join(
            " ",
            _cleanExceptions
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => $"-e '{EscapeSingleQuotedPowerShellString(value)}'"));

        var cleanCommand = string.IsNullOrWhiteSpace(exceptionArgs)
            ? "git clean -ffdx"
            : $"git clean -ffdx {exceptionArgs}";

        string[] commands =
        [
            $"Set-Location '{EscapeSingleQuotedPowerShellString(_path)}'",
            $"git config --global --add safe.directory '{EscapeSingleQuotedPowerShellString(_path)}'",
            "git fetch --all --tags --prune --force",
            "git reset --hard",
            cleanCommand,
            $"git checkout -f tags/{_tag}",
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
