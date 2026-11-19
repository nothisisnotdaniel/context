using AngleSharp.Dom;
using Bunit;
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

        IElement heading = Assert.Single(cut.FindAll(".appointment-page h3"));
        Assert.Equal("Notes", heading.TextContent);
        IElement list = cut.Find(".appointment-page ul.appointment-notes");
        Assert.True(Before(cut.Find(".appointment-page .panel-bar"), heading));
        Assert.True(Before(heading, list));
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

        IElement heading = Assert.Single(cut.FindAll(".appointment-page h3"));
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
        IElement heading = Assert.Single(cut.FindAll(".appointment-page h3"));
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
        IElement heading = Assert.Single(cut.FindAll(".appointment-page h3"));
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

    // True when a and b are children of the same element and a comes first (bUnit wraps found elements, so the
    // index among the parent's children is found by outer HTML, not CompareDocumentPosition).
    private static bool Before(IElement a, IElement b)
    {
        IElement parent = a.ParentElement!;
        List<string> children = parent.Children.Select(c => c.OuterHtml).ToList();
        Assert.Equal(parent.OuterHtml, b.ParentElement!.OuterHtml);
        int first = children.IndexOf(a.OuterHtml);
        int second = children.IndexOf(b.OuterHtml);
        return first >= 0 && second >= 0 && first < second;
    }

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

    // ".appointment-page shows no Loading…".
    private static void WaitForPage(IRenderedComponent<Main> cut) =>
        cut.WaitForAssertion(() =>
            Assert.DoesNotContain("Loading…", cut.Find(".appointment-page").TextContent, StringComparison.Ordinal), Wait);

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
}
