namespace Shush.Recipe.Steps;

public class WriteFileStep : IRecipeStep
{
    private readonly string _content;
    private readonly string _targetPath;

    public WriteFileStep(string content, string targetPath)
    {
        _content = content;
        _targetPath = targetPath;
    }

    public Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        return context.UploadContentAsync(_content, _targetPath, cancellationToken);
    }
}
