using System.Text;
using Convers.Core;
using Microsoft.Extensions.Time.Testing;

namespace Convers.Console.Tests;

/// <summary>
/// The scripted terminal fake: a fixed queue of input lines, all output captured. When the script
/// runs out the terminal reports a drop (ReadLineAsync → null), so a script that doesn't end in
/// quit/leave ends the session as <see cref="ConverseSessionEndReason.Drop"/>. Mirrors the pdn-bbs
/// <c>ScriptedTerminal</c> idiom.
/// </summary>
internal sealed class ScriptedTerminal : IConverseTerminal
{
    private readonly Queue<string> _input;
    private readonly StringBuilder _output = new();

    public ScriptedTerminal(string remoteCallsign, IEnumerable<string> lines)
    {
        RemoteCallsign = remoteCallsign;
        _input = new Queue<string>(lines);
    }

    public string RemoteCallsign { get; }

    public string Output => _output.ToString();

    /// <summary>Output split into CR-discipline lines (terminators folded), trailing empty dropped.</summary>
    public string[] OutputLines
    {
        get
        {
            string[] parts = _output.ToString()
                .Replace("\r\n", "\r", StringComparison.Ordinal)
                .Split('\r');
            return parts;
        }
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        new(_input.Count > 0 ? _input.Dequeue() : null);

    public ValueTask WriteAsync(string text, CancellationToken cancellationToken)
    {
        _output.Append(text);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A console-session harness: one in-memory <see cref="ConversHub"/> on a fake clock (no store), plus
/// helpers to run a scripted session and to attach extra local sessions / network users so a session
/// has someone to chat with.
/// </summary>
internal sealed class ConsoleHarness
{
    public const string NodeName = "GB7PDN";

    public ConsoleHarness(int defaultChannel = 3333, int pageLength = 20, ConsoleInterface @interface = ConsoleInterface.Plain)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero));
        Hub = new ConversHub(NodeName, Time);
        Config = new ConverseConsoleConfig
        {
            NodeName = NodeName,
            DefaultChannel = defaultChannel,
            PageLength = pageLength,
            Interface = @interface,
        };
    }

    public FakeTimeProvider Time { get; }

    public ConversHub Hub { get; }

    public ConverseConsoleConfig Config { get; set; }

    /// <summary>Attaches another local user directly on the hub (so the session under test has a peer).</summary>
    public void AddLocalUser(string sessionId, string callsign, int channel) =>
        Hub.Advance(new ConversEvent.LocalJoin(sessionId, callsign, channel));

    /// <summary>Brings a remote network user into existence via a host presence event.</summary>
    public void AddRemoteUser(string call, string host, int channel, string personal = "@", bool away = false)
    {
        Hub.Advance(new ConversEvent.HostUser(
            call, host, Time.GetUtcNow(), -1, channel, personal));
        if (away)
        {
            Hub.Advance(new ConversEvent.HostAway(call, host, Time.GetUtcNow(), "out to lunch"));
        }
    }

    /// <summary>Runs one scripted session to completion; returns the end reason and the terminal.</summary>
    public async Task<(ConverseSessionEndReason End, ScriptedTerminal Terminal)> RunAsync(
        string caller, params string[] lines)
    {
        var terminal = new ScriptedTerminal(caller, lines);
        ConverseSessionEndReason end = await ConverseConsoleSession.RunAsync(
            terminal, Hub, Config, SessionIdFor(caller), CancellationToken.None);
        return (end, terminal);
    }

    public static string SessionIdFor(string caller) => $"sess-{caller}";
}
