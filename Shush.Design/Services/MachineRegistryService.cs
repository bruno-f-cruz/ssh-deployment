using System.Net;
using Shush;

namespace Shush.Design.Services;

/// <summary>Looks machine names up in the MPE machine registry (IP addresses are used as is).</summary>
public sealed class MachineRegistryService(ShushSettings settings)
{
    /// <summary>The machine's address, or null when the registry doesn't know it.</summary>
    public async Task<MachineInfo?> ResolveAsync(string name)
    {
        // IP literals bypass the registry. rig_id is unknown for these; no step reads it.
        if (IPAddress.TryParse(name, out var ip))
            return new MachineInfo(ip.ToString(), rig_id: string.Empty);

        return await MachineLoader.ResolveOneAsync(name, settings);
    }

    public Task<List<string>> GetAllNamesAsync() => MachineLoader.GetAllNamesAsync(settings);

    public List<string> ParseYaml(string yamlContent) => MachineLoader.ParseNames(yamlContent);

    public string SerializeYaml(IEnumerable<string> names) => MachineLoader.SerializeNames(names);
}
