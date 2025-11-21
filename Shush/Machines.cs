using System.Text.Json;


public record MachineInfo(string hostname, string rig_id);

public class MachineManager
{

    private readonly static List<string> Include = new List<string>
    {
        "FRG.4",
        "FRG.5",
        "FRG.12",
        "FRG.13",
        "FRG.0-A"
    };

    public async static Task<Dictionary<string, MachineInfo>> GetComputers(string url = "http://mpe-computers/v2.0")
    {
        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetStringAsync(url);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            var jsonDocument = JsonSerializer.Deserialize<JsonDocument>(response, options);
            var machines = jsonDocument?.RootElement.GetProperty("comp_ids").Deserialize<Dictionary<string, MachineInfo>>(options) ?? new Dictionary<string, MachineInfo>();
            return machines
                .Where(kv => Include.Any(include => kv.Key.StartsWith(include)))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}

