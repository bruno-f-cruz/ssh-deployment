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

        var serializer = new XmlSerializer(typeof(RecipeState));
        using var stream = File.OpenRead(path);
        return (RecipeState?)serializer.Deserialize(stream);
    }

    public void Save(IRecipe recipe, List<RecipeProperty> properties, IEnumerable<string> machineNames)
    {
        var state = new RecipeState
        {
            Properties = properties
                .Select(p => new PropertyValue { Name = p.Info.Name, Value = p.GetValueAsString(recipe) })
                .ToList(),
            Machines = machineNames.ToList(),
        };

        var serializer = new XmlSerializer(typeof(RecipeState));
        using var stream = File.Create(GetPath(recipe.Name));
        serializer.Serialize(stream, state);
    }

    private string GetPath(string recipeName)
    {
        var safeName = string.Concat(recipeName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_stateDirectory, $"{safeName}.xml");
    }
}
