namespace Context.Tests;

public sealed class DesktopActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a matching todo and a confirmation remove exactly that todo, return Done with its text, refresh once.
    [Fact]
    public async Task CompleteTodo_Matching_RemovesItAndRefreshesOnce()
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));
        data.AddTodo("Third", DesktopFixture.Today.AddDays(2));
        DesktopSnapshot before = await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CompleteTodo(2, before.Todos[1], new Confirmation());

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

    // AC-2: a changed todo at that number (other text, due or created) leaves the file byte-for-byte unchanged.
    [Theory]
    [InlineData("text")]
    [InlineData("due")]
    [InlineData("created")]
    public async Task CompleteTodo_ExpectedDiffers_RefusedUnchanged(string field)
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today.AddDays(1));
        DesktopSnapshot before = await data.RefreshAsync();
        TodoItem stored = before.Todos[1];
        TodoItem expected = field switch
        {
            "text" => new TodoItem("Other", stored.Due, stored.Created),
            "due" => new TodoItem(stored.Text, stored.Due.AddDays(1), stored.Created),
            _ => new TodoItem(stored.Text, stored.Due, stored.Created.AddTicks(1)),
        };
        SortedDictionary<string, string> files = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.CompleteTodo(2, expected, new Confirmation());

        AssertRefused(result, ActionReason.Changed, "The todo list changed. Nothing was done.");
        Assert.Equal(files, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-2: the list changed since render (a todo was added before it), so number 1 is another todo now.
    [Fact]
    public async Task CompleteTodo_ListChangedSinceRender_RefusedUnchanged()
    {
        data.AddTodo("Later", DesktopFixture.Today.AddDays(3));
        DesktopSnapshot before = await data.RefreshAsync();
        data.AddTodo("Sooner", DesktopFixture.Today);
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(1, before.Todos[0], new Confirmation());

        AssertRefused(result, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(files, Files());
        Assert.Equal(["Sooner", "Later"], data.Todos!.List().Select(t => t.Text).ToArray());
    }

    // AC-2: numbers out of range (and an empty or missing list) change nothing.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task CompleteTodo_OutOfRange_RefusedUnchanged(int number)
    {
        data.AddTodo("First", DesktopFixture.Today);
        data.AddTodo("Second", DesktopFixture.Today);
        DesktopSnapshot before = await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(number, before.Todos[0], new Confirmation());

        AssertRefused(result, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(files, Files());
    }

    // AC-2: no todos.json at all: refused, and nothing is created.
    [Fact]
    public async Task CompleteTodo_NoFile_RefusedNothingCreated()
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(
            1, new TodoItem("Gone", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());

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
            1, new TodoItem("x", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(ActionReason.Unreadable, result.Reason);
        Assert.Equal(
            "Can't read todos.json, so nothing was changed. Fix or move the file and try again.", result.Text);
        Assert.Equal(files, Files());
    }

    // AC-3: an encrypted todos.json without the key (no encryption.json, so not locked) can't be decrypted.
    [Fact]
    public async Task CompleteTodo_Undecryptable_FailedUnchanged()
    {
        data.AddTodo("Secret", DesktopFixture.Today);
        string elsewhere = Path.Combine(Path.GetDirectoryName(data.Root)!, "keys");
        Directory.CreateDirectory(elsewhere);
        Encryption other = new Encryption(elsewhere, minIterations: 1000);
        other.Use(Vault.Create(elsewhere, "a long enough passphrase", 1000));
        string path = Path.Combine(data.Root, TodoStore.FileName);
        File.WriteAllBytes(path, Encryption.Protect(other, Vault.KindTodos, File.ReadAllBytes(path)));
        await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(
            1, new TodoItem("Secret", DesktopFixture.Today, DateTime.UnixEpoch), new Confirmation());

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal(DesktopActions.TodosUnreadableText, result.Text);
        Assert.Equal(files, Files());
    }

    // AC-4: with encryption.json every action is refused as Locked and no file under the root changes.
    [Fact]
    public async Task CompleteTodo_Locked_RefusedNothingTouched()
    {
        data.AddTodo("First", DesktopFixture.Today);
        IReadOnlyList<TodoItem> list = new TodoStore(data.Root).List();
        Vault.Create(data.Root, "a long enough passphrase", 1000).Dispose();
        data.CreateState();
        SortedDictionary<string, string> files = Files();
        int builds = data.State!.Builds;

        ActionResult confirmed = await data.Actions!.CompleteTodo(1, list[0], new Confirmation());
        ActionResult unconfirmed = await data.Actions.CompleteTodo(1, list[0], null);

        AssertRefused(confirmed, ActionReason.Locked, DesktopActions.LockedText);
        AssertRefused(unconfirmed, ActionReason.Locked, DesktopActions.LockedText);
        Assert.Equal(files, Files());
        Assert.Equal(builds, data.State.Builds);
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

        Task<ActionResult> first = data.Actions.CompleteTodo(1, before.Todos[0], new Confirmation());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.CompleteTodo(2, before.Todos[1], new Confirmation());
        release.Set();
        ActionResult firstResult = await first;

        AssertRefused(second, ActionReason.Busy, DesktopActions.BusyText);
        Assert.Equal(ActionKind.Done, firstResult.Kind);
        Assert.Equal(["Second"], data.Todos!.List().Select(t => t.Text).ToArray());

        data.Actions.BeforeWrite = null;
        ActionResult third = await data.Actions.CompleteTodo(1, data.Todos.List()[0], new Confirmation());
        Assert.Equal(ActionKind.Done, third.Kind);
    }

    // AC-5: no confirmation: nothing thrown, nothing changed, Refused.
    [Fact]
    public async Task CompleteTodo_NullConfirmation_RefusedUnchanged()
    {
        data.AddTodo("First", DesktopFixture.Today);
        DesktopSnapshot before = await data.RefreshAsync();
        SortedDictionary<string, string> files = Files();

        ActionResult result = await data.Actions!.CompleteTodo(1, before.Todos[0], null);

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

        ActionResult nullExpected = await data.Actions!.CompleteTodo(1, null!, new Confirmation());
        data.Actions.BeforeWrite = () => throw new InvalidOperationException("secret detail");
        ActionResult thrown = await data.Actions.CompleteTodo(1, data.Todos!.List()[0], new Confirmation());

        AssertRefused(nullExpected, ActionReason.Changed, DesktopActions.TodosChangedText);
        Assert.Equal(ActionKind.Failed, thrown.Kind);
        Assert.Equal(DesktopActions.FailedText, thrown.Text);
        Assert.Equal(files, Files());
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
