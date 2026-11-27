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
            await machines.RecordUnresolvedAsync(machine, "Not found in the machine registry.");
        else
            await machines.RecordProbeAsync(machine, await HostProber.ProbeAsync(info.hostname, ct: ct));

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
