using Convers.Console;

namespace Convers.Console.Tests;

/// <summary>`help` explains in sentences (plain) and lists the `/`-surface (classic).</summary>
public class HelpTests
{
    [Fact]
    public void PlainHelp_NoTopic_ExplainsInSentences()
    {
        List<string> lines = ConsoleHelp.Plain("");
        string text = string.Join('\n', lines);
        // A sentence, not '/'-folklore.
        Assert.Contains("round-table chat", text, StringComparison.Ordinal);
        Assert.Contains("join <channel>", text, StringComparison.Ordinal);
        Assert.Contains("just type", text, StringComparison.OrdinalIgnoreCase);
        // Plain help must not be dominated by '/'-commands.
        Assert.DoesNotContain("/Quit", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("join")]
    [InlineData("j")]          // resolves via the same prefix rule as the parser
    [InlineData("msg")]
    [InlineData("who")]
    public void PlainHelp_WithTopic_GivesOneLine(string topic)
    {
        List<string> lines = ConsoleHelp.Plain(topic);
        Assert.Single(lines);
    }

    [Fact]
    public void PlainHelp_UnknownTopic_FallsBackToOverview()
    {
        List<string> lines = ConsoleHelp.Plain("wibble");
        Assert.True(lines.Count > 1);
    }

    [Fact]
    public void ClassicHelp_NoTopic_ListsSlashCommands()
    {
        List<string> lines = ConsoleHelp.Classic("");
        string text = string.Join('\n', lines);
        Assert.Contains("/Join", text, StringComparison.Ordinal);
        Assert.Contains("/Msg", text, StringComparison.Ordinal);
        Assert.Contains("/Quit", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("topic")]
    [InlineData("/topic")]     // a leading slash on the topic is tolerated
    [InlineData("to")]
    public void ClassicHelp_WithTopic_GivesOneLine(string topic)
    {
        List<string> lines = ConsoleHelp.Classic(topic);
        Assert.Single(lines);
    }

    [Fact]
    public async Task PlainSession_Help_IsShownToTheUser()
    {
        var h = new ConsoleHarness();
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "help", "quit");
        Assert.Contains("round-table chat", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassicSession_Help_ListsSlashCommands()
    {
        var h = new ConsoleHarness(@interface: ConsoleInterface.Classic);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "/help", "/quit");
        Assert.Contains("/Quit", t.Output, StringComparison.Ordinal);
    }
}
