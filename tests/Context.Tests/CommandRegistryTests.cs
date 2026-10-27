using Spectre.Console.Testing;

namespace Context.Tests;

public class CommandRegistryTests
{
    private static CommandResult Nothing(string argument) => CommandResult.None;

    // AC-9
    [Fact]
    public void Register_SameNameTwice_Throws()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/note", "First", Nothing);

        Assert.Throws<ArgumentException>(() => registry.Register("/note", "Second", Nothing));
    }

    // AC-9
    [Fact]
    public void Register_SameNameDifferentCase_Throws()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/note", "First", Nothing);

        Assert.Throws<ArgumentException>(() => registry.Register("/NOTE", "Second", Nothing));
    }

    // AC-9
    [Fact]
    public void Default_HasHelp_DuplicateHelpThrows()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.Throws<ArgumentException>(() => registry.Register("/Help", "Again", Nothing));
    }

    [Theory]
    [InlineData("note")]
    [InlineData("/")]
    [InlineData("/two words")]
    public void Register_InvalidName_Throws(string name)
    {
        CommandRegistry registry = new CommandRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(name, "Bad", Nothing));
    }

    // AC-9
    [Fact]
    public void TryGet_IgnoresCase()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.True(registry.TryGet("/Help", out Command command));
        Assert.Equal("/help", command.Name);
    }

    // AC-9
    [Fact]
    public void TryGet_Unknown_ReturnsFalse()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();

        Assert.False(registry.TryGet("/nope", out _));
    }

    [Fact]
    public void Commands_AreOrderedByName()
    {
        CommandRegistry registry = new CommandRegistry();
        registry.Register("/zeta", "Z", Nothing);
        registry.Register("/alpha", "A", Nothing);

        Assert.Equal(["/alpha", "/zeta"], registry.Commands.Select(c => c.Name));
    }

    [Fact]
    public void RenderHelp_ListsEveryCommandWithDescription()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        TestConsole console = new TestConsole();

        console.Write(registry.RenderHelp());

        Assert.Contains("/help", console.Output);
        Assert.Contains("List all commands", console.Output);
        Assert.Contains("/quit", console.Output);
        Assert.Contains("Exit Context", console.Output);
    }

    // Command popup (job 010) --------------------------------------------------------------

    private static CommandRegistry WithDummies()
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        registry.Register("/next", "Next", Nothing);
        registry.Register("/prev", "Previous", Nothing);
        registry.Register("/today", "Today", Nothing);
        return registry;
    }

    // AC-1
    [Theory]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("", "", 0)]
    [InlineData("help", "help", 0)]
    [InlineData("hepl", "help", 2)]
    [InlineData("kitten", "sitting", 3)]
    public void Levenshtein_CountsEdits(string a, string b, int expected)
    {
        Assert.Equal(expected, CommandRegistry.Levenshtein(a, b));
    }

    // AC-2
    [Theory]
    [InlineData("/", "/help,/next,/prev,/quit,/today")]
    [InlineData("", "/help,/next,/prev,/quit,/today")]
    [InlineData("/t", "/today")]
    [InlineData("/T", "/today")]
    [InlineData("/zzzzz", "")]
    [InlineData("/he", "/help,/next")]
    public void Match_ReturnsExpectedCommands(string line, string expected)
    {
        string[] names = expected.Length == 0 ? [] : expected.Split(',');

        Assert.Equal(names, WithDummies().Match(line).Select(c => c.Name));
    }

    // AC-2
    [Theory]
    [InlineData("/hepl", "/help")]
    [InlineData("/HEPL", "/help")]
    [InlineData("/next foo", "/next")]
    [InlineData("/tdoay", "/today")]
    [InlineData("/qiut", "/quit")]
    public void Match_FirstIsBest(string line, string expected)
    {
        Assert.Equal(expected, WithDummies().Match(line)[0].Name);
    }

    // AC-2
    [Fact]
    public void Match_SortsByScoreBeforeName()
    {
        // "ne" is a prefix of /next (0) and one edit from "he" (/help scores 1).
        Assert.Equal(["/next", "/help"], WithDummies().Match("/ne").Select(c => c.Name));
    }
}
