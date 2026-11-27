using Shush.Recipe;
using Shush.Recipe.Options;
using Shush.Recipe.Serialization;
using Shush.Recipe.Steps;

namespace Shush.Tests;

public class ConditionEvaluatorTests
{
    [Theory]
    [InlineData("dev == dev", true)]
    [InlineData("dev == prod", false)]
    [InlineData("dev != prod", true)]
    [InlineData("\"-dev\" == \"-dev\"", true)]
    [InlineData(" Dev ==dev ", true)]
    [InlineData("\"a == b\" == \"a == b\"", true)]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("off", false)]
    public void Evaluates(string expression, bool expected) =>
        Assert.Equal(expected, ConditionEvaluator.Evaluate(expression));

    [Fact]
    public void Rejects_a_value_that_is_not_true_or_false() =>
        Assert.Throws<RecipeValidationException>(() => ConditionEvaluator.Evaluate("maybe"));

    [Theory]
    [InlineData("${params.x} == dev", true)]
    [InlineData("${params.flag}", true)]
    [InlineData("true", true)]
    [InlineData("sometimes", false)]
    public void Checks_syntax_before_resolution(string template, bool ok) =>
        Assert.Equal(ok, ConditionEvaluator.CheckSyntax(template) is null);
}

public class RecipeV2FeatureTests
{
    private static readonly StepRegistry Registry = new(typeof(IRecipeStep).Assembly);

    private static SerializedRecipe Recipe(string yaml, Dictionary<string, string>? paramValues = null) =>
        new(YamlRecipeSerializer.Deserialize(yaml), Registry, FunctionLibrary.Default, paramValues ?? new Dictionary<string, string>());

    private static void Validate(string yaml, IReadOnlyCollection<string>? groupVariables = null) =>
        new RecipeValidator(Registry, FunctionLibrary.Default).Validate(YamlRecipeSerializer.Deserialize(yaml), groupVariables);

    private const string WhenRecipe = """
        name: T
        params:
          flavor:
            default: prod
        steps:
          - type: Reboot
            name: Reboot dev only
            when: ${params.flavor} == dev
          - type: Reboot
            name: Always
        """;

    [Fact]
    public void When_false_skips_the_step_with_a_reason()
    {
        var steps = Recipe(WhenRecipe).CreatePlan().Steps().ToList();

        Assert.False(steps[0].Enabled);
        Assert.Null(steps[0].Step);
        Assert.Contains("when:", steps[0].SkipReason);
        Assert.True(steps[1].Enabled);
    }

    [Fact]
    public void When_true_runs_the_step()
    {
        var steps = Recipe(WhenRecipe, new() { ["flavor"] = "dev" }).CreatePlan().Steps().ToList();
        Assert.True(steps[0].Enabled);
    }

    [Fact]
    public void Step_name_is_the_display_name()
    {
        var recipe = Recipe(WhenRecipe);
        Assert.Equal(["Reboot dev only", "Always"], recipe.StepNames);
    }

    [Fact]
    public void Validation_rejects_a_malformed_when()
    {
        var ex = Assert.Throws<RecipeValidationException>(() => Validate("""
            name: T
            steps:
              - type: Reboot
                when: sometimes
            """));
        Assert.Contains(ex.Errors, e => e.Contains("when"));
    }

    [Fact]
    public void Validation_rejects_an_unknown_param_in_when() =>
        Assert.Throws<RecipeValidationException>(() => Validate("""
            name: T
            steps:
              - type: Reboot
                when: ${params.nope} == x
            """));

    private const string GroupRecipe = """
        name: T
        vars:
          root: ${group.repoRoot}/x
        steps:
          - id: clone
            type: GitClone
            with:
              repositoryUrl: https://x/y
              rootPath: ${vars.root}
              folderName: ${group.folder}
        """;

    [Fact]
    public void Group_variables_resolve_per_machine_in_vars_and_inputs()
    {
        var plan = Recipe(GroupRecipe).CreatePlan(new PlanContext("FRG.0-A", new Dictionary<string, string>
        {
            ["repoRoot"] = "D:/git",
            ["folder"] = "y-dev",
        }));

        var clone = (GitCloneStep)plan.Steps().Single().Step!;
        Assert.Equal("D:/git/x", clone.RootPath);
        Assert.Equal("y-dev", clone.FolderName);
    }

    [Fact]
    public void Group_variable_missing_at_run_time_fails_resolution() =>
        Assert.Throws<RecipeValidationException>(() => Recipe(GroupRecipe).CreatePlan());

    [Fact]
    public void Group_references_are_accepted_without_a_deployment() => Validate(GroupRecipe);

    [Fact]
    public void Group_references_are_checked_against_a_deployment_s_variables()
    {
        Validate(GroupRecipe, ["repoRoot", "folder"]);
        var ex = Assert.Throws<RecipeValidationException>(() => Validate(GroupRecipe, ["repoRoot"]));
        Assert.Contains(ex.Errors, e => e.Contains("'folder'"));
    }

    [Fact]
    public void Fixed_param_round_trips_and_reaches_the_form()
    {
        var doc = YamlRecipeSerializer.Deserialize("""
            name: T
            params:
              folder:
                default: a
                fixed: true
              ref:
                default: main
            steps: []
            """);

        var fields = RecipeFormBinder.GetFields(doc);
        Assert.True(fields.Single(f => f.Name == "folder").Fixed);
        Assert.False(fields.Single(f => f.Name == "ref").Fixed);

        var yaml = YamlRecipeSerializer.Serialize(doc);
        Assert.Contains("fixed: true", yaml);
        Assert.DoesNotContain("isFixed", yaml);
        Assert.DoesNotContain("isEnabled", yaml);
        Assert.DoesNotContain("displayName", yaml);
    }

    [Fact]
    public void Capture_outputs_returns_outputs_even_without_an_id()
    {
        var plan = Recipe("""
            name: T
            steps:
              - type: GitClone
                with:
                  repositoryUrl: https://x/y
                  rootPath: C:/git
            """).CreatePlan();

        var outputs = plan.Steps().Single().CaptureOutputs();
        Assert.Equal(@"C:/git\y", outputs["clonedPath"]);
    }

    [Fact]
    public void Git_checkout_declares_a_commit_output() =>
        Assert.Contains(Registry.Get("GitCheckout").Outputs, o => o.Name == "commit");
}

public class MergedVrForagingRecipeTests
{
    private static RecipeDocument Load() =>
        YamlRecipeSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recipes", "VrForagingExperiment.yml")));

    [Fact]
    public void Install_params_are_fixed_and_ref_is_not()
    {
        var fields = RecipeFormBinder.GetFields(Load());
        Assert.False(fields.Single(f => f.Name == "ref").Fixed);
        Assert.All(new[] { "installFolder", "shortcutName", "iconFile" }, name => Assert.True(fields.Single(f => f.Name == name).Fixed));
    }

    [Fact]
    public void A_dev_deployment_installs_next_to_prod()
    {
        var registry = new StepRegistry(typeof(IRecipeStep).Assembly);
        var recipe = new SerializedRecipe(Load(), registry, FunctionLibrary.Default, new Dictionary<string, string>
        {
            ["ref"] = "v1.4.0",
            ["installFolder"] = "Aind.Experiment.VrForaging-dev",
            ["shortcutName"] = "DEV-VrForagingExperiment",
            ["iconFile"] = "icon_dev.ico",
        });

        var preview = PlanPreview.For("FRG.4-A", recipe, MachineRunSettings.Empty);
        Assert.Null(preview.Error);

        string Input(string step, string input) =>
            preview.Steps.Single(s => s.Name == step).Inputs.Single(i => i.Key == input).Value;

        Assert.Equal("Aind.Experiment.VrForaging-dev", Input("Clone", "folderName"));
        Assert.Equal("v1.4.0", Input("Checkout", "reference"));
        Assert.Equal("DEV-VrForagingExperiment", Input("Desktop shortcut", "shortName"));
        Assert.EndsWith("Aind.Experiment.VrForaging-dev/assets/icon_dev.ico", Input("Desktop shortcut", "iconPath"));
    }
}

public class PlanPreviewTests
{
    [Fact]
    public void Masks_values_and_lists_the_environment_phase_first()
    {
        var recipe = new SerializedRecipe(
            YamlRecipeSerializer.Deserialize("""
                name: T
                steps:
                  - type: WriteFile
                    with:
                      targetPath: C:/x.txt
                      content: token=${group.token}
                """),
            new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default, new Dictionary<string, string>());

        var settings = new MachineRunSettings
        {
            Variables = new Dictionary<string, string> { ["token"] = "hunter2" },
            Environment = [new EnvironmentAssignment("TOKEN", "hunter2", Secret: true), new EnvironmentAssignment("HUB", "http://hub")],
        };

        var preview = PlanPreview.For("M", recipe, settings, v => v.Replace("hunter2", "••••"));

        Assert.Equal(RecipeRunner.EnvironmentStepName, preview.Steps[0].Name);
        Assert.DoesNotContain(preview.Steps.SelectMany(s => s.Inputs), i => i.Value.Contains("hunter2"));
        Assert.Contains(preview.Steps[0].Inputs, i => i is { Key: "HUB", Value: "http://hub" });
    }

    [Fact]
    public void Reports_a_resolution_error_instead_of_throwing()
    {
        var recipe = new SerializedRecipe(
            YamlRecipeSerializer.Deserialize("""
                name: T
                steps:
                  - type: Reboot
                    when: ${group.missing}
                """),
            new StepRegistry(typeof(IRecipeStep).Assembly), FunctionLibrary.Default, new Dictionary<string, string>());

        Assert.NotNull(PlanPreview.For("M", recipe, MachineRunSettings.Empty).Error);
    }
}

public class PreflightTests
{
    [Theory]
    [InlineData(@"CORP\jane", "jane", true)]
    [InlineData("jane@corp.org", "Jane", true)]
    [InlineData(@"CORP\jane", @"corp\JANE", true)]
    [InlineData(@"CORP\jane", "john", false)]
    public void Compares_accounts_ignoring_the_domain(string a, string b, bool same) =>
        Assert.Equal(same, Preflight.SameAccount(a, b));

    [Fact]
    public void Parses_the_facts_line()
    {
        var facts = Preflight.ParseFacts("""
            some banner
            {"user":"CORP\\jane","boot":"2026-11-20T08:00:00.0000000Z","os":"Windows 11","drive":"C:","freeGb":3.2,"sizeGb":476.1}
            """);

        Assert.Equal(@"CORP\jane", facts.LoggedOnUser);
        Assert.Equal(3.2, facts.FreeGb);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), facts.LastBoot);
    }

    [Fact]
    public void Warns_on_another_user_and_low_disk()
    {
        var checks = Preflight.Evaluate(new MachineFacts(@"CORP\john", null, null, "C:", 2, 100), "jane").ToList();
        Assert.All(checks, c => Assert.Equal(CheckLevel.Warning, c.Level));
        Assert.Equal(2, checks.Count);
    }

    [Fact]
    public void Ok_when_the_ssh_user_is_logged_on_with_room_to_spare()
    {
        var checks = Preflight.Evaluate(new MachineFacts(@"CORP\jane", null, null, "C:", 200, 400), "jane");
        Assert.All(checks, c => Assert.Equal(CheckLevel.Ok, c.Level));
    }
}

public class GitRefOptionsTests
{
    [Theory]
    [InlineData("git.refs(https://github.com/AllenNeuralDynamics/Aind.Experiment.VrForaging)", true)]
    [InlineData("git.refs( https://github.com/a/b.git )", true)]
    [InlineData("git.refs(https://gitlab.com/a/b)", false)]
    [InlineData("clabe.versions", false)]
    public void Recognises_github_repositories(string name, bool recognised) =>
        Assert.Equal(recognised, GitRefOptions.TryCreate(name, new HttpClient()) is not null);
}
