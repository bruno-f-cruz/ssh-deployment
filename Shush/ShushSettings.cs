namespace Shush;

public sealed class ShushSettings
{
    // Shared by the CLI and Shush.Design
    public Secrets Credentials { get; set; } = new();
    public string MachineRegistryUrl { get; set; } = "http://mpe-computers/v2.0";
    public int MachineRegistryCacheSeconds { get; set; } = 60;

    // Shush.Design only
    public string DataDirectoryName { get; set; } = ".shush";
}
