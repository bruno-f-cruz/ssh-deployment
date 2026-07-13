using Shush;

namespace Shush.Design.Services;

public sealed class MachineRegistryService
{
    private readonly ShushSettings _settings;

    public MachineRegistryService(ShushSettings settings)
    {
        _settings = settings;
    }

    public async Task<MachineRow> ResolveAsync(string name)
    {
        var info = await MachineLoader.ResolveOneAsync(name, _settings);
        return info is null
            ? new MachineRow { Name = name, Status = RowStatus.Invalid, Error = "Not found in registry" }
            : new MachineRow { Name = name, Info = info };
    }

    public List<string> ParseYaml(string yamlContent) => MachineLoader.ParseNames(yamlContent);

    public Task<List<string>> GetAllNamesAsync() => MachineLoader.GetAllNamesAsync(_settings);
}
