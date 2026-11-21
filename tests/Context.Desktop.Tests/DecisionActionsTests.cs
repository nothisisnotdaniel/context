using System.Text;

namespace Context.Tests;

// 343 AC-4, AC-5: DesktopActions.SaveNote numbers bare #!d markers and strips markers another note owns; rename and
// move keep the owners; against real files and a real context.db.
public sealed class DecisionActionsTests : IDisposable
{
    private static readonly DateTime Old = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Older = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4: bare markers that start a decision get consecutive numbers in document order; a lone marker (no text),
    // CRLF and every other byte stay; the next note continues from the counter.
    [Fact]
    public async Task SaveNote_BareMarkers_NumberedInOrder_KeepsBytes()
    {
        EditableNote opened = await Open("Plan", "# Plan\r\n\r\nText\r\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\n#!d one\n\n- #!d two\n\n#!d\n\n#!d three\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("# Plan\r\n\r\n#!d1 one\r\n\r\n- #!d2 two\r\n\r\n#!d\r\n\r\n#!d3 three\r\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal([1, 2, 3], result.Decisions!.Bare);
        Assert.Empty(result.Decisions.Strip);
        Assert.Equal("# Plan\n\n#!d1 one\n\n- #!d2 two\n\n#!d\n\n#!d3 three\n", result.Note!.Text);

        EditableNote other = await Open("Other", "# Other\n");
        ActionResult next = await data.Actions.SaveNote(other, "#!d four\n");

        Assert.Equal("#!d4 four\n", File.ReadAllText(PathOf("Other")));
        Assert.Equal([4], next.Decisions!.Bare);
    }

    // AC-4: a deleted decision's number is never given again; the next number is above every number in the index and
    // in the saved text itself.
    [Fact]
    public async Task SaveNote_NeverReusesANumber_AboveIndexAndText()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        ActionResult first = await data.Actions!.SaveNote(opened, "#!d one\n");
        ActionResult removed = await data.Actions.SaveNote(first.Note!, "gone\n");
        Assert.Equal(ActionKind.Done, removed.Kind);

        ActionResult again = await data.Actions.SaveNote(removed.Note!, "#!d again\n");
        Assert.Equal("#!d2 again\n", File.ReadAllText(PathOf("Plan")));

        data.WriteNote("Far", "#!d50 far\n");
        File.SetLastWriteTimeUtc(PathOf("Far"), Old);
        ActionResult above = await data.Actions.SaveNote(again.Note!, "#!d2 again\n\n#!d next\n");
        Assert.Equal("#!d2 again\n\n#!d51 next\n", File.ReadAllText(PathOf("Plan")));

        ActionResult own = await data.Actions.SaveNote(above.Note!, "#!d2 again\n\n#!d51 next\n\n#!d900 typed\n\n#!d new\n");
        Assert.Equal("#!d2 again\n\n#!d51 next\n\n#!d900 typed\n\n#!d901 new\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal([901], own.Decisions!.Bare);
    }

    // AC-4: the numbers are reserved before the write, so a save refused as changed on disk only leaves a gap.
    [Fact]
    public async Task SaveNote_ChangedOnDisk_LeavesAGap()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        data.WriteNote("Plan", "theirs\n");

        ActionResult refused = await data.Actions!.SaveNote(opened, "#!d mine\n");
        Assert.Equal((ActionKind.Refused, ActionReason.ChangedOnDisk), (refused.Kind, refused.Reason));
        Assert.Equal("theirs\n", File.ReadAllText(PathOf("Plan")));

        EditableNote reopened = (await data.State!.ReadForEdit("Plan"))!;
        await data.Actions.SaveNote(reopened, "#!d mine\n");
        Assert.Equal("#!d2 mine\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-5: a number another note owns loses its marker on save; the paragraph and every reference stay. A repeat in
    // the same note loses to the first.
    [Fact]
    public async Task SaveNote_OwnedElsewhereOrRepeated_Stripped()
    {
        data.WriteNote("Owner", "#!d1234 the first\n");
        File.SetLastWriteTimeUtc(PathOf("Owner"), Old);
        EditableNote opened = await Open("Copy", "# Copy\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "#!d1234 x\nsee #!d1234\n\n#!d7 a\n\n#!d7 b\n");

        Assert.Equal("x\nsee #!d1234\n\n#!d7 a\n\nb\n", File.ReadAllText(PathOf("Copy")));
        Assert.Equal([1234, 7], result.Decisions!.Strip);
        Assert.Empty(result.Decisions.Bare);
        Assert.Equal("#!d1234 the first\n", File.ReadAllText(PathOf("Owner")));
        Assert.Equal("Owner", new TagIndex(data.Root).DecisionByNumber(1234)!.Note);
    }

    // AC-4: an unreadable context.db saves the text as typed (Done, Saved) and leaves the file alone.
    [Fact]
    public async Task SaveNote_UnreadableIndex_SavedUnnumbered()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        data.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");
        byte[] db = File.ReadAllBytes(Path.Combine(data.Root, TagIndex.FileName));

        ActionResult result = await data.Actions!.SaveNote(opened, "#!d one\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Null(result.Decisions);
        Assert.Equal("#!d one\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal(db, File.ReadAllBytes(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // AC-4: nothing to number gives no edits and the text as typed.
    [Fact]
    public async Task SaveNote_NoMarker_NoEdits()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\nsee #!d3 and #!d12\n");

        Assert.Null(result.Decisions);
        Assert.Equal("# Plan\n\nsee #!d3 and #!d12\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-5: renaming a note in the app keeps its numbers, even when an older note holds the same number.
    [Fact]
    public async Task RenameNote_KeepsOwnership()
    {
        data.WriteNote("Plan", "#!d5 mine\n");
        File.SetLastWriteTimeUtc(PathOf("Plan"), Old);
        await data.RefreshAsync();
        Assert.Equal("Plan", new TagIndex(data.Root).DecisionByNumber(5)!.Note);
        data.WriteNote("Rival", "#!d5 copy\n");
        File.SetLastWriteTimeUtc(PathOf("Rival"), Older);
        await data.RefreshAsync();

        ActionResult renamed = await data.Actions!.RenameNote("Plan", "Project", null);
        Assert.Equal(ActionKind.Done, renamed.Kind);
        await data.RefreshAsync();

        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Project", index.DecisionByNumber(5)!.Note);
    }

    // AC-5: moving a note in the app keeps its and its subpages' numbers.
    [Fact]
    public async Task MoveNote_KeepsOwnership_WithSubpages()
    {
        data.WriteNote("Plan", "#!d5 mine\n");
        data.WriteFile(Path.Combine("Plan", "Sub.md"), "#!d6 below\n");
        data.WriteNote("Archive", "# Archive\n");
        File.SetLastWriteTimeUtc(PathOf("Plan"), Old);
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan", "Sub.md"), Old);
        await data.RefreshAsync();
        data.WriteNote("Rival", "#!d5 copy\n\n#!d6 copy\n");
        File.SetLastWriteTimeUtc(PathOf("Rival"), Older);
        await data.RefreshAsync();

        ActionResult moved = await data.Actions!.MoveNote("Plan", "Archive");
        Assert.Equal(ActionKind.Done, moved.Kind);

        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Archive/Plan", index.DecisionByNumber(5)!.Note);
        Assert.Equal("Archive/Plan/Sub", index.DecisionByNumber(6)!.Note);
    }

    // AC-4: only SaveNote numbers; capture, import, the day's end, the briefing and link rewrites never call it.
    [Fact]
    public void Source_OnlySaveNoteNumbers()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), "src", "Context.Desktop", "DesktopActions.cs"));

        Assert.Equal(1, CountOf(source, "NumberDecisions("));
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private string PathOf(string name) => Path.Combine(data.Root, name + ".md");

    private async Task<EditableNote> Open(string name, string text)
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(PathOf(name), Encoding.UTF8.GetBytes(text));
        await data.RefreshAsync();
        EditableNote? opened = await data.State!.ReadForEdit(name);
        Assert.NotNull(opened);
        return opened;
    }
}
