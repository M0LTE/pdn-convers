namespace Convers.Console;

/// <summary>
/// The canonical plain-language command vocabulary (design.md decision 9: "Canonical commands are
/// words … any unambiguous prefix works"). Seed for wave W3's console surface — the full
/// sentence-style parser, paging and the per-user <c>classic</c> '/'-mode build on this.
/// </summary>
public static class ConsoleVerbs
{
    /// <summary>
    /// The canonical verbs, in help order. Words, not conversd '/'-folklore. <c>pers</c> is an
    /// unambiguous prefix of <c>personal</c>, so both spellings resolve (design.md decision 9 lists
    /// "pers/personal"); every verb's first letter is unique, so single-letter prefixes all work.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        "join",
        "say",
        "who",
        "msg",
        "topic",
        "personal",
        "away",
        "invite",
        "leave",
        "quit",
        "help",
    ];

    /// <summary>
    /// Resolves <paramref name="input"/> (case-insensitive) to a canonical verb by unambiguous
    /// prefix: returns the single verb that starts with <paramref name="input"/>, or <c>null</c>
    /// when nothing matches or the prefix is ambiguous (matches more than one).
    /// </summary>
    public static string? Resolve(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        string? match = null;
        foreach (string verb in All)
        {
            if (!verb.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Exact hit wins outright, even if it is also a prefix of nothing else.
            if (verb.Length == input.Length)
            {
                return verb;
            }

            if (match is not null)
            {
                return null; // ambiguous
            }

            match = verb;
        }

        return match;
    }
}
