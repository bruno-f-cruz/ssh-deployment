using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests;

public class BuiltinRecipesTests
{
    [Theory]
    [InlineData("VrForagingExperiment.yml")]
    [InlineData("Reboot.yml")]
    [InlineData("CleanDesktop.yml")]
    public void Builtin_recipe_validates(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Recipes", file);
        var doc = YamlRecipeSerializer.Deserialize(File.ReadAllText(path));
        new RecipeValidator(new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default).Validate(doc);
    }
}
