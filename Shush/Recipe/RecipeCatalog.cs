namespace Shush.Recipe;

public static class RecipeCatalog
{
    public static List<IRecipe> Discover()
    {
        var recipeTypes = typeof(IRecipe).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.IsAssignableTo(typeof(IRecipe))
                        && t.GetConstructor(Type.EmptyTypes) is not null);

        return recipeTypes
            .Select(t => (IRecipe?)Activator.CreateInstance(t))
            .OfType<IRecipe>()
            .ToList();
    }
}
