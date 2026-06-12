namespace Convers.Protocol;

/// <summary>
/// The convers '/'-grammar prefixes (see <c>reference/SPECS.txt</c> and <c>user.c</c>/<c>host.c</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The host-command prefix on the wire is three bytes:</b> <c>'/'</c> (0x2F) followed by
/// <c>0xFF</c> and <c>0x80</c> — see <c>conversd.c</c>'s command table (<c>{"\377\200user", ...}</c>)
/// and the dispatcher, which matches <c>/\377\200VERB</c>. <c>doc/SPECS</c> writes this as <c>/..</c>
/// because the two high bytes print as dots. The captured oracle handshake is byte-exact:
/// <c>2f ff 80 48 4f 53 54 ...</c> (<c>/..HOST ...</c>). <see cref="HostCommandPrefix"/> holds those
/// three characters (the 0xFF/0x80 bytes round-trip through Latin-1 as U+00FF/U+0080).
/// </para>
/// <para>
/// The documentation <see cref="DottedHostCommandPrefix"/> string (<c>"/.."</c>) is kept for the W0
/// seed's tests and for human-readable text, but it is NOT what travels on the wire.
/// </para>
/// </remarks>
public static class ConversCommand
{
    /// <summary>Every command is introduced by a forward slash.</summary>
    public const string CommandPrefix = "/";

    /// <summary>The high byte (0xFF) that begins the host-command marker after the leading slash.</summary>
    public const char HostMarkerByte1 = '\u00FF';

    /// <summary>The high byte (0x80) that completes the host-command marker.</summary>
    public const char HostMarkerByte2 = '\u0080';

    /// <summary>
    /// The literal on-the-wire host-command prefix: <c>'/'</c> + <c>0xFF</c> + <c>0x80</c> (three bytes,
    /// rendered <c>/..</c> in <c>doc/SPECS</c>). This is what <see cref="IsHostCommand"/> tests and what
    /// the codec emits.
    /// </summary>
    public const string HostCommandPrefix = "/\u00FF\u0080";

    /// <summary>
    /// The documentation/seed spelling of the host prefix (<c>"/.."</c>). Used in human-facing text and
    /// by the W0 seed tests. The wire never carries literal dots here — see <see cref="HostCommandPrefix"/>.
    /// </summary>
    public const string DottedHostCommandPrefix = "/..";

    /// <summary>
    /// True when <paramref name="line"/> is a host-to-host command — i.e. it begins with the real
    /// three-byte host prefix (<c>/</c> 0xFF 0x80) OR the documentation spelling <c>/..</c>. Accepting
    /// both keeps the W0 seed's golden-rule examples (written with dots) valid while matching real wire
    /// bytes. A leaf bridges these between its single uplink and local users.
    /// </summary>
    public static bool IsHostCommand(string? line) =>
        line is not null &&
        (line.StartsWith(HostCommandPrefix, StringComparison.Ordinal) ||
         line.StartsWith(DottedHostCommandPrefix, StringComparison.Ordinal));

    /// <summary>
    /// True when <paramref name="line"/> begins a command (starts with <c>/</c>) — as opposed to
    /// ordinary chat text destined for the current channel.
    /// </summary>
    public static bool IsCommand(string? line) =>
        line is not null && line.StartsWith(CommandPrefix, StringComparison.Ordinal);
}
