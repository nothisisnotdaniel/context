using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class TodoPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-3 / 080: every todo in TodoStore.List order as "<yyyy-MM-dd> <text>" with the date class, no number.
    [Fact]
    public async Task Render_Todos_InListOrderWithDateClasses()
    {
        data.AddTodo("Later one", DesktopFixture.Today.AddDays(5));
        data.AddTodo("Due\ntoday", DesktopFixture.Today);
        data.AddTodo("Overdue", DesktopFixture.Today.AddDays(-2));

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.todo");
        Assert.Equal(
            ["2026-10-27 Overdue", "2026-10-29 Due today", "2026-11-03 Later one"],
            items.Select(Line).ToArray());
        Assert.Equal(
            ["overdue", "today", "later"],
            items.Select(i => i.QuerySelector(".date")!.ClassList.Single(c => c != "date")).ToArray());
        Assert.Equal("Due today", items[1].QuerySelector(".text")!.GetAttribute("title"));
        Assert.Empty(cut.FindAll(".number"));
    }

    // AC-3: the empty state.
    [Fact]
    public async Task Render_NoTodos_EmptyLine()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IElement line = cut.Find("p");
        Assert.Equal("No todos yet", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("li"));
    }

    // AC-3: a bad file shows the red line.
    [Fact]
    public async Task Render_BadFile_RedLine()
    {
        data.WriteFile(TodoStore.FileName, "[ {");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IElement line = cut.Find("p");
        Assert.Equal("Can't read todos.json", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
    }

    // AC-5: hostile todo text is encoded text with control chars shown as U+FFFD.
    [Fact]
    public async Task Render_HostileText_Encoded()
    {
        data.AddTodo("<script>alert(1)</script> <img src=x onerror=alert(1)> \u0001", DesktopFixture.Today);

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal(
            "<script>alert(1)</script> <img src=x onerror=alert(1)> �",
            cut.Find(".text").TextContent);
    }

    // AC-6 / 080: every todo is a line with an unchecked checkbox, named by its label (date and text); no id shows.
    [Fact]
    public async Task Render_Todos_CheckboxLines()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> boxes = cut.FindAll("li.todo label input[type=checkbox]");
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, b => Assert.False(b.HasAttribute("checked")));
        Assert.All(boxes, b => Assert.False(b.HasAttribute("disabled")));
        Assert.Equal(
            ["2026-10-29 First", "2026-10-30 Second"],
            cut.FindAll("li.todo label").Select(l => System.Text.RegularExpressions.Regex.Replace(l.TextContent, "\\s+", " ").Trim()).ToArray());
        Assert.Empty(cut.FindAll(".todo-list button"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        foreach (TodoItem todo in data.Todos!.List())
        {
            Assert.DoesNotContain(todo.Id.ToString(), cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(todo.Id.ToString("N"), cut.Markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    // AC-6: ticking the second todo asks with its text, shows that box ticked and changes nothing; Cancel closes
    // the dialog, unticks it and changes nothing.
    [Fact]
    public async Task Tick_Cancel_NoChange()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        SortedDictionary<string, string> files = Files();

        Box(cut, 1).Change(true);

        Assert.Equal("Mark this todo done? Second todo", cut.Find("[role=dialog] .question").TextContent);
        Assert.True(Box(cut, 1).HasAttribute("checked"));
        Assert.False(Box(cut, 0).HasAttribute("checked"));
        Assert.Equal(files, Files());
        cut.Find("[role=dialog] button.cancel").Click();
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(Box(cut, 1).HasAttribute("checked"));
        Assert.Equal(files, Files());
        Assert.Equal(string.Empty, cut.Find("p.result").TextContent);
    }

    // AC-6: Esc closes the dialog with no change.
    [Fact]
    public async Task Tick_Esc_NoChange()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        SortedDictionary<string, string> files = Files();

        Box(cut, 1).Change(true);
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(Box(cut, 1).HasAttribute("checked"));
        Assert.Equal(files, Files());
    }

    // AC-6: confirming removes the second todo and shows "Done: <text>" in the accent colour; the checkboxes and
    // the dialog's buttons are disabled meanwhile.
    [Fact]
    public async Task Tick_Confirm_RemovesTodoAndShowsResult()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () => release.Wait(TimeSpan.FromSeconds(10));

        Box(cut, 1).Change(true);
        Task confirm = cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(5, cut.FindAll(".todo-list input, [role=dialog] button").Count);
            Assert.All(cut.FindAll(".todo-list input, [role=dialog] button"), b => Assert.True(b.HasAttribute("disabled"), b.OuterHtml));
            Assert.Equal(5, cut.FindAll(".todo-entry input, .todo-entry button").Count);
            Assert.All(cut.FindAll(".todo-entry input, .todo-entry button"), b => Assert.True(b.HasAttribute("disabled"), b.OuterHtml));
        }, Wait);
        release.Set();
        await confirm;

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role=dialog]"));
            IElement result = cut.Find("p.result");
            Assert.Equal("Done: Second todo", result.TextContent);
            Assert.True(result.ClassList.Contains("accent"));
            Assert.Equal("status", result.GetAttribute("role"));
            Assert.Equal(["First todo", "Third todo"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
            Assert.All(cut.FindAll(".todo-list input, .todo-entry input, .todo-entry button"), b => Assert.False(b.HasAttribute("disabled")));
            Assert.All(cut.FindAll(".todo-list input"), b => Assert.False(b.HasAttribute("checked")));
        }, Wait);
        Assert.Equal(["First todo", "Third todo"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // AC-2 through the panel: a sooner todo added since render doesn't change which todo goes.
    [Fact]
    public async Task Tick_ListChangedSinceRender_RemovesTheTickedTodo()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        data.AddTodo("Sooner", DesktopFixture.Today.AddDays(-1));

        Box(cut, 1).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Done: Second todo", cut.Find("p.result").TextContent), Wait);
        Assert.Equal(["Sooner", "First todo", "Third todo"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // AC-2 through the panel: a todo removed elsewhere since render shows the fixed red text and deletes nothing.
    [Fact]
    public async Task Tick_TodoGone_RedFixedText()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        new TodoStore(data.Root).Complete(data.Todos!.List()[1].Id);
        SortedDictionary<string, string> files = Files();

        Box(cut, 1).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal("The todo list changed. Nothing was done.", result.TextContent);
            Assert.True(result.ClassList.Contains("red"));
        }, Wait);
        Assert.Equal(files, Files());
    }

    // 159 AC-8: each mention is a typed button showing the raw text, between plain-text spans; the text still reads
    // as before and the full text stays in the title.
    [Fact]
    public async Task Render_Mentions_ButtonsBetweenPlainSpans()
    {
        data.AddTodo("Call @[ Anna ] and @[Bob] about `@[Carl]`", DesktopFixture.Today);
        data.AddTodo("No mention here", DesktopFixture.Today.AddDays(1));
        await data.RefreshAsync();

        IRenderedComponent<TodoPanel> cut = data.Bunit.Render<TodoPanel>();

        IElement text = cut.Find("li.todo:nth-child(1) .text");
        Assert.Equal(
            ["SPAN:Call ", "BUTTON:@[ Anna ]", "SPAN: and ", "BUTTON:@[Bob]", "SPAN: about `@[Carl]`"],
            text.Children.Select(c => c.TagName + ":" + c.TextContent).ToArray());
        Assert.Equal("Call @[ Anna ] and @[Bob] about `@[Carl]`", text.TextContent);
        Assert.Equal("Call @[ Anna ] and @[Bob] about `@[Carl]`", text.GetAttribute("title"));
        Assert.All(cut.FindAll("button.mention"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(["@[ Anna ]", "@[Bob]"], cut.FindAll("button.mention").Select(b => b.GetAttribute("title")!).ToArray());
        Assert.NotNull(cut.Find("li.todo:nth-child(1) label button.mention"));

        // A todo without a mention renders as before: one text span, no button.
        IElement plain = cut.Find("li.todo:nth-child(2) .text");
        Assert.Empty(plain.Children);
        Assert.Equal("No mention here", plain.TextContent);
        Assert.Empty(cut.FindAll("li.todo:nth-child(2) button"));
    }

    // 159 AC-8: clicking a mention calls OnPerson with the normalized name; the checkbox stays unticked, no dialog
    // opens and nothing is written.
    [Fact]
    public async Task Mention_Click_CallsOnPerson_NoTickNoDialog()
    {
        data.AddTodo("Call @[ anna   Schmidt ] now", DesktopFixture.Today);
        await data.RefreshAsync();
        List<string> people = [];
        IRenderedComponent<TodoPanel> cut = data.Bunit.Render<TodoPanel>(p => p
            .Add(c => c.OnPerson, (string name) => people.Add(name)));
        SortedDictionary<string, string> files = Files();

        await cut.Find("button.mention").ClickAsync(new MouseEventArgs());

        Assert.Equal(["anna Schmidt"], people);
        Assert.False(Box(cut, 0).HasAttribute("checked"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
        Assert.Contains("onclick:stopPropagation", SourceOf("TodoPanel.razor"), StringComparison.Ordinal);
    }

    // 159 AC-8: hostile text in a mentioning todo is still text; the button carries only the raw mention.
    [Fact]
    public async Task Mention_HostileText_Encoded()
    {
        data.AddTodo("<img src=x onerror=alert(1)> @[Anna] \u0001", DesktopFixture.Today);
        await data.RefreshAsync();

        IRenderedComponent<TodoPanel> cut = data.Bunit.Render<TodoPanel>();

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<img src=x onerror=alert(1)> @[Anna] �", cut.Find(".text").TextContent);
        Assert.Equal("@[Anna]", cut.Find("button.mention").TextContent);
    }

    // 159 AC-8: done todos are not shown in the list.
    [Fact]
    public async Task Render_DoneTodos_NotShown()
    {
        data.AddTodo("Open @[Anna]", DesktopFixture.Today);
        data.AddTodo("Done @[Anna]", DesktopFixture.Today);
        TodoStore store = new TodoStore(data.Root);
        store.Complete(store.List().Single(t => t.Text == "Done @[Anna]").Id);

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Assert.Equal(["Open @[Anna]"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
    }

    private static string SourceOf(string file) => File.ReadAllText(Path.Combine(RepoFiles.Root(), "src", "Context.Desktop", file));

    private static IElement Box(IRenderedComponent<TodoPanel> cut, int index) =>
        cut.FindAll("li.todo input[type=checkbox]")[index];

    private async Task<IRenderedComponent<TodoPanel>> ThreeTodos()
    {
        data.AddTodo("First todo", DesktopFixture.Today);
        data.AddTodo("Second todo", DesktopFixture.Today.AddDays(1));
        data.AddTodo("Third todo", DesktopFixture.Today.AddDays(2));
        return await data.RenderAsync<TodoPanel>();
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static string Line(IElement item) =>
        string.Join(" ", item.QuerySelectorAll("span").Select(c => c.TextContent));

    // 201 ----------------------------------------------------------------------------------------

    private void WriteNoteInPast(string name, string text)
    {
        data.WriteNote(name, text);
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, name + ".md"), new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    // 201 AC-5, AC-4: note todos merge into the list with their date class (none when undated), the text with
    // mention buttons and a typed note button carrying the full name.
    [Fact]
    public async Task Render_NoteTodos_MergedWithNoteButtons()
    {
        data.AddTodo("Stored today", DesktopFixture.Today);
        WriteNoteInPast("Meeting", "- [ ] call @[Anna] due:2026-10-29\n- [ ] undated\n- [ ] late due:2026-10-01\n");
        WriteNoteInPast("Projects", "# Projects\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "Projects"));
        WriteNoteInPast(Path.Combine("Projects", "Big plan"), "- [ ] later due:2026-12-01\n");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.todo");
        Assert.Equal(
            ["late", "Stored today", "call @[Anna]", "later", "undated"],
            items.Select(i => i.QuerySelector(".text")!.TextContent).ToArray());
        Assert.Equal(
            ["overdue", "today", "today", "later", "none"],
            items.Select(i => i.QuerySelector(".date")?.ClassList.Single(c => c != "date") ?? "none").ToArray());
        Assert.Equal(
            ["Meeting", "Meeting", "Projects/Big plan", "Meeting"],
            cut.FindAll("button.todo-note").Select(b => b.TextContent).ToArray());
        Assert.All(cut.FindAll("button.todo-note"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(cut.FindAll("button.todo-note"), b => Assert.Equal(b.TextContent, b.GetAttribute("title")));
        Assert.Empty(items[1].QuerySelectorAll("button.todo-note"));
        Assert.Equal("Anna", cut.Find("li.todo:nth-child(3) button.mention").TextContent.Trim('@', '[', ']'));
        Assert.Empty(cut.FindAll("p.grey"));
    }

    // 201 AC-5: "No todos yet" only when both lists are empty.
    [Fact]
    public async Task Render_OnlyNoteTodos_NoEmptyLine()
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Assert.Single(cut.FindAll("li.todo"));
        Assert.DoesNotContain("No todos yet", cut.Markup, StringComparison.Ordinal);
    }

    // 201 AC-5: the note button calls OnOpen with the full name; no tick, no dialog, nothing written.
    [Fact]
    public async Task NoteButton_Click_CallsOnOpen_NoTick()
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        await data.RefreshAsync();
        List<string> opened = [];
        IRenderedComponent<TodoPanel> cut = data.Bunit.Render<TodoPanel>(p => p
            .Add(c => c.OnOpen, (string name) => opened.Add(name)));
        SortedDictionary<string, string> files = Files();

        await cut.Find("button.todo-note").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meeting"], opened);
        Assert.False(Box(cut, 0).HasAttribute("checked"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
    }

    // 201 AC-6: ticking asks "Mark this todo done? <text>"; Confirm ticks the line and the todo is gone.
    [Fact]
    public async Task TickNoteTodo_Confirm_WritesX_ShowsDone_AndRemoves()
    {
        string path = Path.Combine(data.Root, "Meeting.md");
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("a\r\n- [ ] call Anna\r\nb\r\n")]);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        data.AddTodo("Stored", DesktopFixture.Today);
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Box(cut, 1).Change(true);
        Assert.Equal("Mark this todo done? call Anna", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[role=dialog]"));
            IElement result = cut.Find("p.result");
            Assert.Equal("Done: call Anna", result.TextContent);
            Assert.True(result.ClassList.Contains("accent"));
            Assert.Equal(["Stored"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        }, Wait);
        Assert.Equal(
            [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("a\r\n- [x] call Anna\r\nb\r\n")],
            File.ReadAllBytes(path));
        Assert.Equal(["Stored"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // 201 AC-6: Cancel writes nothing.
    [Fact]
    public async Task TickNoteTodo_Cancel_WritesNothing()
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        SortedDictionary<string, string> files = Files();

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(Box(cut, 0).HasAttribute("checked"));
        Assert.Equal(files, Files());
    }

    // 201 AC-7: a changed line or a gone note shows the red fixed text and writes nothing.
    [Theory]
    [InlineData("changed", "The todo's line in Meeting changed. Open the note and tick it there.")]
    [InlineData("gone", "Meeting no longer exists. Nothing was done.")]
    public async Task TickNoteTodo_ChangedOrGone_RedText(string how, string text)
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        string path = Path.Combine(data.Root, "Meeting.md");
        if (how == "gone")
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, "- [ ] call Anna today\n");
        }

        SortedDictionary<string, string> files = Files();

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal(text, result.TextContent);
            Assert.True(result.ClassList.Contains("red"));
            Assert.Equal("status", result.GetAttribute("role"));
        }, Wait);
        Assert.Equal(files, Files());
    }

    // 201 AC-9: a bad context.db shows the red line while the todos.json todos still show.
    [Fact]
    public async Task Render_BadDb_RedLine_StoredTodosStay()
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        data.AddTodo("Stored", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, "not a database at all, not at all, really not");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IElement line = cut.Find("p.red");
        Assert.Equal("Can't read the note todos.", line.TextContent);
        Assert.Equal(["Stored"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        Assert.DoesNotContain("No todos yet", cut.Markup, StringComparison.Ordinal);
    }

    // 201 AC-9: a bad todos.json shows its red line while the note todos still show.
    [Fact]
    public async Task Render_BadTodosJson_NoteTodosStay()
    {
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        data.WriteFile(TodoStore.FileName, "[ {");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Assert.Equal("Can't read todos.json", cut.Find("p.red").TextContent);
        Assert.Equal(["call Anna"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());
        Assert.Single(cut.FindAll("button.todo-note"));
    }

    // 201 AC-9: with sensitive notes set up, a plain note's todo lists and ticks; the key store is never asked.
    [Fact]
    public async Task SensitiveSetUp_TickPlainNoteTodo_NoKeyStoreCall()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "- [ ] secret todo\n");
        WriteNoteInPast("Meeting", "- [ ] call Anna\n");
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        Assert.Equal(["call Anna"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray());

        Box(cut, 0).Change(true);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Done: call Anna", cut.Find("p.result").TextContent);
            Assert.Empty(cut.FindAll("li.todo"));
        }, Wait);
        Assert.Equal("- [x] call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Meeting.md")));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // 201 AC-5: hostile note names and texts are encoded text.
    [Fact]
    public async Task Render_NoteTodo_HostileTextEncoded()
    {
        WriteNoteInPast("R&D", "- [ ] <img src=x onerror=alert(1)> \u0001\n");

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<img src=x onerror=alert(1)> \uFFFD", cut.Find(".text").TextContent);
        Assert.Equal("R&D", cut.Find("button.todo-note").TextContent);
    }
}
