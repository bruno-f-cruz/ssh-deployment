using System.Text.Json;
using System.Text.Json.Serialization;

public class Secrets
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    public static Secrets Load(string path = "secrets.json")
    {
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        return JsonSerializer.Deserialize<Secrets>(json, options) ?? new Secrets();
    }
}