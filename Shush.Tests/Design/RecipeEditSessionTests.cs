using Shush.Recipe;
using Shush.Recipe.Serialization;

namespace Shush.Tests.Design;

public class RecipeEditSessionTests
{
    private static readonly StepRegistry Registry = new(typeof(IRecipeStep).Assembly);

    private static RecipeEditSession Open(string baseDir, string userDir) =>
        RecipeEditSession.OpenFromBase("VrForaging", baseDir, userDir, Registry, FunctionLibrary.Default);

    [Fact]
    public void Save_writes_user_copy_and_leaves_base_untouched()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nvars:\n  x: base\nsteps: []"));
        var userDir = TestDir.New();
        var session = Open(baseDir, userDir);

        session.Document.Vars["x"] = "edited";
        session.Save();

        Assert.True(File.Exists(Path.Combine(userDir, "VrForaging.yml")));
        Assert.Contains("base", File.ReadAllText(Path.Combine(baseDir, "VrForaging.yml")));
        Assert.Contains("edited", File.ReadAllText(Path.Combine(userDir, "VrForaging.yml")));
    }

    [Fact]
    public void ResetToDefault_removes_user_copy()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nvars:\n  x: base\nsteps: []"));
        var userDir = TestDir.New();
        var session = Open(baseDir, userDir);

        session.Document.Vars["x"] = "edited";
        session.Save();
        session.ResetToDefault();

        Assert.False(File.Exists(Path.Combine(userDir, "VrForaging.yml")));
        Assert.Equal("base", session.Document.Vars["x"]);
    }

    [Fact]
    public void Open_prefers_user_copy_over_base()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nvars:\n  x: base\nsteps: []"));
        var userDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nvars:\n  x: user\nsteps: []"));
        Assert.Equal("user", Open(baseDir, userDir).Document.Vars["x"]);
    }

    [Fact]
    public void ApplyRawYaml_rejects_invalid_document()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nsteps: []"));
        var session = Open(baseDir, TestDir.New());

        var ok = session.TryApplyRawYaml("name: T\nsteps:\n  - type: Nope", out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Nope"));
    }

    [Fact]
    public void ApplyRawYaml_accepts_valid_document()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nsteps: []"));
        var session = Open(baseDir, TestDir.New());

        var ok = session.TryApplyRawYaml("name: VrForaging\nsteps: []", out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
    }

    [Fact]
    public void CreateNew_starts_a_blank_document_and_writes_nothing_until_saved()
    {
        var baseDir = TestDir.New();
        var userDir = TestDir.New();
        var session = RecipeEditSession.CreateNew("Brand New", baseDir, userDir, Registry, FunctionLibrary.Default);

        Assert.Equal("Brand New", session.Document.Name);
        Assert.Empty(session.Document.Steps);
        Assert.False(session.HasUserCopy());
        Assert.False(session.HasBaseCopy());
        Assert.False(File.Exists(Path.Combine(userDir, "Brand New.yml")));

        session.Save();

        Assert.True(File.Exists(Path.Combine(userDir, "Brand New.yml")));
    }

    [Fact]
    public void Exists_finds_a_name_in_either_directory()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nsteps: []"));
        var userDir = TestDir.WithFiles(("Mine.yml", "name: Mine\nsteps: []"));

        Assert.True(RecipeEditSession.Exists("VrForaging", baseDir, userDir));
        Assert.True(RecipeEditSession.Exists("Mine", baseDir, userDir));
        Assert.False(RecipeEditSession.Exists("Nope", baseDir, userDir));
    }

    [Fact]
    public void HasBaseCopy_is_true_only_for_a_recipe_opened_from_base()
    {
        var baseDir = TestDir.WithFiles(("VrForaging.yml", "name: VrForaging\nsteps: []"));
        Assert.True(Open(baseDir, TestDir.New()).HasBaseCopy());
        Assert.False(RecipeEditSession.CreateNew("New", baseDir, TestDir.New(), Registry, FunctionLibrary.Default).HasBaseCopy());
    }
}
