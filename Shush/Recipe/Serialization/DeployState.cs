using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shush.Recipe.Serialization;

/// <summary>Per-recipe deploy session state: which machines are selected and any param overrides.</summary>
public sealed class DeployState
{
    public List<string> Machines { get; set; } = [];
    public Dictionary<string, string> ParamOverrides { get; set; } = new();
}

/// <summary>Persists <see cref="DeployState"/> as one YAML file per recipe. Replaces the XML state store.</summary>
public sealed class DeployStateStore
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly string _directory;

    public DeployStateStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public DeployState? Load(string recipeName)
    {
        var path = GetPath(recipeName);
        return File.Exists(path) ? Deserializer.Deserialize<DeployState>(File.ReadAllText(path)) : null;
    }

    public void Save(string recipeName, DeployState state) =>
        File.WriteAllText(GetPath(recipeName), Serializer.Serialize(state));

    private string GetPath(string recipeName)
    {
        var safeName = string.Concat(recipeName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_directory, $"{safeName}.yml");
    }
}
