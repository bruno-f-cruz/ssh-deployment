using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public record MachineInfo(string hostname, string rig_id);

public static class MachineLoader
{
    private const string RegistryUrl = "http://mpe-computers/v2.0";
    private static readonly TimeSpan RegistryCacheDuration = TimeSpan.FromSeconds(60);

    private static Dictionary<string, MachineInfo>? _cachedRegistry;
    private static DateTime _cachedAt;

    public static async Task<Dictionary<string, MachineInfo>> LoadAsync(string yamlPath)
    {
        var machineNames = ParseNames(File.ReadAllText(yamlPath));
        var registry = await GetRegistryAsync();

        var missing = machineNames.Except(registry.Keys).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The following machine names were not found in the registry: {string.Join(", ", missing)}");

        return machineNames.ToDictionary(name => name, name => registry[name]);
    }

    public static List<string> ParseNames(string yamlContent)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var doc = deserializer.Deserialize<MachinesYaml>(yamlContent);
        return doc?.Machines ?? [];
    }

    public static async Task<MachineInfo?> ResolveOneAsync(string name)
    {
        var registry = await GetRegistryAsync();
        return registry.GetValueOrDefault(name);
    }

    public static async Task<List<string>> GetAllNamesAsync()
    {
        var registry = await GetRegistryAsync();
        return registry.Keys.OrderBy(name => name).ToList();
    }

    private static async Task<Dictionary<string, MachineInfo>> GetRegistryAsync()
    {
        if (_cachedRegistry is not null && DateTime.UtcNow - _cachedAt < RegistryCacheDuration)
            return _cachedRegistry;

        _cachedRegistry = await FetchRegistryAsync();
        _cachedAt = DateTime.UtcNow;
        return _cachedRegistry;
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
