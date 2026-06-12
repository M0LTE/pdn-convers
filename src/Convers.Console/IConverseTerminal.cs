namespace Convers.Console;

/// <summary>
/// The session seam between the console engine and whatever carries the bytes (the Host, W5, will
/// implement this over an RHPv2 stream or the web tile; tests implement it as a script). The engine
/// is sans-IO: it only ever awaits lines in and writes text out, and never touches sockets or owns a
/// thread (design decision 2).
///
/// <para>Text discipline: the engine emits CR-terminated lines (the RF side is CR-discipline); the
/// terminal implementation owns any translation to the underlying transport (e.g. CRLF for telnet)
/// and MUST present received text to the engine one line at a time with line terminators stripped.
/// Text is Latin-1-safe: implementations should decode/encode bytes with
/// <see cref="System.Text.Encoding.Latin1"/> (byte-transparent) so 8-bit user text survives end to
/// end.</para>
/// </summary>
public interface IConverseTerminal
{
    /// <summary>
    /// The connected, already-authenticated callsign exactly as it arrived on the connect (the RHP
    /// layer authenticated it — design decision 4). The console auto-logs this in and the user never
    /// types <c>/name</c> (auto-login, design decision 3).
    /// </summary>
    string RemoteCallsign { get; }

    /// <summary>
    /// Awaits the next received line, terminators stripped. Returns <see langword="null"/> when the
    /// remote station disconnected (the engine ends the session as a drop).
    /// </summary>
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes text to the remote station. The engine passes CR-terminated lines; implementations send
    /// them as-is apart from transport-mandated translation.
    /// </summary>
    ValueTask WriteAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown internally (and allowed from <see cref="IConverseTerminal"/> implementations) to signal
/// that the remote station vanished mid-session. The session converts it to
/// <see cref="ConverseSessionEndReason.Drop"/>.
/// </summary>
public sealed class ConverseTerminalClosedException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ConverseTerminalClosedException()
        : base("The remote station disconnected.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public ConverseTerminalClosedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ConverseTerminalClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
