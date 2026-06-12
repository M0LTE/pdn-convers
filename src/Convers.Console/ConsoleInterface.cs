namespace Convers.Console;

/// <summary>
/// Which user-input surface a session presents (design.md decision 9). The mode is an <b>input</b> to
/// the session, chosen by the Host at connect from the per-user preference it stores — the console
/// does not own that preference, it only honours the surface it is told to use. The uplink wire is
/// unaffected either way: both surfaces parse to the same Core <see cref="Convers.Core.ConversEvent"/>s.
/// </summary>
public enum ConsoleInterface
{
    /// <summary>
    /// Plain-language chat by default: canonical word commands (<c>join</c>, <c>say</c>, <c>who</c>,
    /// <c>msg</c>, <c>topic</c>, <c>pers</c>, <c>leave</c>, <c>quit</c>, <c>help</c>), any unambiguous
    /// prefix, and bare text said to the current channel. Sentences, not <c>/</c>-folklore.
    /// </summary>
    Plain = 0,

    /// <summary>
    /// The literal conversd <c>/</c>-command surface (<c>/name</c>, <c>/msg</c>, <c>/join</c>,
    /// <c>/who</c>, <c>/topic</c>, <c>/pers</c>, <c>/quit</c>, …) for power users and legacy/automated
    /// (Winpack-era) clients. Bare text (no leading <c>/</c>) is still said to the current channel.
    /// </summary>
    Classic = 1,
}
