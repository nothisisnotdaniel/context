namespace Context.Tests;

// 180 AC-1, AC-2: Decision: lines in Markdown text.
public sealed class DecisionParserTests
{
    private static (int Line, string Text)[] Parse(string text) =>
        DecisionParser.Parse(text).Select(d => (d.Line, d.Text)).ToArray();

    // AC-1: the plain line, list markers, any case, leading spaces and tabs, with their 1-based line numbers.
    [Fact]
    public void Parse_FindsDecisionLines_WithLineNumbers()
    {
        string text = "# Notes\nDecision: x\n  - decision: y\r\n* DECISION:z\n+ Decision: w\n\t- Decision: v";

        Assert.Equal([(2, "x"), (3, "y"), (4, "z"), (5, "w"), (6, "v")], Parse(text));
    }

    // AC-1: an empty rest, a plural, text before the marker, a numbered list and a fenced block are no decisions.
    [Theory]
    [InlineData("Decision:")]
    [InlineData("Decision:   \t ")]
    [InlineData("- Decision:")]
    [InlineData("Decisions: x")]
    [InlineData("Re Decision: x")]
    [InlineData("1. Decision: x")]
    [InlineData("-Decision: x")]
    [InlineData("Decision x")]
    [InlineData("```\nDecision: x\n```")]
    [InlineData("  ```md\nDecision: x")]
    public void Parse_NotADecision(string text)
    {
        Assert.Empty(DecisionParser.Parse(text));
    }

    // AC-1: a decision after a closed fence keeps its line number in the whole text.
    [Fact]
    public void Parse_AfterFence_LineNumberCountsFencedLines()
    {
        Assert.Equal([(4, "after")], Parse("```\nDecision: inside\n```\nDecision: after"));
    }

    // AC-2: the text is trimmed and whitespace runs become one space.
    [Fact]
    public void Parse_TextTrimmedAndCollapsed()
    {
        Assert.Equal([(1, "ship it with @[Anna] now")], Parse("Decision:   ship \t it  with @[Anna]   now   "));
    }

    // AC-2: a 1500-character rest is cut to 1000 characters.
    [Fact]
    public void Parse_LongText_CutTo1000()
    {
        string text = Assert.Single(DecisionParser.Parse("Decision: " + new string('a', 1500))).Text;

        Assert.Equal(DecisionParser.MaxTextLength, text.Length);
        Assert.Equal(1000, DecisionParser.MaxTextLength);
        Assert.Equal(new string('a', 1000), text);
    }

    // AC-2: the cut never splits a surrogate pair: a pair across the limit is left out whole.
    [Fact]
    public void Parse_LongText_CutKeepsSurrogatePairs()
    {
        string rest = new string('a', 999) + string.Concat(Enumerable.Repeat("\U0001F600", 250));

        string text = Assert.Single(DecisionParser.Parse("Decision: " + rest)).Text;

        Assert.Equal(999, text.Length);
        Assert.False(char.IsHighSurrogate(text[^1]));
        Assert.Equal(new string('a', 999), text);
    }

    // AC-2: pairs that fit end exactly at the limit.
    [Fact]
    public void Parse_LongText_PairsEndingAtLimitKept()
    {
        string rest = string.Concat(Enumerable.Repeat("\U0001F600", 750));

        string text = Assert.Single(DecisionParser.Parse("Decision: " + rest)).Text;

        Assert.Equal(1000, text.Length);
        Assert.True(char.IsLowSurrogate(text[^1]));
    }

    [Fact]
    public void ParsedDecision_ToString_IsTypeName()
    {
        Assert.Equal("ParsedDecision", new ParsedDecision(1, "secret").ToString());
    }
}
