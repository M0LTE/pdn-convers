using Convers.Protocol;

namespace Convers.Protocol.Tests;

public class ConversWireTests
{
    [Fact]
    public void Latin1_RoundTripsEveryByteValue()
    {
        var bytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            bytes[i] = (byte)i;
        }

        string decoded = ConversWire.Decode(bytes);
        byte[] reencoded = ConversWire.Encode(decoded);

        Assert.Equal(256, decoded.Length);
        Assert.Equal(bytes, reencoded);
    }

    [Fact]
    public void Decode_PreservesHostPrefixHighBytes()
    {
        // The captured oracle handshake begins 2f ff 80 ('/' 0xFF 0x80). It must survive Latin-1 decode
        // as U+002F U+00FF U+0080 — the chars in ConversCommand.HostCommandPrefix.
        byte[] prefix = { 0x2F, 0xFF, 0x80 };
        string decoded = ConversWire.Decode(prefix);
        Assert.Equal(ConversCommand.HostCommandPrefix, decoded);
    }

    [Theory]
    [InlineData("hello\n", "hello")]
    [InlineData("hello\r", "hello")]
    [InlineData("hello\r\n", "hello")]
    public void SplitLines_StripsEitherTerminator(string input, string expected)
    {
        IReadOnlyList<string> lines = ConversWire.SplitLines(ConversWire.Encode(input), out byte[] remainder);
        Assert.Equal(new[] { expected }, lines);
        Assert.Empty(remainder);
    }

    [Fact]
    public void SplitLines_HandlesMultipleLinesAndMixedTerminators()
    {
        Assert.Equal(new[] { "a", "b", "c" },
            ConversWire.SplitLines(ConversWire.Encode("a\nb\nc\n"), out _));
        Assert.Equal(new[] { "a", "b", "c" },
            ConversWire.SplitLines(ConversWire.Encode("a\r\nb\r\nc\r\n"), out _));
    }

    [Fact]
    public void SplitLines_RunsOfTerminatorsProduceNoEmptyLines()
    {
        IReadOnlyList<string> lines = ConversWire.SplitLines(ConversWire.Encode("\n\n\n"), out byte[] remainder);
        Assert.Empty(lines);
        Assert.Empty(remainder);
    }

    [Fact]
    public void SplitLines_ReturnsTrailingPartialAsRemainder()
    {
        IReadOnlyList<string> lines = ConversWire.SplitLines(ConversWire.Encode("done\npartial"), out byte[] remainder);
        Assert.Equal(new[] { "done" }, lines);
        Assert.Equal("partial", ConversWire.Decode(remainder));
    }

    [Fact]
    public void FrameLine_AppendsLfWhenMissing()
    {
        Assert.Equal(ConversWire.Encode("hi\n"), ConversWire.FrameLine("hi"));
        // Already terminated -> unchanged.
        Assert.Equal(ConversWire.Encode("hi\n"), ConversWire.FrameLine("hi\n"));
        Assert.Equal(ConversWire.Encode("hi\r"), ConversWire.FrameLine("hi\r"));
    }

    [Fact]
    public void SplitLines_SplitsTheCapturedOraclePresenceLine()
    {
        // Real bytes the oracle sent the HOST link on a user join (captured 2026-06-12):
        // /\xff\x80USER n0call ORACLE 1781278611 -1 3333 ~\n
        byte[] wire = ConversWire.Encode("/\u00FF\u0080USER n0call ORACLE 1781278611 -1 3333 ~\n");
        IReadOnlyList<string> lines = ConversWire.SplitLines(wire, out _);
        Assert.Single(lines);
        Assert.True(ConversCommand.IsHostCommand(lines[0]));
    }
}
