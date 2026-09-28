using Shush.Recipe.Steps;

namespace Shush.Tests;

public class GitCloneStepTests
{
    private const string Url = "https://github.com/AllenNeuralDynamics/Aind.Experiment.VrForaging";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_folder_name_falls_back_to_repo_name(string? folderName)
    {
        var step = new GitCloneStep { RepositoryUrl = Url, RootPath = "C:/git", FolderName = folderName };

        Assert.Equal(@"C:/git\Aind.Experiment.VrForaging", step.ClonedPath);
    }

    [Fact]
    public void Explicit_folder_name_is_used()
    {
        var step = new GitCloneStep { RepositoryUrl = Url + ".git", RootPath = "C:/git/", FolderName = "vrf" };

        Assert.Equal(@"C:/git\vrf", step.ClonedPath);
    }
}
