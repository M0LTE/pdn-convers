using Convers.Core;
using Convers.Host.Uplink;
using Convers.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// Unit tests for the sans-IO <see cref="HostLinkEngine"/> over scripted input and a
/// <see cref="FakeTimeProvider"/> — the handshake FSM, facility negotiation, PING/PONG keepalive timing,
/// and the drop decisions (silence, loop, handshake stall). No transport, no hub, fully deterministic.
/// The facility/handshake vectors mirror the transcript captured from the docker oracle.
/// </summary>
public class HostLinkEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private static HostLinkOptions Options() => new()
    {
        HostName = "PDNCONV",
        Software = "pdnconv1",
        Facilities = Facilities.AwayNew | Facilities.AwayOld | Facilities.ChannelModes |
                     Facilities.PingPong | Facilities.Udat | Facilities.Nicknames,
        PingInterval = TimeSpan.FromSeconds(60),
        SilenceTimeout = TimeSpan.FromSeconds(180),
        HandshakeTimeout = TimeSpan.FromSeconds(30),
    };

    private static (HostLinkEngine Engine, FakeTimeProvider Time) NewEngine()
    {
        var time = new FakeTimeProvider(T0);
        return (new HostLinkEngine(Options(), time), time);
    }

    [Fact]
    public void OnConnected_EmitsOurHandshake_AndEntersHandshaking()
    {
        (HostLinkEngine engine, _) = NewEngine();

        EngineStep step = engine.OnConnected();

        Assert.Equal(HostLinkState.Handshaking, engine.State);
        HostCommand only = Assert.Single(step.OutboundCommands);
        var hs = Assert.IsType<HostHandshake>(only);
        Assert.Equal("PDNCONV", hs.Hostname);
        Assert.Equal("pdnconv1", hs.Software);
        // Our advertised facilities, in canonical order: A a m p u n (no d/f/i for a leaf).
        Assert.Equal("Aampun", FacilitiesCodec.Format(hs.Facilities));
    }

    [Fact]
    public void OnHandshakeReply_NegotiatesIntersection_AndEstablishes()
    {
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();

        // The exact reply the oracle sends (docker/compose.oracle.yml): Aadmpunfi.
        EngineStep step = engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        Assert.True(step.HandshakeCompleted);
        Assert.Equal(HostLinkState.Established, engine.State);
        Assert.Equal("ORACLE", engine.PeerHostName);

        // Negotiated = ours (Aampun) ∩ theirs (Aadmpunfi) = A a m p u n. We never offered d/f/i so they drop.
        Facilities negotiated = engine.NegotiatedFacilities;
        Assert.Equal("Aampun", FacilitiesCodec.Format(negotiated));
        Assert.True(negotiated.HasFlag(Facilities.PingPong));
        Assert.False(negotiated.HasFlag(Facilities.DestinationForwarding));
        Assert.False(negotiated.HasFlag(Facilities.Filter));
    }

    [Fact]
    public void Established_InboundCmsg_BecomesHubEvent()
    {
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        EngineStep step = engine.OnLineReceived(Wire.Host("CMSG g4abc 3333 hello from the net"));

        ConversEvent evt = Assert.Single(step.HubEvents);
        var cmsg = Assert.IsType<ConversEvent.HostChannelMessage>(evt);
        Assert.Equal("g4abc", cmsg.User);
        Assert.Equal(3333, cmsg.Channel);
        Assert.Equal("hello from the net", cmsg.Text);
        Assert.Empty(step.OutboundCommands);
    }

    [Fact]
    public void Established_InboundPing_DoesNotAnswerInEngine_ButYieldsHubPing()
    {
        // The engine surfaces an inbound PING as a hub event; the hub decides the PONG answer (decision 5).
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        EngineStep step = engine.OnLineReceived(Wire.Host("PING"));

        Assert.IsType<ConversEvent.HostPing>(Assert.Single(step.HubEvents));
        Assert.Empty(step.OutboundCommands);
    }

    [Fact]
    public void Established_InboundPong_RecordsRoundTrip_AndSurfacesEvent()
    {
        (HostLinkEngine engine, FakeTimeProvider time) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        // Trigger a keepalive ping, then advance a little and answer it.
        time.Advance(TimeSpan.FromSeconds(60));
        EngineStep ping = engine.OnTick();
        Assert.IsType<HostPing>(Assert.Single(ping.OutboundCommands));

        time.Advance(TimeSpan.FromMilliseconds(250));
        EngineStep pong = engine.OnLineReceived(Wire.Host("PONG 3"));

        Assert.IsType<ConversEvent.HostPong>(Assert.Single(pong.HubEvents));
        Assert.NotNull(engine.LastRoundTripMs);
        Assert.InRange(engine.LastRoundTripMs!.Value, 200, 300);
    }

    [Fact]
    public void OnTick_SendsKeepalivePing_WhenIntervalElapses()
    {
        (HostLinkEngine engine, FakeTimeProvider time) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        // Just before the interval: no ping.
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.True(engine.OnTick().IsEmpty);

        // At the interval: a ping.
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<HostPing>(Assert.Single(engine.OnTick().OutboundCommands));
    }

    [Fact]
    public void OnTick_DropsLink_OnSilenceTimeout()
    {
        (HostLinkEngine engine, FakeTimeProvider time) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        // No inbound for the whole silence window (pings go out but no traffic comes back).
        time.Advance(TimeSpan.FromSeconds(181));
        EngineStep step = engine.OnTick();

        Assert.NotNull(step.DropReason);
        Assert.Contains("silence", step.DropReason);
    }

    [Fact]
    public void InboundTraffic_ResetsSilenceClock()
    {
        (HostLinkEngine engine, FakeTimeProvider time) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        time.Advance(TimeSpan.FromSeconds(170));
        engine.OnLineReceived(Wire.Host("CMSG g4abc 3333 still alive")); // resets the clock
        time.Advance(TimeSpan.FromSeconds(170)); // 340s since connect, but only 170s since last inbound

        Assert.Null(engine.OnTick().DropReason);
    }

    [Fact]
    public void OnTick_DropsLink_OnHandshakeStall()
    {
        (HostLinkEngine engine, FakeTimeProvider time) = NewEngine();
        engine.OnConnected();

        // No /..HOST reply within the handshake timeout.
        time.Advance(TimeSpan.FromSeconds(31));
        EngineStep step = engine.OnTick();

        Assert.NotNull(step.DropReason);
        Assert.Contains("handshake", step.DropReason);
    }

    [Fact]
    public void InboundLoop_DropsLink_AndForwardsToHub()
    {
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        EngineStep step = engine.OnLineReceived(Wire.Host("LOOP ORACLE PDNCONV someone HOST"));

        Assert.NotNull(step.DropReason);
        Assert.Contains("LOOP", step.DropReason);
        Assert.IsType<ConversEvent.HostLoop>(Assert.Single(step.HubEvents));
    }

    [Fact]
    public void SecondHandshake_OnEstablishedLink_IsAFault_Drops()
    {
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        EngineStep step = engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        Assert.NotNull(step.DropReason);
    }

    [Fact]
    public void NonHostCommandLine_IsTreatedAsActivity_NoEvent()
    {
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();
        engine.OnLineReceived(Wire.Host("HOST ORACLE saupp1.62a Aadmpunfi"));

        // The oracle greeting / a human-readable notice.
        EngineStep step = engine.OnLineReceived("* Welcome to the pdn-convers interop oracle.");

        Assert.True(step.IsEmpty);
    }

    [Fact]
    public void EarlyPresence_BeforeHandshakeReply_IsBufferedAsHubEvent()
    {
        // conversd announces users right after sending its /..HOST; if a presence line arrives before our
        // parse of the reply (interleaving), it must not be lost.
        (HostLinkEngine engine, _) = NewEngine();
        engine.OnConnected();

        EngineStep step = engine.OnLineReceived(Wire.Host("USER g4abc ORACLE 1781290976 -1 3333 ~"));

        // Still handshaking, but the presence event is captured.
        Assert.Equal(HostLinkState.Handshaking, engine.State);
        Assert.IsType<ConversEvent.HostUser>(Assert.Single(step.HubEvents));
    }
}
