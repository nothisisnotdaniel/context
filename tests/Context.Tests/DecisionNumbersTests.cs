namespace Context.Tests;

// 343 AC-4, AC-5: numbering bare markers and stripping markers a note doesn't own, as pure text.
public sealed class DecisionNumbersTests
{
    private static int Never(int number) => throw new InvalidOperationException("next must not be called");

    // AC-4: bare markers get consecutive numbers in document order; next is called once with their count.
    [Fact]
    public void Apply_NumbersBareMarkers_InOrder()
    {
        List<int> calls = [];
        DecisionEdits edits = DecisionNumbers.Apply(
            "#!d one\n\n- #!d two\n\n#!d7 kept\n\n#!d three",
            _ => false,
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
        DecisionEdits edits = DecisionNumbers.Apply(text, _ => false, _ => 5);

        Assert.Equal("﻿#!d5\r\nOne\r\n\r\n#!d6\t two  \r\n\r\n#!d\r\n", edits.Text);
    }

    // AC-4: nothing to do gives the same instance and never asks for numbers.
    [Fact]
    public void Apply_NothingToDo_SameText()
    {
        string text = "#!d12 x\nsee #!d3\n\nDecision: old";
        DecisionEdits edits = DecisionNumbers.Apply(text, _ => false, Never);

        Assert.Same(text, edits.Text);
        Assert.False(edits.Changed);
    }

    // AC-5: a number owned elsewhere loses its marker; the paragraph and every reference stay.
    [Fact]
    public void Apply_OwnedElsewhere_Stripped()
    {
        DecisionEdits edits = DecisionNumbers.Apply("#!d1234 x\nsee #!d1234\n\n#!d9 mine", n => n == 1234, Never);

        Assert.Equal("x\nsee #!d1234\n\n#!d9 mine", edits.Text);
        Assert.Equal([1234], edits.Strip);
        Assert.Empty(edits.Bare);
    }

    // AC-5: a second marker with the same number in the same note loses to the first.
    [Fact]
    public void Apply_RepeatInNote_LaterStripped()
    {
        DecisionEdits edits = DecisionNumbers.Apply("#!d5 first\n\n#!d5 second\n\n#!d new", _ => false, _ => 6);

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
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionNumbers.Apply("#!d a\n\n#!d b", _ => false, _ => first));
    }

    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(DecisionEdits), new DecisionEdits("secret", [], []).ToString());
    }
}
