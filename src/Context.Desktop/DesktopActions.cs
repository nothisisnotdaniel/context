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

    /// <summary>The sensitive note is not open, so its text is not known.</summary>
    Locked,

    /// <summary>The key store has no recovery key yet, or this OS has no key store.</summary>
    NoKeys,
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

    public const string UnsupportedText = "Sensitive notes need macOS or Windows.";
    public const string NoRecoveryText = "Set up recovery first.";
    public const string InboxSensitiveText = "The inbox can't be sensitive. Nothing was done.";
    public const string RecoveryExistsText = "Recovery is already set up. Nothing was done.";
    public const string PasswordTooShortText = "The password needs at least 12 characters.";

    /// <summary>Shown when the recovery password holds text that has no Unicode normal form.</summary>
    public const string PasswordUnusableText = "That password can't be used.";
    public const string RecoveryPathText = "Choose a place outside the data folder for the recovery file.";
    public const string RecoveryFileExistsText = "That file already exists. Nothing was done.";
    public const string RecoveryWriteFailedText = "Can't write the recovery file. Nothing was done.";
    public const string MachineKeyFailedText = "Can't create the key on this machine. Nothing was done.";
    public const string RecoverySavedText = "Recovery file saved. Keep it and its password safe.";
    public const string RecoveryUnreadableText = "Can't read that recovery file.";

    public const string CalendarLinkInvalidText = "The calendar link must be an https:// or webcal:// address.";
    public const string CalendarLinkNotSavedText =
        "Can't save the calendar link. Check that the Context folder is writable.";
    public const string CalendarLinkNotRemovedText =
        "Can't remove the calendar link. Check that the Context folder is writable.";
    public const string CalendarLinkRemovedText = "Calendar link removed.";
    public const string CalendarLinkNotSetText = "No calendar link set.";

    /// <summary>
    /// The longest calendar link <see cref="SetCalendarLink"/> accepts, in characters as typed. Core has no such
    /// limit; the desktop field has the same <c>maxlength</c>.
    /// </summary>
    public const int MaxCalendarLinkLength = 2048;

    /// <summary>The longest todo text <see cref="AddTodo"/> accepts, as in the console.</summary>
    public const int MaxTodoLength = 200;

    /// <summary>The largest note text <see cref="SaveNote"/> writes (5 MB, as for imports).</summary>
    public const long MaxNoteBytes = NoteStore.MaxEditBytes;

    private readonly NoteStore notes;
    private readonly TodoStore todos;
    private readonly DesktopState state;
    // What CalendarLinkView.Run takes as "remove the link and the cache".
    private const string RemoveArgument = "off";

    private readonly INoteLauncher launcher;
    private readonly CalendarLink? calendarLink;
    private readonly CalendarFeed? calendarFeed;
    private int running;

    public DesktopActions(
        NoteStore notes,
        TodoStore todos,
        DesktopState state,
        INoteLauncher launcher,
        CalendarLink? calendarLink = null,
        CalendarFeed? calendarFeed = null)
    {
        this.calendarLink = calendarLink;
        this.calendarFeed = calendarFeed;
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

    /// <summary>The red line when a sensitive note must be open for the action.</summary>
    public static string UnlockFirstText(string name) => $"Unlock {name} first.";

    /// <summary>The red line when a note can't be sealed.</summary>
    public static string MarkFailedText(string name) => $"Can't mark {name} sensitive. Nothing was done.";

    /// <summary>The red line when a sensitive note can't be made plain.</summary>
    public static string MakePlainFailedText(string name) => $"Can't make {name} plain. Nothing was done.";

    /// <summary>The red line when a note is too large to seal.</summary>
    public static string TooLargeToSealText(string name) => $"{name} is larger than 5 MB. Nothing was done.";

    /// <summary>The red line when a note changed on disk before a change other than a save.</summary>
    public static string ChangedText(string name) => $"{name} changed on disk. Nothing was done.";

    /// <summary>The line after a restore: how many of the sensitive notes this machine can now open.</summary>
    public static string RestoredText(int restored, int total) => $"Restored {restored} of {total} notes.";

    /// <summary>The line after the calendar link was saved; <paramref name="masked"/> is the link without its secret part.</summary>
    public static string CalendarLinkSavedText(string masked) => $"Calendar link saved ({masked}).";

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

    /// <summary>
    /// Makes the plain note <paramref name="name"/> sensitive, only when the user <paramref name="confirmed"/> it:
    /// seals its text for this machine's key and the recovery key (both from the key store, no private-key call),
    /// writes <c>&lt;name&gt;.ctxs</c> through a private temp file and a move that never overwrites, then deletes
    /// the <c>.md</c>. Refused, with nothing written or deleted, for the inbox, without a recovery key, when the
    /// note is gone, changed, not UTF-8 or over <see cref="MaxNoteBytes"/>, and on an OS without a key store.
    /// </summary>
    public Task<ActionResult> MarkSensitive(string name, Confirmation? confirmed) =>
        Run(() => MarkSensitiveNow(name, confirmed));

    /// <summary>
    /// Makes the open sensitive note <paramref name="name"/> plain again, only when the user
    /// <paramref name="confirmed"/> it: writes <c>&lt;name&gt;.md</c> from the open text (never overwriting), then
    /// removes the sealed file through <see cref="NoteStore.DeleteSensitive"/>. No private-key call.
    /// </summary>
    public Task<ActionResult> MakePlain(string name, Confirmation? confirmed) => Run(() => MakePlainNow(name, confirmed));

    /// <summary>
    /// Sets up sensitive notes: creates this machine's key when there is none, makes the recovery key in memory,
    /// writes it to the new file <paramref name="path"/> (outside the data folder, never overwriting, owner-only off
    /// Windows) protected with <paramref name="password"/>, and only then stores the recovery public key. The
    /// password and the private key are in no result.
    /// </summary>
    public Task<ActionResult> SetUpSensitive(string password, string path) => Run(() => SetUpSensitiveNow(password, path));

    /// <summary>
    /// Makes this machine able to open the sensitive notes again from the recovery file <paramref name="path"/>:
    /// reads it (at most <see cref="RecoveryFile.MaxFileBytes"/>; the key stays in memory), creates the machine key
    /// and stores the recovery public key when missing, and adds a wrap for the machine key to every sensitive note
    /// the recovery key unwraps. A wrong password or file changes nothing.
    /// </summary>
    public Task<ActionResult> RestoreFromRecovery(string path, string password) =>
        Run(() => RestoreFromRecoveryNow(path, password));

    /// <summary>
    /// Saves <paramref name="value"/> as the Exchange calendar link through <see cref="CalendarLinkView.Run"/>, which
    /// also deletes the cached calendar copy. The value is a bearer secret: it goes only into
    /// <c>calendar-link.txt</c>, and the result holds only the masked link. A value over
    /// <see cref="MaxCalendarLinkLength"/> characters or one <see cref="CalendarLink.Validate"/> rejects (so also
    /// <c>off</c>) is refused first; then, when a link is already stored or its file is unreadable, replacing it
    /// needs the user's <paramref name="confirmed"/>. Nothing is written in either case.
    /// </summary>
    public Task<ActionResult> SetCalendarLink(string value, Confirmation? confirmed) =>
        Run(() => SetCalendarLinkNow(value, confirmed));

    /// <summary>
    /// Removes the Exchange calendar link and the cached calendar copy through <see cref="CalendarLinkView.Run"/>,
    /// only when the user <paramref name="confirmed"/> it.
    /// </summary>
    public Task<ActionResult> RemoveCalendarLink(Confirmation? confirmed) => Run(() => RemoveCalendarLinkNow(confirmed));

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

    // The checks on the value come first, so a bad value gets the same answer whether or not a link is stored.
    private ActionResult SetCalendarLinkNow(string? value, Confirmation? confirmed)
    {
        if (value is null || value.Length > MaxCalendarLinkLength || CalendarLink.Validate(value) is null)
        {
            return Refused(ActionReason.Invalid, CalendarLinkInvalidText);
        }

        if (calendarLink is null || calendarFeed is null)
        {
            return Failed(ActionReason.WriteFailed, CalendarLinkNotSavedText);
        }

        try
        {
            // Replacing a link (or an unreadable link file) also drops the downloaded copy: only when confirmed.
            if (confirmed is null && CalendarLinkView.Show(calendarLink, calendarFeed).Kind != CalendarLinkViewKind.NotSet)
            {
                return Refused(ActionReason.NotConfirmed, NotConfirmedText);
            }

            CalendarLinkView saved = CalendarLinkView.Run(calendarLink, calendarFeed, value);
            return saved.Kind switch
            {
                CalendarLinkViewKind.Saved =>
                    new ActionResult(ActionKind.Done, ActionReason.None, CalendarLinkSavedText(saved.Masked ?? string.Empty)),
                CalendarLinkViewKind.Invalid => Refused(ActionReason.Invalid, CalendarLinkInvalidText),
                _ => Failed(ActionReason.WriteFailed, CalendarLinkNotSavedText),
            };
        }
        catch (Exception)
        {
            // Never the exception's message: it could hold the link.
            return Failed(ActionReason.WriteFailed, CalendarLinkNotSavedText);
        }
    }

    private ActionResult RemoveCalendarLinkNow(Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        if (calendarLink is null || calendarFeed is null)
        {
            return Failed(ActionReason.WriteFailed, CalendarLinkNotRemovedText);
        }

        try
        {
            CalendarLinkView removed = CalendarLinkView.Run(calendarLink, calendarFeed, RemoveArgument);
            return removed.Kind switch
            {
                CalendarLinkViewKind.Removed => new ActionResult(ActionKind.Done, ActionReason.None, CalendarLinkRemovedText),

                // Nothing was stored (any leftover copy is gone now).
                CalendarLinkViewKind.NotSet => new ActionResult(ActionKind.Done, ActionReason.None, CalendarLinkNotSetText),
                _ => Failed(ActionReason.WriteFailed, CalendarLinkNotRemovedText),
            };
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CalendarLinkNotRemovedText);
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
        if (IsSealedPath(opened.Path))
        {
            try
            {
                return SaveSensitiveNow(opened, text);
            }
            catch (Exception)
            {
                return Failed(ActionReason.WriteFailed, NoteWriteFailedText(opened.Name));
            }
        }

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
                return notes.TryFindSensitive(name, out NoteInfo? sealedSource) && sealedSource is not null
                    ? RenameSensitiveNow(sealedSource, title)
                    : Refused(ActionReason.Missing, NoteMissingText(name));
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
            if (notes.Delete(name))
            {
                return new ActionResult(ActionKind.Done, ActionReason.None, name);
            }

            // A sensitive note is deleted without being opened: no private-key call.
            if (notes.TryFindSensitive(name, out NoteInfo? locked) && locked is not null && notes.DeleteSensitive(locked.Name))
            {
                state.Session.Close(locked.Name);
                return new ActionResult(ActionKind.Done, ActionReason.None, name);
            }

            return Refused(ActionReason.Missing, NoteMissingText(name));
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
                // A sensitive note never leaves the app.
                return notes.TryFindSensitive(name, out _)
                    ? Refused(ActionReason.Invalid, OpenExternallyText(name))
                    : Refused(ActionReason.Missing, NoteMissingText(name));
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

    private static bool IsSealedPath(string path) =>
        string.Equals(Path.GetExtension(path), NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase);

    // A new file only the owner can read and write (mode 0600 off Windows); never an existing one.
    private static void WritePrivate(string path, byte[] bytes)
    {
        FileStreamOptions options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using FileStream stream = new FileStream(path, options);
        stream.Write(bytes);
    }

    private static void Remove(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file holds only what its final file would.
        }
    }

    // True when the file at path still hashes to version (read with the given bound).
    private static bool IsVersion(string path, long maxBytes, string version)
    {
        try
        {
            return string.Equals(EditableNote.Hash(BoundedFile.Read(path, maxBytes)), version, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Writes bytes to target through a private temp file next to it. stillValid runs between the write and the move.
    private static bool WriteThroughTemp(string target, string tempName, byte[] bytes, bool overwrite, Func<bool> stillValid)
    {
        string temp = Path.Combine(Path.GetDirectoryName(target) ?? string.Empty, tempName);
        try
        {
            WritePrivate(temp, bytes);
            if (!stillValid())
            {
                return false;
            }

            File.Move(temp, target, overwrite);
            return true;
        }
        finally
        {
            Remove(temp);
        }
    }

    private static string TempName(string name, string extension) => $".{name}{extension}.{Guid.NewGuid():N}.tmp";

    // The machine key and the recovery key, from the key store only; null when either is missing.
    private SealedRecipient[]? Recipients()
    {
        IKeyStore keys = state.KeyStore;
        if (!keys.Supported)
        {
            return null;
        }

        byte[]? machine = keys.MachinePublicKey();
        byte[]? recovery = keys.RecoveryPublicKey();
        if (machine is null || recovery is null)
        {
            return null;
        }

        return [new SealedRecipient(SealedRecipient.Machine, machine), new SealedRecipient(SealedRecipient.Recovery, recovery)];
    }

    // The session's note when it is the sensitive note found on disk, else null.
    private EditableNote? OpenSensitive(NoteInfo found)
    {
        EditableNote? open = state.Session.Note;
        return open is not null
            && string.Equals(open.Name, found.Name, StringComparison.Ordinal)
            && string.Equals(open.Path, found.Path, StringComparison.Ordinal)
            ? open
            : null;
    }

    private ActionResult MarkSensitiveNow(string name, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        if (!state.KeyStore.Supported)
        {
            return Refused(ActionReason.NoKeys, UnsupportedText);
        }

        try
        {
            if (!notes.TryFind(name, out NoteInfo? source) || source is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (string.Equals(source.Name, Inbox.NoteName, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(ActionReason.Invalid, InboxSensitiveText);
            }

            if (Recipients() is not { } recipients)
            {
                return Refused(ActionReason.NoKeys, NoRecoveryText);
            }

            if (new FileInfo(source.Path).Length > MaxNoteBytes)
            {
                return Refused(ActionReason.Invalid, TooLargeToSealText(source.Name));
            }

            if (notes.TryFindSensitive(source.Name, out _))
            {
                return Refused(ActionReason.Exists, ExistsText(source.Name));
            }

            // The text exactly as the file holds it (a byte order mark stays, as U+FEFF); only UTF-8 is sealed.
            byte[] plain = BoundedFile.Read(source.Path, MaxNoteBytes);
            string version = EditableNote.Hash(plain);
            string text;
            try
            {
                text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(plain);
            }
            catch (ArgumentException)
            {
                return Refused(ActionReason.Invalid, MarkFailedText(source.Name));
            }

            byte[] file = SealedNote.Seal(source.Name, text, recipients);
            string target = Path.Combine(notes.Root, source.Name + NoteStore.SensitiveExtension);
            bool written = WriteThroughTemp(
                target,
                TempName(source.Name, NoteStore.SensitiveExtension),
                file,
                overwrite: false,
                () => IsVersion(source.Path, MaxNoteBytes, version));
            if (!written)
            {
                return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
            }

            try
            {
                if (!IsVersion(source.Path, MaxNoteBytes, version) || !notes.Delete(source.Name))
                {
                    notes.DeleteSensitive(source.Name);
                    return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
                }
            }
            catch (Exception)
            {
                // The plain file could not be removed: take the sealed copy away again, so there is one note.
                notes.DeleteSensitive(source.Name);
                throw;
            }

            return new ActionResult(ActionKind.Done, ActionReason.None, source.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MarkFailedText(name));
        }
    }

    // Re-seals the open sensitive note with a new note key. Needs no private key; never writes plain text.
    private ActionResult SaveSensitiveNow(EditableNote opened, string text)
    {
        string plainText = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.Equals(plainText, opened.Text, StringComparison.Ordinal))
        {
            return new ActionResult(ActionKind.Done, ActionReason.Unchanged, opened.Name, opened);
        }

        if (!notes.TryFindSensitive(opened.Name, out NoteInfo? found)
            || found is null
            || !string.Equals(found.Name, opened.Name, StringComparison.Ordinal))
        {
            return Refused(ActionReason.Missing, MissingText(opened.Name));
        }

        if (OpenSensitive(found) is not { } open || !string.Equals(open.Version, opened.Version, StringComparison.Ordinal))
        {
            return Refused(ActionReason.Locked, UnlockFirstText(opened.Name));
        }

        if (!IsVersion(found.Path, SealedNote.MaxFileBytes, opened.Version))
        {
            return Refused(ActionReason.ChangedOnDisk, ChangedOnDiskText(opened.Name));
        }

        string sealedText = SensitiveSession.ToSealedText(opened, plainText);
        if (System.Text.Encoding.UTF8.GetByteCount(sealedText) > MaxNoteBytes)
        {
            return Refused(ActionReason.Invalid, TooLargeText(opened.Name));
        }

        if (Recipients() is not { } recipients)
        {
            return Refused(ActionReason.NoKeys, NoRecoveryText);
        }

        byte[] file = SealedNote.Seal(found.Name, sealedText, recipients);
        bool written = WriteThroughTemp(
            found.Path,
            TempName(found.Name, NoteStore.SensitiveExtension),
            file,
            overwrite: true,
            () => IsVersion(found.Path, SealedNote.MaxFileBytes, opened.Version));
        if (!written)
        {
            return Refused(ActionReason.ChangedOnDisk, ChangedOnDiskText(opened.Name));
        }

        EditableNote saved = new EditableNote(
            found.Name, found.Path, plainText, EditableNote.Hash(file), opened.HadCrlf, opened.HadBom);
        state.Session.Open(saved);
        return new ActionResult(ActionKind.Done, ActionReason.None, opened.Name, saved);
    }

    // A sealed file is never moved (its name is part of what is sealed): the open text is sealed again under the new
    // name, and only then the old file is removed. Runs inside RenameNoteNow's try.
    private ActionResult RenameSensitiveNow(NoteInfo source, string title)
    {
        if (string.Equals(source.Name, title, StringComparison.Ordinal))
        {
            return new ActionResult(ActionKind.Done, ActionReason.Unchanged, source.Name);
        }

        if (OpenSensitive(source) is not { } open)
        {
            return Refused(ActionReason.Locked, UnlockFirstText(source.Name));
        }

        if (notes.TryFind(title, out _) || notes.TryFindSensitive(title, out _))
        {
            return Refused(ActionReason.Exists, ExistsText(title));
        }

        if (Recipients() is not { } recipients)
        {
            return Refused(ActionReason.NoKeys, NoRecoveryText);
        }

        if (!IsVersion(source.Path, SealedNote.MaxFileBytes, open.Version))
        {
            return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
        }

        byte[] file = SealedNote.Seal(title, SensitiveSession.ToSealedText(open, open.Text), recipients);
        string target = Path.Combine(notes.Root, title + NoteStore.SensitiveExtension);
        try
        {
            WriteThroughTemp(target, TempName(title, NoteStore.SensitiveExtension), file, overwrite: false, () => true);
        }
        catch (IOException) when (File.Exists(target))
        {
            return Refused(ActionReason.Exists, ExistsText(title));
        }

        notes.DeleteSensitive(source.Name);
        state.Session.Open(new EditableNote(title, target, open.Text, EditableNote.Hash(file), open.HadCrlf, open.HadBom));
        return new ActionResult(ActionKind.Done, ActionReason.None, title);
    }

    private ActionResult MakePlainNow(string name, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        try
        {
            if (!notes.TryFindSensitive(name, out NoteInfo? source) || source is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (OpenSensitive(source) is not { } open)
            {
                return Refused(ActionReason.Locked, UnlockFirstText(source.Name));
            }

            if (notes.TryFind(source.Name, out _))
            {
                return Refused(ActionReason.Exists, ExistsText(source.Name));
            }

            if (!IsVersion(source.Path, SealedNote.MaxFileBytes, open.Version))
            {
                return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
            }

            byte[] plain = new System.Text.UTF8Encoding(false).GetBytes(SensitiveSession.ToSealedText(open, open.Text));
            string target = Path.Combine(notes.Root, source.Name + ".md");
            try
            {
                WriteThroughTemp(target, TempName(source.Name, ".md"), plain, overwrite: false, () => true);
            }
            catch (IOException) when (File.Exists(target))
            {
                return Refused(ActionReason.Exists, ExistsText(source.Name));
            }

            try
            {
                notes.DeleteSensitive(source.Name);
            }
            catch (Exception)
            {
                // The sealed file could not be removed: take the plain copy away again, so there is one note.
                Remove(target);
                throw;
            }

            state.Session.Close(source.Name);
            return new ActionResult(ActionKind.Done, ActionReason.None, source.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MakePlainFailedText(name));
        }
    }

    private ActionResult SetUpSensitiveNow(string password, string path)
    {
        IKeyStore keys = state.KeyStore;
        if (!keys.Supported)
        {
            return Refused(ActionReason.NoKeys, UnsupportedText);
        }

        if (RecoveryFile.Normalized(password) is not { } normalized)
        {
            return Refused(ActionReason.Invalid, PasswordUnusableText);
        }

        // From here on the password is its Unicode form C, the form RecoveryFile derives the key from.
        password = normalized;
        if (password.Length < RecoveryFile.MinPasswordLength)
        {
            return Refused(ActionReason.Invalid, PasswordTooShortText);
        }

        string target;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || notes.Contains(path))
            {
                return Refused(ActionReason.Invalid, RecoveryPathText);
            }

            target = Path.GetFullPath(path);
            if (File.Exists(target) || Directory.Exists(target))
            {
                return Refused(ActionReason.Exists, RecoveryFileExistsText);
            }
        }
        catch (Exception)
        {
            return Refused(ActionReason.Invalid, RecoveryPathText);
        }

        try
        {
            if (keys.RecoveryPublicKey() is not null)
            {
                return Refused(ActionReason.Exists, RecoveryExistsText);
            }

            if (keys.MachinePublicKey() is null)
            {
                keys.CreateMachineKey();
            }
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MachineKeyFailedText);
        }

        byte[] publicKey;
        try
        {
            byte[] file = RecoveryFile.Create(password, out publicKey);
            try
            {
                WritePrivate(target, file);
            }
            catch (IOException) when (File.Exists(target))
            {
                // Someone else made that file in between; it is not ours to remove.
                return Refused(ActionReason.Exists, RecoveryFileExistsText);
            }
        }
        catch (Exception)
        {
            Remove(target);
            return Failed(ActionReason.WriteFailed, RecoveryWriteFailedText);
        }

        try
        {
            keys.StoreRecoveryPublicKey(publicKey);
        }
        catch (Exception)
        {
            // Without the stored public key the file protects nothing; it must not look like a working recovery file.
            Remove(target);
            return Failed(ActionReason.WriteFailed, MachineKeyFailedText);
        }

        return new ActionResult(ActionKind.Done, ActionReason.None, RecoverySavedText);
    }

    private ActionResult RestoreFromRecoveryNow(string path, string password)
    {
        IKeyStore keys = state.KeyStore;
        if (!keys.Supported)
        {
            return Refused(ActionReason.NoKeys, UnsupportedText);
        }

        System.Security.Cryptography.ECDiffieHellman recoveryKey;
        try
        {
            recoveryKey = RecoveryFile.Read(BoundedFile.Read(path, RecoveryFile.MaxFileBytes), password);
        }
        catch (Exception)
        {
            return Failed(ActionReason.Unreadable, RecoveryUnreadableText);
        }

        using (recoveryKey)
        {
            byte[] recoveryPublic = recoveryKey.ExportSubjectPublicKeyInfo();
            byte[] machine;
            try
            {
                if (keys.MachinePublicKey() is null)
                {
                    keys.CreateMachineKey();
                }

                machine = keys.MachinePublicKey() ?? throw new KeyStoreException();
                if (keys.RecoveryPublicKey() is null)
                {
                    keys.StoreRecoveryPublicKey(recoveryPublic);
                }
            }
            catch (Exception)
            {
                return Failed(ActionReason.WriteFailed, MachineKeyFailedText);
            }

            SealedRecipient added = new SealedRecipient(SealedRecipient.Machine, machine);
            IReadOnlyList<NoteInfo> sealedNotes;
            try
            {
                sealedNotes = notes.ListSensitive();
            }
            catch (Exception)
            {
                return Failed(ActionReason.Unreadable, FailedText);
            }

            int restored = sealedNotes.Count(n => Restore(n, recoveryKey, recoveryPublic, added));
            return new ActionResult(ActionKind.Done, ActionReason.None, RestoredText(restored, sealedNotes.Count));
        }
    }

    // True when the machine key can open the note afterwards: it already could, or its wrap was added with the
    // recovery key (which stays in memory). A note the recovery key can't unwrap is left as it is.
    private static bool Restore(
        NoteInfo note, System.Security.Cryptography.ECDiffieHellman recoveryKey, byte[] recoveryPublic, SealedRecipient added)
    {
        try
        {
            byte[] file = BoundedFile.Read(note.Path, SealedNote.MaxFileBytes);
            if (SealedNote.HasKey(file, added.PublicKey))
            {
                return true;
            }

            string version = EditableNote.Hash(file);
            byte[] rewrapped = SealedNote.AddRecipient(
                note.Name, file, recoveryPublic, peer => SealedNote.Agree(recoveryKey, peer), added);
            return WriteThroughTemp(
                note.Path,
                TempName(note.Name, NoteStore.SensitiveExtension),
                rewrapped,
                overwrite: true,
                () => IsVersion(note.Path, SealedNote.MaxFileBytes, version));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
