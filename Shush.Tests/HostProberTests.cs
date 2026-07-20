using System.Net;
using System.Net.Sockets;
using Shush;

namespace Shush.Tests;

public class HostProberTests
{
    [Fact]
    public async Task ProbeAsync_ReturnsReachable_WhenPortIsListening()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var result = await HostProber.ProbeAsync("127.0.0.1", port);

            Assert.Null(result.ResolutionError);
            var probe = Assert.Single(result.Addresses);
            Assert.True(probe.Reachable);
            Assert.Null(probe.Error);
            Assert.Equal("127.0.0.1", result.ReachableAddress);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeAsync_ReturnsUnreachable_WhenNothingListens()
    {
        // Bind then release a loopback port so we know nothing listens on it.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await HostProber.ProbeAsync("127.0.0.1", port);

        Assert.Null(result.ResolutionError);
        var probe = Assert.Single(result.Addresses);
        Assert.False(probe.Reachable);
        Assert.NotNull(probe.Error);
        Assert.Null(result.ReachableAddress);
        Assert.Contains("127.0.0.1", result.Describe());
    }

    [Fact]
    public async Task ProbeAsync_ReportsResolutionError_ForUnresolvableHost()
    {
        // .invalid is reserved (RFC 2606) and can never resolve.
        var result = await HostProber.ProbeAsync("no-such-machine.invalid");

        Assert.NotNull(result.ResolutionError);
        Assert.Empty(result.Addresses);
        Assert.Null(result.ReachableAddress);
        Assert.Contains("no-such-machine.invalid", result.Describe());
    }

    [Fact]
    public void ReachableAddress_PicksFirstReachable_InDnsOrder()
    {
        var result = new HostProbeResult("multi-homed", new[]
        {
            new AddressProbe("192.168.24.20", Reachable: false, Error: "TimedOut"),
            new AddressProbe("10.128.133.74", Reachable: true, Error: null),
            new AddressProbe("10.128.133.75", Reachable: true, Error: null),
        }, ResolutionError: null);

        Assert.Equal("10.128.133.74", result.ReachableAddress);
    }

    [Fact]
    public void Describe_ListsEveryAddressWithItsOutcome()
    {
        var result = new HostProbeResult("multi-homed", new[]
        {
            new AddressProbe("192.168.24.20", Reachable: false, Error: "TimedOut"),
            new AddressProbe("10.128.133.74", Reachable: true, Error: null),
        }, ResolutionError: null);

        var text = result.Describe();
        Assert.Contains("192.168.24.20 (TimedOut)", text);
        Assert.Contains("10.128.133.74 (ok)", text);
        Assert.Contains("2 address(es)", text);
    }
}
