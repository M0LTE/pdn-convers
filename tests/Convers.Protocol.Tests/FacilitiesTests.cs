using Convers.Protocol;

namespace Convers.Protocol.Tests;

public class FacilitiesTests
{
    [Theory]
    [InlineData("a", Facilities.AwayOld)]
    [InlineData("A", Facilities.AwayNew)]
    [InlineData("d", Facilities.DestinationForwarding)]
    [InlineData("m", Facilities.ChannelModes)]
    [InlineData("p", Facilities.PingPong)]
    [InlineData("u", Facilities.Udat)]
    [InlineData("n", Facilities.Nicknames)]
    [InlineData("f", Facilities.Filter)]
    [InlineData("i", Facilities.SauppInternal)]
    public void Parse_EachLetterSetsItsFlag(string letters, Facilities expected)
    {
        Assert.Equal(expected, FacilitiesCodec.Parse(letters));
    }

    [Fact]
    public void Parse_CapturedOracleFacilityString()
    {
        // From the oracle handshake reply: /..HOST ORACLE saupp1.62a Aadmpunfi
        Facilities f = FacilitiesCodec.Parse("Aadmpunfi");
        Assert.True(f.HasFlag(Facilities.AwayNew));
        Assert.True(f.HasFlag(Facilities.AwayOld));
        Assert.True(f.HasFlag(Facilities.DestinationForwarding));
        Assert.True(f.HasFlag(Facilities.ChannelModes));
        Assert.True(f.HasFlag(Facilities.PingPong));
        Assert.True(f.HasFlag(Facilities.Udat));
        Assert.True(f.HasFlag(Facilities.Nicknames));
        Assert.True(f.HasFlag(Facilities.Filter));
        Assert.True(f.HasFlag(Facilities.SauppInternal));
    }

    [Fact]
    public void Parse_IgnoresUnknownLetters()
    {
        Assert.Equal(Facilities.PingPong, FacilitiesCodec.Parse("pXYZ?"));
    }

    [Fact]
    public void Parse_NullOrEmptyIsNone()
    {
        Assert.Equal(Facilities.None, FacilitiesCodec.Parse(null));
        Assert.Equal(Facilities.None, FacilitiesCodec.Parse(string.Empty));
    }

    [Fact]
    public void Format_UsesCanonicalOrder()
    {
        Facilities all = Facilities.AwayNew | Facilities.AwayOld | Facilities.DestinationForwarding |
                         Facilities.ChannelModes | Facilities.PingPong | Facilities.Udat |
                         Facilities.Nicknames | Facilities.Filter | Facilities.SauppInternal;
        Assert.Equal("Aadmpunfi", FacilitiesCodec.Format(all));
    }

    [Fact]
    public void Format_None_IsEmpty()
    {
        Assert.Equal(string.Empty, FacilitiesCodec.Format(Facilities.None));
    }

    [Fact]
    public void ParseFormat_RoundTripsTheOracleString()
    {
        Assert.Equal("Aadmpunfi", FacilitiesCodec.Format(FacilitiesCodec.Parse("Aadmpunfi")));
    }
}
