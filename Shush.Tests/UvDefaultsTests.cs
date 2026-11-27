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

    [Fact]
    public void Every_rig_grants_the_uv_root_by_default() =>
        Assert.Equal([ShushSettings.DefaultUvRoot], new ShushSettings().SharedAccess.Paths);
}

public class DefaultVariablesTests : DataTestBase
{
    private async Task<List<Variable>> GlobalsAsync()
    {
        await using var db = Factory.CreateDbContext();
        return await db.Variables.Where(v => v.Scope == VariableScope.Global).OrderBy(v => v.Name).ToListAsync();
    }

    [Fact]
    public async Task The_uv_folders_are_global_environment_variables_added_once()
    {
        var paths = new SeedPaths(TestDir.New(), TestDir.New(), TestDir.New(), null);
        await Seeder().RunAsync(paths);

        var globals = await GlobalsAsync();
        Assert.Equal(["UV_TOOL_BIN_DIR", "UV_TOOL_DIR"], globals.Select(v => v.Name));
        Assert.All(globals, v => Assert.True(v.ApplyAsEnv && v.EnvScope == "Machine" && !v.IsSecret));
        Assert.Equal("C:/uv/uv_tool_dir", globals.Single(v => v.Name == "UV_TOOL_DIR").Value);
        Assert.Equal("C:/uv/uv_tool_bin_dir", globals.Single(v => v.Name == "UV_TOOL_BIN_DIR").Value);

        // Deleted in Settings: a restart doesn't bring it back.
        await Variables.DeleteAsync(globals[0].Id);
        await Seeder().RunAsync(paths);
        Assert.Single(await GlobalsAsync());
    }
}
