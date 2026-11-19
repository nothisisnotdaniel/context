using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 282 AC-5: the command bar's Context lines run through Main's hook (registered with the fixture's CommandBarContext),
// with the palette's rules and guards, against real files and the fixture's fixed clocks.
public sealed class CommandBarMainTests : IDisposable
{
    // Opening a note in the editor runs on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public CommandBarMainTests()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 14, 5, 0, DateTimeKind.Local);
    }

    public void Dispose() => data.Dispose();

    private string DailyPath => Path.Combine(data.Root, "2026-10-29.md");

    private async Task<IRenderedComponent<Main>> Render()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        return cut;
    }

    [Fact]
    public async Task Todo_AddsTheTodo()
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run("todo tomorrow 9:00 x").WaitAsync(Wait);

        Assert.True(result.Ok);
        Assert.StartsWith("Added: ", result.Text, StringComparison.Ordinal);
        TodoItem todo = Assert.Single(data.Todos!.List());
        Assert.Equal(("x", new DateOnly(2026, 10, 30), (TimeOnly?)new TimeOnly(9, 0)), (todo.Text, todo.Due, todo.Time));
    }

    [Fact]
    public async Task Todo_Every_AddsARepeatingTodo()
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run("TODO every monday standup").WaitAsync(Wait);

        Assert.True(result.Ok);
        Assert.NotNull(Assert.Single(data.Todos!.List()).Repeat);
    }

    [Theory]
    [InlineData("todo", "Type todo <yyyy-MM-dd|today|tomorrow> <text>.")]
    [InlineData("todo someday x", "Type todo <yyyy-MM-dd|today|tomorrow> <text>.")]
    [InlineData("todo today 25:00", "Type todo <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.")]
    [InlineData("todo every x", "Type todo every <weekday|day|month> <text>.")]
    public async Task Todo_BadForm_ShowsTheUsage(string line, string text)
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run(line).WaitAsync(Wait);

        Assert.False(result.Ok);
        Assert.Equal(text, result.Text);
        Assert.False(File.Exists(Path.Combine(data.Root, "todos.json")));
    }

    [Fact]
    public async Task Capture_CapturesToTheInbox()
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run("capture y").WaitAsync(Wait);

        Assert.Equal((true, "Captured."), (result.Ok, result.Text));
        Assert.Equal("# inbox\n- 2026-10-29 14:05 y\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    [Fact]
    public async Task Note_AppendsToTodaysNote()
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run("note z").WaitAsync(Wait);

        Assert.Equal((true, "Added to 2026-10-29."), (result.Ok, result.Text));
        Assert.EndsWith("\n- 14:05 z\n", File.ReadAllText(DailyPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Note_Sensitive_Refused()
    {
        data.WriteFile("2026-10-29" + NoteStore.SensitiveExtension, "kept\r\n");
        await Render();
        SortedDictionary<string, string> before = data.SnapshotUnderIndexGate(syncFirst: true);

        CommandBarResult result = await data.CommandBar.Run("note z").WaitAsync(Wait);

        Assert.Equal((false, "2026-10-29 is sensitive. Nothing was added."), (result.Ok, result.Text));
        Assert.Equal(before, data.SnapshotUnderIndexGate(syncFirst: false));
    }

    [Fact]
    public async Task Note_Empty_Refused()
    {
        await Render();

        CommandBarResult result = await data.CommandBar.Run("note").WaitAsync(Wait);

        Assert.Equal((false, DesktopActions.NoteEmptyText), (result.Ok, result.Text));
        Assert.False(File.Exists(DailyPath));
    }

    // The open daily note with a clean editor shows the new line (the editor mounts the file again).
    [Fact]
    public async Task Note_DailyNoteOpen_ReloadsTheEditor()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await Render();
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/today" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        CommandBarResult result = await data.CommandBar.Run("note z").WaitAsync(Wait);

        Assert.True(result.Ok);
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.EndsWith("\n- 14:05 z\n", File.ReadAllText(DailyPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMainRendered_IsNotReady()
    {
        data.CreateState();

        CommandBarResult result = await data.CommandBar.Run("todo today x").WaitAsync(Wait);

        Assert.Equal((false, "Context isn't ready yet."), (result.Ok, result.Text));
        Assert.False(File.Exists(Path.Combine(data.Root, "todos.json")));
    }

    [Fact]
    public async Task MainDisposed_IsNotReady()
    {
        await Render();
        await data.Bunit.DisposeComponentsAsync();

        CommandBarResult result = await data.CommandBar.Run("capture y").WaitAsync(Wait);

        Assert.Equal(CommandBarContext.NotReadyText, result.Text);
        Assert.False(File.Exists(Path.Combine(data.Root, "inbox.md")));
    }
}
