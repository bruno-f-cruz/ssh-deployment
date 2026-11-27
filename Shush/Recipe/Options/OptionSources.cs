namespace Shush.Recipe.Options;

/// <summary>One suggested value for an input or a param: what is written, and how it is shown.</summary>
public sealed record InputOption(string Value, string? Label = null);

/// <summary>
/// A named, possibly remote list of suggested values (e.g. the clabe releases with builds). Referenced by name from a
/// step input (<c>[Input(OptionsFrom = "clabe.versions")]</c>) or a recipe param (<c>optionsFrom: clabe.versions</c>);
/// the editor offers them as suggestions, and any other value can still be typed.
/// </summary>
public interface IOptionSource
{
    string Name { get; }

    /// <summary>The options, most relevant first. Never throws: a source that cannot be reached returns none.</summary>
    Task<IReadOnlyList<InputOption>> GetOptionsAsync(CancellationToken cancellationToken = default);
}

public sealed class OptionSourceRegistry
{
    private readonly Dictionary<string, IOptionSource> _sources;

    public OptionSourceRegistry(IEnumerable<IOptionSource> sources) =>
        _sources = sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in sources.</summary>
    public static OptionSourceRegistry Default { get; } =
        new([new ClabeVersionOptions(ClabeReleases.Shared), new ClabeInstallerOptions(ClabeReleases.Shared)]);

    public IReadOnlyCollection<string> Names => _sources.Keys;

    /// <summary>
    /// A named source, or a parameterised one created on first use: <c>git.refs(https://github.com/owner/repo)</c>
    /// (see <see cref="GitRefOptions"/>).
    /// </summary>
    public bool TryGet(string name, out IOptionSource source)
    {
        lock (_sources)
        {
            if (_sources.TryGetValue(name, out source!))
                return true;

            if (GitRefOptions.TryCreate(name, SharedHttp.Value) is { } refs)
            {
                _sources[name] = source = refs;
                return true;
            }

            return false;
        }
    }

    private static readonly Lazy<HttpClient> SharedHttp = new(ClabeReleases.CreateClient);
}
