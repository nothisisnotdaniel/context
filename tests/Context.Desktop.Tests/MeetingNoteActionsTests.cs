using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 167 AC-6: DesktopActions.OpenMeetingNote and the palette's /meeting <n>, against real files and the fixture's clock.
public sealed class MeetingNoteActionsTests : IDisposable
{
    // Opening a note reads it on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Review = "Meetings/Review Q4 plan/2026-10-29";

    // The name meeting notes had before job 220.
    private const string LegacyReview = "2026-10-29 Review Q4 plan";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string PathOf(string name, string extension = ".md") =>
        Path.Combine([data.Root, .. name.Split('/')]) + extension;

    // Two appointments: 1 Standup (09:00 floating), 2 "Review: Q4/plan" (11:00 floating, room, attendee).
    private void WriteDay()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Review: Q4/plan", "20261029T110000", "20261029T120000", "Room 1")
                .Replace("END:VEVENT", "ATTENDEE;CN=Anna Schmidt:mailto:anna@example.org\r\nEND:VEVENT", StringComparison.Ordinal),
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T091500")));
    }

    private async Task<Appointment> AppointmentAsync(int number)
    {
        DesktopSnapshot snapshot = await data.RefreshAsync();
        return snapshot.Current!.Appointments[number - 1];
    }

    // 220 AC-10: a missing note is created as Meetings/<title>/<date> with MeetingNote.Format's bytes, then the state
    // is refreshed.
    [Fact]
    public async Task OpenMeetingNote_Missing_CreatesItAndRefreshes()
    {
        WriteDay();
        Appointment review = await AppointmentAsync(2);

        ActionResult result = await data.Actions!.OpenMeetingNote(review);

        Assert.Equal((ActionKind.Done, ActionReason.None, Review), (result.Kind, result.Reason, result.Text));
        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "# 2026-10-29\n\n#meeting\n\n- Title: Review: Q4/plan\n- Time: 2026-10-29 11:00-12:00\n"
                + "- Location: Room 1\n- Attendees: @[Anna Schmidt]\n"),
            File.ReadAllBytes(PathOf(Review)));
        Assert.Contains(data.State!.Snapshot!.Notes, n => n.Name == Review);
        Assert.Equal("# Meetings\n", File.ReadAllText(PathOf("Meetings")));
        Assert.Equal("# Review Q4 plan\n", File.ReadAllText(PathOf("Meetings/Review Q4 plan")));
        Assert.False(File.Exists(PathOf(LegacyReview)));
    }

    // An existing new-style note, or a legacy plain or sensitive one: Done/Unchanged with its name, nothing written,
    // no key call, no refresh.
    [Theory]
    [InlineData(Review, ".md")]
    [InlineData(LegacyReview, ".md")]
    [InlineData(LegacyReview, ".ctxs")]
    public async Task OpenMeetingNote_Existing_Unchanged(string name, string extension)
    {
        WriteDay();
        if (name == Review)
        {
            data.WriteNote("Meetings", "# Meetings\n");
            data.WriteFile(Path.Combine("Meetings", "Review Q4 plan.md"), "# Review Q4 plan\n");
        }

        data.WriteFile(Path.Combine(name.Split('/')) + extension, "kept\r\n");
        Appointment review = await AppointmentAsync(2);
        DesktopSnapshot snapshot = await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.OpenMeetingNote(review);

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, name), (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Same(snapshot, data.State!.Snapshot);
    }

    // An IO error: Failed with the fixed text, no exception text, nothing created.
    [Fact]
    public async Task OpenMeetingNote_WriteFails_FixedText()
    {
        WriteDay();
        Appointment review = await AppointmentAsync(2);
        data.Actions!.BeforeWrite = () => Directory.CreateDirectory(PathOf(Review));

        ActionResult result = await data.Actions.OpenMeetingNote(review);

        Assert.Equal(
            (ActionKind.Failed, ActionReason.WriteFailed, "Can't write Meetings/Review Q4 plan/2026-10-29."),
            (result.Kind, result.Reason, result.Text));
        Assert.False(File.Exists(PathOf(Review)));
    }

    // 220 AC-8: a sensitive top-level Meetings: WriteFailed with the fixed text, nothing written.
    [Fact]
    public async Task OpenMeetingNote_SensitiveMeetings_WriteFailed()
    {
        WriteDay();
        data.WriteFile("Meetings.ctxs", "sealed");
        Appointment review = await AppointmentAsync(2);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.OpenMeetingNote(review);

        Assert.Equal(
            (ActionKind.Failed, ActionReason.WriteFailed, "Can't write Meetings/Review Q4 plan/2026-10-29."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // Busy-guarded: a call while another action runs is refused and creates nothing.
    [Fact]
    public async Task OpenMeetingNote_WhileRunning_RefusedBusy()
    {
        WriteDay();
        Appointment review = await AppointmentAsync(2);
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("a thought");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.OpenMeetingNote(review);
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (second.Kind, second.Reason));
        Assert.False(File.Exists(PathOf(Review)));
    }

    // /meeting <n> creates the note for the n-th shown appointment, opens it and shows the accent status.
    [Theory]
    [InlineData("/meeting 2")]
    [InlineData("/MEETING   02")]
    public async Task PaletteMeeting_CreatesOpensAndShowsCreated(string line)
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal(Review, cut.Find(".note-title").GetAttribute("title")), Wait);
        Assert.Equal("Created " + Review, cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.StartsWith("# 2026-10-29\n\n#meeting\n", File.ReadAllText(PathOf(Review)), StringComparison.Ordinal);
    }

    // AC-1: the number counts every appointment of today, as `context current` does (216: no dashboard shows them).
    [Fact]
    public async Task PaletteMeeting_HiddenAppointment_StillNumbered()
    {
        string[] events = Enumerable.Range(0, 20)
            .Select(i => DesktopFixture.Event($"M{i:00}", $"20261029T{8 + i / 4:00}{i % 4 * 15:00}00",
                $"20261029T{8 + i / 4:00}{i % 4 * 15 + 10:00}00"))
            .ToArray();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(events));
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await RunPalette(cut, "/meeting 20");

        cut.WaitForAssertion(() => Assert.Equal("Meetings/M19/2026-10-29", cut.Find(".note-title").GetAttribute("title")), Wait);
        Assert.True(File.Exists(PathOf("Meetings/M19/2026-10-29")));
    }

    // The appointment comes from the shown snapshot, not from a new read of the calendar.
    [Fact]
    public async Task PaletteMeeting_UsesTheShownSnapshot()
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        data.WriteCalendar("early.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Early", "20261029T070000", "20261029T071500")));

        await RunPalette(cut, "/meeting 1");

        cut.WaitForAssertion(() => Assert.Equal("Meetings/Standup/2026-10-29", cut.Find(".note-title").GetAttribute("title")), Wait);
        Assert.False(Directory.Exists(PathOf("Meetings/Early", string.Empty)));
    }

    // A blank or invalid number: red usage line, nothing written.
    [Theory]
    [InlineData("/meeting")]
    [InlineData("/meeting   ")]
    [InlineData("/meeting x")]
    [InlineData("/meeting 0")]
    [InlineData("/meeting -1")]
    [InlineData("/meeting 1 2")]
    [InlineData("/meeting 99999999999")]
    public async Task PaletteMeeting_InvalidNumber_RedUsage(string line)
    {
        WriteDay();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        await RunPalette(cut, line);

        Assert.Equal("Type /meeting <number>.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // No appointments, or a number beyond the count: the CLI's texts in red, nothing written.
    [Fact]
    public async Task PaletteMeeting_OutOfRange_RedTexts()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/meeting 1");
        Assert.Equal("There are no appointments today.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));

        WriteDay();
        await data.State!.Refresh();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        await RunPalette(cut, "/meeting 3");

        cut.WaitForAssertion(
            () => Assert.Equal("There is no appointment 3 today.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // 220 AC-6: an existing legacy note opens as it is, with no "Created" line and no new-style note.
    [Fact]
    public async Task PaletteMeeting_Existing_OpensUnchanged()
    {
        WriteDay();
        data.WriteNote(LegacyReview, "# mine\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(() => Assert.Equal(LegacyReview, cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Equal("# mine\n", File.ReadAllText(PathOf(LegacyReview)));
        Assert.False(File.Exists(PathOf("Meetings")));
        Assert.DoesNotContain("Created", cut.Find(".main-status").TextContent, StringComparison.Ordinal);
    }

    // With unsaved changes to another note it asks first and writes nothing until the user discards them.
    [Fact]
    public async Task PaletteMeeting_DirtyEditor_AsksFirst()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(
            () => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.False(File.Exists(PathOf(Review)));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(Review, cut.Find(".note-title").GetAttribute("title")), Wait);
        Assert.True(File.Exists(PathOf(Review)));
        Assert.Equal("Created " + Review, cut.Find(".main-status").TextContent);
        Assert.Equal("# Plan\n", File.ReadAllText(PathOf("Plan")));
    }

    // 203 AC-7: with unsaved changes the open note is saved first; the meeting note opens with no question.
    [Fact]
    public async Task PaletteMeeting_DirtyEditor_SavesFirstThenOpens()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nSaved first\n");

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(() => Assert.Equal(Review, cut.Find(".note-title").GetAttribute("title")), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.True(File.Exists(PathOf(Review)));
        Assert.Equal("# Plan\n\nSaved first\n", File.ReadAllText(PathOf("Plan")));
    }

    // Keep editing leaves the dirty note open and writes nothing.
    [Fact]
    public async Task PaletteMeeting_DirtyEditor_KeepEditing_NothingWritten()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/meeting 2");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.False(File.Exists(PathOf(Review)));
    }

    // 220 AC-11: the open meeting note itself (legacy or new-style, any case) with unsaved changes: no question, the
    // editor stays as it is.
    [Theory]
    [InlineData(LegacyReview)]
    [InlineData("2026-10-29 REVIEW Q4 PLAN")]
    [InlineData(Review)]
    public async Task PaletteMeeting_DirtySameNote_NoQuestion(string name)
    {
        WriteDay();
        if (name.Contains('/', StringComparison.Ordinal))
        {
            data.WriteNote("Meetings", "# Meetings\n");
            data.WriteFile(Path.Combine("Meetings", "Review Q4 plan.md"), "# Review Q4 plan\n");
        }

        data.WriteFile(Path.Combine(name.Split('/')) + ".md", "# mine\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenByPalette(cut, js, name);
        js.EditUnsavable("# mine\n\nUnsaved\n");

        await RunPalette(cut, "/meeting 2");

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(name, cut.Find(".note-title").GetAttribute("title"));
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Equal("# mine\n", File.ReadAllText(PathOf(name)));
    }

    // A write failure shows the fixed red line and opens nothing.
    [Fact]
    public async Task PaletteMeeting_WriteFails_RedLine()
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        data.Actions!.BeforeWrite = () => Directory.CreateDirectory(PathOf(Review));

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(
            () => Assert.Equal("Can't write Meetings/Review Q4 plan/2026-10-29.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
    }

    // 220 AC-6: a sensitive legacy meeting note opens through the unlock flow, never rewritten.
    [Fact]
    public async Task PaletteMeeting_SensitiveNote_OpensThroughUnlock()
    {
        WriteDay();
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive(LegacyReview, "# secret\n");
        data.Keys.AgreeThrows = true;
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        byte[] before = File.ReadAllBytes(PathOf(LegacyReview, ".ctxs"));

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock " + LegacyReview + ".", cut.Find(".unreadable").TextContent), Wait);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal(before, File.ReadAllBytes(PathOf(LegacyReview, ".ctxs")));
        Assert.False(File.Exists(PathOf(LegacyReview)));
        Assert.False(File.Exists(PathOf(Review)));
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    // Opens a note by its full name through /open: the editor is mounted and ready and shows that name.
    private static async Task OpenByPalette(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await RunPalette(cut, "/open " + name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
    }

    // 227 AC-7: a private appointment takes no number: /meeting 2 opens the second public one, /meeting 3 is out of
    // range, and no note is ever made for the private one.
    [Fact]
    public async Task PaletteMeeting_PrivateAppointment_NotNumbered()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T091500"),
            DesktopFixture.Event("Doctor", "20261029T100000", "20261029T110000")
                .Replace("END:VEVENT", "CLASS:PRIVATE\r\nEND:VEVENT", StringComparison.Ordinal),
            DesktopFixture.Event("Plan", "20261029T120000", "20261029T130000")));
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/meeting 2");

        cut.WaitForAssertion(() => Assert.Equal("Meetings/Plan/2026-10-29", cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        await RunPalette(cut, "/meeting 3");

        cut.WaitForAssertion(
            () => Assert.Equal("There is no appointment 3 today.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.False(Directory.Exists(PathOf("Meetings/Doctor", string.Empty)));
    }
}
