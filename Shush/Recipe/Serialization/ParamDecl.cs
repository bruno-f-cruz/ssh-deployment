namespace Shush.Recipe.Serialization;

public enum ParamType { String, Dropdown, Collection }

public sealed class ParamDecl
{
    public ParamType Type { get; set; } = ParamType.String;
    public string? Label { get; set; }
    public string? Default { get; set; }
    public List<string>? Options { get; set; }
}
