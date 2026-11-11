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
    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);
}
