using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shush.Groups;

public static class GroupYamlSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static GroupDocument Deserialize(string yaml) =>
        Deserializer.Deserialize<GroupDocument>(yaml)
        ?? throw new InvalidOperationException("Group YAML deserialized to null.");

    public static string Serialize(GroupDocument document) => Serializer.Serialize(document);
}
