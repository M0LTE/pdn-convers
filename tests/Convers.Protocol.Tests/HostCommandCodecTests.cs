using Convers.Protocol;

namespace Convers.Protocol.Tests;

/// <summary>
/// Golden-vector tests for the HOST command codec, pinned to the SPECS field layouts (reference/SPECS.txt)
/// and to real bytes captured from the conversd-saupp oracle on 2026-06-12. These are plain unit tests
/// (NOT Category=Interop): the vectors are baked in, so the lane is green without docker.
/// </summary>
public class HostCommandCodecTests
{
    // The on-wire host prefix is '/' + 0xFF + 0x80. In test source we spell it with the two high chars
    // U+00FF and U+0080 (Latin-1 of 0xFF/0x80); P is the canonical constant under test.
    private const string P = ConversCommand.HostCommandPrefix;

    [Fact]
    public void HostCommandPrefix_IsTheThreeWireBytes()
    {
        byte[] bytes = ConversWire.Encode(P);
        Assert.Equal(new byte[] { 0x2F, 0xFF, 0x80 }, bytes);
    }

    // --- /..USER (NECESSARY): captured oracle vectors -----------------------------------------------

    [Fact]
    public void Parse_CapturedUserJoin_FromOracle()
    {
        // Real bytes: HOST link saw this when a user joined channel 3333.
        string line = P + "USER n0call ORACLE 1781278611 -1 3333 ~";
        var cmd = Assert.IsType<HostUser>(HostCommandCodec.Parse(line));
        Assert.Equal("n0call", cmd.User);
        Assert.Equal("ORACLE", cmd.Host);
        Assert.Equal(1781278611L, cmd.Timestamp);
        Assert.Equal(-1, cmd.FromChannel);
        Assert.Equal(3333, cmd.ToChannel);
        Assert.Equal("~", cmd.Text);
        Assert.False(cmd.IsObserver);
        Assert.False(cmd.IsSignoff);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_CapturedUserSignoff_FromOracle()
    {
        // Real bytes: HOST link saw this when the user /quit (tochan -1, text = reason).
        string line = P + "USER n0call ORACLE 1781278617 3333 -1 /quit";
        var cmd = Assert.IsType<HostUser>(HostCommandCodec.Parse(line));
        Assert.Equal(3333, cmd.FromChannel);
        Assert.Equal(-1, cmd.ToChannel);
        Assert.True(cmd.IsSignoff);
        Assert.Equal("/quit", cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_ObserverPresence_UsesObsvVerb()
    {
        string line = P + "OBSV obsuser HUB 1700000000 -1 100 @";
        var cmd = Assert.IsType<HostUser>(HostCommandCodec.Parse(line));
        Assert.True(cmd.IsObserver);
        Assert.Equal("OBSV", cmd.Verb);
        Assert.Equal("@", cmd.Text);
        // The '@' personal note round-trips back to a lone '@'.
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    // --- /..HOST handshake: captured oracle reply ---------------------------------------------------

    [Fact]
    public void Parse_CapturedHandshakeReply_FromOracle()
    {
        // Real bytes: the oracle's /..HOST reply to our handshake.
        string line = P + "HOST ORACLE saupp1.62a Aadmpunfi";
        var cmd = Assert.IsType<HostHandshake>(HostCommandCodec.Parse(line));
        Assert.Equal("ORACLE", cmd.Hostname);
        Assert.Equal("saupp1.62a", cmd.Software);
        Assert.Equal(
            Facilities.AwayNew | Facilities.AwayOld | Facilities.DestinationForwarding |
            Facilities.ChannelModes | Facilities.PingPong | Facilities.Udat |
            Facilities.Nicknames | Facilities.Filter | Facilities.SauppInternal,
            cmd.Facilities);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Format_Handshake_DefaultsSoftwareToQuestionMark_WhenAbsent()
    {
        var cmd = Assert.IsType<HostHandshake>(HostCommandCodec.Parse(P + "HOST leafnode"));
        Assert.Equal("leafnode", cmd.Hostname);
        Assert.Equal("?", cmd.Software);
        Assert.Equal(Facilities.None, cmd.Facilities);
        Assert.Equal(P + "HOST leafnode ?", HostCommandCodec.Format(cmd));
    }

    // --- /..PING /..PONG: captured oracle vector ---------------------------------------------------

    [Fact]
    public void Parse_Ping_HasNoArguments()
    {
        var cmd = Assert.IsType<HostPing>(HostCommandCodec.Parse(P + "PING"));
        Assert.Equal("PING", cmd.Verb);
        Assert.Equal(P + "PING", HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_CapturedPong_FromOracle()
    {
        // Real bytes: the oracle replied /..PONG 3 to our /..PING.
        string line = P + "PONG 3";
        var cmd = Assert.IsType<HostPong>(HostCommandCodec.Parse(line));
        Assert.Equal(3L, cmd.Time);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    public void Pong_SpecialValues_RoundTrip(long time)
    {
        string line = P + "PONG " + time.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cmd = Assert.IsType<HostPong>(HostCommandCodec.Parse(line));
        Assert.Equal(time, cmd.Time);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    // --- The other NECESSARY commands: SPECS field layouts -----------------------------------------

    [Fact]
    public void Parse_ChannelMessage_SpecsLayout()
    {
        string line = P + "CMSG g4abc 3333 hello channel here";
        var cmd = Assert.IsType<HostChannelMessage>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal(3333, cmd.Channel);
        Assert.Equal("hello channel here", cmd.Text);
        Assert.False(cmd.IsBroadcast);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_ChannelMessage_FromConversd_IsBroadcast()
    {
        var cmd = Assert.IsType<HostChannelMessage>(
            HostCommandCodec.Parse(P + "CMSG conversd 3333 *** something happened"));
        Assert.True(cmd.IsBroadcast);
    }

    [Fact]
    public void Parse_UserMessage_SpecsLayout()
    {
        string line = P + "UMSG g4abc n0call private text with spaces";
        var cmd = Assert.IsType<HostUserMessage>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.From);
        Assert.Equal("n0call", cmd.To);
        Assert.Equal("private text with spaces", cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_UserData_EmptyMarkerNormalises()
    {
        var withText = Assert.IsType<HostUserData>(HostCommandCodec.Parse(P + "UDAT g4abc HUB Real Name"));
        Assert.Equal("Real Name", withText.Text);
        Assert.Equal(P + "UDAT g4abc HUB Real Name", HostCommandCodec.Format(withText));

        var empty = Assert.IsType<HostUserData>(HostCommandCodec.Parse(P + "UDAT g4abc HUB @"));
        Assert.Equal(string.Empty, empty.Text);
        // Empty text re-emits as the '@' marker.
        Assert.Equal(P + "UDAT g4abc HUB @", HostCommandCodec.Format(empty));
    }

    [Fact]
    public void Parse_Invite_SpecsLayout()
    {
        string line = P + "INVI g4abc n0call 3333";
        var cmd = Assert.IsType<HostInvite>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.From);
        Assert.Equal("n0call", cmd.User);
        Assert.Equal(3333, cmd.Channel);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    // --- OPTIONAL commands: SPECS field layouts ----------------------------------------------------

    [Fact]
    public void Parse_Topic_SpecsLayout()
    {
        string line = P + "TOPI g4abc HUB 1700000000 3333 The channel topic";
        var cmd = Assert.IsType<HostTopic>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("HUB", cmd.Host);
        Assert.Equal(1700000000L, cmd.Time);
        Assert.Equal(3333, cmd.Channel);
        Assert.Equal("The channel topic", cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Topic_EmptyTextRemovesTopic()
    {
        string line = P + "TOPI g4abc HUB 1700000000 3333";
        var cmd = Assert.IsType<HostTopic>(HostCommandCodec.Parse(line));
        Assert.Equal(string.Empty, cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Mode_SpecsLayout()
    {
        string line = P + "MODE 3333 +mt";
        var cmd = Assert.IsType<HostMode>(HostCommandCodec.Parse(line));
        Assert.Equal(3333, cmd.Channel);
        Assert.Equal("+mt", cmd.Options);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Oper_SpecsLayout()
    {
        string line = P + "OPER g4abc 3333 n0call";
        var cmd = Assert.IsType<HostOper>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.FromName);
        Assert.Equal(3333, cmd.Channel);
        Assert.Equal("n0call", cmd.User);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Away_SpecsLayout()
    {
        string line = P + "AWAY g4abc HUB 1700000000 gone for lunch";
        var cmd = Assert.IsType<HostAway>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("HUB", cmd.Host);
        Assert.Equal(1700000000L, cmd.Time);
        Assert.Equal("gone for lunch", cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Away_EmptyTextMeansBack()
    {
        string line = P + "AWAY g4abc HUB 1700000000";
        var cmd = Assert.IsType<HostAway>(HostCommandCodec.Parse(line));
        Assert.Equal(string.Empty, cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Dest_SpecsLayout()
    {
        string line = P + "DEST somehost 42 saupp1.62a";
        var cmd = Assert.IsType<HostDest>(HostCommandCodec.Parse(line));
        Assert.Equal("somehost", cmd.Host);
        Assert.Equal(42L, cmd.Time);
        Assert.Equal("saupp1.62a", cmd.Software);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Route_SpecsLayout()
    {
        string line = P + "ROUT desthost g4abc 5";
        var cmd = Assert.IsType<HostRoute>(HostCommandCodec.Parse(line));
        Assert.Equal("desthost", cmd.Dest);
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal(5, cmd.Ttl);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_SysInfo_SpecsLayout()
    {
        string line = P + "SYSI g4abc all";
        var cmd = Assert.IsType<HostSysInfo>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("all", cmd.Host);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Loop_PreservesTail()
    {
        string line = P + "LOOP hubA hubB hostX HOST";
        var cmd = Assert.IsType<HostLoop>(HostCommandCodec.Parse(line));
        Assert.Equal("hubA hubB hostX HOST", cmd.Detail);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_ExtendedCommand_SpecsLayout()
    {
        string line = P + "ECMD g4abc weather london today";
        var cmd = Assert.IsType<HostExtendedCommand>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("weather", cmd.CommandName);
        Assert.Equal("london today", cmd.Parameters);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_Help_SpecsLayout()
    {
        string line = P + "HELP g4abc weather";
        var cmd = Assert.IsType<HostHelp>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("weather", cmd.CommandName);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_UserAdd_SpecsLayout()
    {
        string line = P + "UADD g4abc HUB ChrisNick -1 Personal text";
        var cmd = Assert.IsType<HostUserAdd>(HostCommandCodec.Parse(line));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal("HUB", cmd.Host);
        Assert.Equal("ChrisNick", cmd.Nickname);
        Assert.Equal(-1, cmd.Channel);
        Assert.Equal("Personal text", cmd.Text);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    // --- Golden rule: unknown /.. round-trips losslessly -------------------------------------------

    [Fact]
    public void Parse_UnknownHostVerb_RoundTripsVerbatim()
    {
        string line = P + "WXYZ some arbitrary payload here";
        var cmd = Assert.IsType<UnknownHostCommand>(HostCommandCodec.Parse(line));
        Assert.Equal("WXYZ", cmd.Verb);
        Assert.Equal("some arbitrary payload here", cmd.Body);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_UnknownHostVerb_NoBody_RoundTrips()
    {
        string line = P + "ZZZZ";
        var cmd = Assert.IsType<UnknownHostCommand>(HostCommandCodec.Parse(line));
        Assert.Equal("ZZZZ", cmd.Verb);
        Assert.Equal(string.Empty, cmd.Body);
        Assert.Equal(line, HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_AcceptsTheDottedDocumentationSpelling()
    {
        // SPECS-style vectors are written with literal dots; the codec accepts them but always
        // re-emits the real byte prefix.
        var cmd = Assert.IsType<HostPing>(HostCommandCodec.Parse("/..PING"));
        Assert.Equal(P + "PING", HostCommandCodec.Format(cmd));
    }

    [Fact]
    public void Parse_VerbIsCaseInsensitive()
    {
        Assert.IsType<HostPing>(HostCommandCodec.Parse(P + "ping"));
        Assert.IsType<HostUser>(HostCommandCodec.Parse(P + "user g4abc HUB 1 -1 100 @"));
    }

    [Fact]
    public void TryParse_ReturnsFalseForNonHostLine()
    {
        Assert.False(HostCommandCodec.TryParse("/who", out HostCommand? cmd));
        Assert.Null(cmd);
        Assert.False(HostCommandCodec.TryParse("plain chat", out _));
        Assert.True(HostCommandCodec.TryParse(P + "PING", out HostCommand? ping));
        Assert.IsType<HostPing>(ping);
    }
}
