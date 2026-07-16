using Shush.Recipe.Serialization;

namespace Shush.Tests.Design;

public class DeployStateStoreTests
{
    [Fact]
    public void DeployState_roundtrips_as_yaml()
    {
        var store = new DeployStateStore(TestDir.New());
        store.Save("VrForaging", new DeployState
        {
            Machines = ["frg-01", "frg-02"],
            ParamOverrides = new() { ["tag"] = "v1.3.0" },
        });

        var loaded = store.Load("VrForaging")!;
        Assert.Equal(new[] { "frg-01", "frg-02" }, loaded.Machines);
        Assert.Equal("v1.3.0", loaded.ParamOverrides["tag"]);
    }

    [Fact]
    public void Load_returns_null_when_absent() =>
        Assert.Null(new DeployStateStore(TestDir.New()).Load("Nothing"));
}
