using System.Diagnostics;
using System.Text;

namespace Context.Tests;

public sealed class TextDiffTests
{
    // AC-11: Same+Removed give the old text, Same+Added the current one.
    [Theory]
    [InlineData("a\nb\nc", "a\nc")]
    [InlineData("a\nc", "a\nb\nc")]
    [InlineData("one\ntwo\nthree\n", "zero\ntwo\nthree and more\nfour\n")]
    [InlineData("", "new")]
    [InlineData("old", "")]
    [InlineData("x\ny\nz", "z\ny\nx")]
    public void Lines_RoundTrip(string old, string current)
    {
        TextDiffResult diff = TextDiff.Lines(old, current);

        Assert.False(diff.TooDifferent);
        Assert.Equal(old, string.Join('\n', diff.Lines.Where(l => l.Kind != DiffKind.Added).Select(l => l.Text)));
        Assert.Equal(current, string.Join('\n', diff.Lines.Where(l => l.Kind != DiffKind.Removed).Select(l => l.Text)));
    }

    // AC-11: lines split at \n with a trailing \r dropped, so CRLF and LF texts are the same.
    [Fact]
    public void Lines_CrlfIsTheSameAsLf()
    {
        TextDiffResult diff = TextDiff.Lines("a\r\nb\r\n", "a\nb\n");

        Assert.All(diff.Lines, l => Assert.Equal(DiffKind.Same, l.Kind));
        Assert.Equal(["a", "b", ""], diff.Lines.Select(l => l.Text));
    }

    // AC-11: one changed line in the middle is one removed and one added line.
    [Fact]
    public void Lines_OneChange_IsMinimal()
    {
        TextDiffResult diff = TextDiff.Lines("a\nb\nc", "a\nB\nc");

        Assert.Equal(
            [(DiffKind.Same, "a"), (DiffKind.Removed, "b"), (DiffKind.Added, "B"), (DiffKind.Same, "c")],
            diff.Lines.Select(l => (l.Kind, l.Text)));
    }

    // AC-11: more than 1000 changed lines is TooDifferent, with no lines.
    [Fact]
    public void Lines_OverTheBound_IsTooDifferent()
    {
        string old = string.Join('\n', Enumerable.Range(0, 501).Select(i => "old " + i));
        string current = string.Join('\n', Enumerable.Range(0, 501).Select(i => "new " + i));

        TextDiffResult diff = TextDiff.Lines(old, current);

        Assert.True(diff.TooDifferent);
        Assert.Empty(diff.Lines);
    }

    // AC-11: exactly 1000 changed lines is still shown.
    [Fact]
    public void Lines_AtTheBound_IsShown()
    {
        string old = string.Join('\n', Enumerable.Range(0, 500).Select(i => "old " + i));
        string current = string.Join('\n', Enumerable.Range(0, 500).Select(i => "new " + i));

        TextDiffResult diff = TextDiff.Lines(old, current);

        Assert.False(diff.TooDifferent);
        Assert.Equal(TextDiff.MaxChangedLines, diff.Lines.Count(l => l.Kind != DiffKind.Same));
    }

    // AC-11: a length difference over the bound is TooDifferent at once.
    [Fact]
    public void Lines_ManyAdded_IsTooDifferent()
    {
        TextDiffResult diff = TextDiff.Lines(string.Empty, string.Join('\n', Enumerable.Range(0, 1002).Select(i => "l" + i)));

        Assert.True(diff.TooDifferent);
    }

    // AC-11: two 5 MB texts with one changed line finish quickly.
    [Fact]
    public void Lines_FiveMegabytes_OneChange_IsFast()
    {
        StringBuilder text = new StringBuilder();
        int line = 0;
        while (text.Length < 5 * 1024 * 1024)
        {
            text.Append("line number ").Append(line++).Append(" with some words\n");
        }

        string old = text.ToString();
        string current = old.Replace("line number 70000 ", "line number 70000 changed ", StringComparison.Ordinal);
        Stopwatch watch = Stopwatch.StartNew();

        TextDiffResult diff = TextDiff.Lines(old, current);

        watch.Stop();
        Assert.False(diff.TooDifferent);
        Assert.Equal(2, diff.Lines.Count(l => l.Kind != DiffKind.Same));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
    }

    // AC-11: many scattered changes under the bound stay within the time limit too.
    [Fact]
    public void Lines_ScatteredChanges_IsFast()
    {
        string[] lines = Enumerable.Range(0, 100_000).Select(i => "line " + i).ToArray();
        string old = string.Join('\n', lines);
        for (int i = 0; i < lines.Length; i += 250)
        {
            lines[i] += " changed";
        }

        Stopwatch watch = Stopwatch.StartNew();
        TextDiffResult diff = TextDiff.Lines(old, string.Join('\n', lines));

        watch.Stop();
        Assert.False(diff.TooDifferent);
        Assert.Equal(800, diff.Lines.Count(l => l.Kind != DiffKind.Same));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
    }
}
