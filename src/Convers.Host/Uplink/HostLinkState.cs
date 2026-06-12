namespace Convers.Host.Uplink;

/// <summary>
/// The states of the upstream HostLink FSM (we dial the parent). A leaf has exactly one uplink, so this
/// is the whole link lifecycle: dial → send <c>/..HOST</c> → await the parent's reply → steady state,
/// then back to <see cref="Disconnected"/> on loss (which triggers reconnect with backoff).
/// </summary>
public enum HostLinkState
{
    /// <summary>No transport; not dialed (or just torn down). The driver dials and moves to <see cref="Handshaking"/>.</summary>
    Disconnected = 0,

    /// <summary>
    /// Transport is up and our <c>/..HOST</c> has been sent; we are awaiting the parent's <c>/..HOST</c>
    /// reply (and negotiating facilities from it). Inbound non-handshake lines before the reply are
    /// tolerated but the link is not yet "up".
    /// </summary>
    Handshaking = 1,

    /// <summary>
    /// Handshake complete: facilities negotiated, presence is being bridged both ways, and PING/PONG
    /// keepalive holds the link. The steady state.
    /// </summary>
    Established = 2,
}
