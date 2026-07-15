using System.Xml.Serialization;
using Shush;
using Shush.Recipe;

namespace Shush.Design.Services;

public sealed class PropertyValue
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    [XmlText]
    public string Value { get; set; } = string.Empty;
}

public sealed class RecipeState
{
    public string RecipeName { get; set; } = string.Empty;

    [XmlArrayItem("Property")]
    public List<PropertyValue> Properties { get; set; } = [];

    [XmlArrayItem("Machine")]
    public List<string> Machines { get; set; } = [];
}

public sealed class RecipeStateStore
{
    private readonly string _stateDirectory;

    public RecipeStateStore(IWebHostEnvironment env, ShushSettings settings)
    {
        _stateDirectory = Path.Combine(ShushPaths.GetShushDirectory(env, settings), "state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public RecipeState? Load(string recipeName)
    {
        var path = GetPath(recipeName);
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        return DeserializeXml(stream);
    }

    public void Save(IRecipe recipe, List<RecipeProperty> properties, IEnumerable<string> machineNames)
    {
        var state = BuildState(recipe, properties, machineNames);
        using var stream = File.Create(GetPath(recipe.Name));
        Serialize(state, stream);
    }

    /// <summary>Serializes the current state to an XML string, for an explicit "download config" export.</summary>
    public string SerializeToXml(IRecipe recipe, List<RecipeProperty> properties, IEnumerable<string> machineNames)
    {
        var state = BuildState(recipe, properties, machineNames);
        using var stream = new MemoryStream();
        Serialize(state, stream);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Deserializes an uploaded "config" file, for an explicit "upload config" import.</summary>
    public RecipeState? DeserializeXml(Stream stream)
    {
        var serializer = new XmlSerializer(typeof(RecipeState));
        return (RecipeState?)serializer.Deserialize(stream);
    }

    private static RecipeState BuildState(IRecipe recipe, List<RecipeProperty> properties, IEnumerable<string> machineNames) =>
        new()
        {
            RecipeName = recipe.Name,
            Properties = properties
                .Select(p => new PropertyValue { Name = p.Info.Name, Value = p.GetValueAsString(recipe) })
                .ToList(),
            Machines = machineNames.ToList(),
        };

    private static void Serialize(RecipeState state, Stream stream) =>
        new XmlSerializer(typeof(RecipeState)).Serialize(stream, state);

    private string GetPath(string recipeName)
    {
        var safeName = string.Concat(recipeName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_stateDirectory, $"{safeName}.xml");
    }
}
