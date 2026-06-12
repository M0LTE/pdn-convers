namespace Convers.Protocol;

/// <summary>
/// The four connection states a convers link may occupy (see <c>reference/SPECS.txt</c> and the
/// <c>CT_*</c> constants in <c>conversd.h</c>/<c>conversd.c</c>).
/// </summary>
/// <remarks>
/// A connection starts in <see cref="Unknown"/>. From there four commands are recognised:
/// <c>/NAME</c> (→ <see cref="User"/>), <c>/..HOST</c> (→ <see cref="Host"/>), and the read-only
/// <c>/ONLINE</c>/<c>/CSTAT</c> (which run without login). The <see cref="Observer"/> state is a
/// subset of <see cref="User"/> that may only issue commands producing no output to others; a peer
/// announces observers via the <c>/..OBSV</c> presence command.
/// </remarks>
public enum ConnectionState
{
    /// <summary>Just connected; not yet logged in as a user nor handshaken as a host.</summary>
    Unknown = 0,

    /// <summary>A logged-in human user (entered via <c>/NAME</c>).</summary>
    User = 1,

    /// <summary>An observer — a read-only user subset (no output to others).</summary>
    Observer = 2,

    /// <summary>A peer node link (entered via the <c>/..HOST</c> handshake).</summary>
    Host = 3,
}
