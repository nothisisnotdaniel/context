namespace Context.Tests;

// 178: DesktopActions.AutoReminders (start-up and reminder timer, AC-6 and AC-7), against a real todos.json, the
// fixture's fixed local clock (2026-10-29 10:05) and FakeNotifier. Nothing here waits on time.
public sealed class ReminderActionsTests : IDisposable
{
    private static readonly DateOnly Day = DesktopFixture.Today;

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string TodosPath => Path.Combine(data.Root, "todos.json");

    private TodoItem Stored(string text) => new TodoStore(data.Root).List().Single(t => t.Text == text);

    // AC-6 (2-4): the due timed todos are marked in one write, then shown one by one as "<HH:mm> <text>".
    [Fact]
    public async Task AutoReminders_MarksDueTodosThenShowsEach()
    {
        data.AddTodo("Call Anna", Day, null, new TimeOnly(9, 30));
        data.AddTodo("Lunch", Day, null, new TimeOnly(10, 5));
        data.AddTodo("Later", Day, null, new TimeOnly(10, 6));
        data.AddTodo("Untimed", Day);
        data.AddTodo("Tomorrow", Day.AddDays(1), null, new TimeOnly(8, 0));
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AutoReminders();

        Assert.Equal((ActionKind.Done, ActionReason.None, "2"), (result.Kind, result.Reason, result.Text));
        Assert.Equal([("Context", "09:30 Call Anna"), ("Context", "10:05 Lunch")], data.Notifier.Shown);
        Assert.NotNull(Stored("Call Anna").Notified);
        Assert.NotNull(Stored("Lunch").Notified);
        Assert.Null(Stored("Later").Notified);
        Assert.Null(Stored("Untimed").Notified);
        Assert.Null(Stored("Tomorrow").Notified);
    }

    // AC-6: a second call at the same time shows nothing and writes nothing.
    [Fact]
    public async Task AutoReminders_SecondCall_ShowsAndWritesNothing()
    {
        data.AddTodo("Call Anna", Day, null, new TimeOnly(9, 30));
        await data.RefreshAsync();
        await data.Actions!.AutoReminders();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        Assert.Single(data.Notifier.Shown);

        ActionResult again = await data.Actions.AutoReminders();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (again.Kind, again.Reason));
        Assert.Single(data.Notifier.Shown);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: more than three due todos are one "<n> todos are due." notification; all are marked.
    [Fact]
    public async Task AutoReminders_MoreThanThree_ShowsOneCount()
    {
        for (int i = 0; i < 4; i++)
        {
            data.AddTodo("Todo " + i, Day, null, new TimeOnly(8, i));
        }

        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AutoReminders();

        Assert.Equal((ActionKind.Done, "4"), (result.Kind, result.Text));
        Assert.Equal([("Context", "4 todos are due.")], data.Notifier.Shown);
        Assert.All(new TodoStore(data.Root).List(), t => Assert.NotNull(t.Notified));
    }

    // AC-6: exactly three are still shown one by one.
    [Fact]
    public async Task AutoReminders_Three_ShowsEach()
    {
        for (int i = 0; i < 3; i++)
        {
            data.AddTodo("Todo " + i, Day, null, new TimeOnly(8, i));
        }

        await data.RefreshAsync();

        await data.Actions!.AutoReminders();

        Assert.Equal(["08:00 Todo 0", "08:01 Todo 1", "08:02 Todo 2"], data.Notifier.Shown.Select(s => s.Body));
    }

    // AC-6: the body is one line, whatever the stored text holds.
    [Fact]
    public async Task AutoReminders_BodyIsOneLine()
    {
        data.AddTodo("Call\nAnna", Day, null, new TimeOnly(9, 0));
        await data.RefreshAsync();

        await data.Actions!.AutoReminders();

        (string title, string body) = Assert.Single(data.Notifier.Shown);
        Assert.Equal("Context", title);
        Assert.Equal("09:00 Call Anna", body);
    }

    // AC-6: with nothing due nothing is written or shown.
    [Fact]
    public async Task AutoReminders_NothingDue_WritesNothing()
    {
        data.AddTodo("Later", Day, null, new TimeOnly(18, 0));
        data.AddTodo("Untimed", Day.AddDays(-1));
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoReminders();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.Empty(data.Notifier.Shown);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: with no todos.json nothing is created.
    [Fact]
    public async Task AutoReminders_NoFile_CreatesNothing()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AutoReminders();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.False(File.Exists(TodosPath));
        Assert.Empty(data.Notifier.Shown);
    }

    // AC-6: an unreadable todos.json gives no write and no Show.
    [Fact]
    public async Task AutoReminders_UnreadableFile_NoWriteNoShow()
    {
        data.WriteFile("todos.json", "{ not json");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoReminders();

        Assert.Equal((ActionKind.Failed, ActionReason.Unreadable), (result.Kind, result.Reason));
        Assert.Empty(data.Notifier.Shown);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: a Show that throws is swallowed and the todo stays notified, so it is never shown twice.
    [Fact]
    public async Task AutoReminders_ShowThrows_TodoStaysNotified()
    {
        data.AddTodo("Call Anna", Day, null, new TimeOnly(9, 30));
        await data.RefreshAsync();
        data.Notifier.Throws = new InvalidOperationException("no notifications");

        ActionResult first = await data.Actions!.AutoReminders();
        data.Notifier.Throws = null;
        ActionResult second = await data.Actions.AutoReminders();

        Assert.Equal((ActionKind.Done, ActionReason.None), (first.Kind, first.Reason));
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (second.Kind, second.Reason));
        Assert.Single(data.Notifier.Shown);
        Assert.NotNull(Stored("Call Anna").Notified);
    }

    // AC-6 (1): a call while another action runs is refused as busy and writes and shows nothing.
    [Fact]
    public async Task AutoReminders_WhileRunning_RefusedBusy()
    {
        data.AddTodo("Call Anna", Day, null, new TimeOnly(9, 30));
        await data.RefreshAsync();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.AutoReminders();
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (second.Kind, second.Reason));
        Assert.Empty(data.Notifier.Shown);
        Assert.Null(Stored("Call Anna").Notified);
    }

    // AC-6: an exception (here from the test hook) is a Failed answer, never a throw.
    [Fact]
    public async Task AutoReminders_NeverThrows()
    {
        data.AddTodo("Call Anna", Day, null, new TimeOnly(9, 30));
        await data.RefreshAsync();
        data.Actions!.BeforeWrite = () => throw new IOException("disk");

        ActionResult result = await data.Actions.AutoReminders();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.DoesNotContain("disk", result.Text, StringComparison.Ordinal);
        Assert.Empty(data.Notifier.Shown);
        Assert.Null(Stored("Call Anna").Notified);
    }

    // AC-7: a todo whose time passed while the app was closed (yesterday, and earlier today) is notified by the
    // first call after start; a later time today waits for its turn.
    [Fact]
    public async Task AutoReminders_AtStart_NotifiesTimesPassedWhileClosed()
    {
        data.AddTodo("Yesterday", Day.AddDays(-1), null, new TimeOnly(16, 0));
        data.AddTodo("Morning", Day, null, new TimeOnly(7, 0));
        data.AddTodo("Afternoon", Day, null, new TimeOnly(14, 0));
        data.LocalNow = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Local);
        await data.RefreshAsync();

        await data.Actions!.AutoReminders();

        Assert.Equal(["16:00 Yesterday", "07:00 Morning"], data.Notifier.Shown.Select(s => s.Body));

        data.LocalNow = new DateTime(2026, 10, 29, 14, 0, 0, DateTimeKind.Local);
        await data.Actions.AutoReminders();

        Assert.Equal("14:00 Afternoon", data.Notifier.Shown[^1].Body);
        Assert.Equal(3, data.Notifier.Shown.Count);
    }
}
