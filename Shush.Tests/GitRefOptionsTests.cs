using System.Text.Json;
using Shush.Recipe.Options;

namespace Shush.Tests;

public class GitRefSuggestionTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Releases_come_first_then_the_default_branch_then_other_branches_then_tags_without_a_release()
    {
        var releases = Json("""
            [
              {"tag_name": "20261122.1", "name": "20261122.1 next", "draft": false, "prerelease": true, "published_at": "2026-11-22T10:00:00Z"},
              {"tag_name": "20261121.2", "name": "20261121.2 test-async-upload", "draft": false, "prerelease": false, "published_at": "2026-11-21T20:32:45Z"},
              {"tag_name": "20261121.1", "name": "20261121.1", "draft": false, "prerelease": false, "published_at": "2026-11-21T06:32:10Z"},
              {"tag_name": "wip", "name": "wip", "draft": true, "prerelease": false, "published_at": null}
            ]
            """);
        var branches = Json("""[{"name": "dev"}, {"name": "main"}]""");
        var tags = Json("""[{"name": "20261121.2"}, {"name": "v1.3.4"}]""");

        var options = GitRefOptions.Build(releases, branches, tags, defaultBranch: "main");

        Assert.Equal(["20261122.1", "20261121.2", "20261121.1", "main", "dev", "v1.3.4"], options.Select(o => o.Value));
        Assert.Equal("20261122.1 · next · pre-release · 2026-11-22", options[0].Label);
        Assert.Equal("20261121.2 · test-async-upload · latest release · 2026-11-21", options[1].Label);
        Assert.Equal("20261121.1 · 2026-11-21", options[2].Label);
        Assert.Equal("main · default branch", options[3].Label);
        Assert.Equal("v1.3.4 · tag", options[5].Label);
    }
}
