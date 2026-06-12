using Convers.Console;

namespace Convers.Console.Tests;

/// <summary>The conversd capital-letter abbreviation rule for classic <c>/</c>-commands.</summary>
public class ClassicCommandsTests
{
    [Theory]
    [InlineData("personal", "personal")]   // full word
    [InlineData("PERSONAL", "personal")]   // case-insensitive
    [InlineData("pe", "personal")]         // minimum abbreviation (/PErsonal)
    [InlineData("pers", "personal")]
    [InlineData("note", "personal")]       // alias
    [InlineData("topic", "topic")]
    [InlineData("to", "topic")]            // /TOpic minimum
    [InlineData("who", "who")]
    [InlineData("wh", "who")]              // /Who minimum
    [InlineData("write", "msg")]
    [InlineData("wr", "msg")]              // /WRite minimum (so wr != who)
    [InlineData("quit", "quit")]
    [InlineData("qu", "quit")]
    [InlineData("bye", "quit")]
    [InlineData("by", "quit")]
    [InlineData("leave", "leave")]
    [InlineData("le", "leave")]
    public void Resolve_HonoursMinimumAbbreviation(string word, string verb) =>
        Assert.Equal(verb, ClassicCommands.Resolve(word));

    [Fact]
    public void Resolve_QuestionMark_IsHelp() => Assert.Equal("help", ClassicCommands.Resolve("?"));

    [Theory]
    [InlineData("t")]      // shorter than /TOpic's "to" minimum
    [InlineData("w")]      // ambiguous-ish; below any minimum
    [InlineData("p")]      // below /PErsonal minimum
    [InlineData("zzz")]    // not a command
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_BelowMinimumOrUnknown_IsNull(string? word) =>
        Assert.Null(ClassicCommands.Resolve(word));

    [Fact]
    public void Resolve_LongerThanName_IsNull() =>
        // "joins" is longer than the command name "join".
        Assert.Null(ClassicCommands.Resolve("joins"));
}
