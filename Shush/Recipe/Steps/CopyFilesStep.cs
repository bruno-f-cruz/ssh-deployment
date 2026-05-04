namespace Shush.Recipe.Steps;

public class CopyFilesStep : IRecipeStep
{
    private readonly string _sourceDirectory;
    private readonly string _remoteBaseDirectory;

    public CopyFilesStep(string sourceDirectory, string remoteBaseDirectory)
    {
        _sourceDirectory = sourceDirectory;
        _remoteBaseDirectory = remoteBaseDirectory;
    }

    public async Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var sourceDir = new DirectoryInfo(_sourceDirectory);

        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir.FullName}");

        foreach (var file in sourceDir.GetFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
            var remoteFilePath = Path.Combine(_remoteBaseDirectory, relativePath).Replace('\\', '/');

            await context.UploadFileAsync(file, remoteFilePath, cancellationToken);
        }
    }
}
