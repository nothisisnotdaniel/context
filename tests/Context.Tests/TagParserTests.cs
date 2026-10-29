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
}
