using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 214 AC-5 to AC-9: the appointment page in the main region, opened from the sidebar, through Main.
public sealed class AppointmentPanelTests : IDisposable
{
    // The sidebar list, the page's related notes and an opened note load on Task.Run; every wait passes this timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private const string ThreeHeadings =
        """[{"level":1,"text":"Top"},{"level":2,"text":"Goals"},{"level":2,"text":"Risks"}]""";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private static string Event(string title, string start, string end, params string[] attendees) =>
        DesktopFixture.Event(title, start, end).Replace(
            "END:VEVENT",
            string.Concat(attendees.Select(a => $"ATTENDEE;CN={a}:mailto:x@example.org\r\n")) + "END:VEVENT",
            StringComparison.Ordinal);

    // Two "Budget" appointments with different attendees, and a "Retro" one, all after the fixture's 10:00 local time.
    private void WriteDay() =>
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            Event("Budget", "20261029T110000", "20261029T120000", "Anna"),
            Event("Retro", "20261029T130000", "20261029T140000"),
            Event("Budget", "20261029T150000", "20261029T160000", "Bob")));

    private void WriteNotes()
    {
        data.WriteNote("Anna notes", "# Anna\n");
        data.WriteNote("Bob stuff", "# Bob\n");
        data.WriteNote("Budget 2027", "# Budget\n");
        data.WriteNote("Zeta", "#budget\n");
        data.WriteNote("Retro ideas", "# Retro\n");
        data.WriteNote("Other", "# Other\n");
    }

    // AC-5: a click shows the page in the main region with the title and Close; Close hides it again.
    [Fact]
    public async Task Click_ShowsPage_CloseHidesIt()
    {
        WriteDay();
        WriteNotes();
        IRenderedComponent<Main> cut = await RenderMain();

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);

        IElement page = cut.Find(".current-region section.appointment-page");
        Assert.Equal("Budget", page.QuerySelector("h2")!.TextContent);
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        IElement close = page.QuerySelectorAll("button").Single(b => b.TextContent == "Close");
        Assert.Equal("button", close.GetAttribute("type"));

        await close.ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".appointment-page"));
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Single(cut.FindAll(".current-region p.no-note"));
    }

    // AC-5: the outline is hidden while the page is shown, and opening a note from the page closes it.
    [Fact]
    public async Task Page_HidesOutline_OpeningANoteClosesIt()
    {
        WriteDay();
        WriteNotes();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await RenderMain();
        await OpenFromList(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".appointment-page .note-button").Single(b => b.TextContent == "Anna notes").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Anna notes", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Empty(cut.FindAll(".appointment-page"));
    }

    // AC-5: a dirty note is saved first, and the page opens with no question.
    [Fact]
    public async Task DirtyNote_SavedFirst_ThenPage()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();
        await OpenFromList(cut, js, "Plan");
        js.Edit("# Plan\n\nSaved first\n");

        await Item(cut, 0).ClickAsync(new MouseEventArgs());

        WaitForPage(cut);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("# Plan\n\nSaved first\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-5: when that save fails, Discard changes to <name>? is asked; Keep editing stays, Discard shows the page.
    [Fact]
    public async Task DirtyNote_SaveFails_AsksThenShowsPage()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();
        await OpenFromList(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".appointment-page"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        WaitForPage(cut);
        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-6, AC-7: same-titled appointments share one page over the union of their attendees, with no cap; the second
    // keeps the page with no Loading…; another title gives another page.
    [Fact]
    public async Task SameTitle_OnePage_UnionOfAttendees_OtherTitleOtherPage()
    {
        WriteDay();
        WriteNotes();
        IRenderedComponent<Main> cut = await RenderMain();

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);
        Assert.Equal(
            [("Anna notes", "Anna notes", "anna"), ("Bob stuff", "Bob stuff", "bob"), ("Budget 2027", "Budget 2027", "budget"), ("Zeta", "Zeta", "#budget")],
            Notes(cut));
        Assert.All(cut.FindAll(".appointment-page .matches"), m => Assert.Contains("grey", m.ClassList));
        IElement heading = cut.Find(".appointment-page h2");

        await Item(cut, 2).ClickAsync(new MouseEventArgs());

        Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal);
        Assert.Equal("Budget", cut.Find(".appointment-page h2").TextContent);
        Assert.Equal(4, Notes(cut).Length);

        await Item(cut, 1).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Retro", cut.Find(".appointment-page h2").TextContent), Wait);
        WaitForPage(cut);
        Assert.Equal([("Retro ideas", "Retro ideas", "retro")], Notes(cut));
    }

    // AC-6: a subpage shows its title, with the full name in title.
    [Fact]
    public async Task Subpage_TitleShown_FullNameInTitle()
    {
        WriteDay();
        data.WriteNote("projects", "# projects\n");
        data.WriteFile(Path.Combine("projects", "Retro board.md"), "# Retro board\n");
        IRenderedComponent<Main> cut = await RenderMain();

        await Item(cut, 1).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);

        Assert.Equal([("Retro board", "projects/Retro board", "retro")], Notes(cut));
    }

    // AC-8: with no related note, the create link makes the meeting note and opens it with an accent Created line.
    [Fact]
    public async Task NoRelated_CreateLink_MakesAndOpensMeetingNote()
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);
        Assert.Empty(cut.FindAll(".appointment-page .note-button"));
        IElement link = cut.Find(".appointment-page button.create-note-link");
        Assert.Equal("Create a new note", link.TextContent);
        Assert.Equal("button", link.GetAttribute("type"));

        await link.ClickAsync(new MouseEventArgs());

        const string Name = "2026-10-29 Budget";
        cut.WaitForAssertion(() => Assert.Equal(Name, cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Equal("Created " + Name, cut.Find(".main-status").TextContent);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
        Assert.Contains("11:00", File.ReadAllText(Path.Combine(data.Root, Name + ".md")), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".appointment-page"));
    }

    // AC-9: a bad context.db shows the red line and no create link; the sidebar still lists the appointments.
    [Fact]
    public async Task CorruptIndex_RedLine_NoCreateLink_SidebarStillLists()
    {
        WriteDay();
        data.WriteNote("Anna notes", "# Anna\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        IRenderedComponent<Main> cut = await RenderMain();

        await Item(cut, 0).ClickAsync(new MouseEventArgs());
        WaitForPage(cut);

        IElement line = cut.Find(".appointment-page p.red");
        Assert.Equal("Can't read the related notes.", line.TextContent);
        Assert.Empty(cut.FindAll(".appointment-page .create-note-link"));
        Assert.Empty(cut.FindAll(".appointment-page .note-button"));
        Assert.Equal(3, cut.FindAll(".day-appointments .appointment-item").Count);
    }

    private async Task<IRenderedComponent<Main>> RenderMain()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForSidebar(cut);
        return cut;
    }

    private static IElement Item(IRenderedComponent<Main> cut, int index) =>
        cut.FindAll(".day-appointments .appointment-item")[index];

    private static (string Text, string? Title, string Matches)[] Notes(IRenderedComponent<Main> cut) =>
        cut.FindAll(".appointment-page li")
            .Select(li => (li.QuerySelector(".note-button")!.TextContent, li.QuerySelector(".note-button")!.GetAttribute("title"), li.QuerySelector(".matches")!.TextContent))
            .ToArray();

    // ".day-appointments shows no Loading… and has 3 .appointment-item".
    private static void WaitForSidebar(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".day-appointments").TextContent, StringComparison.Ordinal);
            Assert.Equal(3, cut.FindAll(".day-appointments .appointment-item").Count);
        }, Wait);

    // ".appointment-page shows no Loading…".
    private static void WaitForPage(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal), Wait);

    // ".note-title is <name>, the editor is mounted and .links-panel shows no Loading…".
    private static async Task OpenFromList(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".notes-region button.name").Single(b => b.TextContent == name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").TextContent), Wait);
    }
}
