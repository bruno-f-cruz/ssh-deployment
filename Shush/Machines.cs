using System.Text.Json;
using Shush;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public record MachineInfo(string hostname, string rig_id);

public static class MachineLoader
{
    private static Dictionary<string, MachineInfo>? _cachedRegistry;
    private static DateTime _cachedAt;

    public static async Task<Dictionary<string, MachineInfo>> LoadAsync(string yamlPath, ShushSettings settings)
    {
        var machineNames = ParseNames(File.ReadAllText(yamlPath));
        var registry = await GetRegistryAsync(settings);

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

    public static async Task<MachineInfo?> ResolveOneAsync(string name, ShushSettings settings)
    {
        var registry = await GetRegistryAsync(settings);
        return registry.GetValueOrDefault(name);
    }

    public static async Task<List<string>> GetAllNamesAsync(ShushSettings settings)
    {
        var registry = await GetRegistryAsync(settings);
        return registry.Keys.OrderBy(name => name).ToList();
    }

    private static async Task<Dictionary<string, MachineInfo>> GetRegistryAsync(ShushSettings settings)
    {
        var cacheDuration = TimeSpan.FromSeconds(settings.MachineRegistryCacheSeconds);
        if (_cachedRegistry is not null && DateTime.UtcNow - _cachedAt < cacheDuration)
            return _cachedRegistry;

        _cachedRegistry = await FetchRegistryAsync(settings.MachineRegistryUrl);
        _cachedAt = DateTime.UtcNow;
        return _cachedRegistry;
    }

    private static async Task<Dictionary<string, MachineInfo>> FetchRegistryAsync(string registryUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var json = await http.GetStringAsync(registryUrl);
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
