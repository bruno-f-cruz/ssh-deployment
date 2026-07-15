using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shush.Recipe.Serialization;

public static class YamlRecipeSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public static RecipeDocument Deserialize(string yaml) =>
        Deserializer.Deserialize<RecipeDocument>(yaml)
        ?? throw new InvalidOperationException("Recipe YAML deserialized to null.");

    public static string Serialize(RecipeDocument document) => Serializer.Serialize(document);
}
