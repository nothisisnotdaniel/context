using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 395 AC-4: once a meeting note has been made (through /meeting or the appointment page's create link), every way to
// add a note still adds one: the notes bar entry row, the palette's /new and /capture, and the top bar Capture popover,
// both while the meeting note is open (unedited) and after it is closed.
public sealed class NewNoteAfterMeetingTests : IDisposable
{
    // Opening a note, the sidebar and the appointment page load on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Meeting = "Meetings/Budget/2026-10-29";

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    public enum Via
    {
        Palette,
        CreateLink,
    }

    public enum After
    {
        Open,
        OtherNote,
        AppointmentPage,
    }

    public enum AddPath
    {
        EntryRow,
        New,
        Capture,
        TopBarCapture,
    }

    public static TheoryData<Via, After, AddPath> Cases()
    {
        TheoryData<Via, After, AddPath> cases = [];
        foreach (Via via in Enum.GetValues<Via>())
        {
            foreach (After after in Enum.GetValues<After>())
            {
                foreach (AddPath path in Enum.GetValues<AddPath>())
                {
                    cases.Add(via, after, path);
                }
            }
        }

        return cases;
    }

    // A "Budget" appointment at 11:00 (after the fixture's 10:05), and a "Retro" one.
    private void WriteDay() =>
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Budget", "20261029T110000", "20261029T120000"),
            DesktopFixture.Event("Retro", "20261029T130000", "20261029T140000")));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AfterMeetingNote_AddingANote_Works(Via via, After after, AddPath path)
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForSidebar(cut);

        await MakeMeetingNote(cut, js, via);
        if (after == After.OtherNote)
        {
            await OpenByPalette(cut, js, "Plan");
        }
        else if (after == After.AppointmentPage)
        {
            await Click(cut, () => cut.FindAll(".day-appointments .appointment-item")[0]);
            WaitForPage(cut);
        }

        await AddNote(cut, js, path);
    }

    // 395 AC-1/AC-3, the reported steps: after the meeting note is made, the appointment page lists it (through its
    // Meetings/Budget parent) and still offers the create link, which now opens the title field and adds a note.
    [Theory]
    [InlineData(Via.Palette)]
    [InlineData(Via.CreateLink)]
    public async Task AfterMeetingNote_AppointmentPageLink_AddsANote(Via via)
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForSidebar(cut);
        await MakeMeetingNote(cut, js, via);

        await Click(cut, () => cut.FindAll(".day-appointments .appointment-item")[0]);
        WaitForPage(cut);
        Assert.Contains(cut.FindAll(".appointment-page .note-button"), b => b.GetAttribute("title") == "Meetings/Budget");
        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".appointment-page input.create-note-title")), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.InvokeAsync(() => cut.Find(".appointment-page input.create-note-title").InputAsync(new ChangeEventArgs { Value = "Fresh" }));
        await cut.InvokeAsync(() => cut.Find(".appointment-page input.create-note-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));

        cut.WaitForAssertion(() => Assert.Equal("Created Meetings/Budget/Fresh", cut.Find(".main-status").TextContent), Wait);
        AssertOpened(cut, js, mounts, "Meetings/Budget/Fresh");
        Assert.False(File.Exists(Path.Combine(data.Root, "Fresh.md")));
    }

    // Makes the Budget meeting note and waits until the editor shows it, mounted and ready; a booking offer is declined.
    private async Task MakeMeetingNote(IRenderedComponent<Main> cut, EditorJs js, Via via)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        if (via == Via.Palette)
        {
            await RunPalette(cut, "/meeting Budget");
        }
        else
        {
            await Click(cut, () => cut.FindAll(".day-appointments .appointment-item")[0]);
            WaitForPage(cut);
            await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        }

        cut.WaitForAssertion(() => Assert.Equal(Meeting, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "Meetings", "Budget", "2026-10-29.md")));
        if (cut.FindAll(".dialog button.cancel").Count > 0)
        {
            await Click(cut, () => cut.Find(".dialog button.cancel"));
            cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dialog")), Wait);
        }
    }

    private async Task AddNote(IRenderedComponent<Main> cut, EditorJs js, AddPath path)
    {
        switch (path)
        {
            case AddPath.EntryRow:
            {
                int mounts = js.Calls("ContextEditor.mount").Count;
                await cut.InvokeAsync(() => cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "Fresh" }));
                await cut.InvokeAsync(() => cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));
                Bar.WaitForAssertion(() => Assert.Equal("Created Fresh", data.StatusBar.Text), Wait);
                Assert.Equal("accent", data.StatusBar.Css);
                AssertOpened(cut, js, mounts, "Fresh");
                break;
            }

            case AddPath.New:
            {
                int mounts = js.Calls("ContextEditor.mount").Count;
                await RunPalette(cut, "/new Fresh");
                cut.WaitForAssertion(() => Assert.Equal("Created Fresh", cut.Find(".main-status").TextContent), Wait);
                Assert.Contains("accent", cut.Find(".main-status").ClassList);
                AssertOpened(cut, js, mounts, "Fresh");
                break;
            }

            case AddPath.Capture:
                await RunPalette(cut, "/capture call Anna");
                AssertCaptured(cut);
                break;

            case AddPath.TopBarCapture:
                await cut.InvokeAsync(() => cut.Find("button.capture-open").ClickAsync(new MouseEventArgs()));
                cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
                await cut.InvokeAsync(() => cut.Find(".capture-pop input").InputAsync(new ChangeEventArgs { Value = "call Anna" }));
                await cut.InvokeAsync(() => cut.Find(".capture-pop input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));
                AssertCaptured(cut);
                break;
        }
    }

    // The new note is on disk, open in a new editor, and listed in the sidebar.
    private void AssertOpened(IRenderedComponent<Main> cut, EditorJs js, int mounts, string name)
    {
        Assert.True(File.Exists(Path.Combine([data.Root, .. (name + ".md").Split('/')])));
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll(".notes .note-list button"), b => b.GetAttribute("title") == name || b.TextContent.Trim() == name), Wait);
    }

    private void AssertCaptured(IRenderedComponent<Main> cut)
    {
        cut.WaitForAssertion(() => Assert.Equal("Captured.", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
        Assert.Contains("call Anna", File.ReadAllText(Path.Combine(data.Root, "inbox.md")), StringComparison.Ordinal);
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.InvokeAsync(() => cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true }));
        await cut.InvokeAsync(() => cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line }));
        await cut.InvokeAsync(() => cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));
    }

    private static async Task OpenByPalette(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await RunPalette(cut, "/open " + name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    // "the reload count is 0", then the element is found and clicked in one dispatcher call.
    private async Task Click(IRenderedComponent<Main> cut, Func<IElement> find)
    {
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => find().ClickAsync(new MouseEventArgs()));
    }

    // ".day-appointments shows no Loading… and has 2 .appointment-item".
    private static void WaitForSidebar(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".day-appointments").TextContent, StringComparison.Ordinal);
            Assert.Equal(2, cut.FindAll(".day-appointments .appointment-item").Count);
        }, Wait);

    // ".appointment-page shows no Loading… and its section.meeting-todos has h3 Todos".
    private static void WaitForPage(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal);
            Assert.Equal("Todos", cut.Find(".appointment-page > section.meeting-todos > h3").TextContent);
        }, Wait);
}
