using AngleSharp.Dom;
using Bunit;

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

    private static string Line(IElement item) =>
        string.Join(" ", item.Children.Select(c => c.TextContent));
}
