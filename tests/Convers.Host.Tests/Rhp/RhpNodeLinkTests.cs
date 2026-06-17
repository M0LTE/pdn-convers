using Convers.Host.Rhp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Convers.Host.Tests.Rhp;

/// <summary>
/// The RHPv2 node link: binds the convers callsign over a wire-faithful <see cref="FakeRhpServer"/>,
/// probe-walks the SSID on a bind clash (design decision 4), and surfaces accepted inbound children.
/// Time is a <see cref="FakeTimeProvider"/> so backoff is deterministic.
/// </summary>
public class RhpNodeLinkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static RhpNodeLink NewLink(FakeRhpServer server, string preferred, bool exactBind = false) => new(
        new RhpLinkOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            PreferredCallsign = preferred,
            ExactBind = exactBind,
        },
        new FakeTimeProvider(T0),
        NullLogger<RhpNodeLink>.Instance);

    [Fact]
    public async Task Bind_BindsPreferredCallsign_WhenFree()
    {
        await using var server = new FakeRhpServer();
        server.Start();
        await using RhpNodeLink link = NewLink(server, "M0LTE-4");
        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        BindRecord bind = await server.WaitForBindAsync();
        Assert.Equal("M0LTE-4", bind.Local);

        await link.WaitForUpAsync(cts.Token);
        Assert.Equal("M0LTE-4", link.BoundCallsign);

        await cts.CancelAsync();
        await Swallow(run);
    }

    [Fact]
    public async Task Bind_ProbeWalksSsid_WhenPreferredIsInUse()
    {
        await using var server = new FakeRhpServer();
        server.Start();

        // Refuse M0LTE-4 as a duplicate; everything else binds. The link must walk to M0LTE-5.
        server.BindResult = local =>
            string.Equals(local, "M0LTE-4", StringComparison.OrdinalIgnoreCase) ? 9 /* DuplicateSocket */ : 0;

        await using RhpNodeLink link = NewLink(server, "M0LTE-4");
        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        // The first successful (recorded) bind is the walked SSID — the refused one is not recorded.
        BindRecord bound = await server.WaitForBindAsync();
        Assert.Equal("M0LTE-5", bound.Local);

        await link.WaitForUpAsync(cts.Token);
        Assert.Equal("M0LTE-5", link.BoundCallsign);

        await cts.CancelAsync();
        await Swallow(run);
    }

    [Fact]
    public async Task Bind_ExactBind_BindsNodeOwnedCallsignVerbatim()
    {
        // The node-owned-callsign contract (PDN_APP_CALLSIGN): bind exactly, no walk.
        await using var server = new FakeRhpServer();
        server.Start();
        await using RhpNodeLink link = NewLink(server, "GB7RDG-4", exactBind: true);
        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        BindRecord bind = await server.WaitForBindAsync();
        Assert.Equal("GB7RDG-4", bind.Local);

        await link.WaitForUpAsync(cts.Token);
        Assert.Equal("GB7RDG-4", link.BoundCallsign);

        await cts.CancelAsync();
        await Swallow(run);
    }

    [Fact]
    public async Task Bind_ExactBind_DoesNotProbeWalk_WhenNodeOwnedCallsignIsRefused()
    {
        await using var server = new FakeRhpServer();
        server.Start();

        // Refuse the node-owned callsign as a duplicate. With ExactBind the link must NOT walk to another
        // SSID — it never binds (never goes up), and no walked SSID is ever recorded as bound.
        int attempts = 0;
        server.BindResult = local =>
        {
            Interlocked.Increment(ref attempts);
            Assert.Equal("GB7RDG-4", local);   // the only callsign ever attempted — no -5, no walk
            return 9; // DuplicateSocket
        };

        await using RhpNodeLink link = NewLink(server, "GB7RDG-4", exactBind: true);
        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        // It must not come up, and no walked callsign is ever recorded as bound. Give the loop time to
        // make at least one (failed) attempt; the FakeTimeProvider stalls reconnect backoff after that.
        await WaitUntilAsync(() => Volatile.Read(ref attempts) >= 1);
        Assert.False(link.IsUp);
        Assert.False(server.Binds.Reader.TryRead(out _)); // no successful bind recorded at all
        Assert.Equal("GB7RDG-4", link.BoundCallsign);     // never updated to a walked SSID

        await cts.CancelAsync();
        await Swallow(run);
    }

    [Fact]
    public async Task Accept_SurfacesInboundChild_WithRemoteCallsign()
    {
        await using var server = new FakeRhpServer();
        server.Start();
        await using RhpNodeLink link = NewLink(server, "M0LTE-4");
        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        await link.WaitForUpAsync(cts.Token);
        _ = await server.AcceptChildAsync("G4ABC");

        RhpChildConnection child = await link.Accepted.ReadAsync(cts.Token);
        Assert.Equal("G4ABC", child.RemoteCallsign);

        await cts.CancelAsync();
        await Swallow(run);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static async Task Swallow(Task run)
    {
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
        }
    }
}
