using Convers.Protocol;

namespace Convers.Protocol.Tests;

public class ConnectionFsmTests
{
    [Fact]
    public void Name_PromotesUnknownToUser()
    {
        UserCommand login = UserCommandCodec.Parse("/name g4abc 3333");
        Assert.Equal(ConnectionState.User, ConnectionFsm.Next(ConnectionState.Unknown, login));
    }

    [Fact]
    public void Name_WithEmptyCall_DoesNotPromote()
    {
        // conversd refuses "/name" with no call; the connection stays UNKNOWN.
        UserCommand login = UserCommandCodec.Parse("/name");
        Assert.Equal(ConnectionState.Unknown, ConnectionFsm.Next(ConnectionState.Unknown, login));
    }

    [Fact]
    public void Observer_PromotesUnknownToObserver()
    {
        UserCommand obs = UserCommandCodec.Parse("/observer");
        Assert.Equal(ConnectionState.Observer, ConnectionFsm.Next(ConnectionState.Unknown, obs));
    }

    [Fact]
    public void HostHandshake_PromotesUnknownToHost()
    {
        HostCommand hs = HostCommandCodec.Parse(ConversCommand.HostCommandPrefix + "HOST leafnode saupp-1 admpu");
        Assert.Equal(ConnectionState.Host, ConnectionFsm.Next(ConnectionState.Unknown, hs));
    }

    [Fact]
    public void OnlineAndCstat_DoNotChangeState()
    {
        Assert.Equal(ConnectionState.Unknown,
            ConnectionFsm.Next(ConnectionState.Unknown, UserCommandCodec.Parse("/online a")));
        Assert.Equal(ConnectionState.Unknown,
            ConnectionFsm.Next(ConnectionState.Unknown, UserCommandCodec.Parse("/cstat")));
    }

    [Fact]
    public void UserCommands_FromNonUnknown_AreStable()
    {
        UserCommand login = UserCommandCodec.Parse("/name g4abc");
        Assert.Equal(ConnectionState.User, ConnectionFsm.Next(ConnectionState.User, login));
        Assert.Equal(ConnectionState.Host, ConnectionFsm.Next(ConnectionState.Host, login));
    }

    [Fact]
    public void HostHandshake_FromHost_IsStable()
    {
        HostCommand hs = HostCommandCodec.Parse(ConversCommand.HostCommandPrefix + "HOST leafnode");
        Assert.Equal(ConnectionState.Host, ConnectionFsm.Next(ConnectionState.Host, hs));
    }

    [Fact]
    public void AcceptsHostCommand_HandshakeFromUnknownOrHost_OnlyOtherCommandsNeedHost()
    {
        HostCommand hs = HostCommandCodec.Parse(ConversCommand.HostCommandPrefix + "HOST leafnode");
        HostCommand ping = HostCommandCodec.Parse(ConversCommand.HostCommandPrefix + "PING");

        Assert.True(ConnectionFsm.AcceptsHostCommand(ConnectionState.Unknown, hs));
        Assert.True(ConnectionFsm.AcceptsHostCommand(ConnectionState.Host, hs));

        Assert.False(ConnectionFsm.AcceptsHostCommand(ConnectionState.Unknown, ping));
        Assert.True(ConnectionFsm.AcceptsHostCommand(ConnectionState.Host, ping));
    }
}
