namespace Context.Tests;

// 094 AC-11: the command ranking, in Core for the desktop palette.
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

    // 145: the lines the removed console's registry was checked with, now on the matcher itself.
    [Theory]
    [InlineData("/", new[] { "/help", "/new", "/notes", "/quit", "/todo", "/todos" })]
    [InlineData("/n", new[] { "/new", "/notes" })]
    [InlineData("/nwe", new string[0])]
    [InlineData("/tod", new[] { "/todo", "/todos" })]
    [InlineData("/q", new[] { "/quit" })]
    public void Match_OverSimilarNames(string line, string[] expected)
    {
        string[] names = ["/help", "/quit", "/new", "/notes", "/todo", "/todos"];

        Assert.Equal(expected, CommandMatcher.Match(names, n => n, line));
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
