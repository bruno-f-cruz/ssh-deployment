using System.Text.RegularExpressions;

namespace Shush.Recipe.Steps;

[Step("TemplatedCopyFiles", Description = "Copy a local directory, replacing {{token}} placeholders in file contents.")]
public class TemplatedCopyFilesStep : IRecipeStep
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);

    [Input(Required = true, Description = "Local directory to copy from.")]
    public string SourceDirectory { get; init; } = "";

    [Input(Required = true, Description = "Remote directory files are uploaded under.")]
    public string RemoteBaseDirectory { get; init; } = "";

    [Input(Description = "Values substituted for {{token}} placeholders in file contents.")]
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    public async Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default)
    {
        var sourceDir = new DirectoryInfo(SourceDirectory);

        if (!sourceDir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir.FullName}");

        foreach (var file in sourceDir.GetFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir.FullName, file.FullName);
            var remotePath = Path.Combine(RemoteBaseDirectory, relativePath).Replace('\\', '/');

            var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            var resolved = TokenPattern.Replace(content, m =>
            {
                var key = m.Groups[1].Value;
                return Variables.TryGetValue(key, out var value)
                    ? value
                    : throw new KeyNotFoundException($"Template variable '{{{{key}}}}' was not provided.");
            });

            await context.UploadContentAsync(resolved, remotePath, cancellationToken);
        }
    }
}
