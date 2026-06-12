using System.Threading.Channels;
using Convers.Core;
using Convers.Protocol;
using Microsoft.Extensions.Logging;

namespace Convers.Host.Uplink;

/// <summary>
/// The resilient upstream attachment to the single convers parent node (design.md src/Convers.Host: "the
/// single UPSTREAM host link … speaking /..HOST with reconnect/backoff + PING/PONG keepalive"). It owns
/// one <see cref="IUpstreamLink"/> at a time — dialed via an <see cref="IUpstreamLinkFactory"/> — runs the
/// <see cref="HostLinkEngine"/> over it, bridges presence both ways through the <see cref="ConversHub"/>,
/// and reconnects with TimeProvider-driven exponential backoff on any loss, re-running the handshake and
/// re-announcing local presence each time (mirroring pdn-bbs <c>RhpNodeLink</c>).
/// </summary>
/// <remarks>
/// <para>
/// The engine is sans-IO; this class is the only I/O. Per loop iteration it: dials a fresh link, drives
/// <see cref="HostLinkEngine.OnConnected"/>, then pumps inbound lines and time ticks through the engine,
/// applying each <see cref="EngineStep"/> — sending the engine's outbound commands, applying its hub
/// events, forwarding the hub's resulting uplink actions back up and its local actions to the
/// <see cref="ILocalDelivery"/> sink. On a dropped link (null receive, a drop decision, or an exception)
/// every in-flight wait faults and the loop backs off and redials.
/// </para>
/// <para>
/// Local-originated activity (an RF/web user joining, speaking, leaving) enters via
/// <see cref="SubmitLocalEventAsync"/>; the hub turns it into uplink actions this link sends, and into
/// local actions for the other local sessions. The inbound demux (W5) feeds those events.
/// </para>
/// </remarks>
public sealed class HostLink : IAsyncDisposable
{
    private readonly HostLinkOptions _options;
    private readonly IUpstreamLinkFactory _factory;
    private readonly ConversHub _hub;
    private readonly ILocalDelivery _local;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _tickInterval;

    // Local events queued by the demux (W5), drained on the link's owning loop so the hub stays single-threaded.
    private readonly System.Threading.Channels.Channel<ConversEvent> _localEvents =
        System.Threading.Channels.Channel.CreateUnbounded<ConversEvent>(
            new UnboundedChannelOptions { SingleReader = true });

    private volatile IUpstreamLink? _link;
    private volatile TaskCompletionSource _up = NewTcs();

    /// <summary>Creates the link. Call <see cref="RunAsync"/> to start the connect/reconnect loop.</summary>
    public HostLink(
        HostLinkOptions options,
        IUpstreamLinkFactory factory,
        ConversHub hub,
        TimeProvider timeProvider,
        ILogger<HostLink> logger,
        ILocalDelivery? localDelivery = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Validate();
        _factory = factory;
        _hub = hub;
        _local = localDelivery ?? NullLocalDelivery.Instance;
        _time = timeProvider;
        _logger = logger;
        // Tick a few times per ping interval so keepalive/silence fire promptly without busy-waiting.
        _tickInterval = TimeSpan.FromMilliseconds(
            Math.Max(50, _options.PingInterval.TotalMilliseconds / 4));
    }

    /// <summary>Whether the uplink is currently established (handshake complete).</summary>
    public bool IsUp => _up.Task.IsCompletedSuccessfully;

    /// <summary>Completes when the uplink reaches the established state (a fresh wait per outage).</summary>
    public Task WaitForUpAsync(CancellationToken cancellationToken) => _up.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Submit a local-originated domain event (an RF/web user's join/say/leave …). It is applied to the
    /// hub on the link's owning loop; the hub's uplink-bound actions are sent to the parent and its
    /// local-bound actions delivered to the <see cref="ILocalDelivery"/> sink. Queued even while the link
    /// is down (the hub still notifies other local sessions); uplink-bound actions are best-effort when
    /// the link is up.
    /// </summary>
    public ValueTask SubmitLocalEventAsync(ConversEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return _localEvents.Writer.WriteAsync(@event, cancellationToken);
    }

    /// <summary>The connect → handshake → keepalive → reconnect loop; runs until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan backoff = _options.InitialBackoff;
        while (!cancellationToken.IsCancellationRequested)
        {
            IUpstreamLink? link = null;
            try
            {
                link = await _factory.ConnectAsync(cancellationToken).ConfigureAwait(false);
                _link = link;

                bool clean = await RunOneConnectionAsync(link, cancellationToken).ConfigureAwait(false);
                if (clean)
                {
                    // A successful connection means the next failure starts the backoff fresh.
                    backoff = _options.InitialBackoff;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogConnectFailed(_logger, ex.Message, null);
            }
            finally
            {
                MarkDown();
                _link = null;
                if (link is not null)
                {
                    await link.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            LogReconnectWait(_logger, backoff.TotalSeconds, null);
            try
            {
                await Task.Delay(backoff, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = backoff * 2 > _options.MaxBackoff ? _options.MaxBackoff : backoff * 2;
        }
    }

    /// <summary>
    /// Drives one connection from handshake to loss. Returns true if the connection reached the established
    /// state (so the caller resets backoff), false if it dropped during the handshake.
    /// </summary>
    private async Task<bool> RunOneConnectionAsync(IUpstreamLink link, CancellationToken cancellationToken)
    {
        var engine = new HostLinkEngine(_options, _time);
        bool everEstablished = false;

        await ApplyStepAsync(link, engine.OnConnected(), cancellationToken).ConfigureAwait(false);

        // The owning loop: race the next inbound line, a queued local event, and the tick timer. Whichever
        // wins, advance the engine / hub and carry out the resulting step. A null inbound line ends it.
        Task<string?> receive = link.ReceiveLineAsync(cancellationToken);
        Task<bool> localWait = _localEvents.Reader.WaitToReadAsync(cancellationToken).AsTask();
        using var ticker = new PeriodicTimer(_tickInterval, _time);
        Task<bool> tick = ticker.WaitForNextTickAsync(cancellationToken).AsTask();

        while (true)
        {
            Task completed = await Task.WhenAny(receive, localWait, tick).ConfigureAwait(false);

            if (completed == receive)
            {
                string? line = await receive.ConfigureAwait(false);
                if (line is null)
                {
                    LogPeerClosed(_logger, null);
                    return everEstablished;
                }

                EngineStep step = engine.OnLineReceived(line);
                everEstablished |= step.HandshakeCompleted;

                if (!await ApplyStepAsync(link, step, cancellationToken).ConfigureAwait(false))
                {
                    return everEstablished; // engine asked to drop
                }

                if (step.HandshakeCompleted)
                {
                    await OnEstablishedAsync(link, engine, cancellationToken).ConfigureAwait(false);
                }

                receive = link.ReceiveLineAsync(cancellationToken);
            }
            else if (completed == localWait)
            {
                if (await localWait.ConfigureAwait(false))
                {
                    await DrainLocalEventsAsync(link, cancellationToken).ConfigureAwait(false);
                }

                localWait = _localEvents.Reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            else
            {
                if (!await tick.ConfigureAwait(false))
                {
                    return everEstablished; // timer cancelled
                }

                EngineStep step = engine.OnTick();
                if (!await ApplyStepAsync(link, step, cancellationToken).ConfigureAwait(false))
                {
                    return everEstablished;
                }

                tick = ticker.WaitForNextTickAsync(cancellationToken).AsTask();
            }
        }
    }

    /// <summary>
    /// Carries out an <see cref="EngineStep"/>: send its outbound commands, apply its hub events (each
    /// yielding further actions to dispatch). Returns false when the step asked to drop the link.
    /// </summary>
    private async Task<bool> ApplyStepAsync(IUpstreamLink link, EngineStep step, CancellationToken cancellationToken)
    {
        foreach (HostCommand command in step.OutboundCommands)
        {
            await SendCommandAsync(link, command, cancellationToken).ConfigureAwait(false);
        }

        foreach (ConversEvent @event in step.HubEvents)
        {
            await DispatchActionsAsync(link, _hub.Advance(@event), cancellationToken).ConfigureAwait(false);
        }

        if (step.DropReason is { } reason)
        {
            LogDropping(_logger, reason, null);
            return false;
        }

        return true;
    }

    private async Task DrainLocalEventsAsync(IUpstreamLink link, CancellationToken cancellationToken)
    {
        while (_localEvents.Reader.TryRead(out ConversEvent? @event))
        {
            await DispatchActionsAsync(link, _hub.Advance(@event), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Routes the hub's fan-out: <c>Send*</c>/<c>SendPong</c> go up the wire, <c>DropUplink</c> is honoured
    /// by reading it (the engine independently drops on /..LOOP), and every <c>Deliver*</c> goes to the
    /// local sink.
    /// </summary>
    private async Task DispatchActionsAsync(
        IUpstreamLink link, IReadOnlyList<ConversAction> actions, CancellationToken cancellationToken)
    {
        foreach (ConversAction action in actions)
        {
            if (action is ConversAction.DropUplink)
            {
                // The engine already drops the transport on /..LOOP; nothing to send here.
                continue;
            }

            HostCommand? command = HostBridge.ToHostCommand(action);
            if (command is not null)
            {
                await SendCommandAsync(link, command, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _local.Deliver(action);
            }
        }
    }

    private static async Task SendCommandAsync(IUpstreamLink link, HostCommand command, CancellationToken cancellationToken)
    {
        string line = HostCommandCodec.Format(command);
        await link.SendLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// On handshake completion, mark the link up and re-announce current presence upstream so a reconnect
    /// restores the parent's view of our local users (live presence is rebuilt from the uplink — design
    /// decision 7). We replay every local user as a fresh <c>/..USER</c> join, plus their personal text and
    /// away state. Runs on the owning loop, so the sends are ordered and single-threaded.
    /// </summary>
    private async Task OnEstablishedAsync(IUpstreamLink link, HostLinkEngine engine, CancellationToken cancellationToken)
    {
        MarkUp();
        LogEstablished(_logger, engine.PeerHostName, FacilitiesCodec.Format(engine.NegotiatedFacilities), null);

        foreach (NetworkUser user in _hub.NetworkUsers)
        {
            // Only our own local users (those on our host name) are ours to announce.
            if (!string.Equals(user.Host, _hub.HostName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long joined = HostBridge.ToUnix(user.JoinedAt);
            await SendCommandAsync(
                link, new HostUser(user.Name, user.Host, joined, -1, user.Channel, user.Personal), cancellationToken)
                .ConfigureAwait(false);
            if (user.Personal.Length != 0)
            {
                await SendCommandAsync(link, new HostUserData(user.Name, user.Host, user.Personal), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (user.Away.Length != 0)
            {
                await SendCommandAsync(link, new HostAway(user.Name, user.Host, joined, user.Away), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        IUpstreamLink? link = _link;
        _link = null;
        MarkDown();
        _localEvents.Writer.TryComplete();
        if (link is not null)
        {
            await link.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void MarkUp() => _up.TrySetResult();

    private void MarkDown()
    {
        if (_up.Task.IsCompleted)
        {
            _up = NewTcs();
        }
    }

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly Action<ILogger, string, Exception?> LogConnectFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "UplinkConnectFailed"),
            "Upstream connect failed: {Reason}");

    private static readonly Action<ILogger, double, Exception?> LogReconnectWait =
        LoggerMessage.Define<double>(LogLevel.Information, new EventId(2, "UplinkReconnectWait"),
            "Reconnecting to parent in {Seconds}s");

    private static readonly Action<ILogger, string, string, Exception?> LogEstablished =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(3, "UplinkEstablished"),
            "Uplink established to {Peer} (facilities {Facilities})");

    private static readonly Action<ILogger, string, Exception?> LogDropping =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, "UplinkDropping"),
            "Dropping uplink: {Reason}");

    private static readonly Action<ILogger, Exception?> LogPeerClosed =
        LoggerMessage.Define(LogLevel.Information, new EventId(5, "UplinkPeerClosed"),
            "Parent closed the uplink");
}
