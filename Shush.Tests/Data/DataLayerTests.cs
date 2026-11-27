using Microsoft.EntityFrameworkCore;
using Shush.Data;
using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Data;

public class RecipeServiceTests : DataTestBase
{
    [Fact]
    public async Task Saving_creates_versions_and_skips_unchanged_content()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, "jane");
        var same = await Recipes.SaveAsync(recipe.Id, SimpleRecipe, VersionSource.Edit, "jane");
        Assert.Equal(1, same.Number);

        var changed = await Recipes.SaveAsync(recipe.Id, SimpleRecipe.Replace("main", "v2"), VersionSource.Edit, "john");
        Assert.Equal(2, changed.Number);
        Assert.Equal("john", changed.CreatedBy);
        Assert.Equal(2, (await Recipes.VersionsAsync(recipe.Id)).Count);
    }

    [Fact]
    public async Task Saving_refuses_invalid_yaml_and_a_different_name()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        await Assert.ThrowsAsync<RecipeValidationException>(() => Recipes.SaveAsync(recipe.Id, "name: Simple\nsteps:\n  - type: Nope\n", VersionSource.Edit, null));
        await Assert.ThrowsAsync<ShushDataException>(() => Recipes.SaveAsync(recipe.Id, SimpleRecipe.Replace("name: Simple", "name: Other"), VersionSource.Edit, null));
    }

    [Fact]
    public async Task Duplicate_names_are_refused_ignoring_case()
    {
        await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        await Assert.ThrowsAsync<ShushDataException>(() => Recipes.CreateAsync(SimpleRecipe.Replace("Simple", "SIMPLE"), VersionSource.Edit, null));
    }

    [Fact]
    public async Task Import_adds_a_version_to_an_existing_recipe()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        await Recipes.ImportAsync(SimpleRecipe.Replace("main", "v3"), null);
        Assert.Equal(2, (await Recipes.CurrentVersionAsync(recipe.Id)).Number);
    }

    [Fact]
    public async Task Rename_rewrites_the_yaml_name_in_a_new_version()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        await Recipes.RenameAsync(recipe.Id, "Renamed", null);
        var current = await Recipes.CurrentVersionAsync(recipe.Id);
        Assert.Equal("Renamed", YamlRecipeSerializer.Deserialize(current.Yaml).Name);
        Assert.Equal("Renamed", (await Recipes.GetAsync(recipe.Id))!.Name);
    }

    [Fact]
    public async Task Duplicate_copies_the_current_version_into_a_new_recipe()
    {
        var source = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        await Recipes.SaveAsync(source.Id, SimpleRecipe.Replace("main", "v2"), VersionSource.Edit, null);

        var copy = await Recipes.DuplicateAsync(source.Id, "  Simple copy ", "jane");

        var stored = (await Recipes.GetAsync(copy.Id))!;
        Assert.Equal("Simple copy", stored.Name);
        Assert.False(stored.IsBuiltIn);
        Assert.Equal("Simple v2", stored.CopiedFrom);

        var version = Assert.Single(await Recipes.VersionsAsync(copy.Id));
        Assert.Equal(1, version.Number);
        Assert.Equal(VersionSource.Duplicate, version.Source);
        Assert.Equal("jane", version.CreatedBy);
        var document = YamlRecipeSerializer.Deserialize(version.Yaml);
        Assert.Equal("Simple copy", document.Name);
        Assert.Equal("v2", document.Params["ref"].Default);

        // The source is untouched.
        Assert.Equal(2, (await Recipes.CurrentVersionAsync(source.Id)).Number);
    }

    [Fact]
    public async Task Duplicate_applies_the_transform_and_keeps_the_new_name()
    {
        var source = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);

        var copy = await Recipes.DuplicateAsync(source.Id, "Detached", null, d =>
        {
            d.Params["ref"].Default = "release-7";
            d.Name = "Something else";
            return d;
        });

        var document = YamlRecipeSerializer.Deserialize((await Recipes.CurrentVersionAsync(copy.Id)).Yaml);
        Assert.Equal("Detached", document.Name);
        Assert.Equal("release-7", document.Params["ref"].Default);
        Assert.Equal("Detached", (await Recipes.GetAsync(copy.Id))!.Name);
    }

    [Fact]
    public async Task Duplicate_refuses_a_taken_or_empty_name()
    {
        var source = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var taken = await Assert.ThrowsAsync<ShushDataException>(() => Recipes.DuplicateAsync(source.Id, "SIMPLE", null));
        Assert.Equal("A recipe named 'SIMPLE' already exists.", taken.Message);
        await Assert.ThrowsAsync<ShushDataException>(() => Recipes.DuplicateAsync(source.Id, "   ", null));
        await Assert.ThrowsAsync<ShushDataException>(() => Recipes.DuplicateAsync(source.Id + 99, "Other", null));
        Assert.Single(await Recipes.ListAsync());
    }

    [Fact]
    public void Diff_marks_added_and_removed_lines()
    {
        var lines = TextDiff.Lines("a\nb\nc", "a\nB\nc\nd");
        Assert.Contains(lines, l => l is { Kind: DiffKind.Removed, Text: "b" });
        Assert.Contains(lines, l => l is { Kind: DiffKind.Added, Text: "B" });
        Assert.Contains(lines, l => l is { Kind: DiffKind.Added, Text: "d" });
        Assert.Equal(2, lines.Count(l => l.Kind == DiffKind.Unchanged));
    }

    [Fact]
    public void Param_diff_lists_only_changed_values()
    {
        var changes = TextDiff.Params(
            new Dictionary<string, string> { ["ref"] = "v1", ["x"] = "same" },
            new Dictionary<string, string> { ["ref"] = "v2", ["x"] = "same", ["new"] = "1" });
        Assert.Equal(["new", "ref"], changes.Select(c => c.Name));
    }
}

public class SeederTests : DataTestBase
{
    private SeedPaths Paths(string? legacy = null) => new(
        Path.Combine(AppContext.BaseDirectory, "Recipes"),
        Path.Combine(AppContext.BaseDirectory, "Groups"),
        Path.Combine(AppContext.BaseDirectory, "Deployments"),
        legacy);

    [Fact]
    public async Task Seeds_shipped_recipes_groups_and_deployments_once()
    {
        await Seeder().RunAsync(Paths());
        await Seeder().RunAsync(Paths());

        var recipes = await Recipes.ListAsync();
        Assert.Contains(recipes, r => r is { Name: "VrForagingExperiment", IsBuiltIn: true, Version: 1 });

        var group = await Groups.FindAsync("VR-Foraging Behavior Machines");
        Assert.NotNull(group);
        Assert.Equal(15, group.Members.Count);

        var deployments = await Deployments.ListAsync();
        Assert.Equal(["VrForaging", "VrForaging dev"], deployments.Select(d => d.Name));

        var dev = await Deployments.GetAsync(deployments.Single(d => d.Name == "VrForaging dev").Id);
        Assert.Equal("Aind.Experiment.VrForaging-dev", Json.ReadParams(dev!.ParamValuesJson)["installFolder"]);
    }

    [Fact]
    public async Task A_deleted_shipped_deployment_stays_deleted()
    {
        await Seeder().RunAsync(Paths());
        var dev = (await Deployments.ListAsync()).Single(d => d.Name == "VrForaging dev");
        await Deployments.DeleteAsync(dev.Id);

        await Seeder().RunAsync(Paths());
        Assert.DoesNotContain(await Deployments.ListAsync(), d => d.Name == "VrForaging dev");
    }

    [Fact]
    public async Task A_changed_shipped_recipe_updates_an_untouched_copy_and_spares_an_edited_one()
    {
        var shipped = Path.Combine(Directory, "shipped");
        System.IO.Directory.CreateDirectory(shipped);
        var paths = new SeedPaths(shipped, Path.Combine(Directory, "none"), Path.Combine(Directory, "none"), null);

        await File.WriteAllTextAsync(Path.Combine(shipped, "a.yml"), SimpleRecipe);
        await File.WriteAllTextAsync(Path.Combine(shipped, "b.yml"), SimpleRecipe.Replace("Simple", "Edited"));
        await Seeder().RunAsync(paths);

        var edited = await Recipes.FindAsync("Edited");
        await Recipes.SaveAsync(edited!.Id, SimpleRecipe.Replace("Simple", "Edited").Replace("main", "mine"), VersionSource.Edit, null);

        await File.WriteAllTextAsync(Path.Combine(shipped, "a.yml"), SimpleRecipe.Replace("main", "v2"));
        await File.WriteAllTextAsync(Path.Combine(shipped, "b.yml"), SimpleRecipe.Replace("Simple", "Edited").Replace("main", "v2"));
        await Seeder().RunAsync(paths);

        var list = await Recipes.ListAsync();
        Assert.Contains(list, r => r is { Name: "Simple", Version: 2, ShippedUpdateAvailable: false });
        Assert.Contains(list, r => r is { Name: "Edited", Version: 2, ShippedUpdateAvailable: true });
        Assert.Contains("mine", (await Recipes.CurrentVersionAsync(edited.Id)).Yaml);

        await Recipes.ResetToShippedAsync(edited.Id, null);
        Assert.Contains("v2", (await Recipes.CurrentVersionAsync(edited.Id)).Yaml);
    }

    [Fact]
    public async Task A_shipped_recipe_that_no_longer_ships_is_deleted_when_unused()
    {
        var shipped = Path.Combine(Directory, "shipped");
        System.IO.Directory.CreateDirectory(shipped);
        var paths = new SeedPaths(shipped, Path.Combine(Directory, "none"), Path.Combine(Directory, "none"), null);
        await File.WriteAllTextAsync(Path.Combine(shipped, "a.yml"), SimpleRecipe);
        await File.WriteAllTextAsync(Path.Combine(shipped, "b.yml"), SimpleRecipe.Replace("Simple", "Retired"));
        await Seeder().RunAsync(paths);

        File.Delete(Path.Combine(shipped, "b.yml"));
        await Seeder().RunAsync(paths);

        Assert.Null(await Recipes.FindAsync("Retired"));
        Assert.True((await Recipes.FindAsync("Simple"))!.IsBuiltIn);
    }

    [Fact]
    public async Task A_shipped_recipe_that_no_longer_ships_becomes_a_user_recipe_when_in_use()
    {
        var shipped = Path.Combine(Directory, "shipped");
        System.IO.Directory.CreateDirectory(shipped);
        var paths = new SeedPaths(shipped, Path.Combine(Directory, "none"), Path.Combine(Directory, "none"), null);
        await File.WriteAllTextAsync(Path.Combine(shipped, "b.yml"), SimpleRecipe.Replace("Simple", "Retired"));
        await Seeder().RunAsync(paths);

        var retired = (await Recipes.FindAsync("Retired"))!;
        var group = await Groups.CreateAsync("Rigs", machines: ["RIG-1"]);
        var deployment = await Deployments.CreateAsync("Retired on rigs", retired.Id, group.Id, new Dictionary<string, string>());

        File.Delete(Path.Combine(shipped, "b.yml"));
        await Seeder().RunAsync(paths);

        var kept = (await Recipes.FindAsync("Retired"))!;
        Assert.False(kept.IsBuiltIn);
        Assert.Null(kept.ShippedYaml);
        Assert.Null(kept.ShippedHash);
        Assert.Equal(1, (await Recipes.CurrentVersionAsync(kept.Id)).Number);

        // It's the operators' now: deletable once nothing uses it.
        await Deployments.DeleteAsync(deployment.Id);
        await Recipes.DeleteAsync(kept.Id);
        Assert.Null(await Recipes.FindAsync("Retired"));
    }

    [Fact]
    public async Task Imports_v1_files_once()
    {
        var legacy = Path.Combine(Directory, ".shush");
        System.IO.Directory.CreateDirectory(Path.Combine(legacy, "recipes"));
        System.IO.Directory.CreateDirectory(Path.Combine(legacy, "groups"));
        System.IO.Directory.CreateDirectory(Path.Combine(legacy, "state"));

        await File.WriteAllTextAsync(Path.Combine(legacy, "recipes", "Simple.yml"), SimpleRecipe);
        await File.WriteAllTextAsync(Path.Combine(legacy, "groups", "Mine.yml"), "name: Mine\nmachines:\n  - FRG.1-A\n  - FRG.1-B\n");
        await File.WriteAllTextAsync(Path.Combine(legacy, "state", "Simple.yml"), "group: Mine\nparamOverrides:\n  ref: v9\n");
        await File.WriteAllTextAsync(Path.Combine(legacy, "state", "CleanDesktop.yml"), "machines:\n  - FRG.2-A\n");

        await Seeder().RunAsync(Paths(legacy));
        await Seeder().RunAsync(Paths(legacy));

        var deployments = await Deployments.ListAsync();
        var simple = deployments.Single(d => d.Name == "Simple on Mine");
        Assert.Equal("v9", Json.ReadParams((await Deployments.GetAsync(simple.Id))!.ParamValuesJson)["ref"]);
        Assert.Equal(2, simple.MachineCount);

        var clean = deployments.Single(d => d.Name == "CleanDesktop on CleanDesktop machines");
        Assert.Equal(1, clean.MachineCount);
    }
}

public class VariableServiceTests : DataTestBase
{
    [Fact]
    public async Task Machine_beats_group_beats_global()
    {
        var group = await Groups.CreateAsync("G", machines: ["FRG.0-A", "FRG.4-A"]);
        var other = await Groups.CreateAsync("Other");

        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "repoRoot", "C:/git", false, false));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "hub", "http://global", false, false));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Group, group.Id, null, "hub", "http://group", false, true));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Group, other.Id, null, "hub", "http://other", false, false));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Machine, null, "FRG.0-A", "repoRoot", "D:/git", false, false));

        var zeroA = (await Variables.ResolveAsync(group.Id, "FRG.0-A")).ToDictionary(v => v.Name);
        Assert.Equal("D:/git", zeroA["repoRoot"].Value);
        Assert.Equal(VariableScope.Machine, zeroA["repoRoot"].Source);
        Assert.Equal(VariableScope.Global, zeroA["repoRoot"].Overrides.Single().Scope);
        Assert.Equal("http://group", zeroA["hub"].Value);

        var fourA = (await Variables.ResolveAsync(group.Id, "frg.4-a")).ToDictionary(v => v.Name);
        Assert.Equal("C:/git", fourA["repoRoot"].Value);

        var settings = VariableService.ToRunSettings(await Variables.ResolveAsync(group.Id, "FRG.4-A"));
        Assert.Equal("http://group", settings.Variables["hub"]);
        Assert.Equal("hub", settings.Environment.Single().Name);
    }

    [Fact]
    public async Task Secrets_are_stored_protected_never_shown_and_masked()
    {
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "TOKEN", "hunter2", true, true, "User"));

        await using (var db = await Factory.CreateDbContextAsync())
        {
            var stored = await db.Variables.SingleAsync();
            Assert.Null(stored.Value);
            Assert.DoesNotContain("hunter2", stored.ProtectedValue);
        }

        var view = (await Variables.ListAsync(VariableScope.Global)).Single();
        Assert.Null(view.Value);
        Assert.True(view.IsSecret);

        var resolved = await Variables.ResolveAsync(null, "M");
        Assert.Equal("hunter2", resolved.Single().Value);

        var mask = VariableService.Masker(resolved);
        Assert.Equal("token=••••••••", mask("token=hunter2"));

        var env = VariableService.ToRunSettings(resolved).Environment.Single();
        Assert.True(env.Secret);
        Assert.Equal("User", env.Scope);
    }

    [Fact]
    public async Task Editing_a_secret_without_a_value_keeps_it()
    {
        var saved = await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "TOKEN", "hunter2", true, false));
        await Variables.SaveAsync(saved.Id, new VariableInput(VariableScope.Global, null, null, "TOKEN", null, true, true, Description: "now an env var"));
        var resolved = (await Variables.ResolveAsync(null, null)).Single();
        Assert.Equal("hunter2", resolved.Value);
        Assert.True(resolved.ApplyAsEnv);
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("has space")]
    [InlineData("")]
    public async Task Bad_names_are_refused(string name) =>
        await Assert.ThrowsAsync<ShushDataException>(() =>
            Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, name, "x", false, false)));

    [Fact]
    public async Task The_same_name_twice_in_one_place_is_refused()
    {
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "a", "1", false, false));
        await Assert.ThrowsAsync<ShushDataException>(() =>
            Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "a", "2", false, false)));
    }
}

public class RunRecordingTests : DataTestBase
{
    private async Task<(Deployment Prod, Deployment Dev, int VersionId)> SetUpAsync()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("G", machines: ["FRG.4-A", "FRG.4-B"]);
        var prod = await Deployments.CreateAsync("prod", recipe.Id, group.Id, new Dictionary<string, string> { ["ref"] = "main" });
        var dev = await Deployments.CreateAsync("dev", recipe.Id, group.Id, new Dictionary<string, string> { ["ref"] = "dev" });
        var version = await Recipes.CurrentVersionAsync(recipe.Id);
        return (prod, dev, version.Id);
    }

    private async Task<Run> StartAsync(Deployment deployment, int versionId, string refValue, params string[] machines) =>
        await Runs.CreateAsync(new NewRun(RunKind.Deploy, deployment.Id, deployment.Name, deployment.RecipeId, versionId, deployment.GroupId, "G",
            new Dictionary<string, string> { ["ref"] = refValue }, "jane", machines, null));

    [Fact]
    public async Task Records_steps_skips_outputs_and_outcomes()
    {
        var (prod, _, versionId) = await SetUpAsync();
        var run = await StartAsync(prod, versionId, "main", "FRG.4-A", "FRG.4-B");
        var recorder = new RunRecorder(Factory, run.Machines.ToDictionary(m => m.MachineName, m => m.Id));

        IDeploymentProgress progress = recorder;
        progress.ReportPlan("FRG.4-A", ["Clone", "Checkout", "Reboot"]);
        progress.ReportStepStart("FRG.4-A", "Clone");
        progress.ReportStep("FRG.4-A", true, "Clone");
        progress.ReportStepStart("FRG.4-A", "Checkout");
        progress.ReportStep("FRG.4-A", true, "Checkout");
        progress.ReportOutputs("FRG.4-A", "Checkout", new Dictionary<string, string> { ["commit"] = "abc123" });
        progress.ReportSkipped("FRG.4-A", "Reboot", "when: false");
        progress.ReportSucceeded("FRG.4-A");

        progress.ReportPlan("FRG.4-B", ["Clone", "Checkout", "Reboot"]);
        progress.ReportStepStart("FRG.4-B", "Clone");
        progress.ReportStep("FRG.4-B", false, "Clone");
        progress.ReportFailure("FRG.4-B", new InvalidOperationException("Could not resolve host: github.com"));

        Assert.Equal(RunStatus.Failed, await Runs.FinishAsync(run.Id, cancelled: false));

        var stored = await Runs.GetAsync(run.Id);
        var a = stored!.Machines.Single(m => m.MachineName == "FRG.4-A");
        Assert.Equal(MachineStatus.Succeeded, a.Status);
        Assert.Equal("abc123", a.Commit);
        Assert.Equal([StepStatus.Succeeded, StepStatus.Succeeded, StepStatus.Skipped], a.Steps.Select(s => s.Status));
        Assert.Equal("when: false", a.Steps[2].SkipReason);

        var b = stored.Machines.Single(m => m.MachineName == "FRG.4-B");
        Assert.Equal(MachineStatus.Failed, b.Status);
        Assert.Contains("github.com", b.Error);
    }

    [Fact]
    public async Task Last_deployed_is_per_deployment_so_prod_and_dev_coexist()
    {
        var (prod, dev, versionId) = await SetUpAsync();

        async Task Succeed(Deployment d, string refValue, string commit)
        {
            var run = await StartAsync(d, versionId, refValue, "FRG.4-A");
            IDeploymentProgress recorder = new RunRecorder(Factory, run.Machines.ToDictionary(m => m.MachineName, m => m.Id));
            recorder.ReportPlan("FRG.4-A", ["Checkout"]);
            recorder.ReportStepStart("FRG.4-A", "Checkout");
            recorder.ReportStep("FRG.4-A", true, "Checkout");
            recorder.ReportOutputs("FRG.4-A", "Checkout", new Dictionary<string, string> { ["commit"] = commit });
            recorder.ReportSucceeded("FRG.4-A");
            await Runs.FinishAsync(run.Id, cancelled: false);
        }

        await Succeed(prod, "v1.0", "p1");
        await Succeed(dev, "feature", "d1");
        await Succeed(prod, "v1.1", "p2");

        var onMachine = await Deployments.LastDeployedOnMachineAsync("frg.4-a");
        Assert.Equal(2, onMachine.Count);
        Assert.Equal("p2", onMachine.Single(l => l.DeploymentName == "prod").Commit);
        Assert.Equal("v1.1", onMachine.Single(l => l.DeploymentName == "prod").Params["ref"]);
        Assert.Equal("feature", onMachine.Single(l => l.DeploymentName == "dev").Params["ref"]);

        var devRigs = await Deployments.LastDeployedAsync(dev.Id);
        Assert.Equal("d1", devRigs.Single().Commit);

        Assert.Equal(2, (await Runs.SuccessfulRunsAsync(prod.Id)).Count);
    }

    [Fact]
    public async Task Restart_marks_running_runs_interrupted()
    {
        var (prod, _, versionId) = await SetUpAsync();
        var run = await StartAsync(prod, versionId, "main", "FRG.4-A");

        Assert.Equal(1, await Runs.MarkInterruptedAsync());
        var stored = await Runs.GetAsync(run.Id);
        Assert.Equal(RunStatus.Interrupted, stored!.Status);
        Assert.Equal(MachineStatus.Interrupted, stored.Machines.Single().Status);
    }

    [Fact]
    public async Task Cancelling_cancels_unfinished_machines()
    {
        var (prod, _, versionId) = await SetUpAsync();
        var run = await StartAsync(prod, versionId, "main", "FRG.4-A", "FRG.4-B");
        Assert.Equal(RunStatus.Cancelled, await Runs.FinishAsync(run.Id, cancelled: true));
        Assert.All((await Runs.GetAsync(run.Id))!.Machines, m => Assert.Equal(MachineStatus.Cancelled, m.Status));
    }

    [Fact]
    public async Task Recent_failures_are_cleared_by_a_later_success()
    {
        var (prod, _, versionId) = await SetUpAsync();

        var first = await StartAsync(prod, versionId, "main", "FRG.4-A");
        IDeploymentProgress r1 = new RunRecorder(Factory, first.Machines.ToDictionary(m => m.MachineName, m => m.Id));
        r1.ReportFailure("FRG.4-A", new Exception("boom"));
        await Runs.FinishAsync(first.Id, false);
        Assert.Single(await Runs.RecentFailuresAsync());

        var second = await StartAsync(prod, versionId, "main", "FRG.4-A");
        IDeploymentProgress r2 = new RunRecorder(Factory, second.Machines.ToDictionary(m => m.MachineName, m => m.Id));
        r2.ReportSucceeded("FRG.4-A");
        await Runs.FinishAsync(second.Id, false);
        Assert.Empty(await Runs.RecentFailuresAsync());
    }
}

public class GroupAndMachineTests : DataTestBase
{
    [Fact]
    public async Task Members_keep_their_order_and_create_machines()
    {
        var group = await Groups.CreateAsync("G", machines: ["FRG.12-A", "FRG.4-A", "frg.4-a"]);
        var stored = await Groups.GetAsync(group.Id);
        Assert.Equal(["FRG.12-A", "FRG.4-A"], stored!.Members.Select(m => m.MachineName));

        var machines = await Machines.ListAsync();
        Assert.Equal(["FRG.4-A", "FRG.12-A"], machines.Select(m => m.Name));
    }

    [Fact]
    public async Task A_group_with_deployments_cannot_be_deleted()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("G");
        await Deployments.CreateAsync("d", recipe.Id, group.Id, new Dictionary<string, string>());
        await Assert.ThrowsAsync<ShushDataException>(() => Groups.DeleteAsync(group.Id));
    }

    [Fact]
    public async Task Facts_and_probes_update_health()
    {
        await Groups.CreateAsync("G", machines: ["M"]);
        await Machines.RecordFactsAsync("M", new MachineFacts(@"CORP\jane", DateTimeOffset.UtcNow.AddDays(-2), "Windows 11", "C:", 12.5, 476));
        await Machines.RecordProbeAsync("M", new HostProbeResult("m.corp", [new AddressProbe("10.0.0.1", false, "timed out")], null));

        var machine = await Machines.GetAsync("M");
        Assert.Equal(@"CORP\jane", machine!.LoggedOnUser);
        Assert.False(machine.Reachable);
        Assert.NotNull(machine.LastSeenAt);
        Assert.Equal(DateTimeKind.Utc, machine.FactsAt!.Value.Kind);
    }

    [Theory]
    [InlineData("FRG.4-A", "FRG.12-A", -1)]
    [InlineData("FRG.12-B", "FRG.12-A", 1)]
    [InlineData("frg.4-a", "FRG.4-A", 0)]
    public void Machine_names_sort_naturally(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(MachineNameComparer.Instance.Compare(a, b)));
}

public class EffectiveParamsTests
{
    [Fact]
    public void Starts_from_defaults_overlaid_by_saved_values_the_recipe_still_declares()
    {
        var recipe = YamlRecipeSerializer.Deserialize("""
            name: T
            params:
              ref:
                default: main
              folder:
                default: prod
            steps: []
            """);

        var values = DeploymentService.EffectiveParams(recipe, new Dictionary<string, string> { ["ref"] = "v2", ["gone"] = "x" });
        Assert.Equal("v2", values["ref"]);
        Assert.Equal("prod", values["folder"]);
        Assert.False(values.ContainsKey("gone"));
    }
}

public class RunListForMachineTests : DataTestBase
{
    [Fact]
    public async Task Lists_the_machine_s_own_status()
    {
        var recipe = await Recipes.CreateAsync(SimpleRecipe, VersionSource.Edit, null);
        var group = await Groups.CreateAsync("G", machines: ["A", "B"]);
        var deployment = await Deployments.CreateAsync("d", recipe.Id, group.Id, new Dictionary<string, string>());
        var version = await Recipes.CurrentVersionAsync(recipe.Id);
        var run = await Runs.CreateAsync(new NewRun(RunKind.Deploy, deployment.Id, "d", recipe.Id, version.Id, group.Id, "G",
            new Dictionary<string, string>(), null, ["A", "B"], null));

        IDeploymentProgress recorder = new RunRecorder(Factory, run.Machines.ToDictionary(m => m.MachineName, m => m.Id));
        recorder.ReportSucceeded("A");
        recorder.ReportFailure("B", new Exception("boom"));
        await Runs.FinishAsync(run.Id, false);

        Assert.Equal(MachineStatus.Succeeded, (await Runs.ListAsync(machine: "A")).Single().MachineStatus);
        Assert.Equal(MachineStatus.Failed, (await Runs.ListAsync(machine: "b")).Single().MachineStatus);
        Assert.Null((await Runs.ListAsync()).Single().MachineStatus);
    }
}

public class MachineHostnameTests : DataTestBase
{
    [Fact]
    public async Task Recording_a_hostname_adds_the_machine_and_keeps_the_latest()
    {
        await Machines.RecordHostnameAsync("FRG.4-A", "w10dt714001");
        var machine = await Machines.GetAsync("FRG.4-A");
        Assert.NotNull(machine);
        Assert.Equal("w10dt714001", machine.Hostname);

        // Names are case-insensitive: no second row, and a changed hostname replaces the old one.
        await Machines.RecordHostnameAsync("frg.4-a", "w10dt714002");
        var all = await Machines.ListAsync();
        Assert.Equal("w10dt714002", Assert.Single(all).Hostname);
        Assert.Equal("FRG.4-A", all[0].Name);

        // A blank answer doesn't erase what we knew.
        await Machines.RecordHostnameAsync("FRG.4-A", null);
        Assert.Equal("w10dt714002", (await Machines.GetAsync("FRG.4-A"))!.Hostname);
    }

    [Fact]
    public async Task Hostnames_report_machines_the_registry_doesnt_know()
    {
        await Groups.CreateAsync("FRG", machines: ["FRG.4-A", "FRG.4-B", "FRG.4-C"]);
        await Machines.RecordHostnameAsync("FRG.4-A", "w10dt714001");
        await Machines.RecordUnresolvedAsync("FRG.4-B", MachineService.NotInRegistry);

        var hosts = await Machines.HostnamesAsync(["frg.4-a", "FRG.4-B", "FRG.4-C", "FRG.9-Z"]);
        Assert.Equal(new RigHost("w10dt714001", false), hosts["FRG.4-A"]);
        Assert.True(hosts["frg.4-b"].Unresolved);
        Assert.Equal(new RigHost(null, false), hosts["FRG.4-C"]);
        Assert.False(hosts.ContainsKey("FRG.9-Z"));

        // Once the registry knows it, it's no longer flagged.
        await Machines.RecordHostnameAsync("FRG.4-B", "w10dt714003");
        Assert.Equal(new RigHost("w10dt714003", false), (await Machines.HostnamesAsync(["FRG.4-B"]))["FRG.4-B"]);
    }
}
