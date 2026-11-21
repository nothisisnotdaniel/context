namespace Context.Tests;

// 349 AC-3, AC-4, AC-5: numbering !d markers with the folder's prefix and adding the decision-folder line, as pure text.
public sealed class DecisionNumbersTests
{
    private static readonly Guid Own = new("11111111-2222-3333-4444-555555555555");

    private static readonly Guid Other = new("99999999-8888-7777-6666-555555555555");

    private static readonly DecisionFolder Folder = new("ab12", Own, true);

    private static readonly string Line = "decision-folder:: " + Own.ToString("D");

    private static int? Never(int count) => throw new InvalidOperationException("next must not be called");

    private static bool KeepAll(int number, string text) => true;

    private static bool KeepNone(int number, string text) => false;

    // AC-3, AC-4: bare markers get consecutive own-prefix tokens in document order, next is called once with their
    // count, and the folder line goes after the title.
    [Fact]
    public void Apply_NumbersBareMarkers_InOrder_AddsFolderLine()
    {
        List<int> calls = [];
        DecisionEdits edits = DecisionNumbers.Apply(
            "# Plan\n!d one\n\n- !d two\n\n!d three",
            Folder,
            KeepAll,
            count =>
            {
                calls.Add(count);
                return 40;
            });

        Assert.Equal($"# Plan\n{Line}\n!d ab12-40 one\n\n- !d ab12-41 two\n\n!d ab12-42 three", edits.Text);
        Assert.Equal(
            [new DecisionSet(0, null, "ab12-40"), new DecisionSet(1, null, "ab12-41"), new DecisionSet(2, null, "ab12-42")],
            edits.Set);
        Assert.Equal(Own, edits.Folder);
        Assert.True(edits.Changed);
        Assert.Equal([3], calls);
    }

    // AC-3: a plain number, another prefix and an own token the callback refuses are replaced in place; an upper-case
    // own token the callback keeps stays as typed.
    [Fact]
    public void Apply_ReplacesPlainForeignAndRefused()
    {
        string text = $"{Line}\n!d 12 plain\n\n!d zz99-3 other\n\n!d ab12-7 refused\n\n!d AB12-8 kept";
        DecisionEdits edits = DecisionNumbers.Apply(text, Folder, (n, _) => n == 8, _ => 50);

        Assert.Equal(
            $"{Line}\n!d ab12-50 plain\n\n!d ab12-51 other\n\n!d ab12-52 refused\n\n!d AB12-8 kept",
            edits.Text);
        Assert.Equal(
            [new DecisionSet(0, "12", "ab12-50"), new DecisionSet(1, "zz99-3", "ab12-51"), new DecisionSet(2, "ab12-7", "ab12-52")],
            edits.Set);
        Assert.Null(edits.Folder);
    }

    // AC-3: every other byte stays: CRLF, a BOM, tabs, a whole-line marker; the folder line takes the note's ending.
    [Fact]
    public void Apply_KeepsEveryOtherChar()
    {
        string text = "﻿!d\r\nOne\r\n\r\n!d\t two  \r\n\r\n!d\r\n";
        DecisionEdits edits = DecisionNumbers.Apply(text, Folder, KeepAll, _ => 5);

        Assert.Equal($"﻿{Line}\r\n!d ab12-5\r\nOne\r\n\r\n!d ab12-6\t two  \r\n\r\n!d\r\n", edits.Text);
    }

    // AC-3: nothing to do gives the same instance and never asks for numbers.
    [Fact]
    public void Apply_NothingToDo_SameText()
    {
        string text = $"{Line}\n!d ab12-12 x\nsee #!d3\n\nDecision: old";
        DecisionEdits edits = DecisionNumbers.Apply(text, Folder, KeepAll, Never);

        Assert.Same(text, edits.Text);
        Assert.False(edits.Changed);
        Assert.False(edits.Noteworthy);
    }

    // AC-4: a kept own token in a note without the line still gets it; a note with no title gets it as line 1.
    [Fact]
    public void Apply_KeptToken_AddsLineAsFirstLine()
    {
        DecisionEdits edits = DecisionNumbers.Apply("!d ab12-4 x\n", Folder, KeepAll, Never);

        Assert.Equal($"{Line}\n!d ab12-4 x\n", edits.Text);
        Assert.Empty(edits.Set);
        Assert.Equal(Own, edits.Folder);
        Assert.True(edits.Changed);
    }

    // AC-4: after the title, the line joins an attribute block that follows blank lines.
    [Fact]
    public void Apply_FolderLine_JoinsAttributeBlock()
    {
        DecisionEdits edits = DecisionNumbers.Apply("# T\n\nstatus:: open\n\n!d x", Folder, KeepAll, _ => 1);

        Assert.Equal($"# T\n\n{Line}\nstatus:: open\n\n!d ab12-1 x", edits.Text);
    }

    // AC-4: a note of another folder is left as it is, bare markers stay bare, next is never called.
    [Fact]
    public void Apply_ForeignNote_Unchanged()
    {
        string text = $"# T\ndecision-folder:: {Other:D}\n!d bare\n\n!d 12 plain";
        DecisionEdits edits = DecisionNumbers.Apply(text, Folder, KeepNone, Never);

        Assert.Same(text, edits.Text);
        Assert.True(edits.Foreign);
        Assert.False(edits.Changed);
        Assert.True(edits.Noteworthy);
    }

    // AC-4: a decision-folder line whose value is no GUID counts as another folder's.
    [Fact]
    public void Apply_BadFolderLine_IsForeign()
    {
        Assert.True(DecisionNumbers.Apply("decision-folder:: nope\n!d x", Folder, KeepAll, Never).Foreign);
    }

    // AC-5: the same own token twice with the same text is one decision (both kept, the callback asked once); with
    // another text the repeat is renumbered.
    [Fact]
    public void Apply_RepeatInNote_SameTextKept_OtherTextReplaced()
    {
        int asked = 0;
        string text = $"{Line}\n!d ab12-5 x\n\n!d ab12-5 x\n\n!d ab12-5 y";
        DecisionEdits edits = DecisionNumbers.Apply(
            text,
            Folder,
            (_, _) =>
            {
                asked++;
                return true;
            },
            _ => 9);

        Assert.Equal($"{Line}\n!d ab12-5 x\n\n!d ab12-5 x\n\n!d ab12-9 y", edits.Text);
        Assert.Equal([new DecisionSet(2, "ab12-5", "ab12-9")], edits.Set);
        Assert.Equal(1, asked);
    }

    // AC-5: the callback gets the number and the paragraph's text.
    [Fact]
    public void Apply_KeepsCallback_GetsNumberAndText()
    {
        List<(int, string)> asked = [];
        DecisionNumbers.Apply(
            $"{Line}\n!d ab12-1234 x\nsee #!d1234\n\n!d ab12-9 mine",
            Folder,
            (n, text) =>
            {
                asked.Add((n, text));
                return true;
            },
            Never);

        Assert.Equal([(1234, "x\nsee #!d1234"), (9, "mine")], asked);
    }

    // AC-3: no numbers left keeps the markers as typed and counts them; no folder line is added for them alone.
    [Fact]
    public void Apply_NoNumbersLeft_Unnumbered()
    {
        string text = "!d a\n\n!d 3 b";
        DecisionEdits edits = DecisionNumbers.Apply(text, Folder, KeepAll, _ => null);

        Assert.Same(text, edits.Text);
        Assert.Equal(2, edits.Unnumbered);
        Assert.False(edits.Changed);
        Assert.True(edits.Noteworthy);
    }

    // A start out of range is a bug in the caller, never written.
    [Theory]
    [InlineData(0)]
    [InlineData(999_999_999)]
    public void Apply_OutOfRangeStart_Throws(int start)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecisionNumbers.Apply("!d a\n\n!d b", Folder, KeepAll, _ => start));
    }

    // The records never print raw text.
    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal(nameof(DecisionEdits), new DecisionEdits("secret", []).ToString());
        Assert.Equal(nameof(DecisionSet), new DecisionSet(0, "secret", "ab12-1").ToString());
    }
}
