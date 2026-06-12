using System.Text;

namespace Convers.Protocol;

/// <summary>
/// Low-level wire primitives for the convers <c>/</c>-grammar: the Latin-1 byte↔string mapping and the
/// CR/LF-tolerant line framing the protocol uses. The convers transport is <em>line-based, CR/LF
/// tolerant, Latin-1</em> (see <c>reference/SPECS.txt</c> and <c>conversd.c</c> — the read loop splits
/// on <c>\r</c> <em>or</em> <c>\n</c> and never UTF-8 decodes the bytes; user text is charset-converted
/// only between ISO-8859-1 and a per-user charset, with ISO-8859-1 as the default).
/// </summary>
/// <remarks>
/// This type is sans-IO: it converts whole frames already lifted off (or about to be written to) a
/// socket. The host-command prefix is the three bytes <c>'/'</c> <c>0xFF</c> <c>0x80</c> — what
/// <c>doc/SPECS</c> renders as <c>/..</c> (the two high bytes print as dots). See
/// <see cref="ConversCommand"/> for the prefix constants.
/// </remarks>
public static class ConversWire
{
    /// <summary>
    /// The convers wire encoding: ISO-8859-1 (Latin-1). Every byte maps 1:1 to a <see cref="char"/> in
    /// U+0000..U+00FF and back, so framing the wire never loses or reinterprets a byte — critical for
    /// the host-command prefix bytes <c>0xFF</c>/<c>0x80</c>, which are not valid UTF-8.
    /// </summary>
    public static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Carriage return (<c>0x0D</c>) — one of the two accepted line terminators.</summary>
    public const char Cr = '\r';

    /// <summary>Line feed (<c>0x0A</c>) — one of the two accepted line terminators.</summary>
    public const char Lf = '\n';

    /// <summary>
    /// Decode wire bytes to a string using Latin-1 (1 byte → 1 char). Never throws on arbitrary bytes.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes) => Latin1.GetString(bytes);

    /// <summary>
    /// Encode a string to wire bytes using Latin-1. Characters above U+00FF are out of range for the
    /// convers wire; this maps them through the encoder's replacement behaviour (a single byte each),
    /// preserving the line length but not the (un-representable) character.
    /// </summary>
    public static byte[] Encode(string text) => Latin1.GetBytes(text);

    /// <summary>
    /// Split a buffer of wire bytes into complete lines, Latin-1 decoded and stripped of their
    /// CR/LF terminators. CR/LF tolerant: a line ends at the first <c>\r</c> <em>or</em> <c>\n</c>, and
    /// a <c>\r\n</c> (or runs of terminators) yields no spurious empty lines — matching conversd's read
    /// loop, which advances past a terminator and only dispatches a line when it has accumulated bytes.
    /// Trailing bytes with no terminator are returned via <paramref name="remainder"/> so a caller
    /// streaming a socket can prepend them to the next read.
    /// </summary>
    /// <param name="bytes">The bytes lifted off the wire.</param>
    /// <param name="remainder">
    /// Bytes after the last terminator (a partial line). Empty when the buffer ended on a terminator.
    /// </param>
    /// <returns>The complete lines, in order, with no terminators and no empty lines from blank input.</returns>
    public static IReadOnlyList<string> SplitLines(ReadOnlySpan<byte> bytes, out byte[] remainder)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b is (byte)Cr or (byte)Lf)
            {
                if (i > start)
                {
                    lines.Add(Decode(bytes[start..i]));
                }
                start = i + 1;
            }
        }

        remainder = bytes[start..].ToArray();
        return lines;
    }

    /// <summary>
    /// Frame a single logical line for transmission: append a line feed (<c>\n</c>) if the text does not
    /// already end in a terminator, then Latin-1 encode. conversd accepts either terminator; <c>\n</c> is
    /// the canonical one for TCP links (AX.25 links use <c>\r</c>, chosen by the I/O layer, not here).
    /// </summary>
    public static byte[] FrameLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length == 0 || (line[^1] != Cr && line[^1] != Lf))
        {
            line += Lf;
        }

        return Encode(line);
    }
}
