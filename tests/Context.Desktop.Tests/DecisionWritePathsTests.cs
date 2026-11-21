using System.Text;

namespace Context.Tests;

// 349 AC-11 (343 round 2 feedback 2): the app's other write paths (capture, note today, triage, import, the day's end,
// the briefing, link rewrites and merge) number !d markers like SaveNote, with the folder's prefix and line; against
// real files, a real context.db and the fixture's fixed local clock.
public sealed class DecisionWritePathsTests : IDisposable
{
    private static readonly string Line = DesktopFixture.DecisionFolderLine;

    private readonly DesktopFixture data = new();

    public DecisionWritePathsTests() => data.WriteDecisionFolder();

    public void Dispose() => data.Dispose();

    // Capture: the inbox is written by Core, then numbered (a bare marker typed there before gets its number).
    [Fact]
    public async Task CaptureText_NumbersTheInbox()
    {
        data.WriteNote("inbox", "# inbox\n\n!d keep the inbox short\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal($"# inbox\n{Line}\n\n!d ab12-1 keep the inbox short\n- 2026-10-29 10:05 call Anna\n", ReadNote("inbox"));
    }

    // Note today: the line and the daily note's own bare marker are numbered before the one write.
    [Fact]
    public async Task NoteToday_NumbersTheDailyNote()
    {
        data.WriteNote("2026-10-29", "# 2026-10-29\n\n!d ship on Friday\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.NoteToday("met Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"# 2026-10-29\n{Line}\n\n!d ab12-1 ship on Friday\n- 10:05 met Anna\n", ReadNote("2026-10-29"));
    }

    // Triage to a new note and onto an existing one: the item's bare marker is numbered as it lands.
    [Fact]
    public async Task Triage_NumbersTheTarget()
    {
        const string Item = "- 2026-10-28 09:15 !d use the new vendor";
        data.WriteNote("inbox", "# inbox\n" + Item + "\n");
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult made = await data.Actions!.TriageToNote(InboxTriage.Parse(Item)!, "Vendor");
        Assert.Equal(ActionKind.Done, made.Kind);
        Assert.Equal($"# Vendor\n{Line}\n\n!d ab12-1 use the new vendor\n", ReadNote("Vendor"));

        const string Next = "- 2026-10-28 09:20 !d hire two people";
        data.WriteNote("inbox", "# inbox\n" + Next + "\n");
        await data.RefreshAsync();
        ActionResult added = await data.Actions.TriageAppend(InboxTriage.Parse(Next)!, "Plan", null);
        Assert.Equal(ActionKind.Done, added.Kind);
        Assert.Equal($"# Plan\n{Line}\n- !d ab12-2 hire two people\n", ReadNote("Plan"));
    }

    // Import: the stored file is numbered right after it is written; the source stays.
    [Fact]
    public async Task ImportNote_NumbersTheImportedNote()
    {
        string source = data.Outside("Plan.md");
        File.WriteAllBytes(source, Encoding.UTF8.GetBytes("# Plan\r\n\r\n!d go\r\n"));
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ImportNote(source, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"# Plan\r\n{Line}\r\n\r\n!d ab12-1 go\r\n", ReadNote("Plan"));
        Assert.Equal("# Plan\r\n\r\n!d go\r\n", File.ReadAllText(source));
    }

    // The day's end: the daily note it wrote is numbered.
    [Fact]
    public async Task EndDay_NumbersTheDailyNote()
    {
        data.WriteNote("2026-10-29", "# 2026-10-29\n\n!d no meetings on Monday\n");
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.EndDay(DesktopFixture.Today);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.StartsWith($"# 2026-10-29\n{Line}\n\n!d ab12-1 no meetings on Monday\n", ReadNote("2026-10-29"), StringComparison.Ordinal);
    }

    // The briefing: its composed text is numbered before it is created (a todo that starts with a bare marker).
    [Fact]
    public async Task OpenBriefing_NumbersTheBriefing()
    {
        data.AddTodo("!d pick the venue", DesktopFixture.Today);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenBriefing();

        Assert.Equal(ActionKind.Done, result.Kind);
        string text = ReadNote(Path.Combine("briefings", "2026-10-29"));
        Assert.Contains("!d ab12-1 pick the venue", text, StringComparison.Ordinal);
        Assert.Contains(Line, text, StringComparison.Ordinal);
    }

    // Link rewrites: each note whose links a rename rewrites is numbered in the same write (the counter starts at 1, the
    // typed ab12-5 elsewhere does not move it); the renamed note keeps its numbers.
    [Fact]
    public async Task RenameNote_NumbersTheRewrittenNotes()
    {
        data.WriteNote("Plan", $"# Plan\n{Line}\n\n!d ab12-5 kept\n");
        data.WriteNote("Notes", "See [[Plan]].\n\n!d linked decision\n");
        await data.RefreshAsync();

        ActionResult renamed = await data.Actions!.RenameNote("Plan", "Project", new Confirmation());

        Assert.Equal(ActionKind.Done, renamed.Kind);
        Assert.Equal($"{Line}\nSee [[Project]].\n\n!d ab12-1 linked decision\n", ReadNote("Notes"));
        // 353: the renamed note's title line follows the new name; its numbers stay.
        Assert.Equal($"# Project\n{Line}\n\n!d ab12-5 kept\n", ReadNote("Project"));
    }

    // Merge: the source's numbers pass to the kept note (its tokens stay) and a bare marker gets a number.
    [Fact]
    public async Task MergeNotes_KeepsSourceNumbers_NumbersBare()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Old", $"# Old\n{Line}\n\n!d ab12-7 seven\n\n!d bare\n");
        await data.RefreshAsync();

        ActionResult merged = await data.Actions!.MergeNotes("Plan", "Old", new Confirmation());

        Assert.Equal(ActionKind.Done, merged.Kind);
        string text = ReadNote("Plan");
        Assert.Contains("!d ab12-7 seven", text, StringComparison.Ordinal);
        Assert.Contains("!d ab12-1 bare", text, StringComparison.Ordinal);
        TagIndex index = new TagIndex(data.Root);
        index.Sync(data.Notes!);
        Assert.Equal("Plan", index.DecisionByKey("ab12", 7)!.Note);
    }

    // With no numbers left the capture still lands, the marker stays bare and the state tells the user.
    [Fact]
    public async Task CaptureText_NoNumbersLeft_StaysBare_Told()
    {
        data.WriteNote("inbox", "# inbox\n\n!d keep it\n");
        await data.RefreshAsync();
        Assert.Equal(1, new TagIndex(data.Root).ReserveDecisionNumbers("ab12", DecisionParser.MaxNumber));

        ActionResult result = await data.Actions!.CaptureText("call Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("# inbox\n\n!d keep it\n- 2026-10-29 10:05 call Anna\n", ReadNote("inbox"));
        Assert.True(data.State!.DecisionNumbersRanOut);
    }

    private string ReadNote(string name) => File.ReadAllText(Path.Combine(data.Root, name + ".md"));
}
