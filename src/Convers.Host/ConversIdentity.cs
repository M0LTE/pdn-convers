namespace Convers.Host;

/// <summary>
/// Resolves the convers node's on-air callsign. The <b>node-owned-callsign contract</b> is the primary
/// path: the pdn node host is the callsign authority and injects <c>PDN_APP_CALLSIGN</c> — the exact
/// callsign this app must bind on air, with uniqueness already guaranteed by the node. When that env var
/// is set the app binds it verbatim and <em>skips</em> the SSID probe-walk entirely.
/// </summary>
/// <remarks>
/// <para>
/// The legacy convention is the fallback for a standalone run or an older node that does not inject
/// <c>PDN_APP_CALLSIGN</c>: pdn's older rule (packet.net <c>AppServiceSupervisor</c>; the DAPPS
/// precedent) is that <b>an app lives at an SSID of the node callsign</b> — the supervisor injects
/// <c>PDN_NODE_CALLSIGN</c>, and the app derives <c>&lt;node-base&gt;-&lt;ssid&gt;</c> automatically
/// (DAPPS used <c>&lt;nodecall&gt;-7</c>; convers uses 4). An explicit config override still wins,
/// verbatim. On that fallback path the clash handling — <b>probe-walking to the next free SSID on a
/// duplicate-socket refusal</b> at RHP bind time — is owned by the RHP bind loop; this resolver only
/// picks the starting SSID and signals whether the walk applies. Pure string logic, no I/O.
/// </para>
/// </remarks>
public static class ConversIdentity
{
    /// <summary>Default preferred SSID for the auto-derived callsign (DAPPS took 7; convers uses 4).</summary>
    public const int DefaultSsid = 4;

    /// <summary>The base callsign used when neither an override nor a node callsign is available.</summary>
    public const string PlaceholderBase = "N0CALL";

    /// <summary>
    /// Resolves the effective callsign and how to bind it, honouring the node-owned-callsign contract.
    /// <list type="bullet">
    /// <item>A non-blank <paramref name="appCallsign"/> (the node-injected <c>PDN_APP_CALLSIGN</c>) wins
    /// above all: it is bound <b>verbatim</b> (normalised) with <c>ExactBind</c> = <see langword="true"/>,
    /// so the RHP bind path skips the SSID probe-walk (the node already guarantees uniqueness).</item>
    /// <item>Otherwise this falls back to <see cref="ResolvePreferred"/> (config override, then
    /// <c>&lt;node-base&gt;-&lt;ssid&gt;</c>, then a placeholder) with <c>ExactBind</c> =
    /// <see langword="false"/> — the RHP bind path probe-walks the SSID on a clash as before.</item>
    /// </list>
    /// </summary>
    public static (string Callsign, bool IsPlaceholder, bool ExactBind) ResolveBinding(
        string? appCallsign, string? overrideCallsign, string? nodeCallsign, int ssid)
    {
        if (!string.IsNullOrWhiteSpace(appCallsign))
        {
            // The node owns the callsign and guarantees uniqueness — bind it exactly, no SSID walk.
            return (Normalise(appCallsign), false, true);
        }

        (string callsign, bool isPlaceholder) = ResolvePreferred(overrideCallsign, nodeCallsign, ssid);
        return (callsign, isPlaceholder, false);
    }

    /// <summary>
    /// Resolves the <em>preferred</em> callsign on the legacy fallback path (no <c>PDN_APP_CALLSIGN</c>).
    /// <list type="bullet">
    /// <item>A non-blank <paramref name="overrideCallsign"/> wins and is used verbatim (normalised) —
    /// including any SSID the owner put in it.</item>
    /// <item>Otherwise the callsign is <c>&lt;base-of(<paramref name="nodeCallsign"/>)&gt;-&lt;ssid&gt;</c>.</item>
    /// <item>If no node callsign is available either (running outside the supervisor), the base is
    /// <see cref="PlaceholderBase"/> and <c>IsPlaceholder</c> is <see langword="true"/>.</item>
    /// </list>
    /// <paramref name="ssid"/> outside 0–15 falls back to <see cref="DefaultSsid"/>.
    /// </summary>
    public static (string Callsign, bool IsPlaceholder) ResolvePreferred(string? overrideCallsign, string? nodeCallsign, int ssid)
    {
        if (!string.IsNullOrWhiteSpace(overrideCallsign))
        {
            return (Normalise(overrideCallsign), false);
        }

        int effectiveSsid = ssid is >= 0 and <= 15 ? ssid : DefaultSsid;

        string? nodeBase = BaseCallsign(nodeCallsign);
        if (nodeBase is { Length: > 0 })
        {
            return ($"{nodeBase}-{effectiveSsid}", false);
        }

        return ($"{PlaceholderBase}-{effectiveSsid}", true);
    }

    /// <summary>Upper-cases and trims a callsign (the wire form is upper-case ASCII).</summary>
    public static string Normalise(string callsign) =>
        callsign.Trim().ToUpperInvariant();

    /// <summary>The base callsign: everything before the first <c>-</c>, normalised. Null/blank in → null.</summary>
    public static string? BaseCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
        {
            return null;
        }

        string trimmed = callsign.Trim();
        int dash = trimmed.IndexOf('-', StringComparison.Ordinal);
        string @base = dash >= 0 ? trimmed[..dash] : trimmed;
        return @base.Length == 0 ? null : @base.ToUpperInvariant();
    }
}
