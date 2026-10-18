using Shush.Recipe;

namespace Shush.Tests;

public class RemotePathTests
{
    [Theory]
    [InlineData(@"C:\git\vrf\settings.json")]
    [InlineData("C:/Program Files/Bonsai/Bonsai.config")]
    [InlineData(@"C:\data\Guns N' Roses\it's fine & 100%.txt")]
    public void Windows_paths_are_accepted(string path) => MachineContext.EnsureSafeRemotePath(path);

    [Theory]
    [InlineData("C:/x\" & calc & \"")]
    [InlineData("\"")]
    public void Double_quotes_are_rejected(string path) =>
        Assert.Throws<ArgumentException>(() => MachineContext.EnsureSafeRemotePath(path));
}
