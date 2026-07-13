namespace Shush.Design.Services;

public sealed class MachineRegistryService
{
    public async Task<MachineRow> ResolveAsync(string name)
    {
        var info = await MachineLoader.ResolveOneAsync(name);
        return info is null
            ? new MachineRow { Name = name, Status = RowStatus.Invalid, Error = "Not found in registry" }
            : new MachineRow { Name = name, Info = info };
    }

    public List<string> ParseYaml(string yamlContent) => MachineLoader.ParseNames(yamlContent);
}
