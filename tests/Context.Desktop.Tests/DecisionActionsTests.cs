using System.Text;

namespace Context.Tests;

// 349 AC-3, AC-4, AC-5 (343 AC-4, AC-5): DesktopActions.SaveNote numbers !d markers with the folder's prefix and adds
// the decision-folder line; copies keep their token; rename and move keep the owners; real files and a real context.db.
// 351 AC-4, AC-5: the app's numbering writes after capture, import, the day's end, a merge and a triage append keep
// the note's previous bytes as a history version first.
public sealed class DecisionActionsTests : IDisposable
{
    private static readonly DateTime Old = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Older = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly string Line = DesktopFixture.DecisionFolderLine;

    private readonly DesktopFixture data = new();

    public DecisionActionsTests() => data.WriteDecisionFolder();

    public void Dispose() => data.Dispose();

    // AC-3, AC-4: bare markers that start a decision get consecutive own-prefix tokens in document order, the folder line
    // goes after the title; a lone marker (no text), CRLF and every other byte stay; the next note continues.
    [Fact]
    public async Task SaveNote_BareMarkers_NumberedInOrder_KeepsBytes()
    {
        EditableNote opened = await Open("Plan", "# Plan\r\n\r\nText\r\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\n!d one\n\n- !d two\n\n!d\n\n!d three\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal(
            $"# Plan\r\n{Line}\r\n\r\n!d ab12-1 one\r\n\r\n- !d ab12-2 two\r\n\r\n!d\r\n\r\n!d ab12-3 three\r\n",
            File.ReadAllText(PathOf("Plan")));
        Assert.Equal(["ab12-1", "ab12-2", "ab12-3"], result.Decisions!.Set.Select(s => s.New));
        Assert.Equal($"# Plan\n{Line}\n\n!d ab12-1 one\n\n- !d ab12-2 two\n\n!d\n\n!d ab12-3 three\n", result.Note!.Text);

        EditableNote other = await Open("Other", "# Other\n");
        ActionResult next = await data.Actions.SaveNote(other, "!d four\n");

        Assert.Equal($"{Line}\n!d ab12-4 four\n", File.ReadAllText(PathOf("Other")));
        Assert.Equal(["ab12-4"], next.Decisions!.Set.Select(s => s.New));
    }

    // AC-3: a plain number typed after !d is replaced by an own token; another prefix in an own note too.
    [Fact]
    public async Task SaveNote_PlainAndOtherPrefix_Replaced()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");

        await data.Actions!.SaveNote(opened, "# Plan\n!d 3 people hired\n\n!d zz99-4 other\n");

        Assert.Equal($"# Plan\n{Line}\n!d ab12-1 people hired\n\n!d ab12-2 other\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-3 (343 round 2 feedback 1): a deleted decision's number is never given again; the next number steps over
    // every own-prefix number in the index or in the saved text, so a huge typed number never moves the counter.
    [Fact]
    public async Task SaveNote_NeverReusesANumber_StepsOverTypedOnes()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        ActionResult first = await data.Actions!.SaveNote(opened, "!d one\n");
        ActionResult removed = await data.Actions.SaveNote(first.Note!, $"{Line}\ngone\n");
        Assert.Equal(ActionKind.Done, removed.Kind);

        ActionResult again = await data.Actions.SaveNote(removed.Note!, $"{Line}\n!d again\n");
        Assert.Equal($"{Line}\n!d ab12-2 again\n", File.ReadAllText(PathOf("Plan")));

        data.WriteNote("Far", "!d ab12-3 far\n\n!d ab12-999999999 huge\n");
        File.SetLastWriteTimeUtc(PathOf("Far"), Old);
        ActionResult above = await data.Actions.SaveNote(again.Note!, $"{Line}\n!d ab12-2 again\n\n!d next\n");
        Assert.Equal($"{Line}\n!d ab12-2 again\n\n!d ab12-4 next\n", File.ReadAllText(PathOf("Plan")));

        ActionResult own = await data.Actions.SaveNote(above.Note!, $"{Line}\n!d ab12-2 again\n\n!d ab12-4 next\n\nsee #!d5\n\n!d new\n");
        Assert.Equal($"{Line}\n!d ab12-2 again\n\n!d ab12-4 next\n\nsee #!d5\n\n!d ab12-6 new\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal(["ab12-6"], own.Decisions!.Set.Select(s => s.New));
        Assert.False(data.State!.DecisionNumbersRanOut);
    }

    // AC-3: with no numbers left the save goes through with the markers as typed, the result says so (Unnumbered)
    // and the state shows it until numbers are left again.
    [Fact]
    public async Task SaveNote_NoNumbersLeft_SavedAsTyped_Told()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        TagIndex index = new TagIndex(data.Root);
        Assert.Equal(1, index.ReserveDecisionNumbers("ab12", DecisionParser.MaxNumber - 1));

        ActionResult result = await data.Actions!.SaveNote(opened, "!d one\n\n!d two\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("!d one\n\n!d two\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal(2, result.Decisions!.Unnumbered);
        Assert.False(result.Decisions.Changed);
        Assert.True(data.State!.DecisionNumbersRanOut);
        Assert.Equal(DesktopActions.DecisionNumbersRanOutText, DesktopActions.DecisionNoticeText(result.Decisions));

        ActionResult one = await data.Actions.SaveNote(result.Note!, "!d one\n\ntwo\n");
        Assert.Equal($"{Line}\n!d ab12-999999999 one\n\ntwo\n", File.ReadAllText(PathOf("Plan")));
        Assert.False(data.State.DecisionNumbersRanOut);
        Assert.Null(DesktopActions.DecisionNoticeText(one.Decisions));
    }

    // AC-3: there is one counter, so a number reserved for any prefix is never handed out again.
    [Fact]
    public async Task SaveNote_OneCounter_NeverReusedAcrossPrefixes()
    {
        Assert.Equal(1, new TagIndex(data.Root).ReserveDecisionNumbers("zz99", 50));
        EditableNote opened = await Open("Plan", "# Plan\n");

        await data.Actions!.SaveNote(opened, "!d one\n");

        Assert.Equal($"{Line}\n!d ab12-51 one\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-3: the numbers are reserved before the write, so a save refused as changed on disk only leaves a gap.
    [Fact]
    public async Task SaveNote_ChangedOnDisk_LeavesAGap()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        data.WriteNote("Plan", "theirs\n");

        ActionResult refused = await data.Actions!.SaveNote(opened, "!d mine\n");
        Assert.Equal((ActionKind.Refused, ActionReason.ChangedOnDisk), (refused.Kind, refused.Reason));
        Assert.Equal("theirs\n", File.ReadAllText(PathOf("Plan")));

        EditableNote reopened = (await data.State!.ReadForEdit("Plan"))!;
        await data.Actions.SaveNote(reopened, "!d mine\n");
        Assert.Equal($"{Line}\n!d ab12-2 mine\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-5: a copy of a decision another note owns (the same text) keeps its token and is listed as a place; a repeat
    // in the same note with another text gets a new number; the owner is left alone.
    [Fact]
    public async Task SaveNote_CopyKept_RepeatWithOtherTextRenumbered()
    {
        data.WriteNote("Owner", $"{Line}\n!d ab12-1234 the first\n");
        File.SetLastWriteTimeUtc(PathOf("Owner"), Old);
        EditableNote opened = await Open("Copy", "# Copy\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "!d ab12-1234 the first\n\nsee #!d1234\n\n!d ab12-1234 b\n");

        Assert.Equal($"{Line}\n!d ab12-1234 the first\n\nsee #!d1234\n\n!d ab12-1 b\n", File.ReadAllText(PathOf("Copy")));
        Assert.Equal(["ab12-1"], result.Decisions!.Set.Select(s => s.New));
        Assert.Equal($"{Line}\n!d ab12-1234 the first\n", File.ReadAllText(PathOf("Owner")));
        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Owner", index.DecisionByKey("ab12", 1234)!.Note);
        DecisionsView view = DecisionsView.Find(index, data.Notes!, new DecisionRef(null, 1234), TimeZoneInfo.Utc);
        Assert.Equal(["Copy"], view.Decisions[0].Places.Select(p => p.Note));
    }

    // AC-3: a token whose owner holds another text is replaced by a new number, so two notes never share a number for
    // different decisions; an owner that can't be read (no text to compare) gives no copy either.
    [Fact]
    public async Task SaveNote_OwnedElsewhereWithOtherText_Renumbered()
    {
        data.WriteNote("Stale", $"{Line}\n!d ab12-1234 old draft\n");
        File.SetLastWriteTimeUtc(PathOf("Stale"), Older);
        EditableNote opened = await Open("Original", "# Original\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "!d ab12-1234 the real decision\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal($"{Line}\n!d ab12-1 the real decision\n", File.ReadAllText(PathOf("Original")));
        Assert.Equal($"{Line}\n!d ab12-1234 old draft\n", File.ReadAllText(PathOf("Stale")));

        data.WriteNote("Locked", $"{Line}\n!d ab12-55 mine\n");
        File.SetLastWriteTimeUtc(PathOf("Locked"), Older);
        EditableNote other = await Open("Later", "# Later\n");
        await data.RefreshAsync();
        File.WriteAllBytes(PathOf("Locked"), new byte[(int)NoteStore.MaxNoteBytes + 1]);
        await data.Actions.SaveNote(other, "!d ab12-55 mine\n");
        Assert.DoesNotContain("ab12-55", File.ReadAllText(PathOf("Later")), StringComparison.Ordinal);
    }

    // AC-4: a note of another folder is never numbered; the result tells the editor so.
    [Fact]
    public async Task SaveNote_ForeignNote_NotNumbered()
    {
        string foreign = "decision-folder:: 99999999-8888-7777-6666-555555555555\n!d bare\n";
        EditableNote opened = await Open("Foreign", "# Foreign\n");

        ActionResult result = await data.Actions!.SaveNote(opened, foreign);

        Assert.Equal(foreign, File.ReadAllText(PathOf("Foreign")));
        Assert.Equal(DesktopActions.DecisionForeignText, DesktopActions.DecisionNoticeText(result.Decisions));
    }

    // AC-2, AC-11: an unreadable decision-folder.json saves the text as typed and shows the red line; the file stays.
    [Fact]
    public async Task SaveNote_UnreadableFolderFile_SavedUnnumbered()
    {
        data.WriteFile(DecisionFolder.FileName, "{broken");
        EditableNote opened = await Open("Plan", "# Plan\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "!d one\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("!d one\n", File.ReadAllText(PathOf("Plan")));
        Assert.True(data.State!.DecisionFolderUnreadable);
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(data.Root, DecisionFolder.FileName)));
    }

    // AC-3: an unreadable context.db saves the text as typed (Done, Saved) and leaves the file alone.
    [Fact]
    public async Task SaveNote_UnreadableIndex_SavedUnnumbered()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");
        data.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");
        byte[] db = File.ReadAllBytes(Path.Combine(data.Root, TagIndex.FileName));

        ActionResult result = await data.Actions!.SaveNote(opened, "!d one\n");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Null(result.Decisions);
        Assert.Equal("!d one\n", File.ReadAllText(PathOf("Plan")));
        Assert.Equal(db, File.ReadAllBytes(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // AC-7: references only give no edits and the text as typed.
    [Fact]
    public async Task SaveNote_NoMarker_NoEdits()
    {
        EditableNote opened = await Open("Plan", "# Plan\n");

        ActionResult result = await data.Actions!.SaveNote(opened, "# Plan\n\nsee #!d3 and #!dab12-12\n");

        Assert.Null(result.Decisions);
        Assert.Equal("# Plan\n\nsee #!d3 and #!dab12-12\n", File.ReadAllText(PathOf("Plan")));
    }

    // AC-5: renaming a note in the app keeps its numbers, even when an older note holds the same token.
    [Fact]
    public async Task RenameNote_KeepsOwnership()
    {
        data.WriteNote("Plan", $"{Line}\n!d ab12-5 mine\n");
        File.SetLastWriteTimeUtc(PathOf("Plan"), Old);
        await data.RefreshAsync();
        TagIndex first = new TagIndex(data.Root);
        first.Sync(data.Notes!);
        Assert.Equal("Plan", first.DecisionByKey("ab12", 5)!.Note);
        data.WriteNote("Rival", $"{Line}\n!d ab12-5 copy\n");
        File.SetLastWriteTimeUtc(PathOf("Rival"), Older);
        await data.RefreshAsync();

        ActionResult renamed = await data.Actions!.RenameNote("Plan", "Project", null);
        Assert.Equal(ActionKind.Done, renamed.Kind);
        await data.RefreshAsync();

        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Project", index.DecisionByKey("ab12", 5)!.Note);
    }

    // AC-5: moving a note in the app keeps its and its subpages' numbers.
    [Fact]
    public async Task MoveNote_KeepsOwnership_WithSubpages()
    {
        data.WriteNote("Plan", $"{Line}\n!d ab12-5 mine\n");
        data.WriteFile(Path.Combine("Plan", "Sub.md"), $"{Line}\n!d ab12-6 below\n");
        data.WriteNote("Archive", "# Archive\n");
        File.SetLastWriteTimeUtc(PathOf("Plan"), Old);
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan", "Sub.md"), Old);
        await data.RefreshAsync();
        new TagIndex(data.Root).Sync(data.Notes!);
        data.WriteNote("Rival", $"{Line}\n!d ab12-5 copy\n\n!d ab12-6 copy\n");
        File.SetLastWriteTimeUtc(PathOf("Rival"), Older);
        await data.RefreshAsync();

        ActionResult moved = await data.Actions!.MoveNote("Plan", "Archive");
        Assert.Equal(ActionKind.Done, moved.Kind);

        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Archive/Plan", index.DecisionByKey("ab12", 5)!.Note);
        Assert.Equal("Archive/Plan/Sub", index.DecisionByKey("ab12", 6)!.Note);
    }

    // Round 2 feedback 2: every app write path goes through the two numbering helpers; SaveNote reads the edits itself.
    [Fact]
    public void Source_WritePathsNumber()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), "src", "Context.Desktop", "DesktopActions.cs"));

        Assert.Equal(2, CountOf(source, "state.NumberDecisions("));
        Assert.Equal(6, CountOf(source, "NumberWritten("));
        Assert.Equal(4, CountOf(source, "NumberDayEnd("));
        Assert.Equal(7, CountOf(source, "Numbered("));
    }

    // 351 AC-4: a capture that numbers the inbox keeps the inbox as captured (its "!d 3 people hired" paragraph with
    // the 3 still there) as one version first. A captured line is a timestamped list item, not a decision paragraph,
    // so the bare marker is typed into the inbox before.
    [Fact]
    public async Task CaptureText_Numbered_KeepsCapturedTextAsVersion()
    {
        data.WriteNote(Inbox.NoteName, $"# {Inbox.NoteName}\n\n!d 3 people hired\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(
            $"# {Inbox.NoteName}\n{Line}\n\n!d ab12-1 people hired\n- 2026-10-29 10:05 call Anna\n",
            File.ReadAllText(PathOf(Inbox.NoteName)));
        string kept = Assert.Single(VersionsOf(Inbox.NoteName));
        Assert.Equal($"# {Inbox.NoteName}\n\n!d 3 people hired\n- 2026-10-29 10:05 call Anna\n", kept);
    }

    // 351 AC-4: import, the day's end and a merge also keep the text as written before they number it.
    [Fact]
    public async Task ImportNote_Numbered_KeepsImportedTextAsVersion()
    {
        string source = data.Outside("Plan.md");
        byte[] imported = Encoding.UTF8.GetBytes("# Plan\r\n\r\n!d go\r\n");
        File.WriteAllBytes(source, imported);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ImportNote(source, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"# Plan\r\n{Line}\r\n\r\n!d ab12-1 go\r\n", File.ReadAllText(PathOf("Plan")));
        NoteInfo note = Find("Plan");
        NoteVersion version = Assert.Single(data.State!.HistoryStore.Versions(note));
        Assert.True(data.State.HistoryStore.TryRead(note, version.Id, out byte[]? bytes));
        Assert.Equal(imported, bytes);
    }

    [Fact]
    public async Task EndDay_Numbered_KeepsWrittenTextAsVersion()
    {
        data.WriteNote("2026-10-29", "# 2026-10-29\n\n!d no meetings on Monday\n");
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.EndDay(DesktopFixture.Today);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Contains("!d ab12-1 no meetings on Monday", File.ReadAllText(PathOf("2026-10-29")), StringComparison.Ordinal);
        string kept = VersionsOf("2026-10-29")[0];
        Assert.Contains("!d no meetings on Monday", kept, StringComparison.Ordinal);
        Assert.Contains("call Anna", kept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MergeNotes_Numbered_KeepsMergedTextAsVersion()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Old", "# Old\n\n!d bare\n");
        await data.RefreshAsync();

        ActionResult merged = await data.Actions!.MergeNotes("Plan", "Old", new Confirmation());

        Assert.Equal(ActionKind.Done, merged.Kind);
        Assert.Contains("!d ab12-1 bare", File.ReadAllText(PathOf("Plan")), StringComparison.Ordinal);
        string kept = VersionsOf("Plan")[0];
        Assert.Contains("!d bare", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("ab12-", kept, StringComparison.Ordinal);
    }

    // 351 AC-5: a triage append that numbers keeps the target's previous bytes as one version; one that numbers
    // nothing keeps none.
    [Fact]
    public async Task TriageAppend_Numbered_KeepsPreviousBytes_PlainKeepsNone()
    {
        const string Item = "- 2026-10-28 09:20 !d hire two people";
        data.WriteNote(Inbox.NoteName, $"# {Inbox.NoteName}\n{Item}\n");
        data.WriteNote("Plan", "# Plan\r\n");
        byte[] before = File.ReadAllBytes(PathOf("Plan"));
        await data.RefreshAsync();

        ActionResult added = await data.Actions!.TriageAppend(InboxTriage.Parse(Item)!, "Plan", null);

        Assert.Equal(ActionKind.Done, added.Kind);
        Assert.Contains("!d ab12-1 hire two people", File.ReadAllText(PathOf("Plan")), StringComparison.Ordinal);
        NoteInfo note = Find("Plan");
        NoteVersion version = Assert.Single(data.State!.HistoryStore.Versions(note));
        Assert.True(data.State.HistoryStore.TryRead(note, version.Id, out byte[]? bytes));
        Assert.Equal(before, bytes);

        const string Plain = "- 2026-10-28 09:25 call Anna";
        data.WriteNote(Inbox.NoteName, $"# {Inbox.NoteName}\n{Plain}\n");
        data.WriteNote("Other", "# Other\n");
        await data.RefreshAsync();

        ActionResult plain = await data.Actions.TriageAppend(InboxTriage.Parse(Plain)!, "Other", null);

        Assert.Equal(ActionKind.Done, plain.Kind);
        Assert.Contains("call Anna", File.ReadAllText(PathOf("Other")), StringComparison.Ordinal);
        Assert.Empty(data.State.HistoryStore.Versions(Find("Other")));
    }

    private NoteInfo Find(string name) => Assert.IsType<NoteInfo>(data.Notes!.TryFind(name, out NoteInfo? found) ? found : null);

    // The note's kept versions as text, newest first.
    private List<string> VersionsOf(string name)
    {
        NoteInfo note = Find(name);
        return data.State!.HistoryStore.Versions(note)
            .Select(v => data.State.HistoryStore.TryRead(note, v.Id, out byte[]? bytes) ? Encoding.UTF8.GetString(bytes!) : "")
            .ToList();
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

    // AC-8: the picker's rows: the log's numbered decisions whose headline contains the query (any case) or whose label
    // starts with it, own ones labelled by the number alone; a sensitive note never appears; at most 20.
    [Fact]
    public async Task DecisionPicks_Filter_SensitiveLeftOut_Capped()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteNote("Plan", $"{Line}\n!d ab12-5 Ship it\n\n!d ab12-12 hire Anna\n\n!d bare one\n");
        data.WriteSensitive("Secret", $"{Line}\n!d ab12-7 ship secretly\n");
        await data.RefreshAsync();

        Assert.Equal([("5", "5", "Ship it")], await data.State!.DecisionPicks("SHIP"));
        Assert.Equal([("12", "12", "hire Anna")], await data.State.DecisionPicks("#!d1"));
        Assert.Equal(2, (await data.State.DecisionPicks("  ")).Count);
        Assert.Empty(await data.State.DecisionPicks("secret"));

        for (int n = 100; n < 125; n++)
        {
            data.WriteNote($"Many {n}", $"{Line}\n!d ab12-{n} many {n}\n");
        }

        Assert.Equal(DesktopState.MaxDecisionPicks, (await data.State.DecisionPicks("many")).Count);
    }

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
