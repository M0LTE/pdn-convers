namespace Convers.Protocol;

/// <summary>
/// A parsed host-to-host (<c>/..</c>) command. Each concrete subtype models one verb's field layout
/// (from <c>reference/SPECS.txt</c> and <c>host.c</c>); <see cref="UnknownHostCommand"/> captures any
/// verb this leaf does not model, preserving the raw line so it round-trips losslessly (the SPECS
/// "golden rule": relay unrecognised <c>/..</c> verbatim — a no-op for a strict leaf, but the codec
/// must not corrupt it).
/// </summary>
/// <remarks>
/// Sans-IO: <see cref="Emit"/> renders the command body <em>without</em> the <c>/..</c> prefix or the
/// line terminator; <see cref="HostCommandCodec.Format(HostCommand)"/> adds the wire prefix, and
/// <see cref="ConversWire.FrameLine"/> adds the terminator. Channel <c>-1</c> in a tochan field means
/// signoff (see <see cref="HostUser"/>).
/// </remarks>
public abstract record HostCommand
{
    /// <summary>The four-letter host verb (upper-case, e.g. <c>USER</c>, <c>CMSG</c>, <c>PING</c>).</summary>
    public abstract string Verb { get; }

    /// <summary>
    /// Render the command body in wire field order, <em>excluding</em> the <c>/..</c> prefix and the
    /// terminator. Used by <see cref="HostCommandCodec.Format(HostCommand)"/>.
    /// </summary>
    public abstract string Emit();
}

/// <summary>
/// <c>/..USER &lt;user&gt; &lt;host&gt; &lt;timestamp&gt; &lt;fromchan&gt; &lt;tochan&gt; [@|text]</c>
/// (NECESSARY). Presence: user@host left <paramref name="FromChannel"/> and joined
/// <paramref name="ToChannel"/> at <paramref name="Timestamp"/> (Unix seconds). <c>Text</c> is the
/// personal note (a lone <c>@</c> means empty). When <paramref name="ToChannel"/> is <c>-1</c> the user
/// signed off and <c>Text</c> is the reason. The same record models <c>/..OBSV</c> (observer presence)
/// via <see cref="IsObserver"/> — in conversd both dispatch to <c>h_user_command</c>, distinguished by
/// the verb's first letter.
/// </summary>
public sealed record HostUser(
    string User,
    string Host,
    long Timestamp,
    int FromChannel,
    int ToChannel,
    string Text,
    bool IsObserver = false) : HostCommand
{
    /// <summary>The signoff sentinel for <see cref="ToChannel"/> (and an unset <see cref="FromChannel"/>).</summary>
    public const int Signoff = -1;

    /// <summary>True when this presence line is a channel signoff (<see cref="ToChannel"/> == -1).</summary>
    public bool IsSignoff => ToChannel == Signoff;

    /// <inheritdoc/>
    public override string Verb => IsObserver ? "OBSV" : "USER";

    /// <inheritdoc/>
    public override string Emit()
    {
        string text = string.IsNullOrEmpty(Text) ? "@" : Text;
        return $"{User} {Host} {Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
               $"{FromChannel.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
               $"{ToChannel.ToString(System.Globalization.CultureInfo.InvariantCulture)} {text}";
    }
}

/// <summary>
/// <c>/..CMSG &lt;user&gt; &lt;channel&gt; &lt;text&gt;</c> (NECESSARY). A channel message: user wrote
/// <c>Text</c> to the whole <paramref name="Channel"/>. When <paramref name="User"/> is
/// <c>conversd</c> this is a broadcast and the receiver does no formatting.
/// </summary>
public sealed record HostChannelMessage(string User, int Channel, string Text) : HostCommand
{
    /// <summary>The reserved sender name marking a broadcast (no formatting on receipt).</summary>
    public const string BroadcastSender = "conversd";

    /// <summary>True when this is a system broadcast (<see cref="User"/> == <c>conversd</c>).</summary>
    public bool IsBroadcast => string.Equals(User, BroadcastSender, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string Verb => "CMSG";

    /// <inheritdoc/>
    public override string Emit() =>
        $"{User} {Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Text}";
}

/// <summary>
/// <c>/..UMSG &lt;from&gt; &lt;to&gt; &lt;text&gt;</c> (NECESSARY). A private message from
/// <paramref name="From"/> to <paramref name="To"/>; not formatted if <paramref name="From"/> is
/// <c>conversd</c>.
/// </summary>
public sealed record HostUserMessage(string From, string To, string Text) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "UMSG";

    /// <inheritdoc/>
    public override string Emit() => $"{From} {To} {Text}";
}

/// <summary>
/// <c>/..UDAT &lt;user&gt; &lt;host&gt; [text]</c> (NECESSARY, though SPECS calls the field obsolete).
/// user@host sets (or, with empty text, removes) their personal description. Empty text emits <c>@</c>.
/// </summary>
public sealed record HostUserData(string User, string Host, string Text) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "UDAT";

    /// <inheritdoc/>
    public override string Emit()
    {
        string text = string.IsNullOrEmpty(Text) ? "@" : Text;
        return $"{User} {Host} {text}";
    }
}

/// <summary>
/// <c>/..INVI &lt;from&gt; &lt;user&gt; &lt;channel&gt;</c> (NECESSARY). An invitation from
/// <paramref name="From"/> to <paramref name="User"/> for <paramref name="Channel"/>, sent network-wide.
/// </summary>
public sealed record HostInvite(string From, string User, int Channel) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "INVI";

    /// <inheritdoc/>
    public override string Emit() =>
        $"{From} {User} {Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// <c>/..PING</c> (no arguments). Requests a <see cref="HostPong"/> in reply.
/// </summary>
public sealed record HostPing : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "PING";

    /// <inheritdoc/>
    public override string Emit() => string.Empty;
}

/// <summary>
/// <c>/..PONG &lt;time&gt;</c>. <paramref name="Time"/> is the sender's measured round-trip time
/// (ping-sent to pong-received). Special values: <c>-1</c> = no measurements; <c>0</c> = implemented but
/// not measured yet.
/// </summary>
public sealed record HostPong(long Time) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "PONG";

    /// <inheritdoc/>
    public override string Emit() => Time.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// <c>/..HOST &lt;hostname&gt; [software [facilities]]</c>. The handshake that joins the HOST state.
/// <paramref name="Hostname"/> ≤ 9 chars; <paramref name="Software"/> ≤ 8 chars (defaults to <c>?</c>
/// when absent); <paramref name="Facilities"/> is the negotiated letter set.
/// </summary>
public sealed record HostHandshake(string Hostname, string Software, Facilities Facilities) : HostCommand
{
    /// <summary>conversd's stand-in when the software field is absent.</summary>
    public const string UnknownSoftware = "?";

    /// <inheritdoc/>
    public override string Verb => "HOST";

    /// <inheritdoc/>
    public override string Emit()
    {
        string software = string.IsNullOrEmpty(Software) ? UnknownSoftware : Software;
        string facilities = FacilitiesCodec.Format(Facilities);
        return facilities.Length == 0
            ? $"{Hostname} {software}"
            : $"{Hostname} {software} {facilities}";
    }
}

/// <summary>
/// <c>/..TOPI &lt;user&gt; &lt;host&gt; &lt;time&gt; &lt;channel&gt; [text]</c> (OPTIONAL). user@host set a
/// new topic on <paramref name="Channel"/> at <paramref name="Time"/> (Unix seconds). Empty text removes
/// the topic.
/// </summary>
public sealed record HostTopic(string User, string Host, long Time, int Channel, string Text) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "TOPI";

    /// <inheritdoc/>
    public override string Emit()
    {
        string body = $"{User} {Host} {Time.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                      $"{Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Text.Length == 0 ? body : $"{body} {Text}";
    }
}

/// <summary>
/// <c>/..MODE &lt;channel&gt; &lt;options&gt;</c> (OPTIONAL). Set <paramref name="Channel"/> to the mode
/// <paramref name="Options"/> string (e.g. <c>+i +l +m +p +s +t</c>).
/// </summary>
public sealed record HostMode(int Channel, string Options) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "MODE";

    /// <inheritdoc/>
    public override string Emit() =>
        $"{Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Options}";
}

/// <summary>
/// <c>/..OPER &lt;fromname&gt; &lt;channel&gt; &lt;user&gt;</c> (OPTIONAL). <paramref name="User"/> becomes
/// channel-operator (or operator when <paramref name="Channel"/> == -1); <paramref name="FromName"/> set
/// the status.
/// </summary>
public sealed record HostOper(string FromName, int Channel, string User) : HostCommand
{
    /// <summary>The <see cref="Channel"/> sentinel meaning a global operator rather than a channel op.</summary>
    public const int GlobalOperator = -1;

    /// <inheritdoc/>
    public override string Verb => "OPER";

    /// <inheritdoc/>
    public override string Emit() =>
        $"{FromName} {Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)} {User}";
}

/// <summary>
/// <c>/..AWAY &lt;user&gt; &lt;host&gt; &lt;time&gt; [text]</c> (OPTIONAL). Mark user@host away; absent
/// text means back again. <paramref name="Time"/> is when the command was issued (Unix seconds).
/// </summary>
public sealed record HostAway(string User, string Host, long Time, string Text) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "AWAY";

    /// <inheritdoc/>
    public override string Emit()
    {
        string body = $"{User} {Host} {Time.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Text.Length == 0 ? body : $"{body} {Text}";
    }
}

/// <summary>
/// <c>/..DEST &lt;host&gt; &lt;time&gt; [software]</c> (OPTIONAL). <paramref name="Host"/> is reachable via
/// the sender in <paramref name="Time"/> seconds, running <paramref name="Software"/>.
/// </summary>
public sealed record HostDest(string Host, long Time, string Software) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "DEST";

    /// <inheritdoc/>
    public override string Emit()
    {
        string body = $"{Host} {Time.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Software.Length == 0 ? body : $"{body} {Software}";
    }
}

/// <summary>
/// <c>/..ROUT &lt;dest&gt; &lt;user&gt; &lt;ttl&gt;</c> (OPTIONAL). <paramref name="User"/> queried the route
/// to <paramref name="Dest"/>. A <paramref name="Ttl"/> &gt; 0 means forward to the next hop.
/// </summary>
public sealed record HostRoute(string Dest, string User, int Ttl) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "ROUT";

    /// <inheritdoc/>
    public override string Emit() =>
        $"{Dest} {User} {Ttl.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// <c>/..SYSI &lt;user&gt; &lt;host&gt;|all</c> (OPTIONAL/SUGGESTED). <paramref name="User"/> requests system
/// information for <paramref name="Host"/> (or all systems).
/// </summary>
public sealed record HostSysInfo(string User, string Host) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "SYSI";

    /// <inheritdoc/>
    public override string Emit() => $"{User} {Host}";
}

/// <summary>
/// <c>/..LOOP &lt;host&gt;</c> (OPTIONAL). A routing loop was discovered; the receiving connection should be
/// dropped. <paramref name="Detail"/> holds the loop info (SPECS documents one host; the saupp impl carries
/// several whitespace-separated tokens, so the whole tail is preserved).
/// </summary>
public sealed record HostLoop(string Detail) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "LOOP";

    /// <inheritdoc/>
    public override string Emit() => Detail;
}

/// <summary>
/// <c>/..ECMD &lt;user&gt; &lt;cmdname&gt; [parameters]</c> (OPTIONAL). Extended command: when
/// <paramref name="User"/> is <c>conversd</c> this advertises command <paramref name="CommandName"/>;
/// otherwise it requests execution of it.
/// </summary>
public sealed record HostExtendedCommand(string User, string CommandName, string Parameters) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "ECMD";

    /// <inheritdoc/>
    public override string Emit() =>
        Parameters.Length == 0 ? $"{User} {CommandName}" : $"{User} {CommandName} {Parameters}";
}

/// <summary>
/// <c>/..HELP &lt;user&gt; &lt;cmdname&gt;</c> (OPTIONAL). A request from <paramref name="User"/> for the help
/// text of extended command <paramref name="CommandName"/>.
/// </summary>
public sealed record HostHelp(string User, string CommandName) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "HELP";

    /// <inheritdoc/>
    public override string Emit() => $"{User} {CommandName}";
}

/// <summary>
/// <c>/..UADD &lt;user&gt; &lt;host&gt; &lt;nickname&gt; &lt;channel&gt; [text]</c> (OPTIONAL). Primarily sets
/// the <paramref name="Nickname"/> for user@host (TNOS-compat); <paramref name="Text"/> is the personal
/// description.
/// </summary>
public sealed record HostUserAdd(
    string User, string Host, string Nickname, int Channel, string Text) : HostCommand
{
    /// <inheritdoc/>
    public override string Verb => "UADD";

    /// <inheritdoc/>
    public override string Emit()
    {
        string body = $"{User} {Host} {Nickname} {Channel.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Text.Length == 0 ? body : $"{body} {Text}";
    }
}

/// <summary>
/// Any <c>/..</c> command this leaf does not model. The raw command body (verb + remaining bytes, without
/// the <c>/..</c> prefix and terminator) is preserved so it round-trips losslessly — the SPECS golden
/// rule. For a strict leaf there is no other host to relay it to, but the codec must not mangle it.
/// </summary>
public sealed record UnknownHostCommand : HostCommand
{
    /// <summary>Create an unknown host command from its verb and raw body.</summary>
    public UnknownHostCommand(string verb, string body)
    {
        Verb = verb;
        Body = body;
    }

    /// <inheritdoc/>
    public override string Verb { get; }

    /// <summary>The raw command body (everything after the verb), preserved for lossless relay.</summary>
    public string Body { get; }

    /// <inheritdoc/>
    // Emit() returns the body only (no verb); HostCommandCodec.Format prepends "Verb " for every command.
    public override string Emit() => Body;
}
