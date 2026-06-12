namespace Convers.Protocol;

/// <summary>
/// A faithful port of conversd's argument tokenizer (<c>getarg</c>/<c>getargcs</c> in <c>conversd.c</c>).
/// The convers grammar is whitespace-separated fields with a final "rest of line" text field. This
/// reader reproduces that exactly:
/// <list type="bullet">
///   <item>leading whitespace is skipped before each token;</item>
///   <item>a token runs up to (not including) the next whitespace run;</item>
///   <item><see cref="Rest"/> returns everything from the current position to end-of-line, after
///         skipping leading whitespace — the trailing text field (e.g. a CMSG body).</item>
/// </list>
/// Whitespace is the C <c>isspace</c> set (space, tab, vertical tab, form feed, CR, LF). The reader does
/// not lowercase: conversd lowercases only the command verb (via <c>getarg</c>); field values use the
/// case-preserving <c>getargcs</c>. Verb-lowercasing is handled by the dispatcher, not here.
/// </summary>
public ref struct ConversTokenizer
{
    private readonly ReadOnlySpan<char> _line;
    private int _pos;

    /// <summary>Create a tokenizer over a single (terminator-stripped) line.</summary>
    public ConversTokenizer(ReadOnlySpan<char> line)
    {
        _line = line;
        _pos = 0;
    }

    /// <summary>C <c>isspace</c>: space, <c>\t \n \v \f \r</c>.</summary>
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private void SkipSpace()
    {
        while (_pos < _line.Length && IsSpace(_line[_pos]))
        {
            _pos++;
        }
    }

    /// <summary>True once all tokens have been consumed (only trailing whitespace, if any, remains).</summary>
    public readonly bool AtEnd
    {
        get
        {
            int p = _pos;
            while (p < _line.Length && IsSpace(_line[p]))
            {
                p++;
            }

            return p >= _line.Length;
        }
    }

    /// <summary>
    /// Read the next whitespace-delimited token (case preserved, like <c>getargcs</c>). Returns the empty
    /// string when no token remains. Advances past the token's trailing whitespace separator.
    /// </summary>
    public string Next()
    {
        SkipSpace();
        int start = _pos;
        while (_pos < _line.Length && !IsSpace(_line[_pos]))
        {
            _pos++;
        }

        return _line[start.._pos].ToString();
    }

    /// <summary>
    /// Read the next token and parse it as a base-10 <see cref="int"/> via the same lenient rule conversd
    /// uses (<c>atoi</c>): leading sign and digits are taken, trailing junk ignored, and a non-numeric or
    /// missing token yields <paramref name="fallback"/> (conversd's <c>atoi</c> returns 0).
    /// </summary>
    public int NextInt(int fallback = 0)
    {
        string tok = Next();
        return ParseAtoi(tok, fallback);
    }

    /// <summary>
    /// Read the next token and parse it as a base-10 <see cref="long"/> (<c>atol</c> semantics) — used for
    /// the Unix-time fields in <c>/..USER</c>, <c>/..TOPI</c>, <c>/..AWAY</c>, <c>/..PONG</c>.
    /// </summary>
    public long NextLong(long fallback = 0)
    {
        string tok = Next();
        return ParseAtol(tok, fallback);
    }

    /// <summary>
    /// Everything from the current position to end-of-line, after skipping leading whitespace — the final
    /// free-text field (<c>getarg(0, 1)</c> in conversd). Empty if nothing remains.
    /// </summary>
    public string Rest()
    {
        SkipSpace();
        string rest = _line[_pos..].ToString();
        _pos = _line.Length;
        return rest;
    }

    /// <summary>
    /// <c>atoi</c>-equivalent: optional leading whitespace, optional sign, then digits; stops at the first
    /// non-digit. Non-numeric input returns <paramref name="fallback"/>.
    /// </summary>
    internal static int ParseAtoi(string? s, int fallback = 0) =>
        (int)ParseAtol(s, fallback);

    /// <summary><c>atol</c>-equivalent (see <see cref="ParseAtoi"/>).</summary>
    internal static long ParseAtol(string? s, long fallback = 0)
    {
        if (string.IsNullOrEmpty(s))
        {
            return fallback;
        }

        int i = 0;
        while (i < s.Length && (s[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r'))
        {
            i++;
        }

        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            neg = s[i] == '-';
            i++;
        }

        if (i >= s.Length || !char.IsAsciiDigit(s[i]))
        {
            return fallback;
        }

        long value = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            value = (value * 10) + (s[i] - '0');
            i++;
        }

        return neg ? -value : value;
    }
}
