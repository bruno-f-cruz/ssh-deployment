using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shush.Data;
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Data;

/// <summary>A fresh, migrated SQLite database file per test, and the services over it.</summary>
public abstract class DataTestBase
{
    protected DataTestBase()
    {
        Directory = TestDir.New();
        Factory = new FileFactory(Path.Combine(Directory, "shush.db"));
        using (var db = Factory.CreateDbContext())
            db.Database.Migrate();

        var registry = new StepRegistry(typeof(IRecipeStep).Assembly);
        Recipes = new RecipeService(Factory, registry, FunctionLibrary.Default);
        Groups = new GroupService(Factory);
        Machines = new MachineService(Factory);
        Variables = new VariableService(Factory, new ReversingProtector());
        Deployments = new DeploymentService(Factory);
        Runs = new RunService(Factory);
    }

    protected string Directory { get; }
    protected IDbContextFactory<ShushDbContext> Factory { get; }
    protected RecipeService Recipes { get; }
    protected GroupService Groups { get; }
    protected MachineService Machines { get; }
    protected VariableService Variables { get; }
    protected DeploymentService Deployments { get; }
    protected RunService Runs { get; }

    protected Seeder Seeder() => new(Factory, Recipes, Groups, Deployments, NullLogger<Seeder>.Instance);

    protected const string SimpleRecipe = """
        name: Simple
        params:
          ref:
            default: main
        steps:
          - type: Reboot
        """;

    private sealed class FileFactory(string path) : IDbContextFactory<ShushDbContext>
    {
        public ShushDbContext CreateDbContext() => new(ShushDbContext.OptionsFor(path));
    }

    /// <summary>Reversible and obviously not plaintext, so tests can see that values are stored protected.</summary>
    protected sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + new string(plaintext.Reverse().ToArray());
        public string Unprotect(string protectedValue) => new(protectedValue["enc:".Length..].Reverse().ToArray());
    }
}
