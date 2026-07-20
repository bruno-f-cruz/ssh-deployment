using System.Net;
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
        // IP literals bypass the MPE registry entirely. rig_id is unknown for these rows;
        // no recipe step consumes it today, but if one ever does, IP-only machines will
        // need to prompt for it.
        if (IPAddress.TryParse(name, out var ip))
            return new MachineRow { Name = name, Info = new MachineInfo(ip.ToString(), rig_id: string.Empty) };

        var info = await MachineLoader.ResolveOneAsync(name, _settings);
        return info is null
            ? new MachineRow { Name = name, Status = RowStatus.Invalid, Error = "Not found in registry" }
            : new MachineRow { Name = name, Info = info };
    }

    public List<string> ParseYaml(string yamlContent) => MachineLoader.ParseNames(yamlContent);

    public string SerializeYaml(IEnumerable<string> names) => MachineLoader.SerializeNames(names);

    public Task<List<string>> GetAllNamesAsync() => MachineLoader.GetAllNamesAsync(_settings);
}
