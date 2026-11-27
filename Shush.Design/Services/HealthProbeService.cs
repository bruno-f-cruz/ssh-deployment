using Shush;
using Shush.Data;

namespace Shush.Design.Services;

/// <summary>
/// Rig health without credentials: does each machine answer on port 22? Runs in the background every
/// <see cref="ShushSettings.HealthProbeMinutes"/>, and on demand from a machine's page. What needs SSH (who's logged
/// on, free disk, last boot) comes from the last pre-flight instead.
/// </summary>
public sealed class HealthChecker(MachineService machines, MachineRegistryService registry, ILogger<HealthChecker> logger)
{
    public event Action? Probed;

    public async Task ProbeAsync(string machine, CancellationToken ct = default)
    {
        MachineInfo? info;
        try
        {
            info = await registry.ResolveAsync(machine);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The registry itself is down: that says nothing about the machine, so record nothing.
            logger.LogWarning("Health probe: the machine registry didn't answer ({Message}).", ex.Message);
            return;
        }

        if (info is null)
            await machines.RecordUnresolvedAsync(machine, MachineService.NotInRegistry);
        else
        {
            await machines.RecordHostnameAsync(machine, info.hostname);
            await machines.RecordProbeAsync(machine, await HostProber.ProbeAsync(info.hostname, ct: ct));
        }

        Probed?.Invoke();
    }

    /// <summary>
    /// Looks the machines up in the registry and records their hostnames (no port-22 probe). Quick, so a newly added
    /// machine shows its hostname right away. Registry failures are logged and skipped.
    /// </summary>
    public async Task ResolveAsync(IEnumerable<string> names, CancellationToken ct = default)
    {
        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (name, _) =>
        {
            try
            {
                var info = await registry.ResolveAsync(name);
                if (info is null)
                    await machines.RecordUnresolvedAsync(name, MachineService.NotInRegistry);
                else
                    await machines.RecordHostnameAsync(name, info.hostname);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Couldn't look {Machine} up in the machine registry ({Message}).", name, ex.Message);
            }
        });
        Probed?.Invoke();
    }

    public async Task ProbeAllAsync(CancellationToken ct)
    {
        var all = await machines.ListAsync();
        await Parallel.ForEachAsync(all, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (machine, token) => await ProbeAsync(machine.Name, token));
    }
}

public sealed class HealthProbeService(HealthChecker checker, ShushSettings settings, ILogger<HealthProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.HealthProbeMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await checker.ProbeAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Health probe failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
