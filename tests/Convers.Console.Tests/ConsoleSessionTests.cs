using Convers.Console;
using Convers.Core;

namespace Convers.Console.Tests;

/// <summary>
/// End-to-end session flow over the scripted terminal: auto-login, the plain↔Core-event mapping
/// (drives the real <see cref="ConversHub"/>) and the action→text rendering surfaced to the user.
/// </summary>
public class ConsoleSessionTests
{
    [Fact]
    public async Task AutoLogin_GreetsAndJoinsDefaultChannel_WithoutPromptingForName()
    {
        var h = new ConsoleHarness(defaultChannel: 3333);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "quit");

        Assert.Contains("Welcome M0LTE.", t.Output, StringComparison.Ordinal);
        Assert.Contains("You are on channel 3333.", t.Output, StringComparison.Ordinal);
        // The user was placed on the hub under their authenticated callsign — no /name asked.
        Assert.DoesNotContain("enter your name", t.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/name", t.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Say_IsReportedAsAJoinToAnAlreadyPresentPeer()
    {
        // A peer is on channel 100 before the user logs in; the user's auto-login join must fan a
        // presence notice out to that peer (observed via the hub action stream after the session).
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddLocalUser("sess-peer", "G4XYZ", 100);

        var speaker = new ScriptedTerminal("M0LTE", ["hello channel", "quit"]);
        await ConverseConsoleSession.RunAsync(speaker, h.Hub, h.Config, "sess-M0LTE", CancellationToken.None);

        // The speaker never sees their own echo (the hub does not re-deliver to the originator).
        Assert.DoesNotContain("<M0LTE>: hello channel", speaker.Output, StringComparison.Ordinal);
        // The speaker did sign off cleanly.
        Assert.Contains("73 de GB7PDN", speaker.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Say_FansOutToOtherLocalUsersOnTheChannel()
    {
        // The plain "say" intent maps to a LocalSay event; the hub delivers it to every other local
        // session on the channel. Verified at the hub+renderer seam (no socket on the peer).
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddLocalUser("sess-peer", "G4XYZ", 100);
        h.Hub.Advance(new ConversEvent.LocalJoin("sess-M0LTE", "M0LTE", 100));

        IReadOnlyList<ConversAction> actions =
            h.Hub.Advance(new ConversEvent.LocalSay("sess-M0LTE", "hello peer"));

        List<string> toPeer = ActionRenderer.Render(actions, "sess-peer");
        Assert.Contains("<M0LTE>: hello peer", toPeer);
    }

    [Fact]
    public void IncomingChannelMessage_IsRenderedToTheUser()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddLocalUser("sess-M0LTE", "M0LTE", 100);

        IReadOnlyList<ConversAction> actions =
            h.Hub.Advance(new ConversEvent.HostChannelMessage("DL9SAU", 100, "hallo"));
        List<string> lines = ActionRenderer.Render(actions, "sess-M0LTE");

        Assert.Contains("<DL9SAU>: hallo", lines);
    }

    [Fact]
    public void PrivateMessage_BetweenTwoLocalUsers_DeliversToTheTarget()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddLocalUser("sess-G4XYZ", "G4XYZ", 100);
        h.Hub.Advance(new ConversEvent.LocalJoin("sess-M0LTE", "M0LTE", 100));

        IReadOnlyList<ConversAction> pm =
            h.Hub.Advance(new ConversEvent.LocalPrivateMessage("sess-M0LTE", "G4XYZ", "secret"));

        List<string> toTarget = ActionRenderer.Render(pm, "sess-G4XYZ");
        Assert.Contains("<*M0LTE*>: secret", toTarget);
    }

    [Fact]
    public async Task Who_ListsUsersOnTheCurrentChannel()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddRemoteUser("DL9SAU", "DB0TUD", 100, personal: "Thomas");
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who", "quit");

        Assert.Contains("Users on channel 100:", t.Output, StringComparison.Ordinal);
        Assert.Contains("DL9SAU@DB0TUD ch 100", t.Output, StringComparison.Ordinal);
        Assert.Contains("Thomas", t.Output, StringComparison.Ordinal);
        Assert.Contains("M0LTE@GB7PDN ch 100", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhoStar_ListsTheWholeNetwork()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddRemoteUser("DL9SAU", "DB0TUD", 7);   // on a different channel
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "who *", "quit");

        Assert.Contains("Users on the network:", t.Output, StringComparison.Ordinal);
        Assert.Contains("DL9SAU@DB0TUD ch 7", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_SwitchesChannel_AndReports()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "join 200", "quit");
        Assert.Contains("You are on channel 200.", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topic_SetThenQuery()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "topic ragchew here", "topic", "quit");
        Assert.Contains("ragchew here", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Personal_Set_Confirms()
    {
        var h = new ConsoleHarness();
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "personal Tom in Reading", "quit");
        Assert.Contains("Personal text set: Tom in Reading", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Away_SetAndBack()
    {
        var h = new ConsoleHarness();
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "away lunch", "away", "quit");
        Assert.Contains("You are away: lunch", t.Output, StringComparison.Ordinal);
        Assert.Contains("You are back.", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quit_EndsAsQuit_WithSignOff()
    {
        var h = new ConsoleHarness();
        (ConverseSessionEndReason end, ScriptedTerminal t) = await h.RunAsync("M0LTE", "quit 73 all");
        Assert.Equal(ConverseSessionEndReason.Quit, end);
        Assert.Contains("73 de GB7PDN (73 all)", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leave_EndsTheSession()
    {
        var h = new ConsoleHarness();
        (ConverseSessionEndReason end, _) = await h.RunAsync("M0LTE", "leave");
        Assert.Equal(ConverseSessionEndReason.Quit, end);
    }

    [Fact]
    public async Task RunningOutOfInput_EndsAsDrop()
    {
        var h = new ConsoleHarness();
        (ConverseSessionEndReason end, _) = await h.RunAsync("M0LTE", "who"); // no quit -> terminal drops
        Assert.Equal(ConverseSessionEndReason.Drop, end);
    }

    [Fact]
    public async Task UnknownInClassicMode_GivesAHint()
    {
        var h = new ConsoleHarness(@interface: ConsoleInterface.Classic);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "/zzz", "/quit");
        Assert.Contains("Unknown command", t.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareTextInPlainMode_IsSaid_NotAnError()
    {
        var h = new ConsoleHarness(defaultChannel: 100);
        h.AddLocalUser("sess-peer", "G4XYZ", 100);
        (_, ScriptedTerminal t) = await h.RunAsync("M0LTE", "good evening all", "quit");
        // No "didn't understand" hint — plain bare text is chat.
        Assert.DoesNotContain("didn't understand", t.Output, StringComparison.OrdinalIgnoreCase);
    }
}
