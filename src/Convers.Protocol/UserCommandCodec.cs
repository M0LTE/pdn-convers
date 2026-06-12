namespace Convers.Protocol;

/// <summary>
/// Parses a USER-surface input line into a <see cref="UserCommand"/>. Reproduces conversd's command
/// matching: a leading <c>/</c> introduces a command; the verb is matched case-insensitively and by
/// <em>prefix in table order</em> (conversd's <c>strncmp(cmdp->name, arg, arglen)</c> over <c>cmdtable</c>),
/// so the first table row whose name starts with the typed letters wins. A line with no leading slash is
/// <see cref="ChatText"/>.
/// </summary>
/// <remarks>
/// Only the verbs in W1 scope are modelled; the relative table order among them is preserved so
/// abbreviations resolve as conversd does (e.g. <c>/n</c> → <c>name</c> because <c>name</c> precedes
/// <c>nickname</c>/<c>note</c>; <c>/w</c> → <c>who</c>). A typed verb that matches no modelled row becomes
/// <see cref="UnknownUserCommand"/> — conversd would either run an unmodelled built-in or answer "Unknown
/// command"; a leaf treats those uniformly. This is parse-only (sans-IO); there is no emit for the user
/// surface — the leaf re-expresses user actions as HOST commands upstream (see <see cref="HostCommandCodec"/>).
/// </remarks>
public static class UserCommandCodec
{
    // The modelled verbs, in conversd cmdtable order (conversd.c lines ~135-236). Prefix matching walks
    // this list top-down and takes the first row whose canonical name starts with the typed verb. Order
    // matters for abbreviations: e.g. "away" before "beep"; "name" before "nickname"/"note"/"news".
    private static readonly (string Name, Func<string, UserCommand> Build)[] Table =
    {
        ("away", Build_Away),
        ("beep", _ => new BeepCommand()),
        ("bye", Build_Quit),
        ("channel", Build_Join),
        ("cstat", _ => new CstatCommand()),
        ("exit", Build_Quit),
        ("invite", Build_Invite),
        ("join", Build_Join),
        ("leave", _ => new LeaveCommand()),
        ("msg", Build_Msg),
        ("name", Build_Name),
        ("nickname", Build_Nickname),
        ("note", Build_Personal),
        ("observer", _ => new ObserverCommand()),
        ("online", Build_Online),
        ("personal", Build_Personal),
        ("quit", Build_Quit),
        ("send", Build_Msg),
        ("topic", Build_Topic),
        ("users", Build_Who),
        ("who", Build_Who),
        ("write", Build_Msg),
    };

    /// <summary>
    /// Parse a USER input line. A leading <c>/</c> selects a command (matched by prefix in table order);
    /// otherwise the whole line is <see cref="ChatText"/>. Never throws on input shape.
    /// </summary>
    public static UserCommand Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // A bare line (no leading slash) is chat. conversd strips a leading "//" to a single "/".
        if (line.Length == 0 || line[0] != '/')
        {
            return new ChatText(line);
        }

        // Split the verb from the rest. The verb is the run of non-space chars after the slash.
        string afterSlash = line[1..];
        int sp = IndexOfSpace(afterSlash);
        string verb = sp < 0 ? afterSlash : afterSlash[..sp];
        string args = sp < 0 ? string.Empty : SkipLeadingSpaces(afterSlash[(sp + 1)..]);

        if (verb.Length == 0)
        {
            // "/" or "/ ..." — treat the remainder as chat (conversd's "//" -> "/" extension aside).
            return new ChatText(line);
        }

        string verbLower = verb.ToLowerInvariant();
        foreach ((string name, Func<string, UserCommand> build) in Table)
        {
            if (name.StartsWith(verbLower, StringComparison.Ordinal))
            {
                return build(args);
            }
        }

        return new UnknownUserCommand(verbLower, args);
    }

    private static int IndexOfSpace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (IsSpace(s[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string SkipLeadingSpaces(string s)
    {
        int i = 0;
        while (i < s.Length && IsSpace(s[i]))
        {
            i++;
        }

        return s[i..];
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private static NameCommand Build_Name(string args)
    {
        var t = new ConversTokenizer(args);
        string first = t.Next();
        if (first.Length == 0)
        {
            return new NameCommand(string.Empty, null);
        }

        // "/name <call> [channel]" or, for AX.25 users, "/name <channel>" (call from transport). We model
        // the explicit form: a leading numeric token is a channel-only login; otherwise it is the call.
        string strippedFirst = StripHash(first);
        if (IsNumber(strippedFirst))
        {
            return new NameCommand(string.Empty, ConversTokenizer.ParseAtoi(strippedFirst));
        }

        string call = first;
        int? channel = null;
        if (!t.AtEnd)
        {
            string chanTok = StripHash(t.Next());
            if (IsNumber(chanTok))
            {
                channel = ConversTokenizer.ParseAtoi(chanTok);
            }
        }

        return new NameCommand(call, channel);
    }

    private static JoinCommand Build_Join(string args)
    {
        var t = new ConversTokenizer(args);
        if (t.AtEnd)
        {
            return new JoinCommand(null);
        }

        string tok = StripHash(t.Next());
        return IsNumber(tok) ? new JoinCommand(ConversTokenizer.ParseAtoi(tok)) : new JoinCommand(null);
    }

    private static QuitCommand Build_Quit(string args) => new(args);

    private static WhoCommand Build_Who(string args) => new(args);

    private static MsgCommand Build_Msg(string args)
    {
        var t = new ConversTokenizer(args);
        string to = t.Next();
        string text = t.Rest();
        return new MsgCommand(to, text);
    }

    private static TopicCommand Build_Topic(string args) => new(args);

    private static PersonalCommand Build_Personal(string args) => new(args);

    private static NicknameCommand Build_Nickname(string args)
    {
        var t = new ConversTokenizer(args);
        return new NicknameCommand(t.Next());
    }

    private static InviteCommand Build_Invite(string args)
    {
        var t = new ConversTokenizer(args);
        string user = t.Next();
        int? channel = null;
        if (!t.AtEnd)
        {
            string tok = StripHash(t.Next());
            if (IsNumber(tok))
            {
                channel = ConversTokenizer.ParseAtoi(tok);
            }
        }

        return new InviteCommand(user, channel);
    }

    private static AwayCommand Build_Away(string args) => new(args);

    private static OnlineCommand Build_Online(string args)
    {
        var t = new ConversTokenizer(args);
        return new OnlineCommand(t.Next());
    }

    private static string StripHash(string token) =>
        token.Length > 0 && token[0] == '#' ? token[1..] : token;

    // conversd's stringisnumber: a non-empty run of decimal digits.
    private static bool IsNumber(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
