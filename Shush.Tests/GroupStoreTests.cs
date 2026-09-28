using Shush.Groups;

namespace Shush.Tests;

public class GroupStoreTests
{
    private const string BaseGroup = "name: Rigs\nmachines:\n  - A\n  - B\n";

    [Fact]
    public void User_copy_overrides_base_by_name()
    {
        var baseDir = TestDir.WithFiles(("rigs.yml", BaseGroup));
        var userDir = TestDir.WithFiles(("mine.yml", "name: rigs\nmachines:\n  - C\n"));

        var group = new GroupStore(baseDir, userDir).GetAll().Single();

        Assert.Equal(["C"], group.Machines);
    }

    [Fact]
    public void Save_writes_to_user_dir_and_leaves_base_untouched()
    {
        var baseDir = TestDir.WithFiles(("rigs.yml", BaseGroup));
        var userDir = TestDir.New();
        var store = new GroupStore(baseDir, userDir);

        store.Save(new GroupDocument { Name = "Rigs", Machines = ["A"] });

        Assert.Equal(["A"], store.Get("Rigs")!.Machines);
        Assert.Equal(BaseGroup, File.ReadAllText(Path.Combine(baseDir, "rigs.yml")));
        Assert.True(store.HasUserCopy("Rigs"));
    }

    [Fact]
    public void Save_replaces_existing_user_file_even_with_a_different_file_name()
    {
        var userDir = TestDir.WithFiles(("legacy-name.yml", "name: Rigs\nmachines: []\n"));
        var store = new GroupStore(TestDir.New(), userDir);

        store.Save(new GroupDocument { Name = "Rigs", Machines = ["X"] });

        Assert.Single(Directory.GetFiles(userDir));
        Assert.Equal(["X"], store.Get("Rigs")!.Machines);
    }

    [Fact]
    public void Delete_reverts_built_in_group_to_base()
    {
        var baseDir = TestDir.WithFiles(("rigs.yml", BaseGroup));
        var store = new GroupStore(baseDir, TestDir.New());
        store.Save(new GroupDocument { Name = "Rigs", Machines = [] });

        store.Delete("Rigs");

        Assert.Equal(["A", "B"], store.Get("Rigs")!.Machines);
        Assert.False(store.HasUserCopy("Rigs"));
    }

    [Fact]
    public void Delete_removes_user_only_group()
    {
        var store = new GroupStore(TestDir.New(), TestDir.New());
        store.Save(new GroupDocument { Name = "Temp" });

        store.Delete("Temp");

        Assert.Null(store.Get("Temp"));
    }

    [Fact]
    public void Builtin_group_file_loads()
    {
        var store = new GroupStore(Path.Combine(AppContext.BaseDirectory, "Groups"), TestDir.New());

        var group = store.Get("VR-Foraging Behavior Machines");

        Assert.NotNull(group);
        Assert.Equal(15, group.Machines.Count);
        Assert.True(store.IsBuiltIn(group.Name));
    }
}
