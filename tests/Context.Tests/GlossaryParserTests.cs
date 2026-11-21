namespace Context.Tests;

public sealed class GlossaryParserTests
{
    // 380 AC-1: accepted lines report their 0-based line number and the collapsed term and meaning.
    [Fact]
    public void Parse_AcceptsTermLines_WithLineNumberAndCollapsedText()
    {
        IReadOnlyList<GlossaryLine> found = GlossaryParser.Parse(
            "# Words\nAPI:: Application programming interface\nContext  window::   Tokens   a model sees  \n");

        Assert.Equal(
            [(1, "API", "Application programming interface"), (2, "Context window", "Tokens a model sees")],
            found.Select(l => (l.Line, l.Term, l.Meaning)));
    }

    // 380 AC-1: lower-case keys, list items, indented lines, a space before ::, an empty meaning, `[ ] # in the term.
    [Theory]
    [InlineData("type:: person")]
    [InlineData("- API:: x")]
    [InlineData(" API:: x")]
    [InlineData("\tAPI:: x")]
    [InlineData("API :: x")]
    [InlineData("API::")]
    [InlineData("API::   ")]
    [InlineData("API")]
    [InlineData("A`b:: x")]
    [InlineData("A[b]:: x")]
    [InlineData("A#b:: x")]
    [InlineData("1API:: x")]
    public void Parse_RejectsNonGlossaryLines(string line)
    {
        Assert.Empty(GlossaryParser.Parse(line));
    }

    // 380 AC-1: a term over 100 chars and a meaning over 400 chars are no glossary line; the limits themselves are.
    [Fact]
    public void Parse_RejectsTooLongTermOrMeaning_AcceptsTheLimits()
    {
        string term = "T" + new string('x', GlossaryParser.MaxTermLength - 1);
        string meaning = new string('m', GlossaryParser.MaxMeaningLength);

        Assert.Single(GlossaryParser.Parse(term + ":: ok"));
        Assert.Empty(GlossaryParser.Parse(term + "x:: ok"));
        Assert.Single(GlossaryParser.Parse("API:: " + meaning));
        Assert.Empty(GlossaryParser.Parse("API:: " + meaning + "m"));
    }

    // 380 AC-1: a line inside a fence is no glossary line; the line after the fence is, with its own line number.
    [Fact]
    public void Parse_SkipsFences()
    {
        GlossaryLine line = Assert.Single(GlossaryParser.Parse("```\nAPI:: in code\n```\nSdk:: kit\n"));

        Assert.Equal((3, "Sdk", "kit"), (line.Line, line.Term, line.Meaning));
    }

    // 380 AC-1: within a note, lines are distinct by term and meaning, both ignoring case; the first spelling stays.
    [Fact]
    public void Parse_DistinctByTermAndMeaning_IgnoringCase_FirstKept()
    {
        IReadOnlyList<GlossaryLine> found = GlossaryParser.Parse(
            "API:: Interface\nApi:: interface\nAPI:: Another meaning\n");

        Assert.Equal([(0, "API", "Interface"), (2, "API", "Another meaning")], found.Select(l => (l.Line, l.Term, l.Meaning)));
    }

    // 380: the term ends at the first ::, so the meaning may hold more; ToString never prints the raw text.
    [Fact]
    public void Parse_FirstSeparatorEndsTerm_ToStringIsTypeName()
    {
        GlossaryLine line = Assert.Single(GlossaryParser.Parse("Rule:: a:: b"));

        Assert.Equal(("Rule", "a:: b"), (line.Term, line.Meaning));
        Assert.Equal(nameof(GlossaryLine), line.ToString());
    }

    // 380: AttributeParser is unchanged: a lower-case key stays an attribute, an upper-case line was never one.
    [Fact]
    public void AttributeParser_IsUnchanged()
    {
        Assert.Single(AttributeParser.Parse("type:: person"));
        Assert.Empty(GlossaryParser.Parse("type:: person"));
    }
}
