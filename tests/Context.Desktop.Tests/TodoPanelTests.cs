using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class TodoPanelTests : IDisposable
{
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
        });
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
            Assert.All(cut.FindAll(".todo-list input"), b => Assert.False(b.HasAttribute("disabled")));
            Assert.All(cut.FindAll(".todo-list input"), b => Assert.False(b.HasAttribute("checked")));
        });
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

        cut.WaitForAssertion(() => Assert.Equal("Done: Second todo", cut.Find("p.result").TextContent));
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
        });
        Assert.Equal(files, Files());
    }

    private static IElement Box(IRenderedComponent<TodoPanel> cut, int index) =>
        cut.FindAll("li.todo input[type=checkbox]")[index];

    private async Task<IRenderedComponent<TodoPanel>> ThreeTodos()
    {
        data.AddTodo("First todo", DesktopFixture.Today);
        data.AddTodo("Second todo", DesktopFixture.Today.AddDays(1));
        data.AddTodo("Third todo", DesktopFixture.Today.AddDays(2));
        return await data.RenderAsync<TodoPanel>();
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);

    private static string Line(IElement item) =>
        string.Join(" ", item.QuerySelectorAll("span").Select(c => c.TextContent));
}
