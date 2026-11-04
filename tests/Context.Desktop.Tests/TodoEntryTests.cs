using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 094 AC-10: the todo entry row under the todo list.
public sealed class TodoEntryTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // The row: a text field (max 200), a date field defaulting to the injected today, Today/Tomorrow and Add.
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
        Assert.Equal(["Today", "Tomorrow", "Add"], cut.FindAll(".todo-entry button").Select(b => b.TextContent).ToArray());
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

        await cut.Find(".todo-entry button.add").ClickAsync(new MouseEventArgs());

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Buy milk", added.Text);
        Assert.Equal(new DateOnly(2026, 10, 30), added.Due);
        IElement result = cut.Find(".add-result");
        Assert.Equal("Added: Buy milk", result.TextContent);
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Equal(string.Empty, cut.Find("input.todo-text").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal(["Buy milk"], cut.FindAll("li.todo .text").Select(t => t.TextContent).ToArray()));
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

    // The Add button keeps the focus where it is (only Enter in the field gives the focus back to it).
    [Fact]
    public async Task AddButton_DoesNotMoveFocus()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Buy milk" });

        await cut.Find(".todo-entry button.add").ClickAsync(new MouseEventArgs());

        Assert.Single(data.Todos!.List());
        Assert.DoesNotContain(data.Bunit.JSInterop.Invocations, i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
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

        Task add = cut.Find(".todo-entry button.add").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input"),
            e => Assert.True(e.HasAttribute("disabled"), e.OuterHtml)));
        Assert.Equal(6, cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input").Count);
        release.Set();
        await add;
        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".todo-entry input, .todo-entry button, .todo-list input"),
            e => Assert.False(e.HasAttribute("disabled"), e.OuterHtml)));
    }

    // Empty text writes nothing and asks for the todo.
    [Fact]
    public async Task Add_Empty_WritesNothing()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find(".todo-entry button.add").ClickAsync(new MouseEventArgs());

        Assert.Equal("Write the todo first.", cut.Find(".add-result").TextContent);
        Assert.True(cut.Find(".add-result").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // Locked (encryption.json appeared after rendering) writes nothing.
    [Fact]
    public async Task Add_Locked_WritesNothing()
    {
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        Vault.Create(data.Root, "a long enough passphrase", 1000).Dispose();
        await cut.Find("input.todo-text").InputAsync(new ChangeEventArgs { Value = "Secret" });

        await cut.Find(".todo-entry button.add").ClickAsync(new MouseEventArgs());

        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        Assert.Equal(DesktopActions.LockedText, cut.Find(".add-result").TextContent);
    }
}
