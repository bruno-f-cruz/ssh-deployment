using Shush.Data;
using Shush.Recipe;
using Shush.Recipe.Serialization;
using Shush.Tests.Data;

namespace Shush.Tests;

public class RecipeOverridesTests
{
    private static readonly StepRegistry Registry = new(typeof(IRecipeStep).Assembly);

    private const string Yaml = """
        name: Two steps
        steps:
          - id: clone
            name: Clone
            type: GitClone
            with:
              repositoryUrl: https://github.com/example/repo
              rootPath: C:/git
          - type: RunScript
            name: Build
            with:
              commands:
                - echo build
          - type: Reboot
            name: Reboot
        """;

    private static List<PlannedStep> Plan(RecipeDocument document) =>
        new SerializedRecipe(document, Registry, FunctionLibrary.Default, new Dictionary<string, string>()).CreatePlan().Steps().ToList();

    [Fact]
    public void Keys_are_the_id_else_the_display_name()
    {
        var document = YamlRecipeSerializer.Deserialize(Yaml);
        Assert.Equal(["clone", "Build", "Reboot"], document.Steps.Select(s => s.Key));
    }

    [Fact]
    public void A_turned_off_step_is_skipped_with_its_reason_and_the_original_is_untouched()
    {
        var document = YamlRecipeSerializer.Deserialize(Yaml);
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Reboot", true);

        var steps = Plan(overrides.Apply(document));

        Assert.False(steps[2].Enabled);
        Assert.Equal(RecipeOverrides.DisabledReason, steps[2].SkipReason);
        Assert.True(steps[0].Enabled);
        Assert.True(document.Steps[2].IsEnabled);
    }

    [Fact]
    public void An_input_override_replaces_the_recipe_value()
    {
        var document = YamlRecipeSerializer.Deserialize(Yaml);
        var overrides = new RecipeOverrides();
        overrides.SetInput("clone", "rootPath", "D:/dev");

        var applied = overrides.Apply(document);

        Assert.Equal("D:/dev", applied.Steps[0].With["rootPath"]);
        Assert.Equal("C:/git", document.Steps[0].With["rootPath"]);
    }

    [Fact]
    public void Overrides_for_steps_the_recipe_no_longer_has_are_stale_and_ignored()
    {
        var document = YamlRecipeSerializer.Deserialize(Yaml);
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Gone", true);
        overrides.SetInput("Also gone", "x", "y");

        Assert.Equal(2, overrides.Stale(document).Count);
        Assert.All(Plan(overrides.Apply(document)), s => Assert.True(s.Enabled));
    }

    [Fact]
    public void Round_trips_through_json_and_clearing_removes_entries()
    {
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Build", true);
        overrides.SetInput("clone", "rootPath", "D:/dev");

        var copy = RecipeOverrides.FromJson(overrides.ToJson());
        Assert.True(copy.IsDisabled("Build"));
        Assert.Equal("D:/dev", copy.InputOverride("clone", "rootPath"));
        Assert.Equal(2, copy.Count);

        copy.SetDisabled("Build", false);
        copy.SetInput("clone", "rootPath", null);
        Assert.True(copy.IsEmpty);
        Assert.Empty(copy.Inputs);
        Assert.True(RecipeOverrides.FromJson("not json").IsEmpty);
    }

    [Fact]
    public void Detaching_bakes_a_turned_off_step_into_the_yaml()
    {
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Reboot", true);
        var yaml = YamlRecipeSerializer.Serialize(overrides.Apply(YamlRecipeSerializer.Deserialize(Yaml)));
        var reread = YamlRecipeSerializer.Deserialize(yaml);
        Assert.False(reread.Steps[2].IsEnabled);
        Assert.Equal("disabled", Plan(reread)[2].SkipReason);
    }
}

public class DeploymentCopyTests : DataTestBase
{
    [Fact]
    public async Task An_attached_copy_keeps_the_recipe_overrides_and_account_with_new_values()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("Rigs", machines: ["RIG-1"]);
        var prod = await Deployments.CreateAsync("prod", recipe.Id, group.Id, new Dictionary<string, string> { ["ref"] = "main" });
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Reboot", true);
        await Deployments.SaveOverridesAsync(prod.Id, overrides);
        await Deployments.SetSshUserAsync(prod.Id, " svc_vr ");

        var dev = await Deployments.DuplicateAsync(prod.Id, "dev", new Dictionary<string, string> { ["ref"] = "dev" });

        var loaded = (await Deployments.GetAsync(dev.Id))!;
        Assert.Equal(recipe.Id, loaded.RecipeId);
        Assert.Equal("svc_vr", loaded.SshUser);
        Assert.True(RecipeOverrides.FromJson(loaded.OverridesJson).IsDisabled("Reboot"));
        Assert.Equal("dev", Json.ReadParams(loaded.ParamValuesJson)["ref"]);
        await Assert.ThrowsAsync<ShushDataException>(() => Deployments.DuplicateAsync(prod.Id, "PROD", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task Detaching_moves_the_deployment_to_its_own_recipe_and_clears_overrides()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("Rigs", machines: ["RIG-1"]);
        var dev = await Deployments.CreateAsync("dev", recipe.Id, group.Id, new Dictionary<string, string>());
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Reboot", true);
        await Deployments.SaveOverridesAsync(dev.Id, overrides);

        var own = await Recipes.DuplicateAsync(recipe.Id, "Simple (dev)", null, doc => overrides.Apply(doc));
        await Deployments.SwitchRecipeAsync(dev.Id, own.Id);

        var loaded = (await Deployments.GetAsync(dev.Id))!;
        Assert.Equal(own.Id, loaded.RecipeId);
        Assert.True(RecipeOverrides.FromJson(loaded.OverridesJson).IsEmpty);
        var yaml = (await Recipes.CurrentVersionAsync(own.Id)).Yaml;
        Assert.False(YamlRecipeSerializer.Deserialize(yaml).Steps.Single().IsEnabled);
        Assert.True(YamlRecipeSerializer.Deserialize((await Recipes.CurrentVersionAsync(recipe.Id)).Yaml).Steps.Single().IsEnabled);
    }

    [Fact]
    public async Task The_list_shows_fixed_values_and_how_many_changes()
    {
        const string yaml = """
            name: Fixed
            params:
              ref:
                default: main
              folder:
                label: Install folder
                default: App
                fixed: true
            steps:
              - type: Reboot
            """;
        var recipe = await Recipes.CreateAsync(yaml, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("Rigs", machines: ["RIG-1"]);
        var dev = await Deployments.CreateAsync("dev", recipe.Id, group.Id, new Dictionary<string, string> { ["folder"] = "App-dev" });
        var overrides = new RecipeOverrides();
        overrides.SetDisabled("Reboot", true);
        await Deployments.SaveOverridesAsync(dev.Id, overrides);

        var summary = (await Deployments.ListAsync()).Single();
        Assert.Equal([new KeyValuePair<string, string>("Install folder", "App-dev")], summary.Fixed);
        Assert.Equal(1, summary.OverrideCount);
    }

    [Fact]
    public async Task A_deployment_can_move_to_another_group_and_a_copy_can_target_one()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var rigsA = await Groups.CreateAsync("A", machines: ["RIG-1"]);
        var rigsB = await Groups.CreateAsync("B", machines: ["RIG-2", "RIG-3"]);
        var prod = await Deployments.CreateAsync("prod", recipe.Id, rigsA.Id, new Dictionary<string, string>());

        var copy = await Deployments.DuplicateAsync(prod.Id, "prod B", new Dictionary<string, string>(), groupId: rigsB.Id);
        Assert.Equal(rigsB.Id, (await Deployments.GetAsync(copy.Id))!.GroupId);

        await Deployments.SetGroupAsync(prod.Id, rigsB.Id);
        Assert.Equal(2, (await Deployments.GetAsync(prod.Id))!.Group.Members.Count);
        await Assert.ThrowsAsync<ShushDataException>(() => Deployments.SetGroupAsync(prod.Id, 999));
    }
}