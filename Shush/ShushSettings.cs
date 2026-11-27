namespace Shush;

public sealed class ShushSettings
{
    public string MachineRegistryUrl { get; set; } = "http://mpe-computers/v2.0";
    public int MachineRegistryCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Machine-wide access granted on these roots before every recipe runs (env: SharedAccess__Paths__0=C:/git).
    /// Recipes can add more roots with their own <c>sharedAccess</c> block. No paths means nothing is granted.
    /// </summary>
    public Shush.Recipe.Serialization.SharedAccessSpec SharedAccess { get; set; } = new();

    /// <summary>Folder (next to the app) holding the database, logs and the data-protection keys.</summary>
    public string DataDirectoryName { get; set; } = ".shush";

    /// <summary>Path the app is served under behind a reverse proxy (e.g. "/shush"). Empty means root.</summary>
    public string BasePath { get; set; } = "";

    /// <summary>How often the background health probe checks that every known machine answers on port 22.</summary>
    public int HealthProbeMinutes { get; set; } = 5;

    /// <summary>At most this many machines deploy at the same time, per run.</summary>
    public int MaxConcurrentMachines { get; set; } = 16;
}
