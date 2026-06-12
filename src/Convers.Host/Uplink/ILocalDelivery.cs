using Convers.Core;

namespace Convers.Host.Uplink;

/// <summary>
/// The sink for hub actions bound for <em>local</em> sessions (RF and web users) — the half of the bridge
/// the uplink does not own. As the <see cref="HostLink"/> applies inbound host commands to the hub, the
/// hub fans out <c>Deliver*</c> actions for local listeners; the link hands those here. W5's inbound demux
/// implements this against live RF/web sessions; W4 ships a recording implementation for tests and a
/// no-op default so the link runs standalone.
/// </summary>
public interface ILocalDelivery
{
    /// <summary>
    /// Deliver one local-bound action (a <c>DeliverChannelMessage</c>, <c>DeliverPrivateMessage</c>,
    /// <c>DeliverJoinNotice</c>, …). Implementations route it to the addressed session. Uplink-bound and
    /// link-control actions never reach here — the <see cref="HostLink"/> filters them out.
    /// </summary>
    void Deliver(ConversAction action);
}

/// <summary>A no-op <see cref="ILocalDelivery"/> — the default when no local sink is wired (W4 standalone).</summary>
public sealed class NullLocalDelivery : ILocalDelivery
{
    /// <summary>The shared instance.</summary>
    public static readonly NullLocalDelivery Instance = new();

    private NullLocalDelivery()
    {
    }

    /// <inheritdoc/>
    public void Deliver(ConversAction action)
    {
        // intentionally nothing
    }
}
