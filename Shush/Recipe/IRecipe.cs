namespace Shush.Recipe;

public interface IRecipe
{
    string Name { get; }
    IEnumerable<IRecipeStep> Steps { get; }
}
