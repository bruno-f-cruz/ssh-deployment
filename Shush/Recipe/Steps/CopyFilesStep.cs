namespace Shush.Recipe.Steps;

[Step("CopyFiles", Description = "Recursively upload a local directory to a remote base directory.")]
public class CopyFilesStep : IRecipeStep
{
    [Input(Required = true, Description = "Local directory to copy from.")]
    public string SourceDirectory { get; init; } = "";

    [Input(Required = true, Description = "Remote directory files are uploaded under.")]
    public string RemoteBaseDirectory { get; init; } = "";

    public async Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var sourceDir = new DirectoryInfo(SourceDirectory);

        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir.FullName}");

        foreach (var file in sourceDir.GetFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
            var remoteFilePath = Path.Combine(RemoteBaseDirectory, relativePath).Replace('\\', '/');

            await context.UploadFileAsync(file, remoteFilePath, cancellationToken);
        }
    }
}
