using System.Text;

namespace Context.Tests;

// 256: review dates through DesktopState and DesktopActions, against real files and the fixture's fixed clocks
// (today is 2026-10-29).
public sealed class ReviewActionsTests : IDisposable
{
    private const string Id = "20261029T090000000Z";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly DateTime Past = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string PathOf(string name) => Path.Combine(data.Root, name + ".md");

    // Writes the note with a last-write time in the past, so any later write is seen as a change by the index.
    private void Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(PathOf(name), bytes);
        File.SetLastWriteTimeUtc(PathOf(name), Past);
    }

    private static byte[] Utf8(string text, bool bom = false) =>
        bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);

    private static void AssertRefused(ActionResult result, ActionKind kind, ActionReason reason, string text)
    {
        Assert.Equal(kind, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(text, result.Text);
    }

    // AC-3, AC-9: the snapshot holds only the due rows, in order; future rows and sensitive notes never show.
    [Fact]
    public async Task Refresh_FillsDueReviews_Only()
    {
        data.Keys = FakeKeyStore.Ready();
        Write("beta", Utf8("review:2026-10-29\n"));
        Write("Alpha", Utf8("review:2026-10-29\n"));
        Write("Old", Utf8("x\n- review: 2026-09-01\n"));
        Write("Future", Utf8("review:2026-10-30\n"));
        data.WriteSensitive("Secret", "review:2026-01-01\n");

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Equal(["Old", "Alpha", "beta"], snapshot.Reviews.Select(r => r.Note).ToArray());
        Assert.Equal((2, new DateOnly(2026, 9, 1)), (snapshot.Reviews[0].Line, snapshot.Reviews[0].Due));
        Assert.False(snapshot.ReviewsUnreadable);
    }

    // AC-9: a deleted note's row is gone on the next refresh.
    [Fact]
    public async Task Refresh_DeletedNote_RowGone()
    {
        Write("Plan", Utf8("review:2026-10-01\n"));
        Assert.Single((await data.RefreshAsync()).Reviews);

        File.Delete(PathOf("Plan"));

        Assert.Empty((await data.RefreshAsync()).Reviews);
    }

    // AC-7: a bad context.db marks the review dates unreadable.
    [Fact]
    public async Task Refresh_BadDb_ReviewsUnreadable()
    {
        Write("Plan", Utf8("review:2026-10-01\n"));
        data.WriteFile(TagIndex.FileName, "not a database at all, not at all, really not");

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.True(snapshot.ReviewsUnreadable);
        Assert.Empty(snapshot.Reviews);
        Assert.False(snapshot.Failed);
    }

    // AC-4, AC-7, AC-8: 3 months and 1 year from the local clock; only the date changes, the old bytes are kept as a
    // version, and the row is gone after the refresh.
    [Theory]
    [InlineData(3, "2027-01-29")]
    [InlineData(12, "2027-10-29")]
    public async Task RescheduleReview_MovesTheDate_KeepsVersion_AndRefreshes(int months, string day)
    {
        byte[] before = Utf8("# Plan\r\n  * Review:\t2026-10-01 \r\nafter", bom: true);
        Write("Plan", before);
        NoteReview review = Assert.Single((await data.RefreshAsync()).Reviews);

        ActionResult result = await data.Actions!.RescheduleReview(review, months);

        Assert.Equal((ActionKind.Done, ActionReason.None, "Plan"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(Utf8("# Plan\r\n  * Review:\t" + day + " \r\nafter", bom: true), File.ReadAllBytes(PathOf("Plan")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, ".history", "Plan", Id + ".md")));
        Assert.Empty(data.State!.Snapshot!.Reviews);
    }

    // AC-7: a month count out of range writes nothing.
    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public async Task RescheduleReview_BadMonths_WritesNothing(int months)
    {
        Write("Plan", Utf8("review:2026-10-01\n"));
        NoteReview review = Assert.Single((await data.RefreshAsync()).Reviews);
        SortedDictionary<string, string> files = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.RescheduleReview(review, months);

        AssertRefused(result, ActionKind.Refused, ActionReason.Invalid, DesktopActions.ReviewMonthsText);
        Assert.Equal(files, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-5, AC-8: a confirmed remove deletes the line and its line end; the row is gone after the refresh.
    [Fact]
    public async Task RemoveReview_DeletesTheLine_AndRefreshes()
    {
        byte[] before = Utf8("# Plan\r\nreview:2026-10-01\r\nafter\r\n");
        Write("Plan", before);
        NoteReview review = Assert.Single((await data.RefreshAsync()).Reviews);

        ActionResult result = await data.Actions!.RemoveReview(review, new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None, "Plan"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(Utf8("# Plan\r\nafter\r\n"), File.ReadAllBytes(PathOf("Plan")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, ".history", "Plan", Id + ".md")));
        Assert.Empty(data.State!.Snapshot!.Reviews);
    }

    // AC-7: without a confirmation nothing is written.
    [Fact]
    public async Task RemoveReview_NotConfirmed_WritesNothing()
    {
        Write("Plan", Utf8("review:2026-10-01\n"));
        NoteReview review = Assert.Single((await data.RefreshAsync()).Reviews);
        SortedDictionary<string, string> files = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.RemoveReview(review, null);

        AssertRefused(result, ActionKind.Refused, ActionReason.NotConfirmed, DesktopActions.NotConfirmedText);
        Assert.Equal(files, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-4, AC-5, AC-8: a changed line and a deleted note are refused with NoteTodos' texts and write nothing.
    [Fact]
    public async Task ChangedOrMissing_Refused()
    {
        Write("Plan", Utf8("review:2026-10-01\n"));
        NoteReview review = Assert.Single((await data.RefreshAsync()).Reviews);
        byte[] changed = Utf8("review:2026-10-02\n");
        Write("Plan", changed);

        AssertRefused(
            await data.Actions!.RescheduleReview(review, 3), ActionKind.Refused, ActionReason.Changed, NoteTodos.ChangedText("Plan"));
        AssertRefused(
            await data.Actions.RemoveReview(review, new Confirmation()), ActionKind.Refused, ActionReason.Changed, NoteTodos.ChangedText("Plan"));
        Assert.Equal(changed, File.ReadAllBytes(PathOf("Plan")));

        File.Delete(PathOf("Plan"));
        AssertRefused(
            await data.Actions.RescheduleReview(review, 3), ActionKind.Refused, ActionReason.Missing, NoteTodos.MissingText("Plan"));
        AssertRefused(
            await data.Actions.RemoveReview(review, new Confirmation()), ActionKind.Refused, ActionReason.Missing, NoteTodos.MissingText("Plan"));
    }

    // AC-6, AC-9: the briefing lists the due notes (overdue ones with their date), never a future or deleted one.
    [Fact]
    public async Task AutoBriefing_ListsDueReviews()
    {
        Write("Old", Utf8("review:2026-09-01\n"));
        Write("Plan", Utf8("review:2026-10-29\n"));
        Write("Future", Utf8("review:2026-10-30\n"));
        Write("Gone", Utf8("review:2026-09-02\n"));
        await data.RefreshAsync();
        File.Delete(PathOf("Gone"));

        ActionResult result = await data.Actions!.AutoBriefing();

        Assert.Equal(ActionKind.Done, result.Kind);
        string page = File.ReadAllText(Path.Combine(data.Root, "briefings", "2026-10-29.md"));
        Assert.Contains("\n\n## To review\n- [[Old]] (due 2026-09-01)\n- [[Plan]]\n\n## Open checkpoint\n", page, StringComparison.Ordinal);
    }
}
