using AngleSharp.Dom;
using Bunit;
using Bunit.Web.AngleSharp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 214 AC-5 to AC-9: the appointment page in the main region, opened from the sidebar, through Main.
public sealed class AppointmentPanelTests : IDisposable
{
    // The sidebar list, the page's related notes and an opened note load on Task.Run; every wait passes this timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string CloseButton = "section.appointment-page.page > .panel-bar button.close";

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

    // AC-5, 219 AC-1/AC-2/AC-5: a click shows the page in the main region with the title and the icon-only ×; the ×
    // hides it again.
    [Fact]
    public async Task Click_ShowsPage_CloseHidesIt()
    {
        WriteDay();
        WriteNotes();
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement page = cut.Find(".current-region section.appointment-page");
        Assert.Contains("page", page.ClassList);
        Assert.Equal("Appointment", page.GetAttribute("aria-label"));
        Assert.Equal("Budget", page.QuerySelector("h2")!.TextContent);
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.DoesNotContain(page.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Close");
        IElement close = Assert.Single(cut.FindAll(CloseButton));
        Assert.Equal("button", close.GetAttribute("type"));
        Assert.Equal("Close", close.GetAttribute("aria-label"));
        Assert.Equal("Close", close.GetAttribute("title"));
        Assert.Equal(string.Empty, close.TextContent);
        IElement svg = close.QuerySelector("svg")!;
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.Same(close, page.QuerySelector(".panel-bar")!.LastElementChild);

        await Click(cut, () => cut.Find(CloseButton));

        Assert.Empty(cut.FindAll(".appointment-page"));
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Single(cut.FindAll(".current-region p.no-note"));
    }

    // 219 AC-3: one h3 Notes sits between the panel bar and the list, and labels the list.
    [Fact]
    public async Task Related_NotesHeading_LabelsTheList()
    {
        WriteDay();
        WriteNotes();
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement heading = Assert.Single(cut.FindAll(".appointment-page > h3"));
        Assert.Equal("Notes", heading.TextContent);
        IElement list = cut.Find(".appointment-page ul.appointment-notes");
        IElement bar = cut.Find(".appointment-page .panel-bar");
        IElement todos = cut.Find(".appointment-page > section.meeting-todos");
        Assert.True(Before(bar, heading));
        Assert.True(Before(heading, list));
        Assert.True(Before(bar, todos));
        Assert.True(Before(todos, heading));
        Assert.False(string.IsNullOrEmpty(heading.Id));
        Assert.Equal(heading.Id, list.GetAttribute("aria-labelledby"));
    }

    // 219 AC-4: with no related note, the heading shows before the create link.
    [Fact]
    public async Task NoRelated_NotesHeading_BeforeCreateLink()
    {
        WriteDay();
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement heading = Assert.Single(cut.FindAll(".appointment-page > h3"));
        Assert.Equal("Notes", heading.TextContent);
        Assert.True(Before(heading, cut.Find(".appointment-page button.create-note-link")));
    }

    // 219 AC-4: with no related note and nothing to create from (no appointment of the day has the title), the heading
    // shows before the grey line.
    [Fact]
    public async Task NoRelated_NothingToCreateFrom_NotesHeading_BeforeGreyLine()
    {
        WriteDay();
        await data.RefreshAsync();

        IRenderedComponent<AppointmentPanel> cut = data.Bunit.Render<AppointmentPanel>(p => p
            .Add(c => c.Title, "Nothing")
            .Add(c => c.Day, new DateOnly(2026, 10, 29)));
        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal), Wait);

        IElement line = cut.Find(".appointment-page p.grey");
        Assert.Equal("No related notes.", line.TextContent);
        IElement heading = Assert.Single(cut.FindAll(".appointment-page > h3"));
        Assert.Equal("Notes", heading.TextContent);
        Assert.True(Before(heading, line));
    }

    // 219 AC-4: with an unreadable index, the heading shows before the red line.
    [Fact]
    public async Task CorruptIndex_NotesHeading_BeforeRedLine()
    {
        WriteDay();
        data.WriteFile(TagIndex.FileName, NotADatabase);
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement line = cut.Find(".appointment-page p.red");
        Assert.Equal("Can't read the related notes.", line.TextContent);
        IElement heading = Assert.Single(cut.FindAll(".appointment-page > h3"));
        Assert.Equal("Notes", heading.TextContent);
        Assert.True(Before(heading, line));
    }

    // 219 AC-6: the heading rule exists and uses no colour literal.
    [Fact]
    public void AppCss_HeadingRule_ExistsWithoutColourLiterals()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        int at = css.IndexOf("\n.appointment-page h3 {", StringComparison.Ordinal);
        Assert.True(at >= 0);
        string block = css[at..css.IndexOf('}', at)];
        Assert.Contains("margin: 12px 0 4px;", block, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b|\b(rgb|rgba|hsl|hsla)\(|\b(white|black|red|green|blue|gray|grey|transparent|orange|yellow|purple|silver)\b", block);
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

        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.FindAll(".appointment-page .note-button").Single(b => b.TextContent == "Anna notes"));

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

        await ClickItem(cut, 0);

        WaitForPage(cut);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("# Plan\n\nSaved first\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // 279 AC-5: a state reload after the edit re-renders the sidebar; the click still reaches the current row, the
    // dirty note is saved first and the page opens with no question.
    [Fact]
    public async Task DirtyNote_ReloadBeforeClick_SavedFirst()
    {
        WriteDay();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();
        await OpenFromList(cut, js, "Plan");
        js.Edit("# Plan\n\nSaved first\n");

        // "Refresh() completed": the sidebar rows are rendered again with new handler IDs.
        await data.State!.Refresh().WaitAsync(Wait);
        await ClickItem(cut, 0);

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

        await ClickItem(cut, 0);
        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        await Click(cut, () => cut.Find("[role=dialog] button.cancel"));
        Assert.Empty(cut.FindAll(".appointment-page"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);

        await ClickItem(cut, 0);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await Click(cut, () => cut.Find("[role=dialog] button.confirm"));

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

        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.Equal(
            [("Anna notes", "Anna notes", "anna"), ("Bob stuff", "Bob stuff", "bob"), ("Budget 2027", "Budget 2027", "budget"), ("Zeta", "Zeta", "#budget")],
            Notes(cut));
        Assert.All(cut.FindAll(".appointment-page .matches"), m => Assert.Contains("grey", m.ClassList));
        IElement heading = cut.Find(".appointment-page h2");

        await ClickItem(cut, 2);

        Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal);
        Assert.Equal("Budget", cut.Find(".appointment-page h2").TextContent);
        Assert.Equal(4, Notes(cut).Length);

        await ClickItem(cut, 1);
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

        await ClickItem(cut, 1);
        WaitForPage(cut);

        Assert.Equal([("Retro board", "projects/Retro board", "retro")], Notes(cut));
    }

    // AC-8, 220 AC-10: with no related note, the create link makes Meetings/Budget/2026-10-29 and opens it with an
    // accent Created line.
    [Fact]
    public async Task NoRelated_CreateLink_MakesAndOpensMeetingNote()
    {
        WriteDay();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.Empty(cut.FindAll(".appointment-page .note-button"));
        IElement link = cut.Find(".appointment-page button.create-note-link");
        Assert.Equal("Create a new note", link.TextContent);
        Assert.Equal("button", link.GetAttribute("type"));

        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));

        const string Name = "Meetings/Budget/2026-10-29";
        cut.WaitForAssertion(() => Assert.Equal(Name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Equal("Created Meetings/Budget/2026-10-29", cut.Find(".main-status").TextContent);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
        Assert.Contains(
            "11:00", File.ReadAllText(Path.Combine(data.Root, "Meetings", "Budget", "2026-10-29.md")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29 Budget.md")));
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

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement line = cut.Find(".appointment-page p.red");
        Assert.Equal("Can't read the related notes.", line.TextContent);
        Assert.Empty(cut.FindAll(".appointment-page .create-note-link"));
        Assert.Empty(cut.FindAll(".appointment-page .note-button"));
        Assert.Equal(3, cut.FindAll(".day-appointments .appointment-item").Count);
    }

    private const string BudgetMeeting = "Meetings/Budget/2026-10-29";

    private const string TitleField = ".appointment-page .create-note-entry input.create-note-title";

    // The Budget meeting note of 2026-10-29 with its parents, as /meeting writes them.
    private void WriteBudgetMeeting()
    {
        data.WriteNote("Meetings", "# Meetings\n");
        data.WriteFile(Path.Combine("Meetings", "Budget.md"), "# Budget\n");
        data.WriteFile(Path.Combine("Meetings", "Budget", "2026-10-29.md"), "# 2026-10-29\n\n#meeting\n");
    }

    // Renders Main, opens the first Budget appointment's page and waits until it lists its related notes.
    private async Task<IRenderedComponent<Main>> BudgetPage()
    {
        IRenderedComponent<Main> cut = await RenderMain();
        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.NotEmpty(cut.FindAll(".appointment-page .note-button"));
        return cut;
    }

    // "the title field shows", then one empty dispatcher call, so the render that showed it has run its after-render
    // focus call (that call renders nothing, so no wait could see it); the last focus call went to the field.
    private async Task WaitForTitleField(IRenderedComponent<Main> cut)
    {
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(TitleField)), Wait);
        await cut.InvokeAsync(() => { });
        Assert.Equal(TitleFieldId(cut), LastFocusId());
    }

    // "the field is gone", then as above: the last focus call went to the create link.
    private async Task WaitForLinkFocus(IRenderedComponent<Main> cut)
    {
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".appointment-page .create-note-entry")), Wait);
        await cut.InvokeAsync(() => { });
        Assert.Equal(cut.FindComponent<AppointmentPanel>().Instance.CreateLinkReference.Id, LastFocusId());
    }

    private static string TitleFieldId(IRenderedComponent<Main> cut)
    {
        string id = cut.FindComponent<AppointmentPanel>().Instance.TitleFieldReference.Id;
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    private string? LastFocusId() =>
        data.Bunit.JSInterop.Invocations.LastOrDefault(i => i.Identifier == "Blazor._internal.domWrapper.focus") is
            { Arguments: [ElementReference target, ..] }
            ? target.Id
            : null;

    private async Task TypeTitle(IRenderedComponent<Main> cut, string title, string? key)
    {
        await cut.InvokeAsync(() => cut.Find(TitleField).InputAsync(new ChangeEventArgs { Value = title }));
        if (key is not null)
        {
            await cut.InvokeAsync(() => cut.Find(TitleField).KeyDownAsync(new KeyboardEventArgs { Key = key }));
        }
    }

    // ".note-title is <name>, a new editor is mounted and .main-status is the accent Created <name>".
    private void WaitForCreated(IRenderedComponent<Main> cut, EditorJs js, int mounts, string name)
    {
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal("Created " + name, cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
    }

    // 395 AC-1: with related notes listed, the create link shows after the list.
    [Fact]
    public async Task Related_CreateLink_ShowsAfterTheList()
    {
        WriteDay();
        WriteNotes();
        IRenderedComponent<Main> cut = await BudgetPage();

        IElement link = Assert.Single(cut.FindAll(".appointment-page button.create-note-link"));
        Assert.Equal("Create a new note", link.TextContent);
        Assert.Equal("button", link.GetAttribute("type"));
        Assert.True(Before(cut.Find(".appointment-page ul.appointment-notes"), link));
        Assert.Empty(cut.FindAll(".appointment-page > p.grey"));
        Assert.Empty(cut.FindAll(".appointment-page .create-note-entry"));
    }

    // 395 AC-1: once the meeting note exists (its Meetings/Budget parent is related), the link still shows.
    [Fact]
    public async Task MeetingNoteExists_CreateLinkStillShows()
    {
        WriteDay();
        WriteBudgetMeeting();
        IRenderedComponent<Main> cut = await BudgetPage();

        Assert.Contains("Meetings/Budget", Notes(cut).Select(n => n.Title));
        Assert.Single(cut.FindAll(".appointment-page button.create-note-link"));
    }

    // 395 AC-1: with no matching appointment but related notes, the list shows with the link; with neither, the grey
    // line stays and there is no link.
    [Fact]
    public async Task NoAppointment_RelatedListedWithLink_NoneGreyLine()
    {
        WriteDay();
        data.WriteNote("Nothing much", "# Nothing\n");
        await data.RefreshAsync();

        IRenderedComponent<AppointmentPanel> cut = data.Bunit.Render<AppointmentPanel>(p => p
            .Add(c => c.Title, "Nothing")
            .Add(c => c.Day, new DateOnly(2026, 10, 29)));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".appointment-page .note-button")), Wait);
        Assert.Single(cut.FindAll(".appointment-page button.create-note-link"));

        IRenderedComponent<AppointmentPanel> none = data.Bunit.Render<AppointmentPanel>(p => p
            .Add(c => c.Title, "Zilch")
            .Add(c => c.Day, new DateOnly(2026, 10, 29)));
        none.WaitForAssertion(() =>
            Assert.DoesNotContain("Loading…", none.Find(".appointment-page").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal("No related notes.", none.Find(".appointment-page p.grey").TextContent);
        Assert.Empty(none.FindAll(".appointment-page .create-note-link"));
    }

    // 395 AC-2: related notes listed but no meeting note yet: the link runs the /meeting steps.
    [Fact]
    public async Task Related_NoMeetingNote_CreateLink_MakesAndOpensMeetingNote()
    {
        WriteDay();
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await BudgetPage();

        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));

        WaitForCreated(cut, js, 0, BudgetMeeting);
        Assert.Contains(
            "11:00", File.ReadAllText(Path.Combine(data.Root, "Meetings", "Budget", "2026-10-29.md")), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".appointment-page"));
    }

    // 395 AC-3: the meeting note exists: the link opens the title field (prefilled, focused) and writes nothing;
    // Create note makes the note, opens it and shows Created.
    [Fact]
    public async Task MeetingNoteExists_CreateLink_ShowsField_CreateMakesAndOpensNote()
    {
        WriteDay();
        WriteBudgetMeeting();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await BudgetPage();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");

        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        await WaitForTitleField(cut);

        IElement input = cut.Find(TitleField);
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("Note title", input.GetAttribute("aria-label"));
        Assert.Equal("Note title", input.GetAttribute("placeholder"));
        Assert.Equal("200", input.GetAttribute("maxlength"));
        Assert.Equal("Budget", input.GetAttribute("value"));
        Assert.Equal(
            ["create-note-title", "create-note-confirm", "create-note-cancel"],
            cut.FindAll(".appointment-page .create-note-entry > *").Select(e => e.ClassName ?? string.Empty).ToArray());
        Assert.Equal("Create note", cut.Find(".create-note-entry button.create-note-confirm").TextContent);
        Assert.Equal("Cancel", cut.Find(".create-note-entry button.create-note-cancel").TextContent);
        Assert.True(Before(cut.Find(".appointment-page button.create-note-link"), cut.Find(".appointment-page .create-note-entry")));
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));

        await Click(cut, () => cut.Find(".create-note-entry button.create-note-confirm"));

        WaitForCreated(cut, js, 0, "Budget");
        Assert.Equal("# Budget\n", File.ReadAllText(Path.Combine(data.Root, "Budget.md")));
        Assert.Empty(cut.FindAll(".appointment-page"));
    }

    // 395 AC-3: Enter in the field creates the typed title.
    [Fact]
    public async Task MeetingNoteExists_EnterCreatesTypedTitle()
    {
        WriteDay();
        WriteBudgetMeeting();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await BudgetPage();
        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        await WaitForTitleField(cut);

        await TypeTitle(cut, "Budget follow-up", "Enter");

        WaitForCreated(cut, js, 0, "Budget follow-up");
        Assert.True(File.Exists(Path.Combine(data.Root, "Budget follow-up.md")));
    }

    // 395 AC-3: Esc and Cancel close the field, write nothing and give the focus back to the link.
    [Theory]
    [InlineData("Escape")]
    [InlineData("Cancel")]
    public async Task MeetingNoteExists_EscOrCancel_ClosesNothingWritten(string how)
    {
        WriteDay();
        WriteBudgetMeeting();
        IRenderedComponent<Main> cut = await BudgetPage();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");
        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        await WaitForTitleField(cut);
        await TypeTitle(cut, "Typed", how == "Escape" ? "Escape" : null);
        if (how == "Cancel")
        {
            await Click(cut, () => cut.Find(".create-note-entry button.create-note-cancel"));
        }

        await WaitForLinkFocus(cut);
        Assert.Single(cut.FindAll(".appointment-page"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));
    }

    // 395 AC-3: a taken name or an invalid title shows the refusal in red and keeps the field and its text.
    [Theory]
    [InlineData("Budget 2027", "Note Budget 2027 already exists.")]
    [InlineData("a/b", null)]
    public async Task MeetingNoteExists_Refused_RedLineFieldKept(string title, string? text)
    {
        WriteDay();
        WriteNotes();
        WriteBudgetMeeting();
        IRenderedComponent<Main> cut = await BudgetPage();
        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        await WaitForTitleField(cut);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, ".desktop");

        await TypeTitle(cut, title, "Enter");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".appointment-page p.red.create-note-result")), Wait);
        IElement line = cut.Find(".appointment-page p.create-note-result");
        Assert.Equal(text ?? NoteStore.ValidateTitle(title), line.TextContent);
        Assert.Equal(title, cut.Find(TitleField).GetAttribute("value"));
        Assert.Single(cut.FindAll(".appointment-page"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".desktop"));
    }

    // 395 AC-3: with a dirty editor whose save fails, the note is made and the discard question asks, as for the notes
    // bar's entry row; Discard opens the new note.
    [Fact]
    public async Task MeetingNoteExists_DirtyEditor_CreatedThenDiscardAsked()
    {
        WriteDay();
        WriteBudgetMeeting();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();
        await OpenFromList(cut, js, "Plan");
        await ClickItem(cut, 0);
        WaitForPage(cut);
        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));
        await WaitForTitleField(cut);
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await TypeTitle(cut, "Fresh", "Enter");

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "Fresh.md")));
        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.Find("[role=dialog] button.confirm"));
        cut.WaitForAssertion(() => Assert.Equal("Fresh", cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
    }

    private static string[] TopicRows(IRenderedComponent<Main> cut) =>
        cut.FindAll(".appointment-page section.topics li.topic .text").Select(t => t.TextContent).ToArray();

    // 253 AC-8: the open topics of the attendees (both Budget occurrences: Anna, then Bob) show in section.topics above
    // Notes; ticking one removes it.
    [Fact]
    public async Task Topics_AboveNotes_TickRemoves()
    {
        WriteDay();
        data.WriteNote("Plan", "next: @[Anna] budget\n- next: @[anna] hiring\nnext: @[Bob] travel\n");
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        IElement section = cut.Find(".appointment-page section.topics");
        Assert.Equal("appointment-topics-heading", section.GetAttribute("aria-labelledby"));
        Assert.Equal("Topics", section.QuerySelector("h3")!.TextContent);
        Assert.Equal(["Anna: budget", "anna: hiring", "Bob: travel"], TopicRows(cut));
        Assert.True(Before(section, cut.Find("#appointment-notes-heading")));

        await TickFirstTopic(cut);
        await ConfirmBudget(cut);
        AssertBudgetDone(cut);
    }

    // 276 AC-4: a state reload after the page has loaded re-renders the topic rows; the tick still opens the dialog.
    [Fact]
    public async Task Topics_ReloadBeforeTick_DialogStillOpens()
    {
        WriteDay();
        data.WriteNote("Plan", "next: @[Anna] budget\n- next: @[anna] hiring\nnext: @[Bob] travel\n");
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.Equal(["Anna: budget", "anna: hiring", "Bob: travel"], TopicRows(cut));

        // "Refresh() completed", then "the reload count is 0".
        await data.State!.Refresh().WaitAsync(Wait);
        await data.State.ReloadsSettled().WaitAsync(Wait);

        await TickFirstTopic(cut);
        await ConfirmBudget(cut);
        AssertBudgetDone(cut);
    }

    // 276: no reload may hold the renderer ("the reload count is 0", then a flush), and the checkbox is found again,
    // so the awaited change goes to the current element.
    private async Task TickFirstTopic(IRenderedComponent<Main> cut)
    {
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => { });
        await cut.FindAll(".appointment-page li.topic input.done")[0].ChangeAsync(new ChangeEventArgs { Value = true });
    }

    // "the dialog question is Mark this topic done? budget", then the confirm click.
    private async Task ConfirmBudget(IRenderedComponent<Main> cut)
    {
        cut.WaitForAssertion(
            () => Assert.Equal("Mark this topic done? budget", cut.Find("[role=dialog] .question").TextContent), Wait);
        await Click(cut, () => cut.Find("[role=dialog] button.confirm"));
    }

    private void AssertBudgetDone(IRenderedComponent<Main> cut)
    {
        // "section.topics has no row with budget".
        cut.WaitForAssertion(() => Assert.Equal(["anna: hiring", "Bob: travel"], TopicRows(cut)), Wait);
        Assert.Equal(
            "- [x] next: @[Anna] budget\n- next: @[anna] hiring\nnext: @[Bob] travel\n",
            File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(cut.FindAll(".appointment-page p.topic-result"));
    }

    // 253 AC-8: an appointment without matching attendees has no section.topics.
    [Fact]
    public async Task Topics_NoMatchingAttendee_NoSection()
    {
        WriteDay();
        data.WriteNote("Plan", "next: @[Carl] budget\n");
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 1);
        WaitForPage(cut);
        Assert.Equal("Retro", cut.Find(".appointment-page h2").TextContent);
        Assert.Empty(cut.FindAll(".appointment-page section.topics"));

        await ClickItem(cut, 0);
        cut.WaitForAssertion(() => Assert.Equal("Budget", cut.Find(".appointment-page h2").TextContent), Wait);
        WaitForPage(cut);
        Assert.Empty(cut.FindAll(".appointment-page section.topics"));
    }

    // 253 AC-8: a bad context.db shows the existing red line and no topics.
    [Fact]
    public async Task Topics_CorruptIndex_RedLineNoTopics()
    {
        WriteDay();
        data.WriteNote("Plan", "next: @[Anna] budget\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);

        Assert.Equal("Can't read the related notes.", cut.Find(".appointment-page p.red").TextContent);
        Assert.Empty(cut.FindAll(".appointment-page section.topics"));
    }

    // 253 AC-9: "Create a new note" creates the meeting note with the Topics section.
    [Fact]
    public async Task Topics_CreateLink_NoteHasTopicsSection()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            Event("Weekly", "20261029T110000", "20261029T120000", "Ann Lee"),
            Event("Retro", "20261029T130000", "20261029T140000"),
            Event("Other", "20261029T150000", "20261029T160000")));
        data.WriteNote("Plan", "next: @[Ann Lee] budget\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await RenderMain();

        await ClickItem(cut, 0);
        WaitForPage(cut);
        Assert.Equal(["Ann Lee: budget"], TopicRows(cut));

        await Click(cut, () => cut.Find(".appointment-page button.create-note-link"));

        const string Name = "Meetings/Weekly/2026-10-29";
        cut.WaitForAssertion(() => Assert.Equal(Name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.EndsWith(
            "\n## Topics\n\n- @[Ann Lee]: budget (from [[Plan]])\n",
            File.ReadAllText(Path.Combine(data.Root, "Meetings", "Weekly", "2026-10-29.md")),
            StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<Main>> RenderMain()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForSidebar(cut);
        return cut;
    }

    // True when a and b are children of the same element and a comes first. bUnit wraps found elements and re-finds
    // them after each render, so both are unwrapped to the DOM nodes of one render; a's parent is read once and b's
    // parent must be that same node. Each index among the parent's element children (no text nodes) is found by node
    // identity, never by markup, so two siblings with the same markup can't be mixed up.
    private static bool Before(IElement a, IElement b)
    {
        IElement first = Raw(a);
        IElement second = Raw(b);
        IElement parent = first.ParentElement!;
        Assert.Same(parent, second.ParentElement);
        IElement[] children = parent.Children.ToArray();
        int firstIndex = Array.FindIndex(children, c => ReferenceEquals(c, first));
        int secondIndex = Array.FindIndex(children, c => ReferenceEquals(c, second));
        return firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex;
    }

    // The DOM node a bUnit wrapper stands for right now (the element itself when it isn't wrapped).
    private static IElement Raw(IElement element) =>
        element is IElementWrapper<IElement> wrapper ? wrapper.WrappedElement : element;

    private static IElement Item(IRenderedComponent<Main> cut, int index) =>
        cut.FindAll(".day-appointments .appointment-item")[index];

    // 279: "the reload count is 0", then the element is found and clicked in one dispatcher call, so no render can
    // replace its handler ID between the find and the click.
    private async Task Click(IRenderedComponent<Main> cut, Func<IElement> find)
    {
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => find().ClickAsync(new MouseEventArgs()));
    }

    private Task ClickItem(IRenderedComponent<Main> cut, int index) => Click(cut, () => Item(cut, index));

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

    // ".appointment-page shows no Loading… and contains section.meeting-todos with its h3 Todos": both of the page's own
    // Task.Run loads (the related notes and MeetingTodosPanel's appointments) have rendered, so no later render can
    // change the markup an assertion reads.
    private static void WaitForPage(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal);
            Assert.Equal("Todos", cut.Find(".appointment-page > section.meeting-todos > h3").TextContent);
        }, Wait);

    // ".note-title is <name>, the editor is mounted and .links-panel shows no Loading…".
    private static async Task OpenFromList(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
    }

    // 287 AC-6: h3 Todos lists the open todos tied to today's Sync, in List() order; a done one and one tied to next
    // week's occurrence are left out. A typed todo and Add store a todo due today tied to the first Sync, and it shows.
    [Fact]
    public async Task MeetingTodos_ListsTiedTodos_AddTiesToFirst()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        store.Add("Bring budget", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        store.Add("Untied", DesktopFixture.Today.AddDays(1));
        store.Add("Next week", DesktopFixture.Today.AddDays(7), null, null, MeetingCalendar.SyncLink(1));
        store.Complete(store.Add("Done one", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink()).Id);
        store.Add("Ask about Q4", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");

        Assert.Equal(["Bring budget", "Ask about Q4"], TiedRows(cut));
        IElement field = cut.Find(".meeting-todos input.meeting-todo-text");
        Assert.Equal("Todo for this meeting", field.GetAttribute("placeholder"));
        Assert.Equal("Todo for this meeting", field.GetAttribute("aria-label"));
        Assert.Equal("200", field.GetAttribute("maxlength"));

        await Act(cut, () => cut.Find("input.meeting-todo-text").InputAsync(new ChangeEventArgs { Value = "Numbers" }));
        await Act(cut, () => cut.Find("button.add-meeting-todo").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal(["Bring budget", "Ask about Q4", "Numbers"], TiedRows(cut)), Wait);
        TodoItem added = store.List().Single(t => t.Text == "Numbers");
        Assert.Equal(DesktopFixture.Today, added.Due);
        Assert.True(added.Meeting!.Matches(MeetingCalendar.At("Sync", MeetingCalendar.SyncToday, MeetingCalendar.SyncUid)));
        Assert.Equal("Sync", added.Meeting.Title);
        Assert.Equal(string.Empty, cut.Find("input.meeting-todo-text").GetAttribute("value") ?? string.Empty);
    }

    // 287 keyboard order: the text field, Add, the tie select, then each row's checkbox and Push; every control is
    // labelled.
    [Fact]
    public async Task MeetingTodos_KeyboardOrder_InputAddSelectThenRows()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        store.Add("One", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        store.Add("Two", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");

        IReadOnlyList<IElement> controls = cut.FindAll(".meeting-todos input, .meeting-todos button, .meeting-todos select");

        Assert.Equal(
            ["meeting-todo-text", "add-meeting-todo", "tie-todo", "done", "push-todo", "done", "push-todo"],
            controls.Select(c => c.ClassList[0]).ToArray());
        Assert.Equal("Add", controls[1].TextContent);
        Assert.Equal("Tie an open todo", controls[2].GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button.push-todo"), b => Assert.Equal("Push to next meeting", b.TextContent));
        Assert.All(cut.FindAll(".meeting-todo-list input.done"), c => Assert.NotNull(c.Closest("label")));
    }

    // 287 AC-7: the select starts with "Tie an open todo…" and lists only the untied open todos; choosing one ties it to
    // today's Sync and makes it due today.
    [Fact]
    public async Task MeetingTodos_TieSelect_TiesUntiedTodoToFirst()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        store.Add("Tied", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        TodoItem later = store.Add("Later", DesktopFixture.Today.AddDays(3), null, new TimeOnly(9, 30));
        store.Add("Elsewhere", DesktopFixture.Today, null, null, MeetingCalendar.OnceLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");

        IReadOnlyList<IElement> options = cut.FindAll("select.tie-todo option");
        Assert.Equal(["Tie an open todo…", "Later"], options.Select(o => o.TextContent).ToArray());
        Assert.Equal(string.Empty, options[0].GetAttribute("value"));

        await Act(cut, () => cut.Find("select.tie-todo").ChangeAsync(new ChangeEventArgs { Value = options[1].GetAttribute("value") }));

        cut.WaitForAssertion(() => Assert.Equal(["Tied", "Later"], TiedRows(cut)), Wait);
        TodoItem tied = store.List().Single(t => t.Id == later.Id);
        Assert.Equal(DesktopFixture.Today, tied.Due);
        Assert.Equal(new TimeOnly(9, 30), tied.Time);
        Assert.Equal(MeetingCalendar.SyncLink(), tied.Meeting);
        Assert.Equal(["Tie an open todo…"], cut.FindAll("select.tie-todo option").Select(o => o.TextContent).ToArray());
    }

    // 287 AC-8 and AC-4: an appointment without a UID shows the grey line, with no field, no select and no Push.
    [Fact]
    public async Task MeetingTodos_NoUid_GreyLineOnly()
    {
        MeetingCalendar.WriteAll(data);
        MeetingCalendar.Store(data).Add("Untied", DesktopFixture.Today);
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Plain");

        IElement line = cut.Find(".meeting-todos p");
        Assert.Equal("This appointment can't hold todos.", line.TextContent);
        Assert.Contains("grey", line.ClassList);
        Assert.Empty(cut.FindAll("input.meeting-todo-text"));
        Assert.Empty(cut.FindAll("select.tie-todo"));
        Assert.Empty(cut.FindAll("button.push-todo"));
    }

    // 287 AC-9: Push on a weekly meeting's todo re-ties it to next week's occurrence; it leaves the list and the accent
    // line shows the new date.
    [Fact]
    public async Task MeetingTodos_Push_MovesToNextOccurrence()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem todo = store.Add("Bring budget", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");

        await Act(cut, () => cut.Find("button.push-todo").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal("Moved to 2026-11-05.", cut.Find(".meeting-todos p.accent").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Empty(TiedRows(cut)), Wait);
        TodoItem moved = store.List().Single(t => t.Id == todo.Id);
        Assert.Equal(new DateOnly(2026, 11, 5), moved.Due);
        Assert.Equal(MeetingCalendar.SyncLink(1), moved.Meeting);
    }

    // 287 AC-10: with no next occurrence, Push lists the next 14 days' appointments with a UID (date, time, title), not
    // the current one; a click ties the todo to the chosen one.
    [Fact]
    public async Task MeetingTodos_PushWithoutNext_PicksAnotherAppointment()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem todo = store.Add("Follow up", DesktopFixture.Today, null, null, MeetingCalendar.OnceLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Once");

        await Act(cut, () => cut.Find("button.push-todo").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal(
            "No next meeting found. Pick another appointment:", cut.Find(".meeting-picker-heading").TextContent), Wait);
        Assert.Equal(
            ["2026-10-29 11:00 Sync", "2026-11-05 11:00 Sync"],
            cut.FindAll(".meeting-picker button.pick-meeting").Select(b => b.TextContent).ToArray());

        await Act(cut, () => cut.FindAll(".meeting-picker button.pick-meeting")[1].ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal("Moved to 2026-11-05.", cut.Find(".meeting-todos p.accent").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Empty(TiedRows(cut)), Wait);
        Assert.Empty(cut.FindAll(".meeting-picker"));
        TodoItem moved = store.List().Single(t => t.Id == todo.Id);
        Assert.Equal(new DateOnly(2026, 11, 5), moved.Due);
        Assert.Equal(MeetingCalendar.SyncLink(1), moved.Meeting);
    }

    // 287 AC-10: with no other appointment in the next 14 days, Push shows the grey line and writes nothing.
    [Fact]
    public async Task MeetingTodos_PushWithoutNextOrOthers_GreyLine()
    {
        data.WriteCalendar("meetings.ics", DesktopFixture.Calendar(MeetingCalendar.Once(), MeetingCalendar.Plain()));
        MeetingCalendar.Store(data).Add("Follow up", DesktopFixture.Today, null, null, MeetingCalendar.OnceLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Once");
        byte[] before = File.ReadAllBytes(TodosPath);

        await Act(cut, () => cut.Find("button.push-todo").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal(
            "No other appointments in the next 14 days.", cut.Find(".meeting-todos p.meeting-picker-none").TextContent), Wait);
        Assert.Contains("grey", cut.Find("p.meeting-picker-none").ClassList);
        Assert.Empty(cut.FindAll(".meeting-picker"));
        Assert.Equal(before, File.ReadAllBytes(TodosPath));
    }

    // 287 AC-11: a todo done elsewhere after the page showed it: Push shows the red line and writes nothing.
    [Fact]
    public async Task MeetingTodos_PushAfterDoneElsewhere_RedLineNothingWritten()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem todo = store.Add("Bring budget", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");
        store.Complete(todo.Id);
        byte[] before = File.ReadAllBytes(TodosPath);

        await Act(cut, () => cut.Find("button.push-todo").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal(
            "The todo list changed. Nothing was moved.", cut.Find(".meeting-todos p.red").TextContent), Wait);
        Assert.Equal(before, File.ReadAllBytes(TodosPath));
    }

    // 287 AC-11: a todo removed elsewhere after the select listed it: tying it shows the red line and writes nothing.
    [Fact]
    public async Task MeetingTodos_TieAfterRemovedElsewhere_RedLineNothingWritten()
    {
        MeetingCalendar.WriteAll(data);
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem todo = store.Add("Later", DesktopFixture.Today.AddDays(2));
        IRenderedComponent<AppointmentPanel> cut = await RenderMeeting("Sync");
        store.Complete(todo.Id);
        byte[] before = File.ReadAllBytes(TodosPath);

        await Act(cut, () => cut.Find("select.tie-todo").ChangeAsync(new ChangeEventArgs { Value = todo.Id.ToString("D") }));

        cut.WaitForAssertion(() => Assert.Equal(
            "The todo list changed. Nothing was moved.", cut.Find(".meeting-todos p.red").TextContent), Wait);
        Assert.Equal(before, File.ReadAllBytes(TodosPath));
    }

    private string TodosPath => Path.Combine(data.Root, "todos.json");

    // Refreshes, renders the page for today's appointments named title and waits for its notes and its todo panel.
    private async Task<IRenderedComponent<AppointmentPanel>> RenderMeeting(string title)
    {
        await data.RefreshAsync();
        IRenderedComponent<AppointmentPanel> cut = data.Bunit.Render<AppointmentPanel>(p => p
            .Add(c => c.Title, title)
            .Add(c => c.Day, DesktopFixture.Today));
        WaitForMeetingTodos(cut);
        return cut;
    }

    // ".appointment-page shows no Loading… and its .meeting-todos has h3 Todos".
    private static void WaitForMeetingTodos(IRenderedComponent<AppointmentPanel> cut) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal);
            Assert.Equal("Todos", cut.Find(".meeting-todos h3").TextContent);
        }, Wait);

    private static string[] TiedRows(IRenderedComponent<AppointmentPanel> cut) =>
        cut.FindAll(".meeting-todo-list .text").Select(e => e.TextContent).ToArray();

    // "the reload count is 0", then the event runs in one dispatcher call.
    private async Task Act(IRenderedComponent<AppointmentPanel> cut, Func<Task> act)
    {
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(act);
    }
}
