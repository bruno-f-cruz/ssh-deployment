namespace Shush.Recipe.Steps;

[Step("WriteFile", Description = "Upload literal text content to a file on the remote machine.")]
public class WriteFileStep : IRecipeStep
{
    [Input(Required = true, Multiline = true, Description = "The file content to write.")]
    public string Content { get; init; } = "";

    [Input(Required = true, Description = "Remote path the content is written to.")]
    public string TargetPath { get; init; } = "";

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        return context.UploadContentAsync(Content, TargetPath, cancellationToken);
    }
}
