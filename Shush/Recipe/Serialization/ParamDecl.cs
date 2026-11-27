using YamlDotNet.Serialization;

namespace Shush.Recipe.Serialization;

public enum ParamType { String, Dropdown, Collection }

public sealed class ParamDecl
{
    public ParamType Type { get; set; } = ParamType.String;
    public string? Label { get; set; }

    /// <summary>A sentence shown under the field (what the value means); keeps <see cref="Label"/> short.</summary>
    public string? Help { get; set; }
    public string? Default { get; set; }
    public List<string>? Options { get; set; }

    /// <summary>
    /// A string param's suggested values, from a named option source (e.g. <c>clabe.versions</c>: the clabe releases
    /// with builds). Unlike a dropdown's <see cref="Options"/>, any other value can still be typed.
    /// </summary>
    public string? OptionsFrom { get; set; }

    /// <summary>
    /// Set once per deployment (when it's created), then read-only on the deploy screen. For values that decide
    /// <em>where</em> an install goes (its folder, its shortcut), so two deployments of one recipe never collide.
    /// Omitted from YAML unless true.
    /// </summary>
    public bool? Fixed { get; set; }

    [YamlIgnore]
    public bool IsFixed => Fixed ?? false;
}
