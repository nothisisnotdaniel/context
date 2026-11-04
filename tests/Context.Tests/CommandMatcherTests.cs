namespace Context.Tests;

// 094 AC-11: the console's command ranking, now in Core for the desktop palette too.
public sealed class CommandMatcherTests
{
    private static readonly string[] Palette = ["/current", "/open", "/save", "/tags", "/todo"];

    [Fact]
    public void Match_Typo_ListsOpen()
    {
        Assert.Equal(["/open"], CommandMatcher.Match(Palette, n => n, "/opne"));
    }

    [Fact]
    public void Match_Empty_EveryNameInOrder()
    {
        Assert.Equal(["/current", "/open", "/save", "/tags", "/todo"], CommandMatcher.Match(Palette.Reverse(), n => n, "/"));
    }

    [Theory]
    [InlineData("/t", new[] { "/tags", "/todo" })]
    [InlineData("/to", new[] { "/todo", "/tags" })]
    [InlineData("/sav extra words", new[] { "/save" })]
    [InlineData("SAVE", new[] { "/save" })]
    [InlineData("/xyzzy", new string[0])]
    public void Match_RanksLikeTheConsole(string line, string[] expected)
    {
        Assert.Equal(expected, CommandMatcher.Match(Palette, n => n, line));
    }

    // The console's registry gives the same order as the matcher over its commands.
    [Theory]
    [InlineData("/")]
    [InlineData("/n")]
    [InlineData("/nwe")]
    [InlineData("/tod")]
    [InlineData("/q")]
    public void Registry_DelegatesToMatcher(string line)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        registry.Register("/new", "x", _ => new CommandResult());
        registry.Register("/notes", "x", _ => new CommandResult());
        registry.Register("/todo", "x", _ => new CommandResult());
        registry.Register("/todos", "x", _ => new CommandResult());

        Assert.Equal(
            CommandMatcher.Match(registry.Commands, c => c.Name, line).Select(c => c.Name).ToArray(),
            registry.Match(line).Select(c => c.Name).ToArray());
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("open", "opne", 2)]
    [InlineData("kitten", "sitting", 3)]
    public void Levenshtein_CountsEdits(string a, string b, int expected)
    {
        Assert.Equal(expected, CommandMatcher.Levenshtein(a, b));
    }
}
