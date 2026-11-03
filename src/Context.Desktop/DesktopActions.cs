namespace Context;

/// <summary>How a <see cref="DesktopActions"/> call ended.</summary>
internal enum ActionKind
{
    /// <summary>The change was made.</summary>
    Done,

    /// <summary>A check said no; nothing was touched.</summary>
    Refused,

    /// <summary>The change could not be made; nothing was changed.</summary>
    Failed,
}

/// <summary>Why a <see cref="DesktopActions"/> call ended the way it did.</summary>
internal enum ActionReason
{
    None,
    Locked,
    Busy,
    NotConfirmed,
    Changed,
    Unreadable,
    WriteFailed,
}

/// <summary>
/// The result of a desktop action: a kind, a reason and raw text (the todo's text for <see cref="ActionKind.Done"/>,
/// else a fixed message; never an exception message, never cleaned or encoded).
/// </summary>
internal sealed record ActionResult
{
    public ActionResult(ActionKind kind, ActionReason reason, string text)
    {
        Kind = kind;
        Reason = reason;
        Text = text;
    }

    public ActionKind Kind { get; }

    public ActionReason Reason { get; }

    public string Text { get; }

    /// <summary>Only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(ActionResult);
}

/// <summary>
/// The only place the desktop app changes data. Every action calls the same Core stores and checks as the console,
/// never throws, runs one at a time (a call made while another runs is refused as <see cref="ActionReason.Busy"/>),
/// is refused as <see cref="ActionReason.Locked"/> without touching a file while <c>encryption.json</c> exists, and
/// refreshes <see cref="DesktopState"/> after a change. Components call it only from event handlers.
/// </summary>
internal sealed class DesktopActions
{
    public const string LockedText = "Your data is encrypted. Nothing was done.";
    public const string BusyText = "Another change is still running. Nothing was done.";
    public const string NotConfirmedText = "Not confirmed. Nothing was done.";
    public const string TodosChangedText = "The todo list changed. Nothing was done.";
    public const string TodosUnreadableText =
        "Can't read todos.json, so nothing was changed. Fix or move the file and try again.";
    public const string TodosWriteFailedText = "Can't write todos.json. Nothing was done.";
    public const string FailedText = "Can't make that change.";

    private readonly Encryption encryption;
    private readonly TodoStore todos;
    private readonly DesktopState state;
    private int running;

    public DesktopActions(Encryption encryption, TodoStore todos, DesktopState state)
    {
        this.encryption = encryption;
        this.todos = todos;
        this.state = state;
    }

    /// <summary>Test hook: runs on the worker thread right before an action's checks and write.</summary>
    internal Action? BeforeWrite { get; set; }

    /// <summary>
    /// Marks todo <paramref name="number"/> (1-based, as listed) done, which removes it through
    /// <see cref="TodoStore.Complete"/>, but only when the stored todo at that number still equals
    /// <paramref name="expected"/> and the user <paramref name="confirmed"/> it.
    /// </summary>
    public Task<ActionResult> CompleteTodo(int number, TodoItem expected, Confirmation? confirmed) =>
        Run(() => CompleteTodoNow(number, expected, confirmed));

    private static ActionResult Refused(ActionReason reason, string text) => new(ActionKind.Refused, reason, text);

    private static ActionResult Failed(ActionReason reason, string text) => new(ActionKind.Failed, reason, text);

    private async Task<ActionResult> Run(Func<ActionResult> action)
    {
        if (IsLocked())
        {
            return Refused(ActionReason.Locked, LockedText);
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return Refused(ActionReason.Busy, BusyText);
        }

        try
        {
            ActionResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    BeforeWrite?.Invoke();
                    return IsLocked() ? Refused(ActionReason.Locked, LockedText) : action();
                });
            }
            catch (Exception)
            {
                result = Failed(ActionReason.WriteFailed, FailedText);
            }

            if (result.Kind == ActionKind.Done)
            {
                try
                {
                    await state.Refresh();
                }
                catch (Exception)
                {
                    // The change is made; the watcher or the next tick shows it.
                }
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    // Any doubt counts as locked, so nothing is touched.
    private bool IsLocked()
    {
        try
        {
            return encryption.IsConfigured;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private ActionResult CompleteTodoNow(int number, TodoItem? expected, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        if (expected is null)
        {
            return Refused(ActionReason.Changed, TodosChangedText);
        }

        try
        {
            IReadOnlyList<TodoItem> list = todos.List();
            if (number < 1 || number > list.Count || list[number - 1] != expected)
            {
                return Refused(ActionReason.Changed, TodosChangedText);
            }

            TodoItem done = todos.Complete(number);
            return new ActionResult(ActionKind.Done, ActionReason.None, done.Text);
        }
        catch (TodoStoreException)
        {
            return Failed(ActionReason.Unreadable, TodosUnreadableText);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Refused(ActionReason.Changed, TodosChangedText);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, TodosWriteFailedText);
        }
    }
}
