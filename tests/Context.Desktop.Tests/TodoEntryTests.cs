using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 094 AC-10: the todo entry row under the todo list.
public sealed class TodoEntryTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // The row: a text field (max 200), a date field defaulting to the injected today, Today/Tomorrow and no Add button.
    [Fact]
    public async Task Render_RowWithDefaults()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IElement text = cut.Find(".todo-entry input.todo-text");
        Assert.Equal("text", text.GetAttribute("type"));
        Assert.Equal("200", text.GetAttribute("maxlength"));
        IElement date = cut.Find(".todo-entry input.todo-due");
        Assert.Equal("date", date.GetAttribute("type"));
        Assert.Equal("2026-10-29", date.GetAttribute("value"));
        Assert.Equal(["Today", "Tomorrow"], cut.FindAll(".todo-entry button").Select(b => b.TextContent).ToArray());
        Assert.Empty(cut.FindAll("button.add"));
        Assert.Equal("New todo", text.GetAttribute("placeholder"));
        Assert.All(cut.FindAll(".todo-entry button"), b => Assert.Equal("button", b.GetAttribute("type")));
    }

    // Tomorrow and Today set the date; Add saves the todo with it and clears the field.
    [Fact]
    public async Task Add_WithTomorrow_AddsAndClears()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        await cut.Find(".todo-entry .due-tomorrow").ClickAsync(new MouseEventArgs());
        Assert.Equal("2026-10-30", cut.Find("input.todo-due").GetAttribute("value"));
        await cut.Find(".todo-entry .due-today").ClickAsync(new MouseEventArgs());
        Assert.Equal("2026-10-29", cut.Find("input.todo-due").GetAttribute("value"));
        await cut.Find(".todo-entry .due-tomorrow").ClickAsync(new MouseEventArgs());
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });

        await cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Buy milk", added.Text);
        Assert.Equal(new DateOnly(2026, 10, 30), added.Due);
        IElement result = cut.Find(".add-result");
        Assert.Equal("Added: Buy milk", result.TextContent);
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Equal(string.Empty, cut.Find("input.todo-text").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal(["Buy milk"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray()), Wait);
    }

    // Enter in the text field adds; a date picked in the date field is used.
    [Fact]
    public async Task Enter_WithPickedDate_Adds()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        string? textId = cut.Find("input.todo-text").GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(textId));
        await cut.Find("input.todo-due").ChangeAsync(new ChangeEventArgs { Value = "2026-12-24" });
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Presents" });

        await cut.Find("input.todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        await cut.Find("input.todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(new DateOnly(2026, 12, 24), added.Due);
        JSRuntimeInvocation focus = data.Bunit.JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal(textId, ((ElementReference)focus.Arguments[0]!).Id);
    }

    // Return adds the todo and gives the focus back to the field.
    [Fact]
    public async Task Enter_AddsAndFocusesTheField()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });

        await cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(data.Todos!.List());
        Assert.Contains(data.Bunit.JSInterop.Invocations, i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    // While a todo is being added the whole row and the todo checkboxes are disabled.
    [Fact]
    public async Task Add_Running_RowAndBoxesDisabled()
    {
        data.AddTodo("Existing", new DateOnly(2026, 10, 29));
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () => release.Wait(TimeSpan.FromSeconds(10));
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });

        Task add = cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input"),
            e => Assert.True(e.HasAttribute("disabled"), e.OuterHtml)), Wait);
        Assert.Equal(6, cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input").Count);
        release.Set();
        await add;
        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input"),
            e => Assert.False(e.HasAttribute("disabled"), e.OuterHtml)), Wait);
    }

    // 194 AC-4: Return in the date or the time input adds the todo with the typed text as well.
    [Theory]
    [InlineData("input.todo-due")]
    [InlineData("input.todo-time")]
    public async Task Enter_InDateOrTimeInput_Adds(string selector)
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });

        await cut.Find(selector).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(data.Todos!.List());
    }

    // Empty text writes nothing and asks for the todo.
    [Fact]
    public async Task Add_Empty_WritesNothing()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Write the todo first.", cut.Find(".add-result").TextContent);
        Assert.True(cut.Find(".add-result").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 178 AC-9: the entry row has an empty time field between the date and Today.
    [Fact]
    public async Task Render_TimeFieldStartsEmpty()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        IElement time = cut.Find(".todo-entry input.todo-time");
        Assert.Equal("time", time.GetAttribute("type"));
        Assert.Equal("Due time", time.GetAttribute("aria-label"));
        Assert.Equal(string.Empty, time.GetAttribute("value"));
        Assert.Equal(
            ["todo-text", "todo-due", "todo-time"],
            cut.FindAll(".todo-entry input").Select(i => i.ClassName ?? string.Empty).ToArray());
    }

    // 178 AC-9: adding with a time stores it (seconds from the web view dropped); the field clears after the add.
    [Theory]
    [InlineData("14:00")]
    [InlineData("14:00:00")]
    public async Task Add_WithTime_StoresTimeAndClears(string value)
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        await cut.Find("input.todo-time").ChangeAsync(new ChangeEventArgs { Value = value });
        Assert.Equal("14:00", cut.Find("input.todo-time").GetAttribute("value"));
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Call Anna" });

        await cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(new TimeOnly(14, 0), added.Time);
        Assert.Null(added.Notified);
        Assert.Equal("Added: Call Anna", cut.Find(".add-result").TextContent);
        Assert.Equal(string.Empty, cut.Find("input.todo-time").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal("14:00", cut.Find("li.todo .time").TextContent), Wait);
    }

    // 178 AC-9: adding with the time field empty (or cleared) stores no time.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Add_WithoutTime_StoresNone(string? cleared)
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        if (cleared is not null)
        {
            await cut.Find("input.todo-time").ChangeAsync(new ChangeEventArgs { Value = "09:15" });
            await cut.Find("input.todo-time").ChangeAsync(new ChangeEventArgs { Value = cleared });
        }

        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });
        await cut.Find(".todo-entry .todo-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Null(added.Time);
        cut.WaitForAssertion(() => Assert.Equal(["Buy milk"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray()), Wait);
        Assert.Empty(cut.FindAll("li.todo .time"));
    }

    // 178 AC-9: a timed row shows span.time right after the date; an untimed row has none.
    [Fact]
    public async Task Rows_TimedShowsTimeAfterDate_UntimedHasNone()
    {
        data.AddTodo("Call Anna", DesktopFixture.Today, null, new TimeOnly(14, 0));
        data.AddTodo("Buy milk", DesktopFixture.Today);

        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.todo").Count), Wait);

        IElement untimed = cut.FindAll("li.todo")[0];
        IElement timed = cut.FindAll("li.todo")[1];
        Assert.Equal("Buy milk", untimed.QuerySelector(".text")!.TextContent);
        Assert.Null(untimed.QuerySelector(".time"));
        IElement time = timed.QuerySelector(".time")!;
        Assert.Equal("14:00", time.TextContent);
        Assert.Equal("SPAN", time.TagName);
        Assert.True(time.PreviousElementSibling!.ClassList.Contains("date"));
    }
}
