using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests;

public class RecipeStoreTests
{
    private static RecipeStore Store() => new(new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default);

    [Fact]
    public void Loads_recipes_from_directory()
    {
        var dir = TestDir.WithFiles(("a.yml", "name: A\nsteps: []"), ("b.yml", "name: B\nsteps: []"));
        var recipes = Store().Discover([dir]);
        Assert.Equal(new[] { "A", "B" }, recipes.Select(r => r.Name).OrderBy(n => n));
    }

    [Fact]
    public void User_dir_overrides_base_by_name()
    {
        var baseDir = TestDir.WithFiles(("a.yml", "name: A\nvars:\n  x: base\nsteps: []"));
        var userDir = TestDir.WithFiles(("a.yml", "name: A\nvars:\n  x: user\nsteps: []"));
        var recipe = (SerializedRecipe)Store().Discover([baseDir, userDir]).Single();
        Assert.Equal("user", recipe.Document.Vars["x"]);
    }

    [Fact]
    public void Missing_directory_is_skipped()
    {
        var recipes = Store().Discover([Path.Combine(TestDir.New(), "does-not-exist")]);
        Assert.Empty(recipes);
    }
}

public class BuiltinRecipesTests
{
    [Theory]
    [InlineData("VrForaging.yml")]
    [InlineData("VrForagingDev.yml")]
    public void Builtin_recipe_validates(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Recipes", file);
        var doc = YamlRecipeSerializer.Deserialize(File.ReadAllText(path));
        new RecipeValidator(new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default).Validate(doc);
    }
}
