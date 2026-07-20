using System.Net;
using System.Net.Sockets;

namespace Shush;

public sealed record AddressProbe(string Address, bool Reachable, string? Error);

public sealed record HostProbeResult(string Host, IReadOnlyList<AddressProbe> Addresses, string? ResolutionError)
{
    /// <summary>First reachable address in DNS order, or null if none accepted a connection.</summary>
    public string? ReachableAddress => Addresses.FirstOrDefault(a => a.Reachable)?.Address;

    public string Describe()
    {
        if (ResolutionError is not null)
            return $"'{Host}' could not be resolved: {ResolutionError}";

        var details = string.Join("; ", Addresses.Select(a => a.Reachable ? $"{a.Address} (ok)" : $"{a.Address} ({a.Error})"));
        return $"'{Host}' resolved to {Addresses.Count} address(es): {details}";
    }
}

/// <summary>
/// Resolves a hostname (or IP literal) and TCP-probes every resolved address, so callers can
/// pick a reachable one. Hosts with multiple NICs may register several DNS records and SSH.NET
/// only ever tries the first, unlike OpenSSH which falls back through all of them.
/// </summary>
public static class HostProber
{
    public const int SshPort = 22;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task<HostProbeResult> ProbeAsync(
        string host, int port = SshPort, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new HostProbeResult(host, [], ex.Message);
        }

        if (addresses.Length == 0)
            return new HostProbeResult(host, [], "name did not resolve to any address");

        var probes = await Task.WhenAll(
            addresses.Select(a => ProbeAddressAsync(a, port, timeout ?? DefaultTimeout, ct)));
        return new HostProbeResult(host, probes, null);
    }

    private static async Task<AddressProbe> ProbeAddressAsync(
        IPAddress address, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(address, port, timeoutCts.Token);
            return new AddressProbe(address.ToString(), true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new AddressProbe(address.ToString(), false, $"timed out after {timeout.TotalSeconds:0.#}s");
        }
        catch (SocketException ex)
        {
            return new AddressProbe(address.ToString(), false, ex.SocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            return new AddressProbe(address.ToString(), false, ex.Message);
        }
    }
}
