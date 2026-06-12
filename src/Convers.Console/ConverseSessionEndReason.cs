namespace Convers.Console;

/// <summary>Why a console session ended, so the Host can disconnect or clean up.</summary>
public enum ConverseSessionEndReason
{
    /// <summary>The user signed off (<c>quit</c> / <c>/quit</c> / <c>leave</c> from the last channel).</summary>
    Quit = 0,

    /// <summary>The remote station vanished (the terminal reported a drop).</summary>
    Drop = 1,
}
