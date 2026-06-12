using Convers.Core;

namespace Convers.Console;

/// <summary>
/// The terse RF user surface (design.md <c>Convers.Console</c>) as a sans-IO session engine: one
/// instance drives one connected user's whole lifetime — auto-login greeting, the command loop, and
/// sign-off — over an <see cref="IConverseTerminal"/>, translating user input lines into Core
/// <see cref="ConversEvent"/>s and rendering Core <see cref="ConversAction"/>s back to text. It is a
/// pure translator + presenter: it owns no socket and no thread; the only state it mutates is the
/// supplied <see cref="ConversHub"/> (Core's model), via <see cref="ConversHub.Advance(ConversEvent)"/>.
///
/// <para>Plain-language by default; <c>classic</c> exposes the literal conversd <c>/</c>-surface. The
/// surface is chosen by <see cref="ConverseConsoleConfig.Interface"/>, an input the Host sets from the
/// per-user preference it stores (design decision 9). The user is already authenticated by RHP and
/// never types <c>/name</c> (auto-login, decision 3): the session logs in
/// <see cref="IConverseTerminal.RemoteCallsign"/> on connect.</para>
/// </summary>
public sealed class ConverseConsoleSession
{
    private readonly IConverseTerminal _terminal;
    private readonly ConversHub _hub;
    private readonly ConverseConsoleConfig _config;
    private readonly CancellationToken _ct;

    /// <summary>The opaque session id this session owns in the hub (so fan-out actions route back here).</summary>
    private readonly string _sessionId;

    /// <summary>The authenticated callsign (canonical), auto-logged-in — never the conversd ~ form (decision 4).</summary>
    private readonly string _call;

    private ConverseConsoleSession(
        IConverseTerminal terminal,
        ConversHub hub,
        ConverseConsoleConfig config,
        string sessionId,
        CancellationToken ct)
    {
        _terminal = terminal;
        _hub = hub;
        _config = config;
        _sessionId = sessionId;
        _ct = ct;
        _call = Callsigns.Normalize(terminal.RemoteCallsign);
    }

    /// <summary>
    /// Runs one console session to completion over <paramref name="terminal"/>, driving
    /// <paramref name="hub"/>. The user is auto-logged-in from
    /// <see cref="IConverseTerminal.RemoteCallsign"/> onto the configured default channel. Returns why
    /// the session ended so the Host can disconnect or clean up.
    /// </summary>
    /// <param name="terminal">The line-oriented session seam (the Host implements it over RHP / web).</param>
    /// <param name="hub">The Core presence model this session reads and advances.</param>
    /// <param name="config">Static per-session console configuration (incl. the chosen surface).</param>
    /// <param name="sessionId">The opaque session id the Host assigns; the hub echoes it in actions.</param>
    /// <param name="cancellationToken">Cancels the session (throws <see cref="OperationCanceledException"/>).</param>
    public static async Task<ConverseSessionEndReason> RunAsync(
        IConverseTerminal terminal,
        ConversHub hub,
        ConverseConsoleConfig config,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var session = new ConverseConsoleSession(terminal, hub, config, sessionId, cancellationToken);
        try
        {
            return await session.RunCoreAsync().ConfigureAwait(false);
        }
        catch (ConverseTerminalClosedException)
        {
            session.SignOff("link lost");
            return ConverseSessionEndReason.Drop;
        }
    }

    // ---------------------------------------------------------------- lifetime

    private async Task<ConverseSessionEndReason> RunCoreAsync()
    {
        await GreetAndJoinAsync().ConfigureAwait(false);

        while (true)
        {
            string line = await ReadLineAsync().ConfigureAwait(false);
            ConsoleIntent intent = Parse(line);

            if (intent is ConsoleIntent.Quit quit)
            {
                SignOff(quit.Reason);
                await WriteLineAsync(SignOffLine(quit.Reason)).ConfigureAwait(false);
                return ConverseSessionEndReason.Quit;
            }

            if (intent is ConsoleIntent.Leave leave)
            {
                // A leaf session is on exactly one channel at a time (decision 5), so leaving it
                // ends the session — conversd's /leave semantics for "your last existence".
                SignOff(leave.Reason.Length == 0 ? "leaving" : leave.Reason);
                await WriteLineAsync(SignOffLine(leave.Reason)).ConfigureAwait(false);
                return ConverseSessionEndReason.Quit;
            }

            await HandleAsync(intent, line).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Auto-login: greet, then emit a <see cref="ConversEvent.LocalJoin"/> for the authenticated
    /// callsign on the default channel and render any resulting notices (e.g. the channel topic) to
    /// the user. The user never typed <c>/name</c> (decision 3).
    /// </summary>
    private async Task GreetAndJoinAsync()
    {
        await WriteLineAsync($"[{_config.NodeName} convers] Welcome {_call}.").ConfigureAwait(false);

        IReadOnlyList<ConversAction> actions =
            _hub.Advance(new ConversEvent.LocalJoin(_sessionId, _call, _config.DefaultChannel));

        await WriteLineAsync($"You are on channel {_config.DefaultChannel}.").ConfigureAwait(false);
        await RenderAsync(actions).ConfigureAwait(false);

        await WriteLineAsync(_config.Interface == ConsoleInterface.Classic
            ? "Classic mode. Type /help for commands."
            : "Type 'help' for commands, or just type to chat.").ConfigureAwait(false);
    }

    private ConsoleIntent Parse(string line) => _config.Interface == ConsoleInterface.Classic
        ? ConsoleParser.ParseClassic(line)
        : ConsoleParser.ParsePlain(line);

    /// <summary>Emits the sign-off event upstream + to the channel; output of that fan-out is irrelevant here.</summary>
    private void SignOff(string reason) => _hub.Advance(new ConversEvent.LocalLeave(_sessionId, reason));

    private string SignOffLine(string reason) =>
        reason.Trim().Length == 0
            ? $"73 de {_config.NodeName}"
            : $"73 de {_config.NodeName} ({reason.Trim()})";

    // ---------------------------------------------------------------- per-intent handling

    private async Task HandleAsync(ConsoleIntent intent, string raw)
    {
        switch (intent)
        {
            case ConsoleIntent.Empty:
                return;

            case ConsoleIntent.Say say:
                await AdvanceAndRenderAsync(new ConversEvent.LocalSay(_sessionId, say.Text)).ConfigureAwait(false);
                return;

            case ConsoleIntent.Join join:
                await HandleJoinAsync(join).ConfigureAwait(false);
                return;

            case ConsoleIntent.Msg msg:
                await AdvanceAndRenderAsync(
                    new ConversEvent.LocalPrivateMessage(_sessionId, msg.To, msg.Text)).ConfigureAwait(false);
                return;

            case ConsoleIntent.Topic topic:
                await HandleTopicAsync(topic).ConfigureAwait(false);
                return;

            case ConsoleIntent.Personal personal:
                await AdvanceAndRenderAsync(
                    new ConversEvent.LocalSetPersonal(_sessionId, personal.Text)).ConfigureAwait(false);
                await WriteLineAsync(personal.Text.Trim().Length == 0
                    ? "Personal text cleared."
                    : $"Personal text set: {personal.Text.Trim()}").ConfigureAwait(false);
                return;

            case ConsoleIntent.Away away:
                await AdvanceAndRenderAsync(new ConversEvent.LocalSetAway(_sessionId, away.Text)).ConfigureAwait(false);
                await WriteLineAsync(away.Text.Trim().Length == 0
                    ? "You are back."
                    : $"You are away: {away.Text.Trim()}").ConfigureAwait(false);
                return;

            case ConsoleIntent.Who who:
                await HandleWhoAsync(who).ConfigureAwait(false);
                return;

            case ConsoleIntent.Invite invite:
                await HandleInviteAsync(invite).ConfigureAwait(false);
                return;

            case ConsoleIntent.Leave:
                // Reaches here only when it is not the only channel (handled in the loop otherwise).
                return;

            case ConsoleIntent.Help help:
                await HandleHelpAsync(help).ConfigureAwait(false);
                return;

            case ConsoleIntent.Unknown:
                await WriteLineAsync(UnknownHint(raw)).ConfigureAwait(false);
                return;

            default:
                return;
        }
    }

    private async Task HandleJoinAsync(ConsoleIntent.Join join)
    {
        if (join.Channel is null)
        {
            int current = _hub.GetSession(_sessionId)?.Channel ?? _config.DefaultChannel;
            await WriteLineAsync($"You are on channel {current}.").ConfigureAwait(false);
            return;
        }

        await AdvanceAndRenderAsync(
            new ConversEvent.LocalSwitchChannel(_sessionId, join.Channel.Value)).ConfigureAwait(false);
        await WriteLineAsync($"You are on channel {join.Channel.Value}.").ConfigureAwait(false);
    }

    private async Task HandleTopicAsync(ConsoleIntent.Topic topic)
    {
        if (topic.Text.Trim().Length == 0)
        {
            // A bare topic command queries the current channel's topic.
            int current = _hub.GetSession(_sessionId)?.Channel ?? _config.DefaultChannel;
            Channel channel = _hub.GetChannel(current);
            await WriteLineAsync(channel.Topic.Length == 0
                ? $"No topic set on channel {current}."
                : $"Topic of channel {current}: {channel.Topic}").ConfigureAwait(false);
            return;
        }

        await AdvanceAndRenderAsync(new ConversEvent.LocalSetTopic(_sessionId, topic.Text)).ConfigureAwait(false);
    }

    private async Task HandleInviteAsync(ConsoleIntent.Invite invite)
    {
        int channel = invite.Channel ?? _hub.GetSession(_sessionId)?.Channel ?? _config.DefaultChannel;
        await AdvanceAndRenderAsync(
            new ConversEvent.LocalInvite(_sessionId, invite.User, channel)).ConfigureAwait(false);
        await WriteLineAsync($"Invited {invite.User} to channel {channel}.").ConfigureAwait(false);
    }

    /// <summary>
    /// <c>who</c> is answered from the hub's tables (decision 5), not via an event: a bare <c>who</c>
    /// lists the current channel; <c>who *</c> / <c>who all</c> lists the whole network table.
    /// </summary>
    private async Task HandleWhoAsync(ConsoleIntent.Who who)
    {
        string arg = who.Argument.Trim();
        bool wholeNetwork = arg is "*" or "all" or "ALL" or "n";

        List<string> lines = [];
        if (wholeNetwork)
        {
            lines.Add("Users on the network:");
            foreach (NetworkUser u in _hub.NetworkUsers)
            {
                lines.Add(FormatUser(u));
            }
        }
        else
        {
            int current = _hub.GetSession(_sessionId)?.Channel ?? _config.DefaultChannel;
            Channel channel = _hub.GetChannel(current);
            lines.Add($"Users on channel {current}:");
            foreach (NetworkUser u in channel.Users)
            {
                lines.Add(FormatUser(u));
            }
        }

        if (lines.Count == 1)
        {
            lines.Add("  (nobody)");
        }

        await WritePagedAsync(lines).ConfigureAwait(false);
    }

    private static string FormatUser(NetworkUser u)
    {
        string flags = u.IsAway ? " (away)" : string.Empty;
        string pers = u.Personal.Length == 0 ? string.Empty : $" - {u.Personal}";
        return $"  {u.Name}@{u.Host} ch {u.Channel}{flags}{pers}";
    }

    private async Task HandleHelpAsync(ConsoleIntent.Help help) =>
        await WritePagedAsync(_config.Interface == ConsoleInterface.Classic
            ? ConsoleHelp.Classic(help.Subject)
            : ConsoleHelp.Plain(help.Subject)).ConfigureAwait(false);

    private string UnknownHint(string raw) => _config.Interface == ConsoleInterface.Classic
        ? $"Unknown command. Type /help for a list. ({raw.Trim()})"
        : $"Sorry, I didn't understand that. Type 'help' for commands. ({raw.Trim()})";

    // ---------------------------------------------------------------- hub + rendering plumbing

    private async Task AdvanceAndRenderAsync(ConversEvent @event) =>
        await RenderAsync(_hub.Advance(@event)).ConfigureAwait(false);

    /// <summary>Renders the fan-out actions addressed to this session and writes them, paged.</summary>
    private async Task RenderAsync(IReadOnlyList<ConversAction> actions)
    {
        List<string> lines = ActionRenderer.Render(actions, _sessionId);
        if (lines.Count > 0)
        {
            await WritePagedAsync(lines).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- paging

    /// <summary>
    /// Writes lines through the per-session pager (paclen-friendly, mirroring the BBS pager). A page
    /// length of 0 disables paging (continuous stream). At each page boundary the user may press
    /// <c>A</c>/<c>abort</c> to stop, or any other key (or CR) to continue.
    /// </summary>
    private async Task WritePagedAsync(List<string> lines)
    {
        int pageLength = _config.PageLength;
        int sincePrompt = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            await WriteLineAsync(lines[i]).ConfigureAwait(false);
            sincePrompt++;

            if (pageLength > 0 && sincePrompt >= pageLength && i < lines.Count - 1)
            {
                await WriteLineAsync("<A>bort, <CR> Continue..>").ConfigureAwait(false);
                string response = (await ReadLineAsync().ConfigureAwait(false)).Trim();
                if (response.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                    response.Equals("abort", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLineAsync("Output aborted").ConfigureAwait(false);
                    return;
                }

                sincePrompt = 0;
            }
        }
    }

    private ValueTask WriteLineAsync(string line) => _terminal.WriteAsync(line + "\r", _ct);

    private async ValueTask<string> ReadLineAsync()
    {
        string? line = await _terminal.ReadLineAsync(_ct).ConfigureAwait(false);
        return line ?? throw new ConverseTerminalClosedException();
    }
}
