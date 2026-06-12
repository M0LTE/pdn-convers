using Convers.Console;

namespace Convers.Console.Tests;

/// <summary>Plain-language parsing + unambiguous-prefix resolution (design.md decision 9).</summary>
public class ConsoleParserPlainTests
{
    [Theory]
    [InlineData("join 3333", 3333)]
    [InlineData("j 100", 100)]              // unambiguous prefix
    [InlineData("JOIN #256", 256)]          // case + leading hash
    public void Plain_Join_ParsesChannel(string line, int expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Join>(ConsoleParser.ParsePlain(line));
        Assert.Equal(expected, intent.Channel);
    }

    [Fact]
    public void Plain_BareJoin_IsAQuery()
    {
        var intent = Assert.IsType<ConsoleIntent.Join>(ConsoleParser.ParsePlain("join"));
        Assert.Null(intent.Channel);
    }

    [Fact]
    public void Plain_JoinOutOfRange_IsUnknown() =>
        Assert.IsType<ConsoleIntent.Unknown>(ConsoleParser.ParsePlain("join 99999"));

    [Theory]
    [InlineData("say hello world", "hello world")]
    [InlineData("s   hi there", "hi there")]
    public void Plain_Say_KeepsText(string line, string expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Say>(ConsoleParser.ParsePlain(line));
        Assert.Equal(expected, intent.Text);
    }

    [Fact]
    public void Plain_BareText_IsSaidToChannel()
    {
        // No leading verb -> the whole line is chat.
        var intent = Assert.IsType<ConsoleIntent.Say>(ConsoleParser.ParsePlain("good morning all"));
        Assert.Equal("good morning all", intent.Text);
    }

    [Fact]
    public void Plain_LeadingSlashVerb_IsForgiven()
    {
        // A classic-minded user typing /who in plain mode still gets a who.
        Assert.IsType<ConsoleIntent.Who>(ConsoleParser.ParsePlain("/who"));
    }

    [Fact]
    public void Plain_SlashNonVerb_IsSaidVerbatim()
    {
        // "/me waves" is not a verb; it must not be lost — it's chat.
        var intent = Assert.IsType<ConsoleIntent.Say>(ConsoleParser.ParsePlain("/me waves"));
        Assert.Equal("me waves", intent.Text);
    }

    [Fact]
    public void Plain_Msg_SplitsTargetAndText()
    {
        var intent = Assert.IsType<ConsoleIntent.Msg>(ConsoleParser.ParsePlain("msg g4abc are you there?"));
        Assert.Equal("G4ABC", intent.To);
        Assert.Equal("are you there?", intent.Text);
    }

    [Fact]
    public void Plain_Msg_WithoutText_IsUnknown() =>
        Assert.IsType<ConsoleIntent.Unknown>(ConsoleParser.ParsePlain("msg g4abc"));

    [Theory]
    [InlineData("topic ragchew here", "ragchew here")]
    [InlineData("t   ", "")]                // bare/whitespace -> query (empty)
    [InlineData("topic @", "")]             // @ clears
    public void Plain_Topic_NormalisesText(string line, string expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Topic>(ConsoleParser.ParsePlain(line));
        Assert.Equal(expected, intent.Text);
    }

    [Theory]
    [InlineData("personal Tom in Reading", "Tom in Reading")]
    [InlineData("pers Tom in Reading", "Tom in Reading")]   // pers is an unambiguous prefix of personal
    [InlineData("personal @", "")]
    public void Plain_Personal_AcceptsBothSpellings(string line, string expected)
    {
        var intent = Assert.IsType<ConsoleIntent.Personal>(ConsoleParser.ParsePlain(line));
        Assert.Equal(expected, intent.Text);
    }

    [Fact]
    public void Plain_Away_KeepsText()
    {
        var intent = Assert.IsType<ConsoleIntent.Away>(ConsoleParser.ParsePlain("away back in 5"));
        Assert.Equal("back in 5", intent.Text);
    }

    [Fact]
    public void Plain_Who_CarriesArgument()
    {
        var intent = Assert.IsType<ConsoleIntent.Who>(ConsoleParser.ParsePlain("who *"));
        Assert.Equal("*", intent.Argument);
    }

    [Fact]
    public void Plain_Invite_ParsesUserAndChannel()
    {
        var intent = Assert.IsType<ConsoleIntent.Invite>(ConsoleParser.ParsePlain("invite g4abc 100"));
        Assert.Equal("G4ABC", intent.User);
        Assert.Equal(100, intent.Channel);
    }

    [Fact]
    public void Plain_Invite_NoChannel_IsNull()
    {
        var intent = Assert.IsType<ConsoleIntent.Invite>(ConsoleParser.ParsePlain("invite g4abc"));
        Assert.Null(intent.Channel);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("l")]
    public void Plain_Leave_Parses(string line) =>
        Assert.IsType<ConsoleIntent.Leave>(ConsoleParser.ParsePlain(line));

    [Theory]
    [InlineData("quit", "")]
    [InlineData("quit 73 all", "73 all")]
    [InlineData("q cheerio", "cheerio")]
    public void Plain_Quit_CarriesReason(string line, string reason)
    {
        var intent = Assert.IsType<ConsoleIntent.Quit>(ConsoleParser.ParsePlain(line));
        Assert.Equal(reason, intent.Reason);
    }

    [Theory]
    [InlineData("help", "")]
    [InlineData("help join", "join")]
    [InlineData("h", "")]
    public void Plain_Help_CarriesSubject(string line, string subject)
    {
        var intent = Assert.IsType<ConsoleIntent.Help>(ConsoleParser.ParsePlain(line));
        Assert.Equal(subject, intent.Subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Plain_Empty_IsNothing(string? line) =>
        Assert.IsType<ConsoleIntent.Empty>(ConsoleParser.ParsePlain(line));
}
