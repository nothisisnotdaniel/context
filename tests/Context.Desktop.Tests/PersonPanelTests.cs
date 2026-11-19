using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 156 AC-11: the person page's panel: the notes that mention the person and their appointments.
public sealed class PersonPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Person on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-11: the mentioning notes are typed buttons that report the note; appointments show date, time text, one-line
    // title and location, latest first; nothing is written but context.db.
    [Fact]
    public async Task Render_NotesAndAppointments()
    {
        WriteAnna();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
        List<string> opened = [];

        IRenderedComponent<PersonPanel> cut = data.Bunit.Render<PersonPanel>(p => p
            .Add(c => c.Person, "Anna")
            .Add(c => c.OnOpen, (string name) => opened.Add(name)));

        cut.WaitForAssertion(() => Assert.Equal(["Plan", "Review"], NoteNames(cut)), Wait);
        Assert.Equal(["Mentioned in", "Appointments"], cut.FindAll("h2").Select(h => h.TextContent).ToArray());
        Assert.Equal("Person", cut.Find("section.person-panel").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(
            ["2026-10-30|all day|Offsite with @[Anna]|", "2026-10-29|11:00-12:00|Sync|Room 1 east"],
            cut.FindAll(".person-appointment").Select(Row).ToArray());
        Assert.Equal("Sync", cut.Find(".person-appointment:nth-child(2) .title").GetAttribute("title"));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".person-notes button")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(["Review"], opened);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-11: nothing found shows the grey line.
    [Fact]
    public async Task Render_NothingFound_GreyLine()
    {
        data.WriteNote("Plan", "@[Bob]\n");
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(
            "No notes, appointments or todos mention Anna.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-11: a context.db that is not a database shows the red line (the appointments still show) and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine_AppointmentsStill()
    {
        WriteAnna();
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the mentions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".person-notes"));
        Assert.Equal(2, cut.FindAll(".person-appointment").Count);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-11: a bad index with no appointments shows only the red line.
    [Fact]
    public async Task Render_BadIndex_NoAppointments_OnlyRedLine()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the mentions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.Single(cut.FindAll("p"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-11: after the state changed the panel shows the new mention without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewMentions()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<PersonPanel> cut = Render("Anna");
        cut.WaitForAssertion(() => Assert.Equal(
            "No notes, appointments or todos mention Anna.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("Plan", "now @[anna]\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
    }

    // AC-11: Loading… before the first result; a result for a person who is no longer shown is dropped; the load
    // runs under indexGate.
    [Fact]
    public async Task PersonChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteNote("Review", "@[Bob]\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<PersonPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("Anna");
            cut.Render(p => p.Add(c => c.Person, "Bob"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
        typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
    }

    // AC-11: titles and locations from disk are text, never markup; controls become U+FFFD; the full text is in the
    // title attribute.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("<b>x</b> \u0007 @[Anna]", "20261029T100000Z", "20261029T110000Z", "<img src=x>")));
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img"));
        Assert.Equal("<b>x</b> \uFFFD @[Anna]", cut.Find(".person-appointment .title").TextContent);
        Assert.Equal("<b>x</b> \uFFFD @[Anna]", cut.Find(".person-appointment .title").GetAttribute("title"));
        Assert.Equal("<img src=x>", cut.Find(".person-appointment .location").GetAttribute("title"));
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    // 158 AC-9: one red line per clash above the other rows, even when nothing mentions the person, cleaned to one line.
    [Fact]
    public async Task Render_AliasClashes_RedLinesAboveRows()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna Schmidt.md"), "# Anna Schmidt\naliases:: Anna, AS, Anna Berg\n");
        data.WriteFile(Path.Combine("people", "Anna Berg.md"), "# Anna Berg\naliases:: Anna\n");
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna Schmidt");

        cut.WaitForAssertion(() => Assert.Equal(
            [
                "Alias Anna is also used by Anna Berg, so @[Anna] links to no one.",
                "Alias Anna Berg is another person's name, so @[Anna Berg] links to them.",
            ],
            cut.FindAll("p.alias-clash").Select(p => p.TextContent).ToArray()), Wait);
        Assert.All(cut.FindAll("p.alias-clash"), p => Assert.True(p.ClassList.Contains("red")));
        Assert.All(cut.FindAll("p.alias-clash"), p => Assert.Equal("status", p.GetAttribute("role")));
        IElement section = cut.Find("section.person-panel");
        Assert.Equal(
            ["alias-clash", "alias-clash", "grey"],
            section.Children.Select(c => c.ClassList.Contains("alias-clash") ? "alias-clash" : c.ClassName ?? c.TagName).ToArray());
        Assert.Equal("No notes, appointments or todos mention Anna Schmidt.", cut.Find("p.grey[role=status]").TextContent);

        // With mentions the lines stay above the headings.
        data.WriteNote("Plan", "@[AS]\n");
        await data.RefreshAsync();
        IRenderedComponent<PersonPanel> again = Render("Anna Schmidt");

        again.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(again)), Wait);
        Assert.Equal(
            ["P", "P", "H2", "UL"],
            again.Find("section.person-panel").Children.Select(c => c.TagName).ToArray());
    }

    // 158 AC-9: no clashes, no line.
    [Fact]
    public async Task Render_NoClashes_NoLine()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna Schmidt.md"), "# Anna Schmidt\naliases:: AS\n");
        data.WriteNote("Plan", "@[AS]\n");
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna Schmidt");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("p.alias-clash"));
    }

    // 159: marks the todo with this text done at noon UTC on the given day.
    private void CompleteTodo(string text, int day)
    {
        TodoItem todo = data.Todos!.List().Single(t => t.Text == text);
        new TodoStore(data.Root, () => new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Utc)).Complete(todo.Id);
    }

    private static string[] TodoRows(IRenderedComponent<PersonPanel> cut, string list) =>
        cut.FindAll($"ul.{list} .person-todo")
            .Select(r => r.QuerySelector(".date")!.TextContent + "|" + r.QuerySelector(".text")!.TextContent)
            .ToArray();

    // 159 AC-9: Open todos (due date, text), then Done todos (done date, text), after the notes and appointments; no
    // checkboxes; todo text is one-line text, never markup.
    [Fact]
    public async Task Render_Todos_OpenThenDone_AfterAppointments()
    {
        WriteAnna();
        data.AddTodo("Call @[anna] <b>now</b>\nplease", DesktopFixture.Today.AddDays(3));
        data.AddTodo("Ask @[Anna]", DesktopFixture.Today);
        data.AddTodo("Only @[Bob]", DesktopFixture.Today);
        data.AddTodo("Book room for @[Anna]", DesktopFixture.Today.AddDays(-5));
        data.AddTodo("Thank @[ANNA]", DesktopFixture.Today.AddDays(-4));
        await data.RefreshAsync();
        CompleteTodo("Book room for @[Anna]", 20);
        CompleteTodo("Thank @[ANNA]", 22);

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Mentioned in", "Appointments", "Open todos", "Done todos"],
            cut.FindAll("h2").Select(h => h.TextContent).ToArray()), Wait);
        Assert.Equal(
            ["2026-10-29|Ask @[Anna]", "2026-11-01|Call @[anna] <b>now</b> please"],
            TodoRows(cut, "open-todos"));
        Assert.Equal(["2026-10-22|Thank @[ANNA]", "2026-10-20|Book room for @[Anna]"], TodoRows(cut, "done-todos"));
        Assert.Equal("Call @[anna] <b>now</b> please", cut.Find("ul.open-todos li:nth-child(2) .text").GetAttribute("title"));
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("p[role=status]"));
        Assert.Equal(
            ["H2", "UL", "H2", "UL", "H2", "UL", "H2", "UL"],
            cut.Find("section.person-panel").Children.Select(c => c.TagName).ToArray());
    }

    // 243 AC-4: Waiting for (due date, the text after the mention through OneLine) before Open todos, only when
    // non-empty; an alias matches; a todo waiting on Anna that mentions Bob stays under Bob's Open todos.
    [Fact]
    public async Task Render_Waiting_BeforeOpenTodos()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna Schmidt.md"), "# Anna Schmidt\naliases:: AS\n");
        data.AddTodo("waiting: @[AS] <b>slides</b>\u0007 for @[Bob]", DesktopFixture.Today.AddDays(2));
        data.AddTodo("waiting: @[Anna Schmidt] contract", DesktopFixture.Today);
        data.AddTodo("Call @[Anna Schmidt]", DesktopFixture.Today);
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna Schmidt");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Waiting for", "Open todos"],
            cut.FindAll("h2").Select(h => h.TextContent).ToArray()), Wait);
        Assert.Equal(
            ["2026-10-29|contract", "2026-10-31|<b>slides</b>\uFFFD for @[Bob]"],
            TodoRows(cut, "waiting-todos"));
        Assert.Equal(["2026-10-29|Call @[Anna Schmidt]"], TodoRows(cut, "open-todos"));
        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("input"));

        IRenderedComponent<PersonPanel> bob = Render("Bob");
        bob.WaitForAssertion(() => Assert.Equal(
            ["Open todos"], bob.FindAll("h2").Select(h => h.TextContent).ToArray()), Wait);
        Assert.Equal(["2026-10-31|waiting: @[AS] <b>slides</b>\uFFFD for @[Bob]"], TodoRows(bob, "open-todos"));
        Assert.Empty(bob.FindAll("ul.waiting-todos"));
    }

    // 159 AC-9: each todo section only when non-empty; a person mentioned only in a done todo is found.
    [Fact]
    public async Task Render_OnlyDoneTodo_OnlyDoneSection()
    {
        data.AddTodo("Thank @[Carl]", DesktopFixture.Today);
        await data.RefreshAsync();
        CompleteTodo("Thank @[Carl]", 28);

        IRenderedComponent<PersonPanel> cut = Render("Carl");

        cut.WaitForAssertion(() => Assert.Equal(["Done todos"], cut.FindAll("h2").Select(h => h.TextContent).ToArray()), Wait);
        Assert.Equal(["2026-10-28|Thank @[Carl]"], TodoRows(cut, "done-todos"));
        Assert.Empty(cut.FindAll("ul.open-todos"));
    }

    // 159 AC-9: an unreadable todos.json shows the red line; the notes and appointments still render.
    [Fact]
    public async Task Render_BadTodos_RedLine_NotesAndAppointmentsStill()
    {
        WriteAnna();
        data.WriteFile(TodoStore.FileName, "[{\"text\":\"@[Anna]\",\"due\":\"2026-11-03\",\"done\":\"nope\"}]");
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(["Plan", "Review"], NoteNames(cut)), Wait);
        IElement line = cut.Find("p[role=status]");
        Assert.Equal("Can't read the todos.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Equal(2, cut.FindAll(".person-appointment").Count);
        Assert.Empty(cut.FindAll(".person-todos"));

        // With nothing else found, only the red line shows (no claim that no todo mentions the person).
        IRenderedComponent<PersonPanel> nobody = Render("Dora");
        nobody.WaitForAssertion(() => Assert.Equal("Can't read the todos.", nobody.Find("p[role=status]").TextContent), Wait);
        Assert.Single(nobody.FindAll("p"));
    }

    // 159 AC-9: an unreadable context.db shows its red line and still renders the todo sections.
    [Fact]
    public async Task Render_BadIndex_TodoSectionsStill()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.AddTodo("Ask @[Anna]", DesktopFixture.Today);
        data.AddTodo("Thank @[Anna]", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        CompleteTodo("Thank @[Anna]", 28);

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the mentions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.Empty(cut.FindAll(".person-notes"));
        Assert.Equal(["2026-10-29|Ask @[Anna]"], TodoRows(cut, "open-todos"));
        Assert.Equal(["2026-10-28|Thank @[Anna]"], TodoRows(cut, "done-todos"));
    }

    private IRenderedComponent<PersonPanel> Render(string person) =>
        data.Bunit.Render<PersonPanel>(p => p.Add(c => c.Person, person));

    private static string[] NoteNames(IRenderedComponent<PersonPanel> cut) =>
        cut.FindAll(".person-notes button").Select(b => b.TextContent).ToArray();

    private static string Row(IElement row) =>
        string.Join("|", new[] { ".date", ".time", ".title", ".location" }
            .Select(s => row.QuerySelector(s)?.TextContent ?? string.Empty));

    // Two notes and the person page mention Anna; a calendar file holds an all-day event mentioning her, a timed
    // event listing her as an attendee (with an address) and one that does not concern her.
    private void WriteAnna()
    {
        data.WriteNote("Plan", "Ask @[Anna]\n");
        data.WriteNote("Review", "With @[ANNA]\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n\n@[Anna] herself\n");
        data.WriteNote("people", "# people\n");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            "BEGIN:VEVENT\r\nSUMMARY:Offsite with @[Anna]\r\nDTSTART;VALUE=DATE:20261030\r\nEND:VEVENT\r\n",
            "BEGIN:VEVENT\r\nSUMMARY:Sync\r\nLOCATION:Room 1\\neast\r\nATTENDEE;CN=\"anna\":mailto:anna@example.com\r\n"
                + "DTSTART:20261029T100000Z\r\nDTEND:20261029T110000Z\r\nEND:VEVENT\r\n",
            DesktopFixture.Event("Other", "20261029T120000Z", "20261029T130000Z")));
    }
}
