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

    // 164 AC-8: with the desktop palette's names, /today and /todo both stay for /tod, /to and /t.
    [Theory]
    [InlineData("/tod", new[] { "/today", "/todo" })]
    [InlineData("/toda", new[] { "/today", "/todo" })]
    [InlineData("/to", new[] { "/today", "/todo", "/tags" })]
    [InlineData("/t", new[] { "/tags", "/today", "/todo" })]
    [InlineData("/today", new[] { "/today", "/todo" })]
    public void Match_TodayNextToTodo(string line, string[] expected)
    {
        string[] names =
            ["/capture", "/current", "/export", "/find", "/import", "/new", "/open", "/save", "/settings", "/tags", "/today", "/todo"];

        Assert.Equal(expected, CommandMatcher.Match(names, n => n, line));
    }

    // 170 AC-10: with the desktop palette's names (now with /search), /search is found by its prefix and a typo.
    [Theory]
    [InlineData("/sea", new[] { "/search", "/settings" })]
    [InlineData("/search budget 2027", new[] { "/search" })]
    [InlineData("/serach", new[] { "/search" })]
    [InlineData("/sa", new[] { "/save", "/capture", "/search", "/settings", "/tags" })]
    public void Match_Search(string line, string[] expected)
    {
        string[] names =
        [
            "/capture", "/current", "/done", "/export", "/find", "/import", "/meeting", "/new", "/open", "/review", "/save",
            "/search", "/settings", "/tags", "/today", "/todo",
        ];

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

    // 168: a swapped pair counts as one edit, only to order ties.
    [Theory]
    [InlineData("open", "opne", 1)]
    [InlineData("done", "opne", 2)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("", "abc", 3)]
    public void SwapDistance_CountsASwapAsOneEdit(string a, string b, int expected)
    {
        Assert.Equal(expected, CommandMatcher.SwapDistance(a, b));
    }

    // 168: /opne matches /open and /done equally (Levenshtein 2), and the swapped /open ranks first.
    [Fact]
    public void Match_SwappedLetters_RankTheMeantCommandFirst()
    {
        string[] names = ["/done", "/open", "/today"];

        Assert.Equal(["/open", "/done"], CommandMatcher.Match(names, n => n, "/opne plan"));
    }
}
