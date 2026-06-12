namespace Convers.Protocol;

/// <summary>
/// A parsed USER-surface input line — a convers <c>/</c>-command or a line of chat text. This is the
/// grammar a connected human (or legacy automated client) sends; the field layouts and accepted
/// abbreviations come from <c>conversd.c</c>'s <c>cmdtable</c> and <c>user.c</c> (and
/// <c>etc/convers.help</c>). Parse-only: this models the wire the leaf interprets, not the plain-language
/// console surface (that is Convers.Console, W3).
/// </summary>
/// <remarks>
/// Abbreviation rule (see <see cref="UserCommandCodec"/>): conversd matches the typed verb against its
/// command table <em>in table order</em> by prefix (<c>strncmp(name, arg, arglen)</c>), so the shortest
/// unambiguous prefix that hits the first matching table row wins — e.g. <c>/n</c> → <c>name</c>. The
/// codec reproduces the table order for the verbs it models.
/// </remarks>
public abstract record UserCommand;

/// <summary>
/// <c>/NAME &lt;call&gt; [channel]</c> (abbrev <c>/n</c>). UNKNOWN→USER login. <paramref name="Channel"/>
/// is optional (null ⇒ default channel; conversd uses 0). A leading <c>#</c> on the channel is accepted
/// and stripped.
/// </summary>
public sealed record NameCommand(string Call, int? Channel) : UserCommand;

/// <summary>
/// <c>/CHANNEL &lt;n&gt;</c> / <c>/JOIN &lt;n&gt;</c>. Join (or switch to) a channel. A leading <c>#</c> is
/// accepted and stripped. <paramref name="Channel"/> is null when no number was given (a bare query).
/// </summary>
public sealed record JoinCommand(int? Channel) : UserCommand;

/// <summary><c>/LEAVE</c>. Leave the current channel.</summary>
public sealed record LeaveCommand : UserCommand;

/// <summary><c>/QUIT</c> / <c>/BYE</c> / <c>/EXIT</c> [reason]. Sign off the connection.</summary>
public sealed record QuitCommand(string Reason) : UserCommand;

/// <summary><c>/WHO</c> / <c>/USERS</c> [argument]. List users (optionally filtered/scoped).</summary>
public sealed record WhoCommand(string Argument) : UserCommand;

/// <summary><c>/MSG &lt;to&gt; &lt;text&gt;</c> (also <c>/send</c>, <c>/write</c>). A private message.</summary>
public sealed record MsgCommand(string To, string Text) : UserCommand;

/// <summary>
/// <c>/TOPIC [text]</c>. Set the current channel's topic (empty text removes it / queries it).
/// </summary>
public sealed record TopicCommand(string Text) : UserCommand;

/// <summary>
/// <c>/PERSONAL [text]</c> (also <c>/note</c>; abbrev <c>/pers</c>). Set the user's personal description
/// text (empty removes it).
/// </summary>
public sealed record PersonalCommand(string Text) : UserCommand;

/// <summary><c>/NICKNAME &lt;nick&gt;</c>. Set the user's nickname.</summary>
public sealed record NicknameCommand(string Nickname) : UserCommand;

/// <summary><c>/INVITE &lt;user&gt; [channel]</c>. Invite a user to a channel.</summary>
public sealed record InviteCommand(string User, int? Channel) : UserCommand;

/// <summary><c>/AWAY [text]</c>. Mark away (empty text ⇒ back).</summary>
public sealed record AwayCommand(string Text) : UserCommand;

/// <summary><c>/BEEP</c> (also <c>/bell</c>). Toggle the audible bell on incoming messages.</summary>
public sealed record BeepCommand : UserCommand;

/// <summary>
/// <c>/ONLINE &lt;a|l|q&gt;</c> — a "who" usable from the UNKNOWN state (no login). The argument selects
/// the listing variant.
/// </summary>
public sealed record OnlineCommand(string Argument) : UserCommand;

/// <summary><c>/CSTAT</c> — the "links" command usable from the UNKNOWN state (no login).</summary>
public sealed record CstatCommand : UserCommand;

/// <summary><c>/OBSERVER</c> — enter the read-only OBSERVER state from UNKNOWN.</summary>
public sealed record ObserverCommand : UserCommand;

/// <summary>
/// A line of ordinary chat text (no leading <c>/</c>) — delivered to the current channel (or active
/// query). Carries the raw text verbatim.
/// </summary>
public sealed record ChatText(string Text) : UserCommand;

/// <summary>
/// A <c>/</c>-command this codec does not model (or an unknown/ambiguous verb). The raw verb (without the
/// leading slash) and the remaining arguments are preserved. conversd answers unknown user commands with
/// "Unknown command"; a leaf may surface or ignore these. Round-trips losslessly.
/// </summary>
public sealed record UnknownUserCommand(string Verb, string Arguments) : UserCommand;
