namespace Shush.Recipe.Serialization;

public sealed class RecipeDocument
{
    public string Name { get; set; } = "";
    public Dictionary<string, ParamDecl> Params { get; set; } = new();
    public Dictionary<string, string> Vars { get; set; } = new();
    public List<StepSpec> Steps { get; set; } = new();
}
