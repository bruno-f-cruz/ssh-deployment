using Microsoft.EntityFrameworkCore;
using Shush.Data;
using Shush.Recipe;
using Shush.Tests.Data;

namespace Shush.Tests;

public class SessionEnvironmentTests
{
    [Fact]
    public void Later_commands_get_the_variables_a_run_set_with_quotes_escaped()
    {
        Assert.Equal("", MachineContext.BuildSessionPrefix([]));
        Assert.Equal("$env:UV_TOOL_DIR = 'C:/uv/uv_tool_dir'; $env:NOTE = 'it''s'; ",
            MachineContext.BuildSessionPrefix([("UV_TOOL_DIR", "C:/uv/uv_tool_dir"), ("NOTE", "it's")]));
    }
}

public class SharedFolderVariablesTests : DataTestBase
{
    private async Task<List<Variable>> GlobalsAsync()
    {
        await using var db = Factory.CreateDbContext();
        return await db.Variables.Where(v => v.Scope == VariableScope.Global).OrderBy(v => v.Name).ToListAsync();
    }

    private SeedPaths EmptyPaths() => new(TestDir.New(), TestDir.New(), TestDir.New(), null);

    [Fact]
    public async Task The_uv_folders_are_global_shared_folder_environment_variables_added_once()
    {
        var paths = EmptyPaths();
        await Seeder().RunAsync(paths);

        var globals = await GlobalsAsync();
        Assert.Equal(["UV_TOOL_BIN_DIR", "UV_TOOL_DIR"], globals.Select(v => v.Name));
        Assert.All(globals, v => Assert.True(v.ApplyAsEnv && v.EnvScope == "Machine" && v.IsSharedFolder && !v.IsSecret));
        Assert.Equal("C:/uv/uv_tool_dir", globals.Single(v => v.Name == "UV_TOOL_DIR").Value);
        Assert.Equal("C:/uv/uv_tool_bin_dir", globals.Single(v => v.Name == "UV_TOOL_BIN_DIR").Value);

        // Deleted in Settings: a restart doesn't bring it back.
        await Variables.DeleteAsync(globals[0].Id);
        await Seeder().RunAsync(paths);
        Assert.Single(await GlobalsAsync());
    }

    [Fact]
    public async Task Variables_seeded_before_shared_folders_existed_are_marked_once_if_untouched()
    {
        await using (var db = Factory.CreateDbContext())
        {
            db.Meta.Add(new MetaEntry { Key = "seed:default-variables:uv", Value = "earlier" });
            db.Variables.Add(new Variable { Scope = VariableScope.Global, Name = "UV_TOOL_DIR", Value = "C:/uv/uv_tool_dir", ApplyAsEnv = true });
            db.Variables.Add(new Variable { Scope = VariableScope.Global, Name = "UV_TOOL_BIN_DIR", Value = "D:/elsewhere", ApplyAsEnv = true });
            await db.SaveChangesAsync();
        }

        await Seeder().RunAsync(EmptyPaths());

        var globals = await GlobalsAsync();
        Assert.True(globals.Single(v => v.Name == "UV_TOOL_DIR").IsSharedFolder);
        Assert.False(globals.Single(v => v.Name == "UV_TOOL_BIN_DIR").IsSharedFolder); // edited: left alone
    }

    [Fact]
    public async Task A_rig_gets_its_shared_folders_from_the_variables_it_resolves()
    {
        var group = await Groups.CreateAsync("Rigs", machines: ["RIG-1", "RIG-2"]);
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "TOOLS", "C:/uv/tools", false, true, IsSharedFolder: true));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Machine, null, "RIG-2", "TOOLS", "D:/uv/tools", false, true, IsSharedFolder: true));
        await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "PLAIN", "C:/not-shared", false, false));

        var rig1 = VariableService.ToRunSettings(await Variables.ResolveAsync(group.Id, "RIG-1"));
        var rig2 = VariableService.ToRunSettings(await Variables.ResolveAsync(group.Id, "RIG-2"));

        Assert.Equal([new SharedAccessGrant("C:/uv/tools")], rig1.SharedFolders);
        Assert.Equal([new SharedAccessGrant("D:/uv/tools")], rig2.SharedFolders); // the rig's own path wins
    }

    [Fact]
    public async Task A_shared_folder_is_a_full_path_and_never_secret()
    {
        await Assert.ThrowsAsync<ShushDataException>(() =>
            Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "X", "uv/tools", false, true, IsSharedFolder: true)));
        await Assert.ThrowsAsync<ShushDataException>(() =>
            Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "X", "C:/uv", true, true, IsSharedFolder: true)));
        var ok = await Variables.SaveAsync(null, new VariableInput(VariableScope.Global, null, null, "X", @"C:\uv", false, true, IsSharedFolder: true));
        Assert.True(ok.IsSharedFolder);
    }
}
