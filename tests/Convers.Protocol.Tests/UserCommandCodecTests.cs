using Convers.Protocol;

namespace Convers.Protocol.Tests;

/// <summary>
/// Tests for the USER command codec: the <c>/</c>-grammar and its abbreviation rule (prefix match in
/// conversd cmdtable order). Field layouts from user.c; abbreviations from etc/convers.help and the
/// dispatcher in conversd.c.
/// </summary>
public class UserCommandCodecTests
{
    [Fact]
    public void Parse_Name_WithCallAndChannel()
    {
        var cmd = Assert.IsType<NameCommand>(UserCommandCodec.Parse("/name g4abc 3333"));
        Assert.Equal("g4abc", cmd.Call);
        Assert.Equal(3333, cmd.Channel);
    }

    [Fact]
    public void Parse_Name_AbbreviatedToSlashN()
    {
        // /n -> name (name precedes nickname/note/news in the table, so the prefix resolves to name).
        var cmd = Assert.IsType<NameCommand>(UserCommandCodec.Parse("/n g4abc"));
        Assert.Equal("g4abc", cmd.Call);
        Assert.Null(cmd.Channel);
    }

    [Fact]
    public void Parse_Name_StripsLeadingHashOnChannel()
    {
        var cmd = Assert.IsType<NameCommand>(UserCommandCodec.Parse("/name g4abc #100"));
        Assert.Equal(100, cmd.Channel);
    }

    [Fact]
    public void Parse_Join_AndChannelAreSynonyms()
    {
        Assert.Equal(3333, Assert.IsType<JoinCommand>(UserCommandCodec.Parse("/join 3333")).Channel);
        Assert.Equal(42, Assert.IsType<JoinCommand>(UserCommandCodec.Parse("/channel 42")).Channel);
        Assert.Equal(42, Assert.IsType<JoinCommand>(UserCommandCodec.Parse("/join #42")).Channel);
    }

    [Fact]
    public void Parse_Who_Abbreviated()
    {
        Assert.IsType<WhoCommand>(UserCommandCodec.Parse("/who"));
        Assert.IsType<WhoCommand>(UserCommandCodec.Parse("/w"));
        Assert.Equal("g4abc", Assert.IsType<WhoCommand>(UserCommandCodec.Parse("/who g4abc")).Argument);
    }

    [Fact]
    public void Parse_Msg_TakesRecipientThenRestAsText()
    {
        var cmd = Assert.IsType<MsgCommand>(UserCommandCodec.Parse("/msg g4abc hi there friend"));
        Assert.Equal("g4abc", cmd.To);
        Assert.Equal("hi there friend", cmd.Text);
    }

    [Fact]
    public void Parse_Topic_TakesWholeRest()
    {
        var cmd = Assert.IsType<TopicCommand>(UserCommandCodec.Parse("/topic Welcome to the channel"));
        Assert.Equal("Welcome to the channel", cmd.Text);
    }

    [Fact]
    public void Parse_Personal_AbbrevPers()
    {
        // /pers -> personal (the only table row starting with "pers").
        var cmd = Assert.IsType<PersonalCommand>(UserCommandCodec.Parse("/pers G4ABC, Chris in Bath"));
        Assert.Equal("G4ABC, Chris in Bath", cmd.Text);
    }

    [Fact]
    public void Parse_Quit_WithReason()
    {
        var cmd = Assert.IsType<QuitCommand>(UserCommandCodec.Parse("/quit 73 es gn"));
        Assert.Equal("73 es gn", cmd.Reason);
    }

    [Fact]
    public void Parse_LeaveAndBeep()
    {
        Assert.IsType<LeaveCommand>(UserCommandCodec.Parse("/leave"));
        Assert.IsType<BeepCommand>(UserCommandCodec.Parse("/beep"));
    }

    [Fact]
    public void Parse_Invite_WithChannel()
    {
        var cmd = Assert.IsType<InviteCommand>(UserCommandCodec.Parse("/invite g4abc 3333"));
        Assert.Equal("g4abc", cmd.User);
        Assert.Equal(3333, cmd.Channel);
    }

    [Fact]
    public void Parse_Online_And_Cstat_FromUnknownState()
    {
        Assert.Equal("a", Assert.IsType<OnlineCommand>(UserCommandCodec.Parse("/online a")).Argument);
        Assert.IsType<CstatCommand>(UserCommandCodec.Parse("/cstat"));
    }

    [Fact]
    public void Parse_Observer()
    {
        Assert.IsType<ObserverCommand>(UserCommandCodec.Parse("/observer"));
    }

    [Fact]
    public void Parse_PlainTextIsChat()
    {
        var cmd = Assert.IsType<ChatText>(UserCommandCodec.Parse("hello everyone on the channel"));
        Assert.Equal("hello everyone on the channel", cmd.Text);
    }

    [Fact]
    public void Parse_UnknownVerbIsUnknownUserCommand()
    {
        var cmd = Assert.IsType<UnknownUserCommand>(UserCommandCodec.Parse("/frobnicate a b c"));
        Assert.Equal("frobnicate", cmd.Verb);
        Assert.Equal("a b c", cmd.Arguments);
    }

    [Fact]
    public void Parse_VerbIsCaseInsensitive()
    {
        Assert.IsType<WhoCommand>(UserCommandCodec.Parse("/WHO"));
        Assert.IsType<NameCommand>(UserCommandCodec.Parse("/Name g4abc"));
    }
}
