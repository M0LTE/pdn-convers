using Convers.Core;
using Convers.Protocol;

namespace Convers.Host.Uplink;

/// <summary>
/// The pure result of one <see cref="HostLinkEngine"/> step: what the (I/O-owning) driver must do. The
/// engine never touches a socket or the hub — it returns this and the <see cref="HostLink"/> carries it
/// out, which is what makes the FSM testable over a scripted transport.
/// </summary>
/// <remarks>
/// Ordering matters: <see cref="OutboundCommands"/> are sent first (e.g. the handshake, or a keepalive
/// <c>/..PING</c>), then <see cref="HubEvents"/> are applied to the hub in order (each may yield its own
/// uplink-bound actions the driver then sends). On <see cref="DropReason"/> being set, the driver tears
/// the transport down and reconnects, after sending any <see cref="OutboundCommands"/> in the same step.
/// </remarks>
public sealed record EngineStep
{
    /// <summary>An empty step (no work) — the common case for a benign tick.</summary>
    public static readonly EngineStep None = new();

    /// <summary>Wire commands the link itself originates, in send order (handshake, keepalive ping).</summary>
    public IReadOnlyList<HostCommand> OutboundCommands { get; init; } = [];

    /// <summary>Hub events to apply (inbound presence/messages), in order. Each may fan out further actions.</summary>
    public IReadOnlyList<ConversEvent> HubEvents { get; init; } = [];

    /// <summary>
    /// True on the step where the parent's <c>/..HOST</c> reply completed the handshake. The driver
    /// replays current presence upstream when this is set (re-announce local users after a reconnect).
    /// </summary>
    public bool HandshakeCompleted { get; init; }

    /// <summary>The facilities the parent advertised in its handshake reply (only meaningful when
    /// <see cref="HandshakeCompleted"/>).</summary>
    public Facilities NegotiatedFacilities { get; init; } = Facilities.None;

    /// <summary>
    /// Non-null when the engine has decided the link must be dropped (silence timeout, a <c>/..LOOP</c>,
    /// a handshake timeout, or a protocol fault). The driver tears down and reconnects with backoff.
    /// </summary>
    public string? DropReason { get; init; }

    /// <summary>True when this step carries no work at all.</summary>
    public bool IsEmpty =>
        OutboundCommands.Count == 0 && HubEvents.Count == 0 && !HandshakeCompleted && DropReason is null;
}
