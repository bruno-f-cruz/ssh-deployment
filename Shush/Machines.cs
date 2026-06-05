using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public record MachineInfo(string hostname, string rig_id);

public static class MachineLoader
{
    private const string RegistryUrl = "http://mpe-computers/v2.0";

    public static async Task<Dictionary<string, MachineInfo>> LoadAsync(string yamlPath)
    {
        var machineNames = ReadNames(yamlPath);
        var registry = await FetchRegistryAsync();

        var missing = machineNames.Except(registry.Keys).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The following machine names were not found in the registry: {string.Join(", ", missing)}");

        return machineNames.ToDictionary(name => name, name => registry[name]);
    }

    private static List<string> ReadNames(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var doc = deserializer.Deserialize<MachinesYaml>(yaml);
        return doc?.Machines ?? [];
    }

    private static async Task<Dictionary<string, MachineInfo>> FetchRegistryAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var json = await http.GetStringAsync(RegistryUrl);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var doc = JsonSerializer.Deserialize<JsonDocument>(json, options)!;
        return doc.RootElement
            .GetProperty("comp_ids")
            .Deserialize<Dictionary<string, MachineInfo>>(options)
            ?? [];
    }

    private sealed class MachinesYaml
    {
        public List<string> Machines { get; set; } = [];
    }
}
