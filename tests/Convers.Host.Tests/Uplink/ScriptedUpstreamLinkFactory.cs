using System.Collections.Concurrent;
using Convers.Host.Uplink;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// A scripted <see cref="IUpstreamLinkFactory"/> that hands the HostLink a pre-seeded sequence of
/// connection attempts. Each attempt is either a <see cref="ScriptedUpstreamLink"/> to return or an
/// exception to throw (a failed dial) — letting reconnect/backoff tests drive a whole outage sequence
/// deterministically. After the script is exhausted it blocks (parks) so the loop simply waits for the
/// next attempt without spinning, until the test cancels it.
/// </summary>
public sealed class ScriptedUpstreamLinkFactory : IUpstreamLinkFactory
{
    private readonly ConcurrentQueue<Func<IUpstreamLink>> _script = new();
    private readonly TaskCompletionSource<bool> _exhausted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The number of times <see cref="ConnectAsync"/> has been called (dial attempts).</summary>
    public int ConnectAttempts { get; private set; }

    /// <summary>Completes once every scripted attempt has been consumed.</summary>
    public Task Exhausted => _exhausted.Task;

    /// <summary>Seed the next attempt to succeed, returning <paramref name="link"/>.</summary>
    public ScriptedUpstreamLinkFactory EnqueueLink(ScriptedUpstreamLink link)
    {
        _script.Enqueue(() => link);
        return this;
    }

    /// <summary>Seed the next attempt to fail the dial with <paramref name="error"/> (a connect failure).</summary>
    public ScriptedUpstreamLinkFactory EnqueueFailure(Exception error)
    {
        _script.Enqueue(() => throw error);
        return this;
    }

    /// <inheritdoc/>
    public Task<IUpstreamLink> ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectAttempts++;
        if (_script.TryDequeue(out Func<IUpstreamLink>? next))
        {
            if (_script.IsEmpty)
            {
                _exhausted.TrySetResult(true);
            }

            try
            {
                return Task.FromResult(next());
            }
            catch (Exception ex)
            {
                return Task.FromException<IUpstreamLink>(ex);
            }
        }

        // Script exhausted: park until cancelled so the loop is quiescent rather than busy-dialing.
        _exhausted.TrySetResult(true);
        return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<IUpstreamLink>(
            _ => throw new OperationCanceledException(cancellationToken),
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
