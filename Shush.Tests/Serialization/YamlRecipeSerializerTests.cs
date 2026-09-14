using Shush.Recipe.Serialization;

namespace Shush.Tests.Serialization;

public class YamlRecipeSerializerTests
{
    private const string Yaml = """
        name: Demo
        params:
          tag:
            type: string
            label: Git Tag
            default: v1.0.0
        vars:
          repoRoot: C:/git
        steps:
          - id: clone
            type: GitClone
            with:
              repositoryUrl: https://example/repo
              rootPath: ${vars.repoRoot}
          - type: GitCheckout
            with:
              path: ${clone.clonedPath}
              cleanExceptions: [".cache.json", "b.txt"]
        """;

    [Fact]
    public void Deserialize_populates_model()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);

        Assert.Equal("Demo", doc.Name);
        Assert.Equal("v1.0.0", doc.Params["tag"].Default);
        Assert.Equal(ParamType.String, doc.Params["tag"].Type);
        Assert.Equal("C:/git", doc.Vars["repoRoot"]);
        Assert.Equal(2, doc.Steps.Count);
        Assert.Equal("clone", doc.Steps[0].Id);
        Assert.Equal("GitClone", doc.Steps[0].Type);
        Assert.Equal("${vars.repoRoot}", doc.Steps[0].With["rootPath"]);
        var exceptions = Assert.IsAssignableFrom<IEnumerable<object>>(doc.Steps[1].With["cleanExceptions"]);
        Assert.Equal(2, exceptions.Count());
    }

    [Fact]
    public void Roundtrip_preserves_document()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);
        var again = YamlRecipeSerializer.Deserialize(YamlRecipeSerializer.Serialize(doc));
        Assert.Equal(doc.Name, again.Name);
        Assert.Equal(doc.Steps.Count, again.Steps.Count);
    }

    [Fact]
    public void Step_is_enabled_by_default_and_omitted_from_yaml()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);

        Assert.True(doc.Steps[0].IsEnabled);
        Assert.Null(doc.Steps[0].Enabled);
        Assert.DoesNotContain("enabled", YamlRecipeSerializer.Serialize(doc));
    }

    [Fact]
    public void Disabled_step_roundtrips_explicitly()
    {
        var doc = YamlRecipeSerializer.Deserialize(Yaml);
        doc.Steps[0].Enabled = false;

        var serialized = YamlRecipeSerializer.Serialize(doc);
        Assert.Contains("enabled: false", serialized);

        var again = YamlRecipeSerializer.Deserialize(serialized);
        Assert.False(again.Steps[0].IsEnabled);
    }
}
