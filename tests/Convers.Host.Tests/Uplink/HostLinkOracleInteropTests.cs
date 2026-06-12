using Convers.Core;
using Convers.Host.Uplink;
using Convers.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// Live-capture interop tests: the W4 HostLink/engine peering with the real conversd-saupp oracle over
/// TCP (the diff-oracle discipline, design decision 10). They pin the handshake and PING/PONG behaviour
/// to the actual wire — the transcript captured while building this wave was
/// <c>/..HOST PDNCONV pdnconv1 Aadmpun → /..HOST ORACLE saupp1.62a Aadmpunfi</c> and
/// <c>/..PING → /..PONG 3</c>. Tagged <c>Interop</c> so they only run when the oracle is up
/// (<c>docker compose -f docker/compose.oracle.yml up -d --build --wait</c>); the unit lane excludes them
/// via <c>--filter Category!=Interop</c>.
/// </summary>
[Trait("Category", "Interop")]
public class HostLinkOracleInteropTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static HostLinkOptions Options() => new()
    {
        HostName = "PDNCONV",
        Software = "pdnconv1",
        // Offer the leaf set; the oracle answers Aadmpunfi and we negotiate the intersection.
        Facilities = Facilities.AwayNew | Facilities.AwayOld | Facilities.ChannelModes |
                     Facilities.PingPong | Facilities.Udat | Facilities.Nicknames,
        PingInterval = TimeSpan.FromSeconds(2),
        SilenceTimeout = TimeSpan.FromSeconds(30),
        HandshakeTimeout = TimeSpan.FromSeconds(10),
    };

    [Fact]
    public async Task RawEngine_HandshakeAndPingPong_MatchTheOracleTranscript()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using TcpUpstreamLink link =
            await TcpUpstreamLink.ConnectAsync(OracleEndpoint.Host, OracleEndpoint.Port, cts.Token);

        var engine = new HostLinkEngine(Options(), TimeProvider.System);

        // Send our /..HOST.
        EngineStep connect = engine.OnConnected();
        foreach (HostCommand c in connect.OutboundCommands)
        {
            await link.SendLineAsync(HostCommandCodec.Format(c), cts.Token);
        }

        // Read until the engine reports the handshake completed.
        bool established = false;
        while (!established)
        {
            string? line = await link.ReceiveLineAsync(cts.Token);
            Assert.NotNull(line); // the oracle must not hang up during the handshake
            EngineStep step = engine.OnLineReceived(line!);
            established |= step.HandshakeCompleted;
        }

        Assert.Equal(HostLinkState.Established, engine.State);
        Assert.Equal("ORACLE", engine.PeerHostName);
        // The negotiated set is our offer ∩ the oracle's Aadmpunfi = A a m p u n.
        Assert.Equal("Aampun", FacilitiesCodec.Format(engine.NegotiatedFacilities));
        Assert.True(engine.NegotiatedFacilities.HasFlag(Facilities.PingPong));

        // Send a /..PING; the oracle answers /..PONG <txtime>.
        await link.SendLineAsync(HostCommandCodec.Format(new HostPing()), cts.Token);
        HostPong? pong = null;
        while (pong is null)
        {
            string? line = await link.ReceiveLineAsync(cts.Token);
            Assert.NotNull(line);
            if (HostCommandCodec.TryParse(line, out HostCommand? cmd) && cmd is HostPong p)
            {
                pong = p;
                engine.OnLineReceived(line!); // let the engine record the round-trip
            }
        }

        Assert.True(pong.Time >= -1, "PONG carries a valid measured time (or a -1/0 sentinel).");
        Assert.NotNull(engine.LastRoundTripMs);
    }

    [Fact]
    public async Task FullHostLink_ComesUp_AgainstTheLiveOracle()
    {
        var factory = new TcpUpstreamLinkFactory(OracleEndpoint.Host, OracleEndpoint.Port);
        var hub = new ConversHub("PDNCONV", TimeProvider.System);
        await using var link = new HostLink(
            Options(), factory, hub, TimeProvider.System, NullLogger<HostLink>.Instance);

        using var cts = new CancellationTokenSource(Timeout);
        Task run = link.RunAsync(cts.Token);

        // The full driver dials, handshakes, and reaches the established state against the real conversd.
        await link.WaitForUpAsync(cts.Token);
        Assert.True(link.IsUp);

        // A local user join is announced upstream; the oracle accepts it without dropping us (the link
        // stays up through the keepalive interval).
        await link.SubmitLocalEventAsync(new ConversEvent.LocalJoin("s1", "M0LTE", 3333), cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token); // span at least one ping interval
        Assert.True(link.IsUp);

        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }
}
