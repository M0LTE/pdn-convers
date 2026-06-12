using Convers.Protocol;

namespace Convers.Host.Tests.Uplink;

/// <summary>
/// Test helpers for building convers host-command wire lines with the real <c>/</c> 0xFF 0x80 prefix —
/// so the engine tests feed exactly the bytes a real conversd sends (the dotted <c>/..</c> spelling is a
/// documentation device only).
/// </summary>
internal static class Wire
{
    /// <summary>The three-byte host-command prefix as a string: <c>'/'</c> + U+00FF + U+0080.</summary>
    public const string Prefix = ConversCommand.HostCommandPrefix;

    /// <summary>Build a host-command line by prefixing <paramref name="body"/> (e.g. "PING", "HOST ORACLE …").</summary>
    public static string Host(string body) => Prefix + body;
}
