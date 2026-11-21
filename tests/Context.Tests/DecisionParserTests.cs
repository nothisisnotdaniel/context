namespace Context.Tests;

// 343 AC-1, AC-2: #!d decision paragraphs and #!d1234 references in Markdown text.
public sealed class DecisionParserTests
{
    private static (int Line, int? Number, string Text)[] Parse(string text) =>
        DecisionParser.Parse(text).Select(d => (d.Line, d.Number, d.Text)).ToArray();

    // AC-1: bare and numbered markers at the start of a note, after a blank line, a heading or a fence, and as list items.
    [Fact]
    public void Parse_FindsMarkedParagraphs_WithNumbersAndLines()
    {
        string text = "#!d first\n\n#!d12 second\n# Head\n#!d third\n```\ncode\n```\n#!d4\tfourth\n- #!d fifth\n  * #!d6 sixth";

        Assert.Equal(
            [(1, null, "first"), (3, 12, "second"), (5, null, "third"), (9, 4, "fourth"), (10, null, "fifth"), (11, 6, "sixth")],
            Parse(text));
    }

    // AC-1: the paragraph runs to the line before a blank, heading, fence or list line; each line collapsed, joined with \n.
    [Theory]
    [InlineData("#!d One\n  two   words \nthree\n\nafter", "One\ntwo words\nthree")]
    [InlineData("#!d One\ntwo\n# Head", "One\ntwo")]
    [InlineData("#!d One\ntwo\n```\ncode", "One\ntwo")]
    [InlineData("#!d One\ntwo\n- item", "One\ntwo")]
    [InlineData("#!d One\ntwo\n- #!d5 other", "One\ntwo")]
    [InlineData("#!d\nOne\ntwo", "One\ntwo")]
    [InlineData("#!d   \t\n  One  ", "One")]
    public void Parse_ParagraphText(string text, string expected)
    {
        ParsedDecision decision = DecisionParser.Parse(text)[0];
        Assert.Equal(1, decision.Line);
        Assert.Equal(expected, decision.Text);
        Assert.Equal(expected.Split('\n')[0], decision.Headline);
    }

    // AC-1: after the title and the attribute block the first body line still counts.
    [Fact]
    public void Parse_FirstBodyLine_AfterTitleAndAttributes()
    {
        Assert.Equal([(5, 7, "x")], Parse("# Plan\n\nstatus:: open\nowner:: Anna\n#!d7 x"));
    }

    // AC-1: an empty text, a bad digit rule, a marker in the middle of a paragraph, inside a fence, a numbered list and
    // the old Decision: syntax are no decisions.
    [Theory]
    [InlineData("#!d")]
    [InlineData("#!d12")]
    [InlineData("- #!d5")]
    [InlineData("#!d   ")]
    [InlineData("#!d12x text")]
    [InlineData("#!d0 text")]
    [InlineData("#!d01 text")]
    [InlineData("#!d1234567890 text")]
    [InlineData("#!dx text")]
    [InlineData("text\n#!d mid")]
    [InlineData("text #!d mid")]
    [InlineData("```\n#!d inside\n```")]
    [InlineData("1. #!d x")]
    [InlineData("-#!d x")]
    [InlineData("Decision: x")]
    [InlineData("- Decision: x")]
    public void Parse_NotADecision(string text)
    {
        Assert.Empty(DecisionParser.Parse(text));
    }

    // AC-1: nine digits are the most; the number is read as an integer.
    [Fact]
    public void Parse_NineDigits_Highest()
    {
        Assert.Equal([(1, 999_999_999, "x")], Parse("#!d999999999 x"));
    }

    // AC-1: CRLF and a BOM keep the same paragraphs and lines, and the marker index points at the '#'.
    [Fact]
    public void Parse_CrlfAndBom()
    {
        string text = "﻿#!d3 One\r\ntwo\r\n\r\n#!d next\r\n";
        IReadOnlyList<ParsedDecision> decisions = DecisionParser.Parse(text);

        Assert.Equal([(1, (int?)3, "One\ntwo"), (4, null, "next")], decisions.Select(d => (d.Line, d.Number, d.Text)).ToArray());
        Assert.Equal(1, decisions[0].MarkerIndex);
        Assert.Equal('#', text[decisions[1].MarkerIndex]);
    }

    // AC-1: the text is cut to 1000 chars, never inside a surrogate pair.
    [Fact]
    public void Parse_LongText_CutWithoutSplittingSurrogate()
    {
        string plain = "#!d " + new string('a', 1200);
        Assert.Equal(DecisionParser.MaxTextLength, Assert.Single(DecisionParser.Parse(plain)).Text.Length);

        string pair = "#!d " + new string('a', 999) + "\U0001F600" + "tail";
        string text = Assert.Single(DecisionParser.Parse(pair)).Text;
        Assert.Equal(999, text.Length);
        Assert.False(char.IsHighSurrogate(text[^1]));
    }

    // AC-2: references at a line start or after whitespace, distinct in first-seen order, not the paragraph's marker.
    [Fact]
    public void References_DistinctInOrder_NotMarkers()
    {
        string text = "#!d5 we ship, see #!d12 and #!d7.\n#!d12 again\n\n#!d9";

        Assert.Equal([12, 7, 9], DecisionParser.References(text));
    }

    // AC-2: what is no reference: in code, glued to a word, followed by a letter, digit, '-', '_' or '#', a bad number.
    [Theory]
    [InlineData("see `#!d12` here")]
    [InlineData("```\n#!d12\n```")]
    [InlineData("x#!d12")]
    [InlineData("see #!d12x")]
    [InlineData("see #!d12-3")]
    [InlineData("see #!d12_")]
    [InlineData("see #!d12#")]
    [InlineData("see #!d0")]
    [InlineData("see #!d")]
    [InlineData("see #!d1234567890")]
    public void References_None(string text)
    {
        Assert.Empty(DecisionParser.References(text));
    }

    // AC-2: punctuation after a reference is fine, a BOM before one too; CRLF lines are read.
    [Theory]
    [InlineData("see #!d12.", 12)]
    [InlineData("(see) #!d12, ok", 12)]
    [InlineData("﻿#!d12", 12)]
    [InlineData("a\r\n#!d12\r\n", 12)]
    [InlineData("text\n#!d12 mid paragraph", 12)]
    [InlineData("`a` #!d3 `b`", 3)]
    public void References_One(string text, int number)
    {
        Assert.Equal([number], DecisionParser.References(text));
    }

    // AC-2: the other parsers find nothing in a marker or a reference.
    [Theory]
    [InlineData("#!d")]
    [InlineData("#!d12")]
    [InlineData("- #!d5 x")]
    public void OtherParsers_FindNothing(string text)
    {
        Assert.Empty(TagParser.Parse(text));
        Assert.Empty(NoteLinkParser.Parse(text));
        Assert.Empty(WorkItemParser.Parse(text));
        Assert.Empty(PersonParser.Parse(text));
    }

    // AC-5: StripMarkers removes the numbered decisions' markers (and a whole-line marker's line ending); bare markers,
    // a lone reference paragraph and references stay; CRLF and the BOM stay.
    [Theory]
    [InlineData("#!d12 We ship.\nsee #!d3", "We ship.\nsee #!d3")]
    [InlineData("#!d12\nWe ship.", "We ship.")]
    [InlineData("#!d12\r\nWe ship.\r\n", "We ship.\r\n")]
    [InlineData("- #!d12 We ship.", "- We ship.")]
    [InlineData("#!d We ship.", "#!d We ship.")]
    [InlineData("#!d12", "#!d12")]
    [InlineData("﻿#!d4  x\n\n#!d5\ty", "﻿x\n\ny")]
    public void StripMarkers(string text, string expected)
    {
        Assert.Equal(expected, DecisionParser.StripMarkers(text));
    }

    // AC-11: a typed number, with or without the token.
    [Theory]
    [InlineData("1234", true, 1234)]
    [InlineData(" #!d1234 ", true, 1234)]
    [InlineData("#!d0", false, 0)]
    [InlineData("01", false, 0)]
    [InlineData("12a", false, 0)]
    [InlineData("1234567890", false, 0)]
    [InlineData("work", false, 0)]
    public void TryParseNumber(string text, bool ok, int number)
    {
        Assert.Equal(ok, DecisionParser.TryParseNumber(text, out int parsed));
        Assert.Equal(number, parsed);
    }

    // The record never prints its raw text.
    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(ParsedDecision), new ParsedDecision(1, 2, "secret", 0, 4, 5).ToString());
    }
}
