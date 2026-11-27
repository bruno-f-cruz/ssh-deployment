using Shush.Recipe;
using Shush.Recipe.Serialization;
using Shush.Recipe.Steps;

namespace Shush.Tests;

public class SharedAccessTests
{
    private static readonly StepRegistry Registry = new(typeof(IRecipeStep).Assembly);

    private static SerializedRecipe Recipe(string yaml) =>
        new(YamlRecipeSerializer.Deserialize(yaml), Registry, FunctionLibrary.Default, new Dictionary<string, string>());

    [Fact]
    public void Recipe_without_sharedAccess_has_no_grants() =>
        Assert.Empty(Recipe("name: T\nsteps: []").CreatePlan().SharedAccess);

    [Fact]
    public void Grants_default_to_Users_with_Modify_and_resolve_vars()
    {
        var grants = Recipe("""
            name: T
            vars:
              repoRoot: C:/git
            sharedAccess:
              paths:
                - ${vars.repoRoot}
                - C:/other
            steps: []
            """).CreatePlan().SharedAccess;

        Assert.Equal(["C:/git", "C:/other"], grants.Select(g => g.Path));
        Assert.All(grants, g =>
        {
            Assert.Equal("*S-1-5-32-545", g.Principal);
            Assert.Equal("M", g.Rights);
            Assert.False(g.ApplyToExisting);
        });
    }

    [Fact]
    public void Principal_rights_and_applyToExisting_are_configurable()
    {
        var grant = Recipe("""
            name: T
            sharedAccess:
              paths: [C:/git]
              principal: "*S-1-1-0"
              rights: RX
              applyToExisting: true
            steps: []
            """).CreatePlan().SharedAccess.Single();

        Assert.Equal(new SharedAccessGrant("C:/git", "*S-1-1-0", "RX", true), grant);
    }

    [Theory]
    [InlineData("C:/git", "*S-1-5-32-545", "M", true)]
    [InlineData("C:/git", "BUILTIN\\Users", "RX", true)]
    [InlineData("C:/git", "*S-1-5-32-545", "X", false)]
    [InlineData("C:/git", "x'; calc; '", "M", false)]
    [InlineData("C:/it's", "*S-1-5-32-545", "M", false)]
    [InlineData("", "*S-1-5-32-545", "M", false)]
    public void Grant_validation(string path, string principal, string rights, bool valid) =>
        Assert.Equal(valid, new SharedAccessGrant(path, principal, rights).Validate().Count == 0);

    [Fact]
    public void Validator_rejects_bad_rights_and_step_references()
    {
        var doc = YamlRecipeSerializer.Deserialize("""
            name: T
            sharedAccess:
              paths: ["${clone.clonedPath}"]
              rights: Z
            steps: []
            """);

        var ex = Assert.Throws<RecipeValidationException>(
            () => new RecipeValidator(Registry, FunctionLibrary.Default).Validate(doc));

        Assert.Contains(ex.Errors, e => e.Contains("rights"));
        Assert.Contains(ex.Errors, e => e.Contains("clone"));
    }

    [Fact]
    public void Validator_requires_at_least_one_path()
    {
        var doc = YamlRecipeSerializer.Deserialize("name: T\nsharedAccess:\n  rights: M\nsteps: []");

        var ex = Assert.Throws<RecipeValidationException>(
            () => new RecipeValidator(Registry, FunctionLibrary.Default).Validate(doc));

        Assert.Contains(ex.Errors, e => e.Contains("at least one path"));
    }

    [Fact]
    public void Settings_default_grants_nothing_and_binds_paths()
    {
        Assert.Empty(new ShushSettings().SharedAccess.ToGrants());

        var settings = new ShushSettings { SharedAccess = new SharedAccessSpec { Paths = ["C:/git"] } };
        Assert.Equal([new SharedAccessGrant("C:/git")], settings.SharedAccess.ToGrants());
    }

    [Fact]
    public void Runner_rejects_invalid_machine_wide_grant()
    {
        var recipe = Recipe("name: T\nsteps: []");
        Assert.Throws<ArgumentException>(() => new RecipeRunner(
            recipe, new(), new Secrets(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            machineWideAccess: [new SharedAccessGrant("C:/git", Rights: "Z")]));
    }

    [Fact]
    public async Task GitClone_rejects_unknown_safe_directory_scope()
    {
        var step = new GitCloneStep { RepositoryUrl = "https://x/y", RootPath = "C:/git", SafeDirectoryScope = "local" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.ExecuteAsync(null!));
    }
}
