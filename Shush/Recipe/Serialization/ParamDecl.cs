namespace Shush.Recipe.Serialization;

public enum ParamType { String, Dropdown, Collection }

public sealed class ParamDecl
{
    public ParamType Type { get; set; } = ParamType.String;
    public string? Label { get; set; }
    public string? Default { get; set; }
    public List<string>? Options { get; set; }

    /// <summary>
    /// A string param's suggested values, from a named option source (e.g. <c>clabe.versions</c>: the clabe releases
    /// with builds). Unlike a dropdown's <see cref="Options"/>, any other value can still be typed.
    /// </summary>
    public string? OptionsFrom { get; set; }
}
