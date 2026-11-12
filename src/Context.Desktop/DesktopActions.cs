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
    Busy,
    NotConfirmed,
    Changed,
    Unreadable,
    WriteFailed,

    /// <summary>The note's text is the same as when it was opened; nothing was written.</summary>
    Unchanged,

    /// <summary>The note's file changed on disk since it was opened.</summary>
    ChangedOnDisk,

    /// <summary>The note's file no longer exists.</summary>
    Missing,

    /// <summary>The input is empty or too long.</summary>
    Invalid,

    /// <summary>Another note already has that name (ignoring case).</summary>
    Exists,
}

/// <summary>
/// The result of a desktop action: a kind, a reason and raw text (the todo's text or the note's name for
/// <see cref="ActionKind.Done"/>, else a fixed message, which may name the note; never an exception message, never
/// cleaned or encoded). A saved note comes back in <see cref="Note"/>.
/// </summary>
internal sealed record ActionResult
{
    public ActionResult(ActionKind kind, ActionReason reason, string text, EditableNote? note = null)
    {
        Kind = kind;
        Reason = reason;
        Text = text;
        Note = note;
    }

    public ActionKind Kind { get; }

    public ActionReason Reason { get; }

    public string Text { get; }

    /// <summary>The note as saved (or as opened, when nothing changed); <c>null</c> for other actions.</summary>
    public EditableNote? Note { get; }

    /// <summary>Only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(ActionResult);
}

/// <summary>
/// The only place the desktop app changes data. Every action calls the same Core stores and checks as the console,
/// never throws, runs one at a time (a call made while another runs is refused as <see cref="ActionReason.Busy"/>) and
/// refreshes <see cref="DesktopState"/> after a change. Components call it only from event handlers.
/// </summary>
internal sealed class DesktopActions
{
    public const string BusyText = "Another change is still running. Nothing was done.";
    public const string NotConfirmedText = "Not confirmed. Nothing was done.";
    public const string TodosChangedText = "The todo list changed. Nothing was done.";
    public const string TodosUnreadableText =
        "Can't read todos.json, so nothing was changed. Fix or move the file and try again.";
    public const string TodosWriteFailedText = "Can't write todos.json. Nothing was done.";
    public const string FailedText = "Can't make that change.";
    public const string EmptyTodoText = "Write the todo first.";
    public const string CreateFailedText = "Can't create that note. Nothing was done.";

    /// <summary>The longest todo text <see cref="AddTodo"/> accepts, as in the console.</summary>
    public const int MaxTodoLength = 200;

    /// <summary>The largest note text <see cref="SaveNote"/> writes (5 MB, as for imports).</summary>
    public const long MaxNoteBytes = NoteStore.MaxEditBytes;

    private readonly NoteStore notes;
    private readonly TodoStore todos;
    private readonly DesktopState state;
    private readonly INoteLauncher launcher;
    private int running;

    public DesktopActions(NoteStore notes, TodoStore todos, DesktopState state, INoteLauncher launcher)
    {
        this.notes = notes;
        this.todos = todos;
        this.state = state;
        this.launcher = launcher;
    }

    /// <summary>The red line when a note with that name exists (ignoring case).</summary>
    public static string ExistsText(string name) => $"Note {name} already exists.";

    /// <summary>The red line when the note to rename, delete or open is gone.</summary>
    public static string NoteMissingText(string name) => $"{name} no longer exists. Nothing was done.";

    /// <summary>The red line when a rename fails.</summary>
    public static string RenameFailedText(string name) => $"Can't rename {name}. Nothing was done.";

    /// <summary>The red line when a delete fails.</summary>
    public static string DeleteFailedText(string name) => $"Can't delete {name}.";

    /// <summary>The red line when a note can't be (or may not be) opened in another app.</summary>
    public static string OpenExternallyText(string name) => $"Can't open {name} in another app.";

    /// <summary>The red line when a note changed on disk since it was opened.</summary>
    public static string ChangedOnDiskText(string name) => $"{name} changed on disk since you opened it. Nothing was saved.";

    /// <summary>The red line when a note's file is gone.</summary>
    public static string MissingText(string name) => $"{name} no longer exists. Nothing was saved.";

    /// <summary>The red line when a note can't be written.</summary>
    public static string NoteWriteFailedText(string name) => $"Can't write {name}. Nothing was saved.";

    /// <summary>The red line when a note's text is over <see cref="MaxNoteBytes"/>.</summary>
    public static string TooLargeText(string name) => $"{name} is larger than 5 MB. Nothing was saved.";

    /// <summary>The red line when a todo's text is too long.</summary>
    public static string TodoTooLongText => $"A todo can be at most {MaxTodoLength} characters. Nothing was done.";

    /// <summary>Test hook: runs on the worker thread right before an action's checks and write.</summary>
    internal Action? BeforeWrite { get; set; }

    /// <summary>
    /// Marks <paramref name="todo"/> done, which removes the stored todo with the same id through
    /// <see cref="TodoStore.Complete"/> in one read and write, but only when the user <paramref name="confirmed"/> it.
    /// A todo that is no longer stored is refused as <see cref="ActionReason.Changed"/>.
    /// </summary>
    public Task<ActionResult> CompleteTodo(TodoItem todo, Confirmation? confirmed) =>
        Run(() => CompleteTodoNow(todo, confirmed));

    /// <summary>
    /// Saves <paramref name="text"/> over the note <paramref name="opened"/> came from through
    /// <see cref="NoteStore.SaveIfUnchanged"/>, keeping its line endings and BOM. Writes nothing when the text is
    /// unchanged (<see cref="ActionReason.Unchanged"/>, done, no refresh), when the file changed on disk or is gone,
    /// or when the text is over <see cref="MaxNoteBytes"/>. The result's text is the note's name when done.
    /// </summary>
    public Task<ActionResult> SaveNote(EditableNote opened, string text) => Run(() => SaveNoteNow(opened, text));

    /// <summary>
    /// Adds a todo due <paramref name="due"/> through <see cref="TodoStore.Add"/>. Blank text is refused with
    /// <see cref="EmptyTodoText"/> and text over <see cref="MaxTodoLength"/> characters is refused; both
    /// write nothing. The result's text is the todo's text when done.
    /// </summary>
    public Task<ActionResult> AddTodo(string text, DateOnly due) => Run(() => AddTodoNow(text, due));

    /// <summary>
    /// Creates the note <paramref name="title"/> through <see cref="NoteStore.Create"/>. An invalid title is refused
    /// with the <see cref="NoteStore.ValidateTitle"/> text, an existing note with <see cref="ExistsText"/>. The result's
    /// text is the new note's name when done.
    /// </summary>
    public Task<ActionResult> CreateNote(string title) => Run(() => CreateNoteNow(title));

    /// <summary>
    /// Renames the note <paramref name="name"/> to <paramref name="newTitle"/> through <see cref="NoteStore.Rename"/>
    /// (the file is moved, its bytes are kept). The same name gives <see cref="ActionReason.Unchanged"/> (done, nothing
    /// written, no refresh). The result's text is the new name when done.
    /// </summary>
    public Task<ActionResult> RenameNote(string name, string newTitle) => Run(() => RenameNoteNow(name, newTitle));

    /// <summary>
    /// Deletes the note <paramref name="name"/> through <see cref="NoteStore.Delete"/>, but only when the user
    /// <paramref name="confirmed"/> it. There is no undo. The result's text is the name when done.
    /// </summary>
    public Task<ActionResult> DeleteNote(string name, Confirmation? confirmed) => Run(() => DeleteNoteNow(name, confirmed));

    /// <summary>
    /// Opens the note <paramref name="name"/> in the OS default app through the <see cref="INoteLauncher"/>. Only a path
    /// from the note list is launched, and only when it is inside the data folder, ends in <c>.md</c> and is a regular
    /// file (no link or reparse point, no folder, seekable). Never writes and never refreshes.
    /// </summary>
    public Task<ActionResult> OpenExternally(string name) => Run(() => OpenExternallyNow(name), refresh: false);

    private static ActionResult Refused(ActionReason reason, string text) => new(ActionKind.Refused, reason, text);

    private static ActionResult Failed(ActionReason reason, string text) => new(ActionKind.Failed, reason, text);

    private async Task<ActionResult> Run(Func<ActionResult> action, bool refresh = true)
    {
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
                    return action();
                });
            }
            catch (Exception)
            {
                result = Failed(ActionReason.WriteFailed, FailedText);
            }

            if (refresh && result.Kind == ActionKind.Done && result.Reason != ActionReason.Unchanged)
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

    private ActionResult CompleteTodoNow(TodoItem? todo, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        if (todo is null)
        {
            return Refused(ActionReason.Changed, TodosChangedText);
        }

        try
        {
            TodoItem? done = todos.Complete(todo.Id);
            return done is null
                ? Refused(ActionReason.Changed, TodosChangedText)
                : new ActionResult(ActionKind.Done, ActionReason.None, done.Text);
        }
        catch (TodoStoreException)
        {
            return Failed(ActionReason.Unreadable, TodosUnreadableText);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, TodosWriteFailedText);
        }
    }

    private ActionResult SaveNoteNow(EditableNote opened, string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxNoteBytes)
        {
            return Refused(ActionReason.Invalid, TooLargeText(opened.Name));
        }

        try
        {
            NoteSaveResult saved = notes.SaveIfUnchanged(opened, text, out EditableNote? savedNote);
            return saved switch
            {
                NoteSaveResult.Saved => new ActionResult(ActionKind.Done, ActionReason.None, opened.Name, savedNote),
                NoteSaveResult.Unchanged => new ActionResult(ActionKind.Done, ActionReason.Unchanged, opened.Name, opened),
                NoteSaveResult.ChangedOnDisk => Refused(ActionReason.ChangedOnDisk, ChangedOnDiskText(opened.Name)),
                NoteSaveResult.TooLarge => Refused(ActionReason.Invalid, TooLargeText(opened.Name)),
                _ => Refused(ActionReason.Missing, MissingText(opened.Name)),
            };
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, NoteWriteFailedText(opened.Name));
        }
    }

    private ActionResult AddTodoNow(string text, DateOnly due)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Refused(ActionReason.Invalid, EmptyTodoText);
        }

        if (trimmed.Length > MaxTodoLength)
        {
            return Refused(ActionReason.Invalid, TodoTooLongText);
        }

        try
        {
            TodoItem added = todos.Add(trimmed, due);
            return new ActionResult(ActionKind.Done, ActionReason.None, added.Text);
        }
        catch (TodoStoreException)
        {
            return Failed(ActionReason.Unreadable, TodosUnreadableText);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, TodosWriteFailedText);
        }
    }

    private ActionResult CreateNoteNow(string title)
    {
        string? invalid = NoteStore.ValidateTitle(NoteStore.NormalizeTitle(title));
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, invalid);
        }

        try
        {
            NoteInfo created = notes.Create(title);
            return new ActionResult(ActionKind.Done, ActionReason.None, created.Name);
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CreateFailedText);
        }
    }

    private ActionResult RenameNoteNow(string name, string newTitle)
    {
        string title = NoteStore.NormalizeTitle(newTitle);
        string? invalid = NoteStore.ValidateTitle(title);
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, invalid);
        }

        try
        {
            if (!notes.TryFind(name, out NoteInfo? source) || source is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (string.Equals(source.Name, title, StringComparison.Ordinal))
            {
                return new ActionResult(ActionKind.Done, ActionReason.Unchanged, source.Name);
            }

            NoteInfo? renamed = notes.Rename(source.Name, title);
            return renamed is null
                ? Refused(ActionReason.Missing, NoteMissingText(name))
                : new ActionResult(ActionKind.Done, ActionReason.None, renamed.Name);
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, RenameFailedText(name));
        }
    }

    private ActionResult DeleteNoteNow(string name, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        try
        {
            return notes.Delete(name)
                ? new ActionResult(ActionKind.Done, ActionReason.None, name)
                : Refused(ActionReason.Missing, NoteMissingText(name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, DeleteFailedText(name));
        }
    }

    private ActionResult OpenExternallyNow(string name)
    {
        NoteInfo? note;
        try
        {
            if (!notes.TryFind(name, out note) || note is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (!IsLaunchable(note.Path))
            {
                return Refused(ActionReason.Invalid, OpenExternallyText(name));
            }
        }
        catch (Exception)
        {
            return Refused(ActionReason.Invalid, OpenExternallyText(name));
        }

        try
        {
            launcher.Launch(note.Path);
            return new ActionResult(ActionKind.Done, ActionReason.None, note.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, OpenExternallyText(name));
        }
    }

    // Only a regular .md file directly in the note folder: no link or reparse point, no folder, and seekable. A file
    // that reports length 0 can't be told apart from a FIFO without opening it (which could block), so off Windows it
    // is refused. Best effort: the file could still be swapped between this check and the launch, but only by someone who
    // can already write to the data folder, and the OS picks the handler by the .md extension.
    private bool IsLaunchable(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase) || !notes.Contains(path))
        {
            return false;
        }

        FileInfo info = new FileInfo(path);
        if (!info.Exists
            || info.LinkTarget is not null
            || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
        {
            return false;
        }

        if (info.Length == 0)
        {
            return OperatingSystem.IsWindows();
        }

        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.CanSeek;
    }
}
