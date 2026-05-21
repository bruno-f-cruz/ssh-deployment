using System.Text.RegularExpressions;

namespace Shush.Recipe.Steps;

public class TemplatedCopyFilesStep : IRecipeStep
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);

    private readonly string _sourceDirectory;
    private readonly string _remoteBaseDirectory;
    private readonly IReadOnlyDictionary<string, string> _variables;

    public TemplatedCopyFilesStep(
        string sourceDirectory,
        string remoteBaseDirectory,
        Dictionary<string, string> variables)
    {
        _sourceDirectory = sourceDirectory;
        _remoteBaseDirectory = remoteBaseDirectory;
        _variables = variables;
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
            var remotePath = Path.Combine(_remoteBaseDirectory, relativePath).Replace('\\', '/');

            var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            var resolved = TokenPattern.Replace(content, m =>
            {
                var key = m.Groups[1].Value;
                return _variables.TryGetValue(key, out var value)
                    ? value
                    : throw new KeyNotFoundException($"Template variable '{{{{key}}}}' was not provided.");
            });

            await context.UploadContentAsync(resolved, remotePath, cancellationToken);
        }
    }
}
