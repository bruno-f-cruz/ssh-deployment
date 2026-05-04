namespace Shush.Recipe;

public interface IRecipeStep
{
    Task ExecuteAsync(MachineContext context, CancellationToken cancellationToken = default);
}
