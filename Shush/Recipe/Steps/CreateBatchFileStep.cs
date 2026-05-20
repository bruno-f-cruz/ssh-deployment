namespace Shush.Recipe.Steps;

public class CreateBatchFileStep : IRecipeStep
{
    private readonly string _remotePath;
    private readonly string[] _lines;

    public CreateBatchFileStep(string remotePath, params string[] lines)
    {
        _remotePath = remotePath;
        _lines = lines;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var items = string.Join(", ", _lines.Select(l => $"'{l.Replace("'", "''")}'"));
        string[] commands =
        [
            $"Set-Content -Path '{_remotePath}' -Value @({items}) -Encoding ASCII",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
