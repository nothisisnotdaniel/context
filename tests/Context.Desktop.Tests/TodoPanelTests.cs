using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class TodoPanelTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-3: every todo in TodoStore.List order as "<n> <yyyy-MM-dd> <text>" with the date class.
    [Fact]
    public async Task Render_Todos_InListOrderWithDateClasses()
    {
        data.AddTodo("Later one", DesktopFixture.Today.AddDays(5));
        data.AddTodo("Due\ntoday", DesktopFixture.Today);
        data.AddTodo("Overdue", DesktopFixture.Today.AddDays(-2));

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.todo");
        Assert.Equal(
            ["1 2026-10-27 Overdue", "2 2026-10-29 Due today", "3 2026-11-03 Later one"],
            items.Select(Line).ToArray());
        Assert.Equal(
            ["overdue", "today", "later"],
            items.Select(i => i.QuerySelector(".date")!.ClassList.Single(c => c != "date")).ToArray());
        Assert.Equal("Due today", items[1].QuerySelector(".text")!.GetAttribute("title"));
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

    // AC-6: every todo has a Done button with an accessible name.
    [Fact]
    public async Task Render_Todos_DoneButtons()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IReadOnlyList<IElement> buttons = cut.FindAll("li.todo button.done");
        Assert.Equal(["Mark todo 1 done", "Mark todo 2 done"], buttons.Select(b => b.GetAttribute("aria-label") ?? string.Empty).ToArray());
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(buttons, b => Assert.Equal("Done", b.TextContent));
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    // AC-6: Done on todo 2 asks with that todo's text and changes nothing; Cancel closes it with no change.
    [Fact]
    public async Task Done_Cancel_NoChange()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        SortedDictionary<string, string> files = Files();

        cut.Find("button[aria-label='Mark todo 2 done']").Click();

        Assert.Equal("Mark this todo done? Second todo", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal(files, Files());
        cut.Find("[role=dialog] button.cancel").Click();
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
        Assert.Equal(string.Empty, cut.Find("p.result").TextContent);
    }

    // AC-6: Esc closes the dialog with no change.
    [Fact]
    public async Task Done_Esc_NoChange()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        SortedDictionary<string, string> files = Files();

        cut.Find("button[aria-label='Mark todo 2 done']").Click();
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(files, Files());
    }

    // AC-6: confirming removes todo 2 and shows "Done: <text>" in the accent colour; buttons are disabled meanwhile.
    [Fact]
    public async Task Done_Confirm_RemovesTodoAndShowsResult()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () => release.Wait(TimeSpan.FromSeconds(10));

        cut.Find("button[aria-label='Mark todo 2 done']").Click();
        Task confirm = cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled"), b.OuterHtml));
            Assert.Equal(5, cut.FindAll("button").Count);
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
            Assert.All(cut.FindAll("button"), b => Assert.False(b.HasAttribute("disabled")));
        });
        Assert.Equal(["First todo", "Third todo"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // AC-2 through the panel: a list changed since render shows the fixed red text and deletes nothing.
    [Fact]
    public async Task Done_ListChanged_RedFixedText()
    {
        IRenderedComponent<TodoPanel> cut = await ThreeTodos();
        data.AddTodo("Sooner", DesktopFixture.Today.AddDays(-1));
        SortedDictionary<string, string> files = Files();

        cut.Find("button[aria-label='Mark todo 2 done']").Click();
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            IElement result = cut.Find("p.result");
            Assert.Equal("The todo list changed. Nothing was done.", result.TextContent);
            Assert.True(result.ClassList.Contains("red"));
        });
        Assert.Equal(files, Files());
    }

    private async Task<IRenderedComponent<TodoPanel>> ThreeTodos()
    {
        data.AddTodo("First todo", DesktopFixture.Today);
        data.AddTodo("Second todo", DesktopFixture.Today.AddDays(1));
        data.AddTodo("Third todo", DesktopFixture.Today.AddDays(2));
        return await data.RenderAsync<TodoPanel>();
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);

    private static string Line(IElement item) =>
        string.Join(" ", item.Children.Where(c => c.LocalName != "button").Select(c => c.TextContent));
}
