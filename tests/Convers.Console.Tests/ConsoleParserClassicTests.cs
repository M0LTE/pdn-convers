using Convers.Console;

namespace Convers.Console.Tests;

/// <summary>Classic <c>/</c>-command parsing — the literal conversd surface for power/legacy clients.</summary>
public class ConsoleParserClassicTests
{
    [Fact]
    public void Classic_NonSlash_IsChat()
    {
        // In conversd, a line without a leading slash is channel text.
        var intent = Assert.IsType<ConsoleIntent.Say>(ConsoleParser.ParseClassic("hello everyone"));
        Assert.Equal("hello everyone", intent.Text);
    }

    [Theory]
    [InlineData("/join 100", 100)]
    [InlineData("/j 100", 100)]
    [InlineData("/channel 256", 256)]
    [InlineData("/c #256", 256)]
    public void Classic_Join_ParsesChannel(string line, int expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Join>(ConsoleParser.ParseClassic(line));
        Assert.Equal(expected, intent.Channel);
    }

    [Theory]
    [InlineData("/name g4abc 100", 100)]   // /NAME <call> [chan] -> we treat as a (re)join to the channel
    [InlineData("/n g4abc", null)]
    public void Classic_Name_BecomesAJoin(string line, int? expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Join>(ConsoleParser.ParseClassic(line));
        Assert.Equal(expected, intent.Channel);
    }

    [Theory]
    [InlineData("/msg g4abc hi there")]
    [InlineData("/m g4abc hi there")]
    [InlineData("/send g4abc hi there")]
    [InlineData("/write g4abc hi there")]
    public void Classic_Msg_Aliases(string line)
    {
        var intent = Assert.IsType<ConsoleIntent.Msg>(ConsoleParser.ParseClassic(line));
        Assert.Equal("G4ABC", intent.To);
        Assert.Equal("hi there", intent.Text);
    }

    [Theory]
    [InlineData("/topic the topic", "the topic")]
    [InlineData("/to the topic", "the topic")]
    [InlineData("/topic @", "")]            // @ clears
    public void Classic_Topic(string line, string expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Topic>(ConsoleParser.ParseClassic(line));
        Assert.Equal(expected, intent.Text);
    }

    [Theory]
    [InlineData("/personal Tom", "Tom")]
    [InlineData("/pe Tom", "Tom")]
    [InlineData("/note Tom", "Tom")]
    public void Classic_Personal_Aliases(string line, string expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Personal>(ConsoleParser.ParseClassic(line));
        Assert.Equal(expected, intent.Text);
    }

    [Theory]
    [InlineData("/who")]
    [InlineData("/wh")]
    [InlineData("/users")]
    [InlineData("/online")]
    public void Classic_Who_Aliases(string line) =>
        Assert.IsType<ConsoleIntent.Who>(ConsoleParser.ParseClassic(line));

    [Fact]
    public void Classic_Away()
    {
        var intent = Assert.IsType<ConsoleIntent.Away>(ConsoleParser.ParseClassic("/away lunch"));
        Assert.Equal("lunch", intent.Text);
    }

    [Fact]
    public void Classic_Invite()
    {
        var intent = Assert.IsType<ConsoleIntent.Invite>(ConsoleParser.ParseClassic("/invite g4abc 100"));
        Assert.Equal("G4ABC", intent.User);
        Assert.Equal(100, intent.Channel);
    }

    [Theory]
    [InlineData("/leave")]
    [InlineData("/le")]
    public void Classic_Leave(string line) =>
        Assert.IsType<ConsoleIntent.Leave>(ConsoleParser.ParseClassic(line));

    [Theory]
    [InlineData("/quit 73", "73")]
    [InlineData("/bye", "")]
    [InlineData("/exit cheerio", "cheerio")]
    public void Classic_Quit_Aliases(string line, string reason)
    {
        var intent = Assert.IsType<ConsoleIntent.Quit>(ConsoleParser.ParseClassic(line));
        Assert.Equal(reason, intent.Reason);
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/?")]
    public void Classic_Help(string line) =>
        Assert.IsType<ConsoleIntent.Help>(ConsoleParser.ParseClassic(line));

    [Theory]
    [InlineData("/zzz")]                    // not a modelled command
    [InlineData("/")]                       // bare slash
    public void Classic_Unknown(string line) =>
        Assert.IsType<ConsoleIntent.Unknown>(ConsoleParser.ParseClassic(line));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classic_Empty_IsNothing(string? line) =>
        Assert.IsType<ConsoleIntent.Empty>(ConsoleParser.ParseClassic(line));
}
