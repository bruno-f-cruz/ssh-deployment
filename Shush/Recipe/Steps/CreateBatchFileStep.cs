namespace Shush.Recipe.Steps;

[Step("CreateBatchFile", Description = "Write an ASCII batch/command file from a list of lines.")]
public class CreateBatchFileStep : IRecipeStep
{
    [Input(Required = true, Description = "Remote path of the file to create.")]
    public string RemotePath { get; init; } = "";

    [Input(Required = true, Description = "Lines written to the file, in order.")]
    public string[] Lines { get; init; } = [];

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var items = string.Join(", ", Lines.Select(l => $"'{l.Replace("'", "''")}'"));
        string[] commands =
        [
            $"Set-Content -Path '{RemotePath}' -Value @({items}) -Encoding ASCII",
        ];

        return context.RunCommandsAsync(commands, cancellationToken);
    }
}
