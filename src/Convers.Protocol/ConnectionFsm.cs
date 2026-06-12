namespace Convers.Protocol;

/// <summary>
/// The pure connection-state transition function for a convers link, for BOTH roles. A connection starts
/// in <see cref="ConnectionState.Unknown"/>; from there four inputs change state (see
/// <c>reference/SPECS.txt</c> "State UNKNOWN" and conversd's <c>cmdtable</c> state masks):
/// <list type="bullet">
///   <item><c>/NAME</c> (a <see cref="NameCommand"/> with a non-empty call) → <see cref="ConnectionState.User"/>;</item>
///   <item><c>/OBSERVER</c> (an <see cref="ObserverCommand"/>) → <see cref="ConnectionState.Observer"/>;</item>
///   <item><c>/..HOST</c> (a <see cref="HostHandshake"/>) → <see cref="ConnectionState.Host"/>;</item>
///   <item><c>/ONLINE</c> and <c>/CSTAT</c> run in UNKNOWN without changing state (no login needed).</item>
/// </list>
/// Sans-IO and side-effect-free: it takes the current state and one parsed input and returns the next
/// state. It does not validate auth/passwords (that is the Host's concern, W4/W5) — it models the wire's
/// state machine only.
/// </summary>
public static class ConnectionFsm
{
    /// <summary>
    /// Compute the next state given the current <paramref name="state"/> and a parsed
    /// <see cref="UserCommand"/>. Only meaningful from <see cref="ConnectionState.Unknown"/>; once a
    /// connection is a USER/OBSERVER/HOST, user-surface commands do not change its state here. A login
    /// (<see cref="NameCommand"/>) with an empty call does not transition (conversd refuses it).
    /// </summary>
    public static ConnectionState Next(ConnectionState state, UserCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (state != ConnectionState.Unknown)
        {
            return state;
        }

        return command switch
        {
            NameCommand { Call.Length: > 0 } => ConnectionState.User,
            ObserverCommand => ConnectionState.Observer,
            _ => state,
        };
    }

    /// <summary>
    /// Compute the next state given the current <paramref name="state"/> and a parsed
    /// <see cref="HostCommand"/>. A <see cref="HostHandshake"/> from <see cref="ConnectionState.Unknown"/>
    /// promotes the connection to <see cref="ConnectionState.Host"/>; once a HOST, the state is stable.
    /// Other host commands are only valid in <see cref="ConnectionState.Host"/> and never change state.
    /// </summary>
    public static ConnectionState Next(ConnectionState state, HostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (state == ConnectionState.Unknown && command is HostHandshake { Hostname.Length: > 0 })
        {
            return ConnectionState.Host;
        }

        return state;
    }

    /// <summary>
    /// True when a host command verb is accepted in the given <paramref name="state"/>. The
    /// <c>/..HOST</c> handshake is the only host command valid from <see cref="ConnectionState.Unknown"/>;
    /// all other host commands require <see cref="ConnectionState.Host"/> (conversd's <c>CM_HOST</c> mask,
    /// with <c>/..HOST</c> masked <c>CM_UNKNOWN</c>).
    /// </summary>
    public static bool AcceptsHostCommand(ConnectionState state, HostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is HostHandshake)
        {
            return state is ConnectionState.Unknown or ConnectionState.Host;
        }

        return state == ConnectionState.Host;
    }
}
