namespace Convers.Protocol;

/// <summary>
/// The host-link facility flags negotiated in the third field of the <c>/..HOST</c> handshake
/// (<c>/..HOST &lt;hostname&gt; [software [facilities]]</c>). The facility string is a run of single
/// letters; see <c>reference/SPECS.txt</c> and the parse in <c>host.c</c> (<c>strchr(features, 'x')</c>
/// per flag). Captured from the oracle: <c>/..HOST ORACLE saupp1.62a Aadmpunfi</c>.
/// </summary>
[Flags]
public enum Facilities
{
    /// <summary>No facilities advertised.</summary>
    None = 0,

    /// <summary>
    /// <c>a</c> — "away feature", old style (SPECS notes it expired Dec 31 1995, but the letter is still
    /// recognised). Old-style away when <c>a</c> present and <c>A</c> absent.
    /// </summary>
    AwayOld = 1 << 0,

    /// <summary><c>A</c> — "away feature", new style.</summary>
    AwayNew = 1 << 1,

    /// <summary><c>d</c> — "destination forwarding".</summary>
    DestinationForwarding = 1 << 2,

    /// <summary><c>m</c> — "channel modes".</summary>
    ChannelModes = 1 << 3,

    /// <summary><c>p</c> — "ping pong link measurement".</summary>
    PingPong = 1 << 4,

    /// <summary><c>u</c> — "udat command extension and user command understood both" (amprnet ext).</summary>
    Udat = 1 << 5,

    /// <summary><c>n</c> — "TNOS-style nickname support".</summary>
    Nicknames = 1 << 6,

    /// <summary><c>f</c> — filter extension (saupp; not in the original SPECS letter list).</summary>
    Filter = 1 << 7,

    /// <summary><c>i</c> — saupp-internal extension (saupp; not in the original SPECS letter list).</summary>
    SauppInternal = 1 << 8,
}

/// <summary>
/// Parse/format helpers for the <see cref="Facilities"/> letter string used in the <c>/..HOST</c>
/// handshake. Round-trips the canonical letter set; unknown letters are ignored on parse (matching
/// <c>host.c</c>, which only tests for the letters it knows).
/// </summary>
public static class FacilitiesCodec
{
    // Canonical emit order, chosen to match the oracle's own ordering: the away letters first
    // (A new-style then a old-style), then d m p u n f i. See host.c's myfeatures assembly.
    private static readonly (Facilities Flag, char Letter)[] Map =
    {
        (Facilities.AwayNew, 'A'),
        (Facilities.AwayOld, 'a'),
        (Facilities.DestinationForwarding, 'd'),
        (Facilities.ChannelModes, 'm'),
        (Facilities.PingPong, 'p'),
        (Facilities.Udat, 'u'),
        (Facilities.Nicknames, 'n'),
        (Facilities.Filter, 'f'),
        (Facilities.SauppInternal, 'i'),
    };

    /// <summary>
    /// Parse a facility letter string (e.g. <c>"Aadmpunfi"</c>) into flags. Each recognised letter sets
    /// its flag; unrecognised letters are ignored. Null or empty yields <see cref="Facilities.None"/>.
    /// </summary>
    public static Facilities Parse(string? facilities)
    {
        if (string.IsNullOrEmpty(facilities))
        {
            return Facilities.None;
        }

        Facilities result = Facilities.None;
        foreach (char c in facilities)
        {
            foreach ((Facilities flag, char letter) in Map)
            {
                if (c == letter)
                {
                    result |= flag;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Format flags back into a facility letter string in the canonical order
    /// (<c>A a d m p u n f i</c>). <see cref="Facilities.None"/> yields the empty string.
    /// </summary>
    public static string Format(Facilities facilities)
    {
        if (facilities == Facilities.None)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(Map.Length);
        foreach ((Facilities flag, char letter) in Map)
        {
            if (facilities.HasFlag(flag))
            {
                sb.Append(letter);
            }
        }

        return sb.ToString();
    }
}
