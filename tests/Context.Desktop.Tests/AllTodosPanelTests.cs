using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// Job 331: the Todos page, every open global todo grouped by due date.
public sealed class AllTodosPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    private readonly List<string> opened = [];

    private readonly List<(string Title, DateOnly Day)> meetings = [];

    private readonly List<string> people = [];

    private int closed;

    public void Dispose() => data.Dispose();

    // AC-2: the page's root, heading and close button.
    [Fact]
    public async Task Render_PageBar_HeadingAndClose()
    {
        IRenderedComponent<AllTodosPanel> cut = await Render();

        IElement page = cut.Find("section.page.all-todos-panel");
        Assert.Equal("Todos", page.GetAttribute("aria-label"));
        Assert.Equal("Todos", cut.Find(".panel-bar h2").TextContent);
        Assert.Equal("close", cut.Find(".panel-bar").LastElementChild!.ClassName);

        await cut.Find(".panel-bar button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, closed);
    }

    // AC-3, AC-4: groups in order, rows in Merge order with date classes, time and repeat; note checkboxes, carried
    // lines and done todos never show.
    [Fact]
    public async Task Render_GroupsRowsAndDateClasses()
    {
        data.AddTodo("Old", DesktopFixture.Today.AddDays(-2));
        data.AddTodo("Now", DesktopFixture.Today);
        data.AddTodo("Soon", DesktopFixture.Today.AddDays(5));
        data.AddTodo("Stretch", DesktopFixture.Today, TodoRepeat.Daily, new TimeOnly(8, 30));
        data.AddTodo("waiting: @[Anna] slides", DesktopFixture.Today.AddDays(1));
        WriteNoteInPast("Meeting", "- { } a due:2026-10-28\n- { } b\n- [ ] local\n- [>] carry\n- {x} done\n");

        IRenderedComponent<AllTodosPanel> cut = await Render();

        Assert.Equal(
            ["Overdue", "Today", "Upcoming", "No due date"],
            cut.FindAll(".all-todos-panel section h3").Select(h => h.TextContent).ToArray());
        Assert.Equal(
            [
                ["2026-10-27 Old", "2026-10-28 a"],
                ["2026-10-29 Now", "2026-10-29 08:30 Stretch"],
                ["2026-10-30 waiting: @[Anna] slides", "2026-11-03 Soon"],
                ["b"],
            ],
            cut.FindAll(".all-todos-panel section").Select(s => s.QuerySelectorAll("ul.all-todos-list li.todo").Select(Line).ToArray()).ToArray());
        IReadOnlyList<IElement> rows = cut.FindAll("li.todo");
        Assert.Equal(
            ["overdue", "overdue", "today", "today", "later", "later", "none"],
            rows.Select(r => r.QuerySelector(".date")?.ClassList.Single(c => c != "date") ?? "none").ToArray());
        Assert.All(rows, r => Assert.Single(r.QuerySelectorAll("input[type=checkbox].done")));
        Assert.Equal("every day", rows[3].QuerySelector("span.repeat")!.TextContent);
        Assert.Equal("Old", rows[0].QuerySelector(".text")!.GetAttribute("title"));
        string markup = cut.Markup;
        Assert.DoesNotContain("local", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("carry", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("done", cut.Find(".all-todos-panel").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("p.grey"));
    }

    // AC-3: a group without rows is not rendered.
    [Fact]
    public async Task Render_OnlyToday_OneGroup()
    {
        data.AddTodo("Now", DesktopFixture.Today);

        IRenderedComponent<AllTodosPanel> cut = await Render();

        Assert.Equal(["Today"], cut.FindAll("section h3").Select(h => h.TextContent).ToArray());
        Assert.Single(cut.FindAll("ul.all-todos-list"));
    }

    // AC-4: a mention is a button that calls OnPerson and leaves the box unticked; hostile text stays text.
    [Fact]
    public async Task Mention_RaisesOnPerson_BoxStaysUnticked()
    {
        WriteNoteInPast("Meeting", "- { } call @[Anna]  <img src=x> today\n");
        IRenderedComponent<AllTodosPanel> cut = await Render();

        IElement mention = cut.Find("li.todo button.mention");
        Assert.Equal("button", mention.GetAttribute("type"));
        await mention.ClickAsync(new MouseEventArgs());

        Assert.Equal(["Anna"], people);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(cut.Find("li.todo input.done").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("img"));
    }

    // AC-5: a note todo's note button calls OnOpen with the note's full name; no box ticks.
    [Fact]
    public async Task NoteButton_RaisesOnOpen()
    {
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        data.AddTodo("Stored", DesktopFixture.Today);
        IRenderedComponent<AllTodosPanel> cut = await Render();

        IElement button = cut.Find("button.todo-note");
        Assert.Equal("Meeting", button.TextContent);
        Assert.Equal("Meeting", button.GetAttribute("title"));
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Single(cut.FindAll("button.todo-note"));
        Assert.Empty(cut.FindAll("button.todo-meeting"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meeting"], opened);
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    // AC-5: a tied todo's meeting button calls OnMeeting with its title and day; a removed occurrence adds the hint.
    [Fact]
    public async Task MeetingButton_RaisesOnMeeting_HintForMovedMeeting()
    {
        data.WriteCalendar("meetings.ics", DesktopFixture.Calendar(MeetingCalendar.Sync("EXDATE:20261105T110000")));
        TodoStore store = MeetingCalendar.Store(data);
        store.Add("Untied", DesktopFixture.Today);
        store.Add("Bring budget", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        store.Add("Ask about Q4", DesktopFixture.Today.AddDays(2), null, null, MeetingCalendar.SyncLink(1));

        IRenderedComponent<AllTodosPanel> cut = await Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".meeting-hint")), Wait);

        IReadOnlyList<IElement> rows = cut.FindAll("li.todo");
        Assert.Null(rows[0].QuerySelector(".todo-meeting"));
        IElement meeting = rows[1].QuerySelector("button.todo-meeting")!;
        Assert.Equal("Sync", meeting.TextContent);
        Assert.Equal("Sync", meeting.GetAttribute("title"));
        Assert.Null(rows[1].QuerySelector(".meeting-hint"));
        IElement hint = rows[2].QuerySelector(".meeting-hint")!;
        Assert.Equal("Meeting cancelled or moved", hint.TextContent);
        Assert.Contains("grey", hint.ClassList);

        await rows[2].QuerySelector("button.todo-meeting")!.ClickAsync(new MouseEventArgs());

        Assert.Equal([("Sync", new DateOnly(2026, 11, 5))], meetings);
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    // AC-6: ticking asks; Cancel and Esc write nothing.
    [Fact]
    public async Task Tick_CancelOrEsc_WritesNothing()
    {
        data.AddTodo("Stored", DesktopFixture.Today);
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        IRenderedComponent<AllTodosPanel> cut = await Render();
        SortedDictionary<string, string> files = Files();

        Box(cut, 0).Change(true);
        Assert.Equal("Mark this todo done? Stored", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Mark done", cut.Find("[role=dialog] button.confirm").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));

        Box(cut, 1).Change(true);
        Assert.Equal("Mark this todo done? call Anna", cut.Find("[role=dialog] .question").TextContent);
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(Box(cut, 0).HasAttribute("checked"));
        Assert.False(Box(cut, 1).HasAttribute("checked"));
        Assert.Equal(files, Files());
        Assert.Equal(2, cut.FindAll("li.todo").Count);
    }

    // AC-6: Confirm on a stored todo marks it done and the row leaves the list.
    [Fact]
    public async Task Tick_ConfirmStored_DoneAndRowGone()
    {
        data.AddTodo("Stored", DesktopFixture.Today);
        data.AddTodo("Other", DesktopFixture.Today.AddDays(1));
        IRenderedComponent<AllTodosPanel> cut = await Render();

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal("Done: Stored", result.TextContent);
            Assert.Contains("accent", result.ClassList);
            Assert.Equal("status", result.GetAttribute("role"));
            Assert.Equal(["Other"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        }, Wait);
        Assert.Equal(["Other"], data.Todos!.List().Select(t => t.Text).ToArray());
        Assert.Equal(["Stored"], data.Todos.ListDone().Select(t => t.Text).ToArray());
    }

    // AC-6: Confirm on a note todo turns its line into {x}.
    [Fact]
    public async Task Tick_ConfirmNoteTodo_WritesX()
    {
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        IRenderedComponent<AllTodosPanel> cut = await Render();

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Done: call Anna", cut.Find("p.result").TextContent);
            Assert.Empty(cut.FindAll("li.todo"));
        }, Wait);
        Assert.Equal("- {x} call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Meeting.md")));
    }

    // AC-6: a recurring todo names its next due date.
    [Fact]
    public async Task Tick_ConfirmRecurring_NextDue()
    {
        data.AddTodo("Stretch", DesktopFixture.Today.AddDays(-3), TodoRepeat.Daily);
        IRenderedComponent<AllTodosPanel> cut = await Render();

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Done: Stretch. Next due 2026-10-30.", cut.Find("p.result").TextContent);
            Assert.Equal(["2026-10-30 Stretch"], cut.FindAll("li.todo").Select(Line).ToArray());
        }, Wait);
    }

    // AC-6: a gone note shows the red refusal text and writes nothing.
    [Fact]
    public async Task Tick_NoteGone_RedText()
    {
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        IRenderedComponent<AllTodosPanel> cut = await Render();
        File.Delete(Path.Combine(data.Root, "Meeting.md"));

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal("Meeting no longer exists. Nothing was done.", result.TextContent);
            Assert.Contains("red", result.ClassList);
        }, Wait);
    }

    // AC-6: a done log that can't be written shows the red DoneLogLeftText.
    [Fact]
    public async Task Tick_DoneLogLeft_RedText()
    {
        data.AddTodo("Send report", DesktopFixture.Today);
        data.WriteNote("done", "# done\n");
        IRenderedComponent<AllTodosPanel> cut = await Render();
        int calls = 0;
        data.Actions!.BeforeDoneLogSave = () =>
        {
            calls++;
            File.AppendAllText(Path.Combine(data.Root, "done.md"), $"- theirs {calls}\n");
        };

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal(DesktopActions.DoneLogLeftText, result.TextContent);
            Assert.Contains("red", result.ClassList);
        }, Wait);
    }

    // AC-7: empty and readable: only the grey line, no list.
    [Fact]
    public async Task Render_Empty_GreyLine()
    {
        IRenderedComponent<AllTodosPanel> cut = await Render();

        IElement line = cut.Find("p.grey");
        Assert.Equal("No open todos. Add one in the todo list or type - { } in a note.", line.TextContent);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Empty(cut.FindAll("p.red"));
    }

    // AC-7: a bad todos.json shows its red line next to the note rows.
    [Fact]
    public async Task Render_BadTodosJson_RedLine_NoteRowsStay()
    {
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        data.WriteFile(TodoStore.FileName, "[ {");

        IRenderedComponent<AllTodosPanel> cut = await Render();

        Assert.Equal("Can't read todos.json", cut.Find("p.red").TextContent);
        Assert.Equal(["call Anna"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        Assert.DoesNotContain("No open todos", cut.Markup, StringComparison.Ordinal);
    }

    // AC-7: a bad context.db shows the red note-todos line; stored rows stay; with nothing else no empty line.
    [Fact]
    public async Task Render_BadDb_RedLine_StoredRowsStay()
    {
        WriteNoteInPast("Meeting", "- { } call Anna\n");
        data.AddTodo("Stored", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, "not a database at all, not at all, really not");

        IRenderedComponent<AllTodosPanel> cut = await Render();

        Assert.Equal("Can't read the note todos.", cut.Find("p.red").TextContent);
        Assert.Equal(["Stored"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        Assert.DoesNotContain("No open todos", cut.Markup, StringComparison.Ordinal);
    }

    // AC-8: a todo added elsewhere appears without reopening the page.
    [Fact]
    public async Task StateChanged_AddedTodoAppears()
    {
        data.AddTodo("First", DesktopFixture.Today);
        IRenderedComponent<AllTodosPanel> cut = await Render();

        ActionResult added = await data.Actions!.AddTodo("Added later", DesktopFixture.Today.AddDays(1), null, null);
        Assert.Equal(ActionKind.Done, added.Kind);

        cut.WaitForAssertion(
            () => Assert.Contains("Added later", cut.Find(".all-todos-panel").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal(["Today", "Upcoming"], cut.FindAll("section h3").Select(h => h.TextContent).ToArray());
    }

    private async Task<IRenderedComponent<AllTodosPanel>> Render()
    {
        await data.RefreshAsync();
        IRenderedComponent<AllTodosPanel> cut = data.Bunit.Render<AllTodosPanel>(p => p
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnMeeting, ((string Title, DateOnly Day) m) => meetings.Add(m))
            .Add(c => c.OnPerson, (string name) => people.Add(name))
            .Add(c => c.OnClose, () => closed++));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        return cut;
    }

    private void WriteNoteInPast(string name, string text)
    {
        data.WriteNote(name, text);
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, name + ".md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static IElement Box(IRenderedComponent<AllTodosPanel> cut, int index) =>
        cut.FindAll("li.todo input[type=checkbox]")[index];

    private static string Line(IElement item) =>
        string.Join(" ", item.QuerySelectorAll(".date, .time, .text").Select(e => e.TextContent));
}
