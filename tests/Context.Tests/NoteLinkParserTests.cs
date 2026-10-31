namespace Context.Tests;

public sealed class NoteLinkParserTests
{
    // AC-1
    [Fact]
    public void Parse_SpecExample_TrimsAndKeepsOrder()
    {
        Assert.Equal(["Project Alpha", "b.md"], NoteLinkParser.Parse("See [[Project Alpha]], and [[ b.md ]]."));
    }

    // AC-1
    [Theory]
    [InlineData("[[a]]", "a")]
    [InlineData("x[[a]]", "a")]
    [InlineData("x[[a]]y", "a")]
    [InlineData("[[a  b]]", "a  b")]
    [InlineData("[[\ta b ]]", "a b")]
    [InlineData("[[a]]]", "a")]
    [InlineData("[[[a]]", "a")]
    [InlineData("[[Note|label]]", "Note|label")]
    [InlineData("[[Note#Heading]]", "Note#Heading")]
    [InlineData("![[embed]]", "embed")]
    [InlineData("line\r\n[[a]]\r\n", "a")]
    [InlineData("[[a]] `code`", "a")]
    public void Parse_FindsLink(string markdown, string target)
    {
        Assert.Equal([target], NoteLinkParser.Parse(markdown));
    }

    // AC-1: distinct ignoring case, first spelling wins.
    [Fact]
    public void Parse_DistinctIgnoringCase_FirstSpellingKept()
    {
        Assert.Equal(["Beta", "a"], NoteLinkParser.Parse("[[Beta]] [[a]] [[BETA]]\n[[ beta ]] [[A]]"));
    }

    // AC-1: 103 characters is the longest target.
    [Fact]
    public void Parse_LengthLimit()
    {
        string longest = new string('x', NoteLinkParser.MaxTargetLength);
        string tooLong = new string('y', NoteLinkParser.MaxTargetLength + 1);

        Assert.Equal(103, NoteLinkParser.MaxTargetLength);
        Assert.Equal([longest], NoteLinkParser.Parse($"[[{longest}]]"));
        Assert.Equal([longest], NoteLinkParser.Parse($"[[  {longest}  ]]"));
        Assert.Empty(NoteLinkParser.Parse($"[[{tooLong}]]"));
    }

    // AC-2
    [Theory]
    [InlineData("[[]]")]
    [InlineData("[[   ]]")]
    [InlineData("[[a]")]
    [InlineData("[[a] ]")]
    [InlineData("[[a\n]]")]
    [InlineData("[[a [b]]")]
    [InlineData("[a]]")]
    [InlineData("[ [a]]")]
    [InlineData("#[a b]")]
    [InlineData("#[[a]]")]
    [InlineData("`[[a]]`")]
    [InlineData("x ``[[a]]`` y")]
    [InlineData("```\n[[a]]\n```")]
    [InlineData("  ```md\n[[a]]")]
    [InlineData("")]
    public void Parse_NoLink(string markdown)
    {
        Assert.Empty(NoteLinkParser.Parse(markdown));
    }

    // AC-2: fences and code spans hide links, text around them still counts.
    [Fact]
    public void Parse_SkipsCodeButNotTheRest()
    {
        string markdown = "[[before]]\n```\n[[inside]]\n```\n`[[span]]` [[after]]";

        Assert.Equal(["before", "after"], NoteLinkParser.Parse(markdown));
    }

    // AC-2: an unmatched backtick is plain text, so the link after it is found.
    [Fact]
    public void Parse_UnmatchedBacktick_IsPlainText()
    {
        Assert.Equal(["a"], NoteLinkParser.Parse("` [[a]]"));
    }

    // AC-2: links and tags never mix.
    [Fact]
    public void Parse_TagsAndLinksStaySeparate()
    {
        const string Markdown = "#[a b] [[c d]] #work";

        Assert.Equal(["c d"], NoteLinkParser.Parse(Markdown));
        Assert.Equal(["a b", "work"], TagParser.Parse(Markdown));
    }

    // Security: a line full of "[[" (no ']' at all, or with many '[') is scanned in linear time.
    [Theory]
    [InlineData("[[")]
    [InlineData("[[a")]
    [InlineData("[[a]")]
    public void Parse_LongHostileLine_IsFast(string unit)
    {
        string line = string.Concat(Enumerable.Repeat(unit, 200_000)) + "]]";
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        NoteLinkParser.Parse(line);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }
}
