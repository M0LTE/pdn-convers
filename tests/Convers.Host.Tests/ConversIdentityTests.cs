using Convers.Host;

namespace Convers.Host.Tests;

public class ConversIdentityTests
{
    [Theory]
    [InlineData("M0LTE-1", 4, "M0LTE-4")]    // strip the node SSID, append ours
    [InlineData("M0LTE", 4, "M0LTE-4")]      // node has no SSID
    [InlineData("g0abc-9", 7, "G0ABC-7")]    // upper-cased
    [InlineData("M0LTE-1", 0, "M0LTE-0")]
    [InlineData("M0LTE-1", 15, "M0LTE-15")]
    public void Resolve_DerivesNodeBasePlusSsid(string nodeCallsign, int ssid, string expected)
    {
        (string callsign, bool placeholder) = ConversIdentity.ResolvePreferred(null, nodeCallsign, ssid);

        Assert.Equal(expected, callsign);
        Assert.False(placeholder);
    }

    [Theory]
    [InlineData("G7XYZ-2")]
    [InlineData(" g7xyz-2 ")]   // trimmed + upper-cased to G7XYZ-2
    public void Resolve_ExplicitOverrideWinsVerbatim(string overrideCallsign)
    {
        (string callsign, bool placeholder) = ConversIdentity.ResolvePreferred(overrideCallsign, "M0LTE-1", 4);

        Assert.Equal("G7XYZ-2", callsign);   // node callsign + ssid ignored
        Assert.False(placeholder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoNodeAndNoOverride_IsPlaceholder(string? nodeCallsign)
    {
        (string callsign, bool placeholder) = ConversIdentity.ResolvePreferred(null, nodeCallsign, 4);

        Assert.Equal("N0CALL-4", callsign);
        Assert.True(placeholder);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(99)]
    public void Resolve_OutOfRangeSsid_FallsBackToDefault(int ssid)
    {
        (string callsign, _) = ConversIdentity.ResolvePreferred(null, "M0LTE-1", ssid);

        Assert.Equal($"M0LTE-{ConversIdentity.DefaultSsid}", callsign);
    }

    [Theory]
    [InlineData("M0LTE-1", "M0LTE")]
    [InlineData("M0LTE", "M0LTE")]
    [InlineData("g0abc-12", "G0ABC")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void BaseCallsign_StripsSsidAndNormalises(string? input, string? expected) =>
        Assert.Equal(expected, ConversIdentity.BaseCallsign(input));

    // --- Node-owned-callsign contract (PDN_APP_CALLSIGN) ---

    [Theory]
    [InlineData("GB7RDG-4", "GB7RDG-4")]
    [InlineData(" gb7rdg-4 ", "GB7RDG-4")]   // trimmed + upper-cased
    [InlineData("M0LTE", "M0LTE")]           // bare callsign honoured verbatim (no SSID forced)
    public void ResolveBinding_AppCallsign_BindsExactlyAndSkipsWalk(string appCallsign, string expected)
    {
        // The node owns the callsign: bind it verbatim, ExactBind=true (skip the SSID walk), and it wins
        // over BOTH a config override and the node-derived <node>-<ssid>.
        (string callsign, bool placeholder, bool exactBind) =
            ConversIdentity.ResolveBinding(appCallsign, overrideCallsign: "G7XYZ-2", nodeCallsign: "M0LTE-1", ssid: 4);

        Assert.Equal(expected, callsign);
        Assert.False(placeholder);
        Assert.True(exactBind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveBinding_NoAppCallsign_FallsBackToDeriveAndWalks(string? appCallsign)
    {
        // No PDN_APP_CALLSIGN → legacy behaviour: derive <node>-<ssid>, ExactBind=false (walk applies).
        (string callsign, bool placeholder, bool exactBind) =
            ConversIdentity.ResolveBinding(appCallsign, overrideCallsign: null, nodeCallsign: "M0LTE-1", ssid: 4);

        Assert.Equal("M0LTE-4", callsign);
        Assert.False(placeholder);
        Assert.False(exactBind);
    }

    [Fact]
    public void ResolveBinding_NoAppCallsign_ConfigOverrideStillWinsOnFallback()
    {
        // The config override remains the fallback-path winner when the node injects no PDN_APP_CALLSIGN,
        // but it is NOT an exact (node-owned) bind — the SSID walk still applies on a clash.
        (string callsign, bool placeholder, bool exactBind) =
            ConversIdentity.ResolveBinding(appCallsign: null, overrideCallsign: "G7XYZ-2", nodeCallsign: "M0LTE-1", ssid: 4);

        Assert.Equal("G7XYZ-2", callsign);
        Assert.False(placeholder);
        Assert.False(exactBind);
    }

    [Fact]
    public void ResolveBinding_NoAppCallsignNoNodeNoOverride_IsPlaceholderAndWalks()
    {
        (string callsign, bool placeholder, bool exactBind) =
            ConversIdentity.ResolveBinding(appCallsign: null, overrideCallsign: null, nodeCallsign: null, ssid: 4);

        Assert.Equal("N0CALL-4", callsign);
        Assert.True(placeholder);
        Assert.False(exactBind);
    }
}
