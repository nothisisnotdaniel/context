using System.Text;

namespace Context.Tests;

// 271: LineEndings.Read records each break's ending and the most common one; Apply keeps the untouched lines' endings
// and gives new and changed lines the most common one.
public sealed class LineEndingsTests
{
    // AC-1: the text has only \n, the flags follow the breaks, the majority wins and a tie goes to the first break.
    [Theory]
    [InlineData("a\r\nb\nc\nd\r\n", "a\nb\nc\nd\n", true, "CLLC")]
    [InlineData("a\nb\nc\r\n", "a\nb\nc\n", false, "LLC")]
    [InlineData("a\nb\r\nc\r\n", "a\nb\nc\n", true, "LCC")]
    [InlineData("a\nb\r\n", "a\nb\n", false, "LC")]
    [InlineData("a\r\nb\n", "a\nb\n", true, "CL")]
    [InlineData("a\nb\nc", "a\nb\nc", false, null)]
    [InlineData("a\r\nb\r\nc", "a\nb\nc", true, null)]
    [InlineData("one line", "one line", false, null)]
    [InlineData("", "", false, null)]
    [InlineData("a\rb\n", "a\rb\n", false, null)]
    public void Read_Cases(string decoded, string text, bool hadCrlf, string? flags)
    {
        (string plain, bool crlf, IReadOnlyList<bool>? lineCrlf) = LineEndings.Read(decoded);

        Assert.Equal(text, plain);
        Assert.Equal(hadCrlf, crlf);
        Assert.Equal(flags, lineCrlf is null ? null : new string(lineCrlf.Select(f => f ? 'C' : 'L').ToArray()));
    }

    // AC-3 / AC-4: edits to a mixed note keep every untouched line's ending; added and changed lines (and a break
    // after an old last line without one) get the most common ending; the final break is exactly as the new text has it.
    [Theory]
    [InlineData("a\r\nb\nc\nd\r\ne\n", "a\nB\nc\nd\ne\n", "a\r\nB\nc\nd\r\ne\n")] // edited middle line, majority LF
    [InlineData("a\r\nb\r\nc\nd\r\n", "a\nb\nX\nd\n", "a\r\nb\r\nX\r\nd\r\n")] // edited line, majority CRLF
    [InlineData("a\r\nb\nc\n", "a\nb\nnew\nc\n", "a\r\nb\nnew\nc\n")] // inserted line
    [InlineData("a\r\nb\r\nc\nd\r\n", "a\nb\nnew\nc\nd\n", "a\r\nb\r\nnew\r\nc\nd\r\n")] // inserted line, CRLF wins
    [InlineData("a\r\nb\nc\nd\r\n", "a\nc\nd\n", "a\r\nc\nd\r\n")] // deleted line
    [InlineData("a\r\nb\nc", "a\nb\nc\n", "a\r\nb\nc\r\n")] // new final break gets the majority (tie: first)
    [InlineData("a\r\nb\nc\nd", "a\nb\nc\nd\n", "a\r\nb\nc\nd\n")] // new final break gets the majority (LF)
    [InlineData("a\r\nb\r\nc\nd", "a\nb\nc\nd\ne", "a\r\nb\r\nc\nd\r\ne")] // break after old last line, then a new line
    [InlineData("a\r\nb\nc\n", "a\nb\nc", "a\r\nb\nc")] // removed final break
    [InlineData("a\r\nb\nc\r\n", "a\nb\nc\r", "a\r\nb\nc\r")] // removed final break, a lone \r stays
    [InlineData("a\r\nb\nc\n", "", "")] // everything deleted
    public void Apply_Edits(string original, string edited, string expected)
    {
        EditableNote opened = Open(original);

        Assert.Equal(expected, LineEndings.Apply(opened, edited));
    }

    // AC-8 (pure part): applying the text as read gives back the original.
    [Theory]
    [InlineData("a\r\nb\nc\nd\r\n")]
    [InlineData("a\nb\r\nc")]
    [InlineData("\r\n\n\r\n\n")]
    public void Apply_Unedited_RoundTrips(string original)
    {
        EditableNote opened = Open(original);

        Assert.Equal(original, LineEndings.Apply(opened, opened.Text));
    }

    // AC-5: more changed lines than TextDiff allows writes everything with the majority ending, never all-CRLF
    // because one \r\n was there.
    [Theory]
    [InlineData("a\r\nb\nc\n", "\n")]
    [InlineData("a\r\nb\r\nc\n", "\r\n")]
    public void Apply_TooDifferent_MajorityEnding(string original, string majority)
    {
        EditableNote opened = Open(original);
        StringBuilder edited = new StringBuilder();
        for (int i = 0; i <= TextDiff.MaxChangedLines; i++)
        {
            edited.Append("line ").Append(i).Append('\n');
        }

        Assert.True(TextDiff.Lines(opened.Text, edited.ToString()).TooDifferent);
        string written = LineEndings.Apply(opened, edited.ToString());

        Assert.Equal(edited.ToString().Replace("\n", majority, StringComparison.Ordinal), written);
    }

    // Flags that don't match the text's breaks can't index out of range: the majority ending is used.
    [Fact]
    public void Apply_FlagsDoNotMatch_MajorityEnding()
    {
        EditableNote opened = new EditableNote("n", "/x/n.md", "a\nb\nc\n", "v", false, false, [true]);

        Assert.Equal("a\nb\nX\n", LineEndings.Apply(opened, "a\nb\nX\n"));
    }

    // AC-7 (pure part): uniform notes get their one ending throughout, as before.
    [Theory]
    [InlineData("a\nb\n", "a\nb\nc\n", "a\nb\nc\n")]
    [InlineData("a\r\nb\r\n", "a\nb\nc\n", "a\r\nb\r\nc\r\n")]
    [InlineData("a", "a\nb", "a\nb")]
    public void Apply_Uniform_AsBefore(string original, string edited, string expected)
    {
        EditableNote opened = Open(original);

        Assert.Null(opened.LineCrlf);
        Assert.Equal(expected, LineEndings.Apply(opened, edited));
    }

    private static EditableNote Open(string original)
    {
        (string text, bool crlf, IReadOnlyList<bool>? lines) = LineEndings.Read(original);
        return new EditableNote("n", "/x/n.md", text, "v", crlf, false, lines);
    }
}
