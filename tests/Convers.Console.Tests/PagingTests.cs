using Convers.Console;
using Convers.Core;

namespace Convers.Console.Tests;

/// <summary>Paclen-friendly paging of long output (mirrors the BBS pager).</summary>
public class PagingTests
{
    private const string ContinuePrompt = "<A>bort, <CR> Continue..>";

    /// <summary>Seeds enough remote users that a `who *` listing exceeds one page.</summary>
    private static void SeedManyUsers(ConsoleHarness h, int count)
    {
        for (int i = 0; i < count; i++)
        {
            h.AddRemoteUser($"R{i:D3}AB", "DB0TUD", 100);
        }
    }

    [Fact]
    public async Task LongListing_ShowsTheContinuePrompt()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 5);
        SeedManyUsers(h, 12);
        // CR (empty line) continues at each page boundary; then quit.
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "", "", "quit");
        Assert.Contains(ContinuePrompt, t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrAtThePrompt_ContinuesToTheEnd()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 5);
        SeedManyUsers(h, 12);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "", "", "quit");
        // The last seeded user must appear once paging is followed through.
        Assert.Contains("R011AB@DB0TUD", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_AtThePrompt_AbortsTheRemainingOutput()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 5);
        SeedManyUsers(h, 12);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "A", "quit");
        Assert.Contains("Output aborted", t.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("R011AB@DB0TUD", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortListing_NoContinuePrompt()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 20);
        SeedManyUsers(h, 3);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "quit");
        Assert.DoesNotContain(ContinuePrompt, t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageLengthZero_DisablesPaging()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 0);
        SeedManyUsers(h, 30);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "quit");
        Assert.DoesNotContain(ContinuePrompt, t.Output, StringComparison.Ordinal);
        Assert.Contains("R029AB@DB0TUD", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DropAtThePagingPrompt_EndsAsDrop()
    {
        var h = new ConsoleHarness(defaultChannel: 100, pageLength: 5);
        SeedManyUsers(h, 12);
        // No response after the prompt -> the terminal runs dry -> drop.
        (ConverseSessionEndReason end, _) = await h.RunAsync("M0LTE", "who");
        Assert.Equal(ConverseSessionEndReason.Drop, end);
    }
}
