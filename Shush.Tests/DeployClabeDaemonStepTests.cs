using System.Net;
using Shush.Recipe.Options;
using Shush.Recipe.Serialization;
using Shush.Recipe.Steps;

namespace Shush.Tests;

public class DeployClabeDaemonStepTests
{
    private const string Url = "'https://raw.githubusercontent.com/AllenNeuralDynamics/clabe/main/scripts/install.ps1'";

    [Fact]
    public void A_release_runs_the_installer_with_it_and_the_hubs()
    {
        var commands = new DeployClabeDaemonStep { Version = "0.19.0", Hubs = "http://a/;http://b/" }.BuildCommands();
        Assert.Equal($"$install = [scriptblock]::Create((Invoke-RestMethod -UseBasicParsing {Url}))", commands[1]);
        Assert.Equal("& $install -Version '0.19.0' -Hubs 'http://a/;http://b/' -Force:$false", commands[2]);
        Assert.DoesNotContain(commands, c => c.Contains('"')); // RunScript escapes double quotes; none needed
    }

    [Fact]
    public void Lock_follows_the_repository_and_latest_passes_no_version()
    {
        var locked = new DeployClabeDaemonStep { Version = "lock", LockRepository = "C:/git/It's here", Force = true };
        Assert.Equal("& $install -Lock 'C:/git/It''s here' -Force", locked.BuildCommands()[2]);
        Assert.Equal("& $install -Force:$false", new DeployClabeDaemonStep { Version = "latest" }.BuildCommands()[2]);
        Assert.Throws<InvalidOperationException>(() => new DeployClabeDaemonStep { Version = "lock" }.BuildCommands());
    }

    [Fact]
    public void A_cli_version_pins_the_clabe_command_and_lock_or_latest_leave_the_installer_default()
    {
        var stop = new DeployClabeDaemonStep { StopOnly = true, CliVersion = "0.18.0rc3", Installer = "v0.18.0rc3" }.BuildCommands();
        Assert.Contains("/clabe/v0.18.0rc3/scripts/install.ps1", stop[1]);
        Assert.Equal("& $install -StopDaemon -CliPackage 'aind-clabe[frontend,otel]==0.18.0rc3' -Force:$false", stop[2]);

        Assert.Equal("& $install -Version '0.18.0rc3' -CliPackage 'aind-clabe[frontend,otel]==0.18.0rc3' -Force:$false",
            new DeployClabeDaemonStep { Version = "0.18.0rc3", CliVersion = "v0.18.0rc3" }.BuildCommands()[2]);
        Assert.DoesNotContain("-CliPackage", new DeployClabeDaemonStep { Version = "latest", CliVersion = "lock" }.BuildCommands()[2]);
    }

    [Fact]
    public void Stop_only_stops_with_the_pinned_installer()
    {
        var commands = new DeployClabeDaemonStep { StopOnly = true, Version = "0.19.0", Hubs = "x", Installer = "v0.19.0" }.BuildCommands();
        Assert.Contains("/clabe/v0.19.0/scripts/install.ps1", commands[1]);
        Assert.Equal("& $install -StopDaemon -Force:$false", commands[2]);
    }

    private const string ReleasesJson = """
        [
          {"tag_name": "v0.19.0rc1", "draft": false, "prerelease": true, "published_at": "2026-12-01T10:00:00Z",
           "assets": [{"name": "aind_clabe-0.19.0rc1-py3-none-any.whl"}]},
          {"tag_name": "v0.18.1", "draft": false, "prerelease": false, "published_at": "2026-11-30T10:00:00Z",
           "assets": [{"name": "notes.txt"}]},
          {"tag_name": "v0.18.0", "draft": false, "prerelease": false, "published_at": "2026-11-28T10:00:00Z",
           "assets": [{"name": "aind_clabe-0.18.0.tar.gz"}, {"name": "aind_clabe-0.18.0-py3-none-any.whl"}]},
          {"tag_name": "v0.20.0", "draft": true, "prerelease": false, "published_at": null,
           "assets": [{"name": "aind_clabe-0.20.0-py3-none-any.whl"}]}
        ]
        """;

    [Fact]
    public void Only_published_releases_with_the_wheel_count()
    {
        var releases = ClabeReleases.Parse(ReleasesJson);
        Assert.Equal(["0.19.0rc1", "0.18.0"], releases.Select(r => r.Version));
        Assert.True(releases[0].Prerelease);
    }

    [Fact]
    public async Task Versions_offer_lock_latest_then_the_releases()
    {
        var options = await new ClabeVersionOptions(Releases(ReleasesJson)).GetOptionsAsync();
        Assert.Equal(["lock", "latest", "0.19.0rc1", "0.18.0"], options.Select(o => o.Value));
        Assert.Equal("v0.19.0rc1 · pre-release · 2026-12-01", options[2].Label);
    }

    [Fact]
    public async Task Installers_are_main_and_the_tags_whose_installer_deploys()
    {
        var releases = Releases(ReleasesJson, scripts: new() { ["v0.19.0rc1"] = "param([switch]$StopDaemon)", ["v0.18.0"] = "param($Version)" });
        var options = await new ClabeInstallerOptions(releases).GetOptionsAsync();
        Assert.Equal(["main", "v0.19.0rc1"], options.Select(o => o.Value));
    }

    [Theory]
    [InlineData("0.18.0", true)]
    [InlineData("v0.18.0", true)]
    [InlineData("lock", true)]
    [InlineData("0.18.1", false)] // a release without builds
    [InlineData("0.99.0", false)]
    public async Task A_version_without_builds_is_refused_before_the_rig_is_touched(string version, bool accepted)
    {
        var check = new DeployClabeDaemonStep { Version = version }.CheckVersionAsync(Releases(ReleasesJson));
        if (accepted)
            await check;
        else
            Assert.Contains("not a release with builds", (await Assert.ThrowsAsync<InvalidOperationException>(() => check)).Message);
    }

    [Fact]
    public async Task Without_GitHub_the_deploy_goes_on_and_the_rig_decides()
    {
        await new DeployClabeDaemonStep { Version = "0.99.0" }.CheckVersionAsync(Releases(null));
        Assert.Equal(["lock", "latest"], (await new ClabeVersionOptions(Releases(null)).GetOptionsAsync()).Select(o => o.Value));
    }

    [Fact]
    public async Task A_release_published_after_the_list_was_cached_is_accepted()
    {
        var github = new FakeGitHub(ReleasesJson, []);
        var releases = new ClabeReleases(new HttpClient(github));
        await releases.GetAsync(); // the editor's suggestions cached the list
        github.Json = ReleasesJson.Replace("v0.19.0rc1", "v0.19.0").Replace("0.19.0rc1-py3", "0.19.0-py3");
        await new DeployClabeDaemonStep { Version = "0.19.0" }.CheckVersionAsync(releases);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, null)] // rate-limited
    [InlineData(HttpStatusCode.OK, """{"message": "API rate limit exceeded"}""")] // an answer of another shape
    [InlineData(HttpStatusCode.OK, """[{"unexpected": true}]""")]
    public async Task A_hand_typed_version_is_never_refused_on_a_list_GitHub_could_not_give(HttpStatusCode status, string? body)
    {
        var github = new FakeGitHub(ReleasesJson, []);
        var releases = new ClabeReleases(new HttpClient(github));
        await releases.GetAsync(); // a list is cached, then GitHub stops answering properly
        (github.Status, github.Json) = (status, body ?? "");
        await new DeployClabeDaemonStep { Version = "0.19.5" }.CheckVersionAsync(releases);
        var options = await new ClabeVersionOptions(new ClabeReleases(new HttpClient(github))).GetOptionsAsync();
        Assert.Equal(["lock", "latest"], options.Select(o => o.Value)); // the suggestions still offer the fixed values
    }

    private const string Sha = "cf17fa8e0219739f3a1c91aed4040f1190c2778a";

    /// <summary>The releases, plus clabe's builds release: two builds of main and a file that is not one.</summary>
    private static readonly string WithBuilds = ReleasesJson.TrimEnd().TrimEnd(']') + $$"""
          ,{"tag_name": "builds", "draft": false, "prerelease": true, "published_at": "2026-11-28T04:00:00Z",
           "assets": [
             {"name": "aind_clabe-0.18.0rc8+g0123456789abcdef0123456789abcdef01234567-py3-none-any.whl", "created_at": "2026-11-28T03:00:00Z"},
             {"name": "aind_clabe-0.18.0rc8+g{{Sha}}-py3-none-any.whl", "created_at": "2026-11-28T04:25:00Z"},
             {"name": "notes.txt", "created_at": "2026-11-28T04:25:00Z"}]}
        ]
        """;

    [Fact]
    public void Builds_are_listed_by_commit_after_the_releases_never_as_a_release_called_builds()
    {
        var releases = ClabeReleases.Parse(WithBuilds);
        Assert.Equal(["0.19.0rc1", "0.18.0", "cf17fa8", "0123456"], releases.Select(r => r.Version));
        Assert.Equal(Sha, releases[2].Commit);
        Assert.Equal("0.18.0rc8", releases[2].Tag);
        Assert.DoesNotContain(releases, r => r.Version == "builds");
    }

    [Fact]
    public async Task Versions_offer_the_recent_builds_after_the_releases_and_installers_never_offer_a_build()
    {
        var options = await new ClabeVersionOptions(Releases(WithBuilds)) { Builds = 1 }.GetOptionsAsync();
        Assert.Equal(["lock", "latest", "0.19.0rc1", "0.18.0", "cf17fa8"], options.Select(o => o.Value));
        Assert.Equal("cf17fa8 — build of main after 0.18.0rc8 · 2026-11-28", options[4].Label);

        var installers = await new ClabeInstallerOptions(Releases(WithBuilds, scripts: new() { ["v0.19.0rc1"] = "StopDaemon" })).GetOptionsAsync();
        Assert.Equal(["main", "v0.19.0rc1"], installers.Select(o => o.Value));
    }

    [Fact]
    public void A_commit_deploys_its_build_and_the_installer_installs_it_as_the_command_too()
    {
        var start = new DeployClabeDaemonStep { Version = "CF17FA8", CliVersion = "cf17fa8", Installer = "cf17fa8", Hubs = "http://h/" }.BuildCommands();
        Assert.Contains("/clabe/cf17fa8/scripts/install.ps1", start[1]);
        Assert.Equal("& $install -Commit 'cf17fa8' -Hubs 'http://h/' -Force:$false", start[2]);

        var stop = new DeployClabeDaemonStep { StopOnly = true, CliVersion = "cf17fa8", Installer = "cf17fa8" }.BuildCommands();
        Assert.Equal("& $install -StopDaemon -Commit 'cf17fa8' -Force:$false", stop[2]);

        Assert.Throws<InvalidOperationException>(() => new DeployClabeDaemonStep { Version = "0.18.0", CliVersion = "cf17fa8" }.BuildCommands());
    }

    [Theory]
    [InlineData("cf17fa8", true)]
    [InlineData(Sha, true)]
    [InlineData("0123456", true)]
    [InlineData("fedcba9", false)] // no build of that commit
    public async Task A_commit_without_a_build_is_refused_before_the_rig_is_touched(string commit, bool accepted)
    {
        var check = new DeployClabeDaemonStep { Version = commit }.CheckVersionAsync(Releases(WithBuilds));
        if (accepted)
            await check;
        else
            Assert.Contains("no build of commit", (await Assert.ThrowsAsync<InvalidOperationException>(() => check)).Message);
    }

    [Fact]
    public void A_param_s_option_source_survives_a_save_and_reaches_the_form()
    {
        const string yaml = """
            name: R
            params:
              clabeVersion:
                label: clabe version
                help: The release the daemon runs.
                default: lock
                optionsFrom: clabe.versions
            steps: []
            """;
        var document = YamlRecipeSerializer.Deserialize(YamlRecipeSerializer.Serialize(YamlRecipeSerializer.Deserialize(yaml)));
        var field = RecipeFormBinder.GetFields(document).Single();
        Assert.Equal("clabe.versions", field.OptionsFrom);
        Assert.Equal("The release the daemon runs.", field.Help);
    }

    [Fact]
    public void The_step_s_inputs_name_their_option_sources()
    {
        var descriptor = StepDescriptor.FromType(typeof(DeployClabeDaemonStep));
        Assert.Equal("clabe.versions", descriptor.Inputs.Single(i => i.Name == "version").OptionsFrom);
        Assert.Equal("clabe.installers", descriptor.Inputs.Single(i => i.Name == "installer").OptionsFrom);
        Assert.All(OptionSourceRegistry.Default.Names, n => Assert.True(OptionSourceRegistry.Default.TryGet(n, out _)));
    }

    /// <summary>Releases served by a fake GitHub: <paramref name="json"/> (null: unreachable) and installers by tag.</summary>
    private static ClabeReleases Releases(string? json, Dictionary<string, string>? scripts = null) =>
        new(new HttpClient(new FakeGitHub(json, scripts ?? [])));

    /// <summary>GitHub's releases answer <see cref="Json"/> with <see cref="Status"/> (Json null: unreachable).</summary>
    private sealed class FakeGitHub(string? json, Dictionary<string, string> scripts) : HttpMessageHandler
    {
        public string? Json { get; set; } = json;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/releases") && Json is not null)
                return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Json) });
            var script = scripts.FirstOrDefault(s => url.Contains($"/{s.Key}/"));
            if (script.Key is not null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(script.Value) });
            if (Json is null)
                throw new HttpRequestException("unreachable");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
