using Shush.Recipe.Serialization;

namespace Shush.Tests.Design;

public class RecipeFormBinderTests
{
    [Fact]
    public void Binder_maps_param_types_to_editor_kinds()
    {
        var doc = YamlRecipeSerializer.Deserialize("""
            name: T
            params:
              tag:
                type: string
                label: Git Tag
                default: v1
              mode:
                type: dropdown
                options: [a, b]
            steps: []
            """);
        var fields = RecipeFormBinder.GetFields(doc);
        Assert.Equal(PropertyEditorKind.Text, fields.Single(f => f.Name == "tag").Kind);
        Assert.Equal("Git Tag", fields.Single(f => f.Name == "tag").Label);
        Assert.Equal("v1", fields.Single(f => f.Name == "tag").Default);
        Assert.Equal(PropertyEditorKind.Dropdown, fields.Single(f => f.Name == "mode").Kind);
        Assert.Equal(new[] { "a", "b" }, fields.Single(f => f.Name == "mode").Options);
    }

    [Fact]
    public void Label_falls_back_to_humanized_name()
    {
        var doc = YamlRecipeSerializer.Deserialize("""
            name: T
            params:
              gitTag:
                type: string
            steps: []
            """);
        Assert.Equal("Git Tag", RecipeFormBinder.GetFields(doc).Single().Label);
    }
}
