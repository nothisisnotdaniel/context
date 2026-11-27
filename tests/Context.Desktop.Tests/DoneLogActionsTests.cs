using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 293: completing a todo on the desktop appends it to done.md.
public sealed class DoneLogActionsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private string DonePath => Path.Combine(data.Root, "done.md");

    // AC-1: a stored todo with a due date, done on the fake local day, is appended with its due part.
    [Fact]
    public async Task CompleteTodo_AppendsLineWithDue()
    {
        data.LocalNow = new DateTime(2026, 11, 19, 10, 5, 0, DateTimeKind.Local);
        data.AddTodo("Send report #work", new DateOnly(2026, 11, 18));
        DesktopSnapshot before = await data.RefreshAsync();

        ActionResult result = await data.Actions!.CompleteTodo(before.Todos[0], new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None, "Send report #work"), (result.Kind, result.Reason, result.Text));
        Assert.Equal("# done\n- 2026-11-19 Send report #work (due 2026-11-18)\n", File.ReadAllText(DonePath));
    }

    // AC-2: a note todo without a due date is appended without the due part, after the existing lines.
    [Fact]
    public async Task CompleteNoteTodo_AppendsLineWithoutDue()
    {
        data.WriteNote("Meeting", "# Meeting\n- { } call Anna\n");
        data.WriteNote("done", "# done\n- 2026-10-01 earlier\n");
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("# done\n- 2026-10-01 earlier\n- 2026-10-29 call Anna\n", File.ReadAllText(DonePath));
    }

    // AC-4: a recurring todo done on two days gives two lines, each with the due date it had.
    [Fact]
    public async Task CompleteRecurring_Twice_TwoLines()
    {
        data.AddTodo("Stretch", DesktopFixture.Today, TodoRepeat.Daily);
        DesktopSnapshot before = await data.RefreshAsync();
        Assert.Equal(ActionKind.Done, (await data.Actions!.CompleteTodo(before.Todos[0], new Confirmation())).Kind);

        data.LocalNow = data.LocalNow.AddDays(1);
        data.Day = data.Day.AddDays(1);
        TodoItem next = Assert.Single(data.Todos!.List());
        Assert.Equal(ActionKind.Done, (await data.Actions.CompleteTodo(next, new Confirmation())).Kind);

        Assert.Equal(
            "# done\n- 2026-10-29 Stretch (due 2026-10-29)\n- 2026-10-30 Stretch (due 2026-10-30)\n",
            File.ReadAllText(DonePath));
    }

    // AC-6: done.md changes on every attempt: the todo is still done, nothing of ours is written, the reason says so.
    [Fact]
    public async Task CompleteTodo_DoneLogChangedEveryTime_DoneButLeft()
    {
        data.AddTodo("Send report", DesktopFixture.Today);
        data.WriteNote("done", "# done\n");
        DesktopSnapshot before = await data.RefreshAsync();
        int calls = 0;
        data.Actions!.BeforeDoneLogSave = () =>
        {
            calls++;
            File.AppendAllText(DonePath, $"- theirs {calls}\n");
        };

        ActionResult result = await data.Actions.CompleteTodo(before.Todos[0], new Confirmation());

        Assert.Equal((ActionKind.Done, ActionReason.DoneLogLeft, "Send report"), (result.Kind, result.Reason, result.Text));
        Assert.Empty(data.Todos!.List());
        string expected = "# done\n" + string.Concat(Enumerable.Range(1, calls).Select(i => $"- theirs {i}\n"));
        Assert.Equal(expected, File.ReadAllText(DonePath));
    }

    // AC-6: the todo panel shows the red status text for a completion the log couldn't take.
    [Fact]
    public async Task TodoPanel_DoneLogTooLarge_ShowsStatusText()
    {
        data.AddTodo("Send report", DesktopFixture.Today);
        Directory.CreateDirectory(data.Root);
        byte[] full = System.Text.Encoding.UTF8.GetBytes("# done\n" + new string('a', (int)NoteStore.MaxEditBytes - 8) + "\n");
        File.WriteAllBytes(DonePath, full);
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();

        StaleClick.Fire(() => cut.Find("li.todo input[type=checkbox]"), e => e.ChangeAsync(new ChangeEventArgs { Value = true }));
        await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");

        Bar.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement shown = Bar.Find(".main-status");
            Assert.Equal("Done, but it couldn't be added to the done log.", shown.TextContent);
            Assert.True(shown.ClassList.Contains("red"));
        }, Wait);
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("li.todo"));
        }, Wait);
        Assert.Equal(full, File.ReadAllBytes(DonePath));
        Assert.Empty(data.Todos!.List());
    }

    // AC-11: refused (no confirmation), changed (gone) and unreadable todos.json append nothing.
    [Fact]
    public async Task CompleteTodo_NotCompleted_AppendsNothing()
    {
        data.AddTodo("Send report", DesktopFixture.Today);
        DesktopSnapshot before = await data.RefreshAsync();

        ActionResult refused = await data.Actions!.CompleteTodo(before.Todos[0], null);
        ActionResult gone = await data.Actions.CompleteTodo(
            new TodoItem(Guid.NewGuid(), "Other", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());
        data.WriteFile(TodoStore.FileName, "[ {");
        ActionResult failed = await data.Actions.CompleteTodo(before.Todos[0], new Confirmation());

        Assert.Equal(
            [ActionReason.NotConfirmed, ActionReason.Changed, ActionReason.Unreadable],
            new[] { refused.Reason, gone.Reason, failed.Reason });
        Assert.False(File.Exists(DonePath));
    }

    // AC-11: a note todo that changed since it was read appends nothing.
    [Fact]
    public async Task CompleteNoteTodo_Changed_AppendsNothing()
    {
        data.WriteNote("Meeting", "# Meeting\n- { } call Anna\n");
        NoteTodo todo = Assert.Single((await data.RefreshAsync()).NoteTodos);
        data.WriteNote("Meeting", "# Meeting\n- { } call Bob\n");

        ActionResult result = await data.Actions!.CompleteNoteTodo(todo, new Confirmation());

        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.False(File.Exists(DonePath));
    }
}
