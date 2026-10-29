namespace Context.Tests;

public sealed class TagParserTests
{
    // AC-1
    [Fact]
    public void Parse_DistinctLowerCase_InOrderOfFirstAppearance()
    {
        Assert.Equal(["work", "home-2"], TagParser.Parse("a #Work, b #work #home-2"));
    }

    // AC-1
    [Theory]
    [InlineData("#work", "work")]
    [InlineData("  #work", "work")]
    [InlineData("x\t#work.", "work")]
    [InlineData("#a_b-1x", "a_b-1x")]
    [InlineData("#Über", "über")]
    [InlineData("line one\n#second", "second")]
    [InlineData("line one\r\n#second\r\n", "second")]
    [InlineData("#work)", "work")]
    [InlineData("#work#home", "work")]
    public void Parse_FindsTag(string markdown, string tag)
    {
        Assert.Equal([tag], TagParser.Parse(markdown));
    }

    // AC-1
    [Theory]
    [InlineData("# Title")]
    [InlineData("## Sub")]
    [InlineData("#12")]
    [InlineData("#_x")]
    [InlineData("#-x")]
    [InlineData("a#b")]
    [InlineData("http://x/#frag")]
    [InlineData("(#paren)")]
    [InlineData("#")]
    [InlineData("")]
    public void Parse_NoTag(string markdown)
    {
        Assert.Empty(TagParser.Parse(markdown));
    }

    // AC-2
    [Fact]
    public void Parse_IgnoresFencedCodeBlocks()
    {
        string markdown = "#before\n```\n#inside\n```\n#after\n   ```csharp\n#code\n   ```\n#end";

        Assert.Equal(["before", "after", "end"], TagParser.Parse(markdown));
    }

    // AC-2
    [Fact]
    public void Parse_UnclosedFence_IgnoresRest()
    {
        Assert.Equal(["a"], TagParser.Parse("#a\n```\n#b\n#c"));
    }

    // AC-2
    [Theory]
    [InlineData("see `#code` and #real", "real")]
    [InlineData("see `` a #code ` b `` and #real", "real")]
    [InlineData("`#a` #real `#b`", "real")]
    [InlineData("an unclosed ` #real", "real")]
    public void Parse_IgnoresInlineCodeSpans(string markdown, string tag)
    {
        Assert.Equal([tag], TagParser.Parse(markdown));
    }

    // AC-2
    [Fact]
    public void Parse_TagOfFiftyCharacters_IsKept_LongerIsNot()
    {
        string fifty = "a" + new string('b', 49);
        string fiftyOne = "a" + new string('b', 50);

        Assert.Equal([fifty], TagParser.Parse("#" + fifty));
        Assert.Empty(TagParser.Parse("#" + fiftyOne));
        Assert.Equal(["ok"], TagParser.Parse("#" + fiftyOne + " #ok"));
    }

    // 024 AC-1
    [Fact]
    public void Parse_BracketTag_TrimmedCollapsedLowerCase()
    {
        Assert.Equal(["project alpha"], TagParser.Parse("#[Project  Alpha] x"));
    }

    // 024 AC-1
    [Theory]
    [InlineData("#[work]", "work")]
    [InlineData("  #[ a b ]", "a b")]
    [InlineData("x\t#[a\t\tb].", "a b")]
    [InlineData("#[a_b-1 x2]", "a_b-1 x2")]
    [InlineData("#[Über Straße]", "über straße")]
    [InlineData("line one\r\n#[two words]\r\n", "two words")]
    [InlineData("#[a b] and ] later", "a b")]
    [InlineData("#[a b]c", "a b")]
    [InlineData("#[1st] #[ok go]", "ok go")]
    [InlineData("#[a, b] #[ok go]", "ok go")]
    public void Parse_FindsBracketTag(string markdown, string tag)
    {
        Assert.Equal([tag], TagParser.Parse(markdown));
    }

    // 024 AC-2
    [Theory]
    [InlineData("#[]")]
    [InlineData("#[   ]")]
    [InlineData("#[1st]")]
    [InlineData("#[a, b]")]
    [InlineData("#[a")]
    [InlineData("#[a\nb]")]
    [InlineData("a#[b c]")]
    [InlineData("#[label](http://x)")]
    [InlineData("#[-x]")]
    [InlineData("#[_x]")]
    [InlineData("#[a.b]")]
    [InlineData("##[a b]")]
    [InlineData("#[a #[b")]
    public void Parse_NoBracketTag(string markdown)
    {
        Assert.Empty(TagParser.Parse(markdown));
    }

    // 024 AC-2
    [Fact]
    public void Parse_BracketTagOfFiftyCharacters_IsKept_LongerIsNot()
    {
        string fifty = "ab " + new string('c', 47);
        string fiftyOne = "ab " + new string('c', 48);

        Assert.Equal(50, fifty.Length);
        Assert.Equal([fifty], TagParser.Parse("#[" + fifty + "]"));
        Assert.Equal([fifty], TagParser.Parse("#[  AB   " + new string('C', 47) + "  ]"));
        Assert.Empty(TagParser.Parse("#[" + fiftyOne + "]"));
        Assert.Equal(["ok"], TagParser.Parse("#[" + fiftyOne + "] #ok"));
    }

    // 024 AC-2
    [Fact]
    public void Parse_IgnoresBracketTagsInFencedCodeBlocks()
    {
        string markdown = "#[before x]\n```\n#[inside x]\n```\n#[after x]";

        Assert.Equal(["before x", "after x"], TagParser.Parse(markdown));
    }

    // 024 AC-2
    [Theory]
    [InlineData("see `#[code x]` and #[real x]", "real x")]
    [InlineData("see `` a #[code x] ` b `` and #[real x]", "real x")]
    [InlineData("#[a `b] c` #[real x]", "real x")]
    [InlineData("an unclosed ` #[real x]", "real x")]
    public void Parse_IgnoresBracketTagsInInlineCodeSpans(string markdown, string tag)
    {
        Assert.Equal([tag], TagParser.Parse(markdown));
    }

    // 024 AC-3
    [Fact]
    public void Parse_BothForms_AreOneTag_InOrderOfFirstAppearance()
    {
        Assert.Equal(["work"], TagParser.Parse("#Work #[work] #[ WORK ]"));
        Assert.Equal(["a b", "c"], TagParser.Parse("#[a b] #c"));
        Assert.Equal(["c", "a b"], TagParser.Parse("#c #[A  b] #[a b]"));
    }

    // 024 Security: many "#[" with no "]" on a long line stays fast (one scan for "]").
    [Fact]
    public void Parse_ManyOpenBrackets_NoClose_FindsNothing()
    {
        string line = string.Concat(Enumerable.Repeat("#[a ", 50_000));

        Assert.Empty(TagParser.Parse(line));
        Assert.Equal(["x y"], TagParser.Parse(line + "#[x y]"));
    }

    // 024 AC-7
    [Theory]
    [InlineData("work", "#work")]
    [InlineData("project alpha", "#[project alpha]")]
    public void Format_UsesBracketsOnlyForSpaces(string tag, string shown)
    {
        Assert.Equal(shown, TagParser.Format(tag));
    }
}
