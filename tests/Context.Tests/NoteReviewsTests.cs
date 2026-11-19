using System.Text;

namespace Context.Tests;

// 256 AC-3 to AC-5: which notes are due, moving a review date on and removing the review line.
public sealed class NoteReviewsTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateOnly Day = new(2027, 3, 5);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-notereviews-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;
    private readonly TagIndex index;

    public NoteReviewsTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        index = new TagIndex(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string PathOf(string name) => Path.Combine(root, name + ".md");

    private void Write(string name, byte[] bytes) => File.WriteAllBytes(PathOf(name), bytes);

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private NoteReview Only()
    {
        index.Sync(store);
        return Assert.Single(index.Reviews());
    }

    private static NoteReview Row(string note, DateOnly due) => new(note, 1, due, "review:x");

    // AC-3: only rows due today or earlier, by due date, then note ignoring case, then ordinal.
    [Fact]
    public void Due_KeepsDueRows_AndOrders()
    {
        DateOnly today = new(2026, 11, 19);
        NoteReview[] rows =
        [
            Row("later", today.AddDays(1)),
            Row("b", today),
            Row("B", today),
            Row("a", today),
            Row("z", today.AddDays(-3)),
        ];

        Assert.Equal(
            ["z", "a", "B", "b"],
            NoteReviews.Due(rows, today).Select(r => r.Note));
    }

    // AC-4: only the date changes; BOM, CRLF, indent, bullet and every other byte stay.
    [Theory]
    [InlineData("review:2026-12-01", "review:2027-03-05")]
    [InlineData("  - Review: 2026-12-01  ", "  - Review: 2027-03-05  ")]
    [InlineData("* REVIEW:\t2026-12-01", "* REVIEW:\t2027-03-05")]
    public void Reschedule_ChangesOnlyTheDate(string line, string moved)
    {
        Write("Plan", Utf8("# Plan\r\n" + line + "\r\nafter\r\n", bom: true));

        Assert.Equal(NoteTodoResult.Done, NoteReviews.Reschedule(store, Only(), Day));

        Assert.Equal(Utf8("# Plan\r\n" + moved + "\r\nafter\r\n", bom: true), File.ReadAllBytes(PathOf("Plan")));
        Assert.Equal(Day, Only().Due);
    }

    // AC-4: the last line without a line end keeps having none.
    [Fact]
    public void Reschedule_LastLine_KeepsNoLineEnd()
    {
        Write("Plan", Utf8("x\nreview:2026-12-01"));

        Assert.Equal(NoteTodoResult.Done, NoteReviews.Reschedule(store, Only(), Day));

        Assert.Equal(Utf8("x\nreview:2027-03-05"), File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-4: with a history the previous bytes are kept as a version.
    [Fact]
    public void Reschedule_WithHistory_KeepsPreviousBytes()
    {
        byte[] before = Utf8("review:2026-12-01\r\n", bom: true);
        Write("Plan", before);
        NoteHistory history = new(store, () => new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(NoteTodoResult.Done, NoteReviews.Reschedule(store, Only(), Day, history));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, ".history", "Plan", "20261119T090000000Z.md")));
    }

    // AC-4, AC-5: a changed line (another text or another line) gives Changed and writes nothing.
    [Fact]
    public void ChangedLine_WritesNothing()
    {
        Write("Plan", Utf8("review:2026-12-01\n"));
        NoteReview review = Only();

        byte[] changed = Utf8("review:2026-12-02\n");
        Write("Plan", changed);
        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Reschedule(store, review, Day));
        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Remove(store, review));
        Assert.Equal(changed, File.ReadAllBytes(PathOf("Plan")));

        byte[] moved = Utf8("new first line\nreview:2026-12-01\n");
        Write("Plan", moved);
        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Reschedule(store, review, Day));
        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Remove(store, review));
        Assert.Equal(moved, File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-4, AC-5: a write in between the read and the save gives Changed and keeps the other write.
    [Fact]
    public void WriteInBetween_Changed()
    {
        Write("Plan", Utf8("review:2026-12-01\n"));
        NoteReview review = Only();
        byte[] other = Utf8("other\n");

        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Reschedule(store, review, Day, null, () => Write("Plan", other)));
        Assert.Equal(other, File.ReadAllBytes(PathOf("Plan")));

        Write("Plan", Utf8("review:2026-12-01\n"));
        Assert.Equal(NoteTodoResult.Changed, NoteReviews.Remove(store, review, null, () => Write("Plan", other)));
        Assert.Equal(other, File.ReadAllBytes(PathOf("Plan")));
    }

    // AC-4, AC-5: a missing note gives Missing.
    [Fact]
    public void MissingNote_Missing()
    {
        Write("Plan", Utf8("review:2026-12-01\n"));
        NoteReview review = Only();
        File.Delete(PathOf("Plan"));

        Assert.Equal(NoteTodoResult.Missing, NoteReviews.Reschedule(store, review, Day));
        Assert.Equal(NoteTodoResult.Missing, NoteReviews.Remove(store, review));
    }

    // AC-5: the line goes with its own line end; BOM, CRLF and every other byte stay.
    [Fact]
    public void Remove_DeletesTheLineAndItsLineEnd()
    {
        Write("Plan", Utf8("# Plan\r\n  - Review: 2026-12-01  \r\nafter\r\n", bom: true));

        Assert.Equal(NoteTodoResult.Done, NoteReviews.Remove(store, Only()));

        Assert.Equal(Utf8("# Plan\r\nafter\r\n", bom: true), File.ReadAllBytes(PathOf("Plan")));
        index.Sync(store);
        Assert.Empty(index.Reviews());
    }

    // AC-5: a last line without a newline goes with the line end before it.
    [Theory]
    [InlineData("# Plan\r\nreview:2026-12-01", "# Plan")]
    [InlineData("# Plan\nreview:2026-12-01", "# Plan")]
    [InlineData("review:2026-12-01", "")]
    public void Remove_LastLine_TakesThePrecedingLineEnd(string text, string expected)
    {
        Write("Plan", Utf8(text));

        Assert.Equal(NoteTodoResult.Done, NoteReviews.Remove(store, Only()));

        Assert.Equal(Utf8(expected), File.ReadAllBytes(PathOf("Plan")));
    }

    [Fact]
    public void ToString_NeverPrintsTheNote()
    {
        Assert.Equal(nameof(NoteReview), Row("secret", Day).ToString());
    }
}
