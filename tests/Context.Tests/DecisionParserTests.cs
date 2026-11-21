namespace Context.Tests;

// 349 AC-1, AC-7 (and 343's paragraph rules): !d decision paragraphs with their number tokens, #!d1234 and
// #!dab12-1234 references, and job 343's markers for the migration.
public sealed class DecisionParserTests
{
    private static (int Line, string? Prefix, int? Number, string Text)[] Parse(string text) =>
        DecisionParser.Parse(text).Select(d => (d.Line, d.Prefix, d.Number, d.Text)).ToArray();

    // AC-1: bare, prefixed and plain markers at the start of a note, after a blank line, a heading or a fence, and as
    // list items.
    [Fact]
    public void Parse_FindsMarkedParagraphs_WithTokensAndLines()
    {
        string text = "!d first\n\n!d ab12-12 second\n# Head\n!d third\n```\ncode\n```\n!d\t4 fourth\n- !d fifth\n  * !d AB12-6 sixth";

        Assert.Equal(
            [
                (1, null, null, "first"), (3, "ab12", 12, "second"), (5, null, null, "third"), (9, null, 4, "fourth"),
                (10, null, null, "fifth"), (11, "ab12", 6, "sixth"),
            ],
            Parse(text));
    }

    // AC-1: the paragraph runs to the line before a blank, heading, fence or list line; each line collapsed, joined with \n.
    [Theory]
    [InlineData("!d One\n  two   words \nthree\n\nafter", "One\ntwo words\nthree")]
    [InlineData("!d One\ntwo\n# Head", "One\ntwo")]
    [InlineData("!d One\ntwo\n```\ncode", "One\ntwo")]
    [InlineData("!d One\ntwo\n- item", "One\ntwo")]
    [InlineData("!d One\ntwo\n- !d other", "One\ntwo")]
    [InlineData("!d\nOne\ntwo", "One\ntwo")]
    [InlineData("!d ab12-5\nOne\ntwo", "One\ntwo")]
    [InlineData("!d   \t\n  One  ", "One")]
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
        Assert.Equal([(5, "ab12", 7, "x")], Parse("# Plan\n\nstatus:: open\ndecision-folder:: x\n!d ab12-7 x"));
    }

    // AC-1: !done, !dx, !D, a mid-paragraph !d, a #!d paragraph start (a reference), an empty text, inside a fence, a
    // numbered list and the old Decision: syntax are no decisions.
    [Theory]
    [InlineData("!done")]
    [InlineData("!done today")]
    [InlineData("!dx text")]
    [InlineData("!D text")]
    [InlineData("!d")]
    [InlineData("!d ab12-5")]
    [InlineData("!d 12")]
    [InlineData("- !d")]
    [InlineData("!d   ")]
    [InlineData("text\n!d mid")]
    [InlineData("text !d mid")]
    [InlineData("#!d text")]
    [InlineData("#!d12 text")]
    [InlineData("```\n!d inside\n```")]
    [InlineData("1. !d x")]
    [InlineData("-!d x")]
    [InlineData("Decision: x")]
    public void Parse_NotADecision(string text)
    {
        Assert.Empty(DecisionParser.Parse(text));
    }

    // AC-1: what is no number token stays text: a leading 0, ten digits, a bad prefix char (i, l, o, u), a short
    // prefix, a glued word; the token must be followed by a space, tab or the line end.
    [Theory]
    [InlineData("!d 0 x", "0 x")]
    [InlineData("!d 01 x", "01 x")]
    [InlineData("!d 1234567890 x", "1234567890 x")]
    [InlineData("!d ai12-5 x", "ai12-5 x")]
    [InlineData("!d ab1-5 x", "ab1-5 x")]
    [InlineData("!d ab12-0 x", "ab12-0 x")]
    [InlineData("!d ab12-5x more", "ab12-5x more")]
    [InlineData("!d 3people", "3people")]
    [InlineData("!d  ab12-5 x", "ab12-5 x")]
    public void Parse_NoToken_StaysText(string text, string expected)
    {
        ParsedDecision decision = Assert.Single(DecisionParser.Parse(text));
        Assert.True(decision.Bare);
        Assert.Equal(expected, decision.Text);
    }

    // AC-1: a plain number is a token (always renumbered by a save); nine digits are the most.
    [Fact]
    public void Parse_PlainNumber_AndNineDigits()
    {
        ParsedDecision plain = Assert.Single(DecisionParser.Parse("!d 3 people hired"));
        Assert.True(plain.Plain);
        Assert.Equal((3, "people hired"), (plain.Number!.Value, plain.Text));
        Assert.Equal([(1, "zz99", 999_999_999, "x")], Parse("!d ZZ99-999999999 x"));
    }

    // AC-1, AC-9: CRLF and a BOM keep the same paragraphs and lines; marker index, token index and length, marker end.
    [Fact]
    public void Parse_CrlfAndBom_Indexes()
    {
        string text = "﻿!d ab12-3 One\r\ntwo\r\n\r\n!d next\r\n";
        IReadOnlyList<ParsedDecision> decisions = DecisionParser.Parse(text);

        Assert.Equal([(1, "ab12", (int?)3, "One\ntwo"), (4, null, null, "next")], decisions.Select(d => (d.Line, d.Prefix, d.Number, d.Text)).ToArray());
        Assert.Equal(1, decisions[0].MarkerIndex);
        Assert.Equal(4, decisions[0].TokenIndex);
        Assert.Equal(6, decisions[0].TokenLength);
        Assert.Equal(10, decisions[0].MarkerEnd);
        Assert.Equal("ab12-3", decisions[0].Token);
        Assert.Equal('!', text[decisions[1].MarkerIndex]);
        Assert.Equal(decisions[1].MarkerIndex + 2, decisions[1].MarkerEnd);
        Assert.Null(decisions[1].Token);
    }

    // AC-1: the text is cut to 1000 chars, never inside a surrogate pair.
    [Fact]
    public void Parse_LongText_CutWithoutSplittingSurrogate()
    {
        string plain = "!d " + new string('a', 1200);
        Assert.Equal(DecisionParser.MaxTextLength, Assert.Single(DecisionParser.Parse(plain)).Text.Length);

        string pair = "!d " + new string('a', 999) + "\U0001F600" + "tail";
        string text = Assert.Single(DecisionParser.Parse(pair)).Text;
        Assert.Equal(999, text.Length);
        Assert.False(char.IsHighSurrogate(text[^1]));
    }

    // AC-10: job 343's markers are read for the migration only: index, length and number of #!d<n> and bare #!d.
    [Fact]
    public void LegacyMarkers_ReadsOldMarkers()
    {
        string text = "#!d12 We ship.\n\n- #!d bare one\n\nsee #!d5\n\n#!d9\n\n!d new";

        Assert.Equal([(0, 5, (int?)12), (18, 3, null)], DecisionParser.LegacyMarkers(text));
    }

    // AC-7: references at a line start or after whitespace, distinct in first-seen order, prefix lower case; a
    // decision's marker never is one, and #!d at a paragraph start is a reference.
    [Fact]
    public void References_DistinctInOrder_NotMarkers()
    {
        string text = "!d ab12-5 we ship, see #!d12 and #!dAB12-7.\n#!d12 again\n\n#!d9\n\n#!dab12-7";

        Assert.Equal(
            [new DecisionRef(null, 12), new DecisionRef("ab12", 7), new DecisionRef(null, 9)],
            DecisionParser.References(text));
    }

    // AC-7: what is no reference: in code, glued to a word, followed by a letter, digit, '-', '_' or '#', a bad number
    // or prefix.
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
    [InlineData("see #!dab12-")]
    [InlineData("see #!dab12-5x")]
    [InlineData("see #!dao12-5")]
    [InlineData("see !d ab12-5")]
    public void References_None(string text)
    {
        Assert.Empty(DecisionParser.References(text));
    }

    // AC-7: punctuation after a reference is fine, a BOM before one too; CRLF lines are read.
    [Theory]
    [InlineData("see #!d12.", null, 12)]
    [InlineData("(see) #!d12, ok", null, 12)]
    [InlineData("﻿#!d12", null, 12)]
    [InlineData("a\r\n#!dab12-12\r\n", "ab12", 12)]
    [InlineData("text\n#!d12 mid paragraph", null, 12)]
    [InlineData("`a` #!dZZ99-3 `b`", "zz99", 3)]
    public void References_One(string text, string? prefix, int number)
    {
        Assert.Equal([new DecisionRef(prefix, number)], DecisionParser.References(text));
    }

    // AC-1: the other parsers find nothing in a marker or a reference.
    [Theory]
    [InlineData("!d")]
    [InlineData("!d ab12-5 x")]
    [InlineData("#!d12")]
    [InlineData("#!dab12-12")]
    [InlineData("- !d x")]
    public void OtherParsers_FindNothing(string text)
    {
        Assert.Empty(TagParser.Parse(text));
        Assert.Empty(NoteLinkParser.Parse(text));
        Assert.Empty(WorkItemParser.Parse(text));
        Assert.Empty(PersonParser.Parse(text));
    }

    // AC-12: a typed reference, with or without the token, any case.
    [Theory]
    [InlineData("1234", true, null, 1234)]
    [InlineData(" #!d1234 ", true, null, 1234)]
    [InlineData("AB12-1234", true, "ab12", 1234)]
    [InlineData("#!dab12-1234", true, "ab12", 1234)]
    [InlineData("#!D1234", true, null, 1234)]
    [InlineData("#!d0", false, null, 0)]
    [InlineData("01", false, null, 0)]
    [InlineData("12a", false, null, 0)]
    [InlineData("ab12-", false, null, 0)]
    [InlineData("al12-3", false, null, 0)]
    [InlineData("1234567890", false, null, 0)]
    [InlineData("work", false, null, 0)]
    public void TryParseReference(string text, bool ok, string? prefix, int number)
    {
        Assert.Equal(ok, DecisionParser.TryParseReference(text, out DecisionRef parsed));
        Assert.Equal(ok ? new DecisionRef(prefix, number) : default, parsed);
    }

    // AC-9, AC-12: labels and reference forms.
    [Fact]
    public void LabelAndFormat()
    {
        Assert.Equal("1234", DecisionParser.Label(null, 1234));
        Assert.Equal("ab12-1234", DecisionParser.Label("ab12", 1234));
        Assert.Equal("#!d1234", DecisionParser.Format(1234));
        Assert.Equal("#!dab12-1234", DecisionParser.Format("ab12", 1234));
        Assert.Equal("ab12-5", new DecisionRef("ab12", 5).Text);
    }

    // The record never prints its raw text.
    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(ParsedDecision), new ParsedDecision(1, "ab12", 2, "secret", 0, 3, 6).ToString());
    }
}
