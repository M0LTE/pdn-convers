using System.Collections.Concurrent;
using Convers.Core;
using Convers.Host.Uplink;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// An <see cref="ILocalDelivery"/> that records every local-bound action the HostLink hands it, so tests
/// can assert that inbound host commands fanned out to local sessions correctly (the inbound half of the
/// presence bridge).
/// </summary>
public sealed class RecordingLocalDelivery : ILocalDelivery
{
    private readonly ConcurrentQueue<ConversAction> _actions = new();

    /// <summary>Every action delivered locally, in order.</summary>
    public IReadOnlyList<ConversAction> Actions => _actions.ToArray();

    /// <inheritdoc/>
    public void Deliver(ConversAction action) => _actions.Enqueue(action);
}
