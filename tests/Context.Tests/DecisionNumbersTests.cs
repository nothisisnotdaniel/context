namespace Context.Tests;

// 343 AC-4, AC-5: numbering bare markers and stripping markers a note doesn't own, as pure text.
public sealed class DecisionNumbersTests
{
    private static int? Never(int number) => throw new InvalidOperationException("next must not be called");

    private static DecisionHolder Mine(int number, string text) => DecisionHolder.None;

    // AC-4: bare markers get consecutive numbers in document order; next is called once with their count.
    [Fact]
    public void Apply_NumbersBareMarkers_InOrder()
    {
        List<int> calls = [];
        DecisionEdits edits = DecisionNumbers.Apply(
            "#!d one\n\n- #!d two\n\n#!d7 kept\n\n#!d three",
            Mine,
            count =>
            {
                calls.Add(count);
                return 40;
            });

        Assert.Equal("#!d40 one\n\n- #!d41 two\n\n#!d7 kept\n\n#!d42 three", edits.Text);
        Assert.Equal([40, 41, 42], edits.Bare);
        Assert.Empty(edits.Strip);
        Assert.True(edits.Changed);
        Assert.Equal([3], calls);
    }

    // AC-4: every other byte stays: CRLF, a BOM, tabs, a whole-line marker, a marker with no text (not a decision).
    [Fact]
    public void Apply_KeepsEveryOtherChar()
    {
        string text = "﻿#!d\r\nOne\r\n\r\n#!d\t two  \r\n\r\n#!d\r\n";
        DecisionEdits edits = DecisionNumbers.Apply(text, Mine, _ => 5);

        Assert.Equal("﻿#!d5\r\nOne\r\n\r\n#!d6\t two  \r\n\r\n#!d\r\n", edits.Text);
    }

    // AC-4: nothing to do gives the same instance and never asks for numbers.
    [Fact]
    public void Apply_NothingToDo_SameText()
    {
        string text = "#!d12 x\nsee #!d3\n\nDecision: old";
        DecisionEdits edits = DecisionNumbers.Apply(text, Mine, Never);

        Assert.Same(text, edits.Text);
        Assert.False(edits.Changed);
    }

    // AC-5: a copy of a decision another note owns (same text) loses its marker; the paragraph and every reference stay.
    [Fact]
    public void Apply_CopyOfOwnedElsewhere_Stripped()
    {
        List<(int, string)> asked = [];
        DecisionEdits edits = DecisionNumbers.Apply(
            "#!d1234 x\nsee #!d1234\n\n#!d9 mine",
            (n, text) =>
            {
                asked.Add((n, text));
                return n == 1234 ? DecisionHolder.Copy : DecisionHolder.None;
            },
            Never);

        Assert.Equal("x\nsee #!d1234\n\n#!d9 mine", edits.Text);
        Assert.Equal([1234], edits.Strip);
        Assert.Empty(edits.Bare);
        Assert.Empty(edits.Kept);
        Assert.Equal([(1234, "x\nsee #!d1234"), (9, "mine")], asked);
    }

    // Round 2 feedback 3: a number another note owns with a different text keeps its marker (the owner may be the wrong
    // one) and is listed once; a later repeat in the same note still goes.
    [Fact]
    public void Apply_OwnedElsewhereWithOtherText_Kept()
    {
        string text = "#!d5 original\n\n#!d5 again";
        DecisionEdits edits = DecisionNumbers.Apply(text, (_, _) => DecisionHolder.Other, Never);

        Assert.Equal("#!d5 original\n\nagain", edits.Text);
        Assert.Equal([5], edits.Kept);
        Assert.Equal([5], edits.Strip);
        Assert.True(edits.Noteworthy);

        DecisionEdits alone = DecisionNumbers.Apply("#!d5 original", (_, _) => DecisionHolder.Other, Never);
        Assert.Equal("#!d5 original", alone.Text);
        Assert.False(alone.Changed);
        Assert.True(alone.Noteworthy);
        Assert.Equal([5], alone.Kept);
    }

    // Round 2 feedback 1: with no numbers left the bare markers stay as typed and are counted; copies still go.
    [Fact]
    public void Apply_NoNumbersLeft_BareStay()
    {
        DecisionEdits edits = DecisionNumbers.Apply(
            "#!d one\n\n#!d3 copy\n\n#!d two",
            (n, _) => n == 3 ? DecisionHolder.Copy : DecisionHolder.None,
            _ => null);

        Assert.Equal("#!d one\n\ncopy\n\n#!d two", edits.Text);
        Assert.Empty(edits.Bare);
        Assert.Equal(2, edits.Unnumbered);
        Assert.Equal([3], edits.Strip);

        string text = "#!d one";
        DecisionEdits none = DecisionNumbers.Apply(text, Mine, _ => null);
        Assert.Same(text, none.Text);
        Assert.False(none.Changed);
        Assert.True(none.Noteworthy);
        Assert.Equal(1, none.Unnumbered);
    }

    // AC-5: a second marker with the same number in the same note loses to the first.
    [Fact]
    public void Apply_RepeatInNote_LaterStripped()
    {
        DecisionEdits edits = DecisionNumbers.Apply("#!d5 first\n\n#!d5 second\n\n#!d new", Mine, _ => 6);

        Assert.Equal("#!d5 first\n\nsecond\n\n#!d6 new", edits.Text);
        Assert.Equal([5], edits.Strip);
        Assert.Equal([6], edits.Bare);
    }

    // AC-4: numbers out of range are refused.
    [Theory]
    [InlineData(0)]
    [InlineData(999_999_999)]
    public void Apply_OutOfRange_Throws(int first)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionNumbers.Apply("#!d a\n\n#!d b", Mine, _ => first));
    }

    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(DecisionEdits), new DecisionEdits("secret", [], []).ToString());
    }
}
