namespace Convers.Protocol;

/// <summary>
/// Parses a host-to-host (<c>/..</c>) wire line into a <see cref="HostCommand"/> and formats one back.
/// Sans-IO: input is a single terminator-stripped line (as produced by
/// <see cref="ConversWire.SplitLines"/>); <see cref="Format(HostCommand)"/> returns a line ready for
/// <see cref="ConversWire.FrameLine"/>.
/// </summary>
/// <remarks>
/// <para>
/// On the wire a host command is <c>'/'</c> + <c>0xFF</c> + <c>0x80</c> + verb + space-separated fields
/// (see <c>conversd.c</c>: the command table keys are <c>"\377\200verb"</c> and the dispatcher matches
/// <c>/\377\200VERB</c>). The verb is matched case-insensitively (conversd lowercases it) and by prefix
/// (conversd uses <c>strncmp(name, arg, arglen)</c>); the canonical verbs here are all four letters.
/// </para>
/// <para>
/// Unknown verbs become <see cref="UnknownHostCommand"/>, preserving the raw body so the line round-trips
/// losslessly (the SPECS golden rule). The documentation prefix <c>/..</c> is also accepted on input so
/// SPECS-style vectors parse, but <see cref="Format(HostCommand)"/> always emits the real byte prefix.
/// </para>
/// </remarks>
public static class HostCommandCodec
{
    /// <summary>
    /// True when <paramref name="line"/> is a host command line (begins with the real <c>/</c> 0xFF 0x80
    /// prefix or the documentation <c>/..</c> spelling). Delegates to <see cref="ConversCommand.IsHostCommand"/>.
    /// </summary>
    public static bool IsHostCommandLine(string? line) => ConversCommand.IsHostCommand(line);

    /// <summary>
    /// Strip the host prefix (real or dotted) from <paramref name="line"/>, returning the body
    /// (<c>VERB args...</c>). Returns <see langword="null"/> if the line is not a host command.
    /// </summary>
    private static string? StripPrefix(string line)
    {
        if (line.StartsWith(ConversCommand.HostCommandPrefix, StringComparison.Ordinal))
        {
            return line[ConversCommand.HostCommandPrefix.Length..];
        }

        if (line.StartsWith(ConversCommand.DottedHostCommandPrefix, StringComparison.Ordinal))
        {
            return line[ConversCommand.DottedHostCommandPrefix.Length..];
        }

        return null;
    }

    /// <summary>
    /// Parse a host command line into a <see cref="HostCommand"/>. Throws
    /// <see cref="FormatException"/> if the line is not a host command line at all (no <c>/..</c> prefix);
    /// any unmodelled but well-formed <c>/..</c> verb yields an <see cref="UnknownHostCommand"/>.
    /// </summary>
    public static HostCommand Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string? body = StripPrefix(line);
        if (body is null)
        {
            throw new FormatException("Not a host command line (missing the '/..' prefix).");
        }

        var t = new ConversTokenizer(body);
        string verbRaw = t.Next();
        string verb = verbRaw.ToUpperInvariant();

        return verb switch
        {
            "USER" => ParseUser(ref t, isObserver: false),
            "OBSV" => ParseUser(ref t, isObserver: true),
            "CMSG" => ParseChannelMessage(ref t),
            "UMSG" => ParseUserMessage(ref t),
            "UDAT" => ParseUserData(ref t),
            "INVI" => ParseInvite(ref t),
            "PING" => new HostPing(),
            "PONG" => new HostPong(t.NextLong(0)),
            "HOST" => ParseHandshake(ref t),
            "TOPI" => ParseTopic(ref t),
            "MODE" => ParseMode(ref t),
            "OPER" => ParseOper(ref t),
            "AWAY" => ParseAway(ref t),
            "DEST" => ParseDest(ref t),
            "ROUT" => ParseRoute(ref t),
            "SYSI" => ParseSysInfo(ref t),
            "LOOP" => new HostLoop(t.Rest()),
            "ECMD" => ParseExtendedCommand(ref t),
            "HELP" => ParseHelp(ref t),
            "UADD" => ParseUserAdd(ref t),
            _ => new UnknownHostCommand(verb, t.Rest()),
        };
    }

    /// <summary>
    /// Try-parse variant: returns <see langword="false"/> (with <paramref name="command"/> null) when the
    /// line is not a host command line, rather than throwing.
    /// </summary>
    public static bool TryParse(string? line, out HostCommand? command)
    {
        if (line is null || StripPrefix(line) is null)
        {
            command = null;
            return false;
        }

        command = Parse(line);
        return true;
    }

    /// <summary>
    /// Format a <see cref="HostCommand"/> into a wire line: the real <c>/</c> 0xFF 0x80 prefix followed by
    /// the command's <see cref="HostCommand.Emit"/> body. No terminator (use
    /// <see cref="ConversWire.FrameLine"/>).
    /// </summary>
    public static string Format(HostCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string body = command.Emit();
        string verbAndBody = body.Length == 0 ? command.Verb : $"{command.Verb} {body}";
        return ConversCommand.HostCommandPrefix + verbAndBody;
    }

    private static HostUser ParseUser(ref ConversTokenizer t, bool isObserver)
    {
        string user = t.Next();
        string host = t.Next();
        long ts = t.NextLong(0);
        int fromChan = t.NextInt(0);
        int toChan = t.NextInt(0);
        string text = t.Rest();
        return new HostUser(user, host, ts, fromChan, toChan, text, isObserver);
    }

    private static HostChannelMessage ParseChannelMessage(ref ConversTokenizer t)
    {
        string user = t.Next();
        int channel = t.NextInt(0);
        string text = t.Rest();
        return new HostChannelMessage(user, channel, text);
    }

    private static HostUserMessage ParseUserMessage(ref ConversTokenizer t)
    {
        string from = t.Next();
        string to = t.Next();
        string text = t.Rest();
        return new HostUserMessage(from, to, text);
    }

    private static HostUserData ParseUserData(ref ConversTokenizer t)
    {
        string user = t.Next();
        string host = t.Next();
        string text = t.Rest();
        // conversd stores "@" as the empty marker; normalise it away so the model is clean.
        return new HostUserData(user, host, text == "@" ? string.Empty : text);
    }

    private static HostInvite ParseInvite(ref ConversTokenizer t)
    {
        string from = t.Next();
        string user = t.Next();
        int channel = t.NextInt(0);
        return new HostInvite(from, user, channel);
    }

    private static HostHandshake ParseHandshake(ref ConversTokenizer t)
    {
        string hostname = t.Next();
        string software = t.Next();
        string facilitiesRaw = t.Next();
        if (software.Length == 0)
        {
            software = HostHandshake.UnknownSoftware;
        }

        return new HostHandshake(hostname, software, FacilitiesCodec.Parse(facilitiesRaw));
    }

    private static HostTopic ParseTopic(ref ConversTokenizer t)
    {
        string user = t.Next();
        string host = t.Next();
        long time = t.NextLong(0);
        int channel = t.NextInt(0);
        string text = t.Rest();
        return new HostTopic(user, host, time, channel, text);
    }

    private static HostMode ParseMode(ref ConversTokenizer t)
    {
        int channel = t.NextInt(0);
        string options = t.Rest();
        return new HostMode(channel, options);
    }

    private static HostOper ParseOper(ref ConversTokenizer t)
    {
        string fromName = t.Next();
        int channel = t.NextInt(0);
        string user = t.Next();
        return new HostOper(fromName, channel, user);
    }

    private static HostAway ParseAway(ref ConversTokenizer t)
    {
        string user = t.Next();
        string host = t.Next();
        long time = t.NextLong(0);
        string text = t.Rest();
        return new HostAway(user, host, time, text);
    }

    private static HostDest ParseDest(ref ConversTokenizer t)
    {
        string host = t.Next();
        long time = t.NextLong(0);
        string software = t.Rest();
        return new HostDest(host, time, software);
    }

    private static HostRoute ParseRoute(ref ConversTokenizer t)
    {
        string dest = t.Next();
        string user = t.Next();
        int ttl = t.NextInt(0);
        return new HostRoute(dest, user, ttl);
    }

    private static HostSysInfo ParseSysInfo(ref ConversTokenizer t)
    {
        string user = t.Next();
        string host = t.Next();
        return new HostSysInfo(user, host);
    }

    private static HostExtendedCommand ParseExtendedCommand(ref ConversTokenizer t)
    {
        string user = t.Next();
        string cmdName = t.Next();
        string parameters = t.Rest();
        return new HostExtendedCommand(user, cmdName, parameters);
    }

    private static HostHelp ParseHelp(ref ConversTokenizer t)
    {
        string user = t.Next();
        string cmdName = t.Next();
        return new HostHelp(user, cmdName);
    }

    private static HostUserAdd ParseUserAdd(ref ConversTokenizer t)
    {
        string user = t.Next();
        string host = t.Next();
        string nickname = t.Next();
        int channel = t.NextInt(0);
        string text = t.Rest();
        return new HostUserAdd(user, host, nickname, channel, text);
    }
}
