namespace Context.Tests;

public sealed class DesktopActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a stored todo and a confirmation remove exactly that todo (by id), return Done with its text, refresh once.
    [Fact]
    public async Task CompleteTodo_Matching_RemovesItAndRefreshesOnce()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));
        data.AddTodo("Third", DesktopFixture.Today.AddDays(2));
        DesktopSnapshot before = await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CompleteTodo(before.Todos[1], new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("Second", result.Text);
        Assert.Equal(["First", "Third"], data.Todos!.List().Select(t => t.Text).ToArray());
        Assert.Equal(builds + 1, data.State.Builds);
        Assert.Equal(["First", "Third"], data.State.Snapshot!.Todos.Select(t => t.Text).ToArray());
    }

    // AC-1: the result never shows its raw text through formatting.
    [Fact]
    public void ActionResult_ToString_IsTypeName()
    {
        Assert.Equal("ActionResult", new ActionResult(ActionKind.Done, ActionReason.None, "secret").ToString());
    }

    // AC-2: a todo that is no longer stored (done elsewhere, or an id the file never had) leaves the file
    // byte-for-byte unchanged and is refused.
    [Fact]
    public async Task CompleteTodo_TodoGone_RefusedUnchanged()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));
        DesktopSnapshot before = await data.RefreshAsync();
        data.Todos!.Complete(before.Todos[1].Id);
        SortedDictionary<string, string> files = Files();
        int builds = data.State!.Builds;

        ActionResult gone = await data.Actions!.CompleteTodo(before.Todos[1], new Confirmation());
        ActionResult unknown = await data.Actions.CompleteTodo(
            new TodoItem(Guid.NewGuid(), "First", before.Todos[0].Due, before.Todos[0].Created), new Confirmation());

        AssertRefused(gone, ActionReason.Changed, "The todo list changed. Nothing was done.");
        AssertRefused(unknown, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(files, Files());
        Assert.Equal(builds, data.State.Builds);
        Assert.Equal(["First"], data.Todos.List().Select(t => t.Text).ToArray());
    }

    // AC-2: the list changed since render (a sooner todo now sits where the shown one was); the shown todo, found
    // by its id, is the one removed.
    [Fact]
    public async Task CompleteTodo_ListChangedSinceRender_RemovesTheShownTodo()
    {
        data.AddTodo("Later", DesktopFixture.Today.AddDays(3));
        DesktopSnapshot before = await data.RefreshAsync();
        data.AddTodo("Sooner", DesktopFixture.Today);

        ActionResult result = await data.Actions!.CompleteTodo(before.Todos[0], new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Later", result.Text);
        Assert.Equal(["Sooner"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // AC-2: no todos.json at all: refused, and nothing is created.
    [Fact]
    public async Task CompleteTodo_NoFile_RefusedNothingCreated()
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(
            new TodoItem(Guid.NewGuid(), "Gone", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());

        AssertRefused(result, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(files, Files());
        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
    }

    // AC-3: an unparsable todos.json fails with the console's text and stays as it is.
    [Theory]
    [InlineData("[ {")]
    [InlineData("null")]
    [InlineData("[{\"text\":\"no due\"}]")]
    public async Task CompleteTodo_Unreadable_FailedUnchanged(string json)
    {
        data.WriteFile(TodoStore.FileName, json);
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(
            new TodoItem(Guid.NewGuid(), "x", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(ActionReason.Unreadable, result.Reason);
        Assert.Equal(
            "Can't read todos.json, so nothing was changed. Fix or move the file and try again.", result.Text);
        Assert.Equal(files, Files());
    }

    // AC-4: a call made while another action runs is refused as Busy; the first one still finishes.
    [Fact]
    public async Task CompleteTodo_WhileRunning_SecondRefusedBusy()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today);
        DesktopSnapshot before = await data.RefreshAsync();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CompleteTodo(before.Todos[0], new Confirmation());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.CompleteTodo(before.Todos[1], new Confirmation());
        release.Set();
        ActionResult firstResult = await first;

        AssertRefused(second, ActionReason.Busy, DesktopActions.BusyText);
        Assert.Equal(ActionKind.Done, firstResult.Kind);
        Assert.Equal(["Second"], data.Todos!.List().Select(t => t.Text).ToArray());

        data.Actions.BeforeWrite = null;
        ActionResult third = await data.Actions.CompleteTodo(data.Todos.List()[0], new Confirmation());
        Assert.Equal(ActionKind.Done, third.Kind);
    }

    // AC-5: no confirmation: nothing thrown, nothing changed, Refused.
    [Fact]
    public async Task CompleteTodo_NullConfirmation_RefusedUnchanged()
    {
        data.AddTodo("First", DesktopFixture.Today);
        DesktopSnapshot before = await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(before.Todos[0], null);

        AssertRefused(result, ActionReason.NotConfirmed, DesktopActions.NotConfirmedText);
        Assert.Equal(files, Files());
    }

    // AC-5 / never throws: a null expected item and a throwing hook both come back as results, not exceptions.
    [Fact]
    public async Task CompleteTodo_NullExpectedOrHookThrows_NoException()
    {
        data.AddTodo("First", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult nullExpected = await data.Actions!.CompleteTodo(null!, new Confirmation());
        data.Actions.BeforeWrite = () => throw new InvalidOperationException("secret detail");
        ActionResult thrown = await data.Actions.CompleteTodo(data.Todos!.List()[0], new Confirmation());

        AssertRefused(nullExpected, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(ActionKind.Failed, thrown.Kind);
        Assert.Equal(DesktopActions.FailedText, thrown.Text);
        Assert.Equal(files, Files());
    }

    // 132 AC-5: a leftover key file of the removed feature refuses nothing: adding and completing a todo work.
    [Fact]
    public async Task WithALeftoverKeyFile_AddAndCompleteTodoSucceed()
    {
        string key = Path.Combine(data.Root, RemovedFeature.KeyFileName);
        data.WriteFile(RemovedFeature.KeyFileName, "{}");
        await data.RefreshAsync();

        ActionResult added = await data.Actions!.AddTodo("Buy milk", DesktopFixture.Today);
        string written = File.ReadAllText(Path.Combine(data.Root, TodoStore.FileName));
        ActionResult completed = await data.Actions.CompleteTodo(data.Todos!.List()[0], new Confirmation());

        Assert.Equal(ActionKind.Done, added.Kind);
        Assert.StartsWith("[", written.TrimStart());
        Assert.Contains("Buy milk", written);
        Assert.Equal(ActionKind.Done, completed.Kind);
        Assert.Empty(data.Todos.List());
        Assert.Equal("{}", File.ReadAllText(key));
    }

    private static void AssertRefused(ActionResult result, ActionReason reason, string text)
    {
        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(text, result.Text);
    }

    // context.db* is the tag index the state's refresh keeps; it is not user data.
    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    // 287 AC-6: AddMeetingTodo stores the trimmed text due on the appointment's date, tied to it; the result's NextDue
    // is that date.
    [Fact]
    public async Task AddMeetingTodo_StoresTodoTiedToTheAppointment()
    {
        await data.RefreshAsync();
        Appointment sync = MeetingCalendar.At("Sync", MeetingCalendar.SyncToday.AddDays(7), MeetingCalendar.SyncUid);

        ActionResult result = await data.Actions!.AddMeetingTodo(sync, "  Bring budget  ");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Bring budget", result.Text);
        Assert.Equal(new DateOnly(2026, 11, 5), result.NextDue);
        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Bring budget", added.Text);
        Assert.Equal(new DateOnly(2026, 11, 5), added.Due);
        Assert.Equal(MeetingCalendar.SyncLink(1), added.Meeting);
        Assert.Null(added.Time);
        Assert.Null(added.Repeat);
    }

    // 287 AC-4/AC-8: an appointment without a UID, a blank text and a too long text are refused and write nothing.
    [Fact]
    public async Task AddMeetingTodo_NoUidOrBadText_RefusedNothingWritten()
    {
        await data.RefreshAsync();
        Appointment sync = MeetingCalendar.At("Sync", MeetingCalendar.SyncToday, MeetingCalendar.SyncUid);

        ActionResult noUid = await data.Actions!.AddMeetingTodo(MeetingCalendar.At("Plain", MeetingCalendar.SyncToday, null), "x");
        ActionResult longUid = await data.Actions.AddMeetingTodo(
            MeetingCalendar.At("Sync", MeetingCalendar.SyncToday, new string('u', MeetingLink.MaxUidLength + 1)), "x");
        ActionResult blank = await data.Actions.AddMeetingTodo(sync, "   ");
        ActionResult tooLong = await data.Actions.AddMeetingTodo(sync, new string('x', DesktopActions.MaxTodoLength + 1));

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid, "This appointment can't hold todos."), (noUid.Kind, noUid.Reason, noUid.Text));
        Assert.Equal("This appointment can't hold todos.", longUid.Text);
        Assert.Equal(DesktopActions.EmptyTodoText, blank.Text);
        Assert.Equal(DesktopActions.TodoTooLongText, tooLong.Text);
        Assert.False(File.Exists(Path.Combine(data.Root, "todos.json")));
    }

    // 287 AC-3/AC-9: TieTodo moves an open todo to the appointment: due its date (the result's NextDue), time kept.
    [Fact]
    public async Task TieTodo_Open_TiesAndMovesDue()
    {
        TodoItem todo = MeetingCalendar.Store(data).Add("Later", DesktopFixture.Today.AddDays(3), null, new TimeOnly(9, 30));
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.TieTodo(
            todo.Id, MeetingCalendar.At("Sync", MeetingCalendar.SyncToday.AddDays(7), MeetingCalendar.SyncUid));

        Assert.Equal((ActionKind.Done, "Later", new DateOnly(2026, 11, 5)), (result.Kind, result.Text, result.NextDue));
        TodoItem tied = Assert.Single(data.Todos!.List());
        Assert.Equal(new DateOnly(2026, 11, 5), tied.Due);
        Assert.Equal(new TimeOnly(9, 30), tied.Time);
        Assert.Equal(MeetingCalendar.SyncLink(1), tied.Meeting);
        Assert.Equal(tied.Meeting, Assert.Single(data.State!.Snapshot!.Todos).Meeting);
    }

    // 287 AC-11: TieTodo on a done or unknown todo is the changed line and leaves the file's bytes; an appointment
    // without a UID is refused the same way.
    [Fact]
    public async Task TieTodo_DoneUnknownOrNoUid_RefusedNothingWritten()
    {
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem done = store.Add("Done", DesktopFixture.Today);
        TodoItem open = store.Add("Open", DesktopFixture.Today);
        store.Complete(done.Id);
        await data.RefreshAsync();
        string path = Path.Combine(data.Root, "todos.json");
        byte[] before = File.ReadAllBytes(path);
        Appointment sync = MeetingCalendar.At("Sync", MeetingCalendar.SyncToday, MeetingCalendar.SyncUid);

        ActionResult doneResult = await data.Actions!.TieTodo(done.Id, sync);
        ActionResult unknown = await data.Actions.TieTodo(Guid.NewGuid(), sync);
        ActionResult noUid = await data.Actions.TieTodo(open.Id, MeetingCalendar.At("Plain", MeetingCalendar.SyncToday, null));

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Changed, "The todo list changed. Nothing was moved."),
            (doneResult.Kind, doneResult.Reason, doneResult.Text));
        Assert.Equal("The todo list changed. Nothing was moved.", unknown.Text);
        Assert.Equal((ActionKind.Refused, "This appointment can't hold todos."), (noUid.Kind, noUid.Text));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
    // 383 AC-4, AC-7, AC-10: BookTimes runs one tm call at the actions' local clock, says what was booked and writes
    // nothing into the data folder or Times' folder.
    [Theory]
    [InlineData("Arrive", "arrive", "Booked arrival at 7:04 in Times.")]
    [InlineData("Break", "break", "Booked break at 7:04 in Times.")]
    [InlineData("BreakEnd", "breakend", "Booked break end at 7:04 in Times.")]
    [InlineData("Leave", "leave", "Booked leaving at 7:04 in Times.")]
    public async Task BookTimes_RunsOneCall_WritesNothing(string actionName, string command, string text)
    {
        TimesAction action = Enum.Parse<TimesAction>(actionName);
        data.LocalNow = new DateTime(2026, 10, 29, 7, 4, 59, DateTimeKind.Local);
        Directory.CreateDirectory(data.TimesFolder);
        File.WriteAllText(Path.Combine(data.TimesFolder, "2026-10-29.json"), """{"entries":[]}""");
        await data.RefreshAsync();
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);
        SortedDictionary<string, string> root = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.BookTimes(action);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(text, result.Text);
        Assert.Equal([[command, "7:04"]], data.TimesRunner.Calls);
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
        Assert.Equal(root, FileSnapshot.Take(data.Root));
    }

    // 383 AC-6: a checkpoint with a quicklist position; a name like -x or #3 y stays one cleaned argument.
    [Fact]
    public async Task BookTimes_Checkpoint_PositionAndCleanName()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 16, 0, 0, DateTimeKind.Local);
        await data.RefreshAsync();

        ActionResult first = await data.Actions!.BookTimes(TimesAction.Checkpoint, 2, " -x\nnext ");
        ActionResult second = await data.Actions.BookTimes(TimesAction.Checkpoint, null, "#3 y");

        Assert.Equal("Booked checkpoint x next at 16:00 in Times.", first.Text);
        Assert.Equal("Booked checkpoint 3 y at 16:00 in Times.", second.Text);
        Assert.Equal([["checkpoint", "16:00", "#2", "x next"], ["checkpoint", "16:00", "3 y"]], data.TimesRunner.Calls);
    }

    // 383 AC-6: a name that is blank after the clean-up is refused and runs nothing.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" # - ")]
    public async Task BookTimes_BlankName_RefusedRunsNothing(string name)
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.BookTimes(TimesAction.Checkpoint, 1, name);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.Invalid, result.Reason);
        Assert.Equal("Name the checkpoint.", result.Text);
        Assert.Empty(data.TimesRunner.Calls);
    }

    // 383 AC-8: failures give job 381's texts; a runner that throws is the not-found text.
    [Fact]
    public async Task BookTimes_Failures()
    {
        await data.RefreshAsync();

        data.TimesRunner.Result = TimesRunResult.TimedOut();
        Assert.Equal(MeetingNote.TimesTimedOutText, (await data.Actions!.BookTimes(TimesAction.Arrive)).Text);
        data.TimesRunner.Result = new TimesRunResult(TimesRunKind.Done, 1, string.Empty, "\u001b[1mAlready\u001b[0m arrived\nmore");
        ActionResult refused = await data.Actions.BookTimes(TimesAction.Arrive);
        Assert.Equal(ActionKind.Failed, refused.Kind);
        Assert.Equal("Times refused the booking: \ufffd[1mAlready\ufffd[0m arrived", refused.Text);
        data.TimesRunner.Throws = new InvalidOperationException("boom");
        ActionResult thrown = await data.Actions.BookTimes(TimesAction.Leave);
        Assert.Equal(ActionKind.Failed, thrown.Kind);
        Assert.Equal(MeetingNote.TimesNotFoundText, thrown.Text);
    }
}
