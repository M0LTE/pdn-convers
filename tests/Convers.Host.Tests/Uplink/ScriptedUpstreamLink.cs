using System.Threading.Channels;
using Convers.Host.Uplink;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// A scripted, in-memory <see cref="IUpstreamLink"/> for the sans-IO HostLink tests — the fake transport
/// design.md asks for ("keep the FSM testable over a scripted/fake transport"). Tests push inbound lines
/// with <see cref="PushLine"/>, end the stream with <see cref="Close"/>, and read what the link sent via
/// <see cref="SentLines"/>. No socket, no real time — fully deterministic.
/// </summary>
public sealed class ScriptedUpstreamLink : IUpstreamLink
{
    private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<string> _sent = Channel.CreateUnbounded<string>();
    private readonly List<string> _sentLog = [];
    private readonly object _gate = new();

    /// <summary>Every line the HostLink has sent (the wire body, terminator stripped), in order.</summary>
    public IReadOnlyList<string> SentLines
    {
        get
        {
            lock (_gate)
            {
                return _sentLog.ToArray();
            }
        }
    }

    /// <summary>True once the link has been disposed (the loop tore it down).</summary>
    public bool Disposed { get; private set; }

    /// <summary>Queue an inbound line for the link to receive (terminator stripped).</summary>
    public void PushLine(string line) => _inbound.Writer.TryWrite(line);

    /// <summary>End the inbound stream — the next <see cref="ReceiveLineAsync"/> returns null (peer hang-up).</summary>
    public void Close() => _inbound.Writer.TryComplete();

    /// <summary>Await the next line the link sends, with a test timeout. Throws on timeout.</summary>
    public async Task<string> ReadSentAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await _sent.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"No line sent within {timeout.TotalSeconds:0.#}s. Sent so far: [{string.Join(" | ", SentLines)}]");
        }
    }

    /// <inheritdoc/>
    public Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sentLog.Add(line);
        }

        _sent.Writer.TryWrite(line);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string?> ReceiveLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _inbound.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _inbound.Writer.TryComplete();
        _sent.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
