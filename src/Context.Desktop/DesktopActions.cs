using System.Globalization;

namespace Context;

/// <summary>How a <see cref="DesktopActions"/> call ended.</summary>
internal enum ActionKind
{
    /// <summary>The change was made.</summary>
    Done,

    /// <summary>A check said no; nothing was touched.</summary>
    Refused,

    /// <summary>
    /// The change could not be made; nothing was changed, except after <see cref="ActionReason.LineKept"/>: a triage
    /// action saved its target, but the inbox line could not be removed.
    /// </summary>
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

    /// <summary>The name is an alias of more than one person, so it links to no one.</summary>
    Ambiguous,

    /// <summary>The note was marked sensitive, but its old plain versions could not be removed.</summary>
    PlainHistoryLeft,

    /// <summary>A triage action saved its target, but the line is still in the inbox (the target is kept).</summary>
    LineKept,

    /// <summary>A note was renamed, but the links in some other notes could not be updated.</summary>
    LinksLeft,
}

/// <summary>
/// The result of a desktop action: a kind, a reason and raw text (the todo's text or the note's name for
/// <see cref="ActionKind.Done"/>, else a fixed message, which may name the note; never an exception message, never
/// cleaned or encoded). A saved note comes back in <see cref="Note"/>, the due date of the todo a recurring todo's
/// completion added in <see cref="NextDue"/>.
/// </summary>
internal sealed record ActionResult
{
    public ActionResult(
        ActionKind kind,
        ActionReason reason,
        string text,
        EditableNote? note = null,
        DateOnly? nextDue = null,
        IReadOnlyList<AttachmentLine>? attached = null,
        string? name = null)
    {
        Name = name;
        Kind = kind;
        Reason = reason;
        Text = text;
        Note = note;
        NextDue = nextDue;
        Attached = attached;
    }

    /// <summary>For <see cref="DesktopActions.RenameNote"/> when done: the note's new full name; else <c>null</c>.</summary>
    public string? Name { get; }

    /// <summary>For <see cref="DesktopActions.AttachFiles"/>: one line per dropped file, in drop order; else <c>null</c>.</summary>
    public IReadOnlyList<AttachmentLine>? Attached { get; }

    public ActionKind Kind { get; }

    public ActionReason Reason { get; }

    public string Text { get; }

    /// <summary>The note as saved (or as opened, when nothing changed); <c>null</c> for other actions.</summary>
    public EditableNote? Note { get; }

    /// <summary>When a recurring todo was marked done: the due date of the new todo; <c>null</c> otherwise.</summary>
    public DateOnly? NextDue { get; }

    /// <summary>Only the type name, so the raw text never leaks through formatting.</summary>
    public override string ToString() => nameof(ActionResult);
}

/// <summary>
/// How one dropped file ended: <see cref="ActionKind.Done"/> with the stored attachment's link source, name, type and
/// (for an image) base64 bytes, else the fixed refusal. <see cref="Text"/> is raw (it may hold the dropped name).
/// </summary>
internal sealed record AttachmentLine
{
    public AttachmentLine(ActionKind kind, string text, string? src = null, string? name = null, bool isImage = false, string? mime = null, string? base64 = null)
    {
        Kind = kind;
        Text = text;
        Src = src;
        Name = name;
        IsImage = isImage;
        Mime = mime;
        Base64 = base64;
    }

    public ActionKind Kind { get; }

    public string Text { get; }

    public string? Src { get; }

    public string? Name { get; }

    public bool IsImage { get; }

    public string? Mime { get; }

    public string? Base64 { get; }

    /// <summary>Only the type name, so the raw text and bytes never leak through formatting.</summary>
    public override string ToString() => nameof(AttachmentLine);
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
    public const string ChildSensitiveText = "Only top-level notes can be sensitive.";
    public const string ParentSensitiveText = "A note with subpages can't be sensitive.";
    public const string NameTooLongText = "A note's full name can be at most 400 characters.";
    public const string MoveUnderItselfText = "A note can't move under itself or one of its subpages. Nothing was moved.";

    public const string CapturedText = "Captured.";
    public const string CaptureEmptyText = "Type something to capture.";
    public const string CaptureSensitiveText = Inbox.NoteName + " is sensitive. Nothing was captured.";
    public const string CaptureTooLargeText = Inbox.NoteName + " is too large. Nothing was captured.";
    public const string CaptureChangedText = Inbox.NoteName + " kept changing on disk. Nothing was captured.";
    public const string CaptureFailedText = "Can't write " + Inbox.NoteName + ". Nothing was captured.";

    public const string TriageSensitiveText = Inbox.NoteName + " is sensitive. Nothing to triage.";
    public const string TriageLineGoneText = "That line is no longer in the inbox. Nothing was done.";
    public const string TriageInboxTargetText = "Pick a note other than " + Inbox.NoteName + ".";
    public const string TriageDroppedText = "Dropped.";
    public const string TriagePastDayText = "Pick a day after today.";
    public const string TriageDateText = "Type a date: yyyy-MM-dd, today or tomorrow.";
    public const string TriageChangedText = Inbox.NoteName + " kept changing on disk. Nothing was done.";
    public const string TriageTooLargeText = Inbox.NoteName + " is too large. Nothing was done.";
    public const string TriageFailedText = "Can't write " + Inbox.NoteName + ". Nothing was done.";
    public const string TriageUnreadableText = "Can't read " + Inbox.NoteName + ". Nothing was done.";

    /// <summary>The longest title a triage note's title field is pre-filled with.</summary>
    public const int TriageTitleLength = 100;

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

    public const string AttachSensitiveText = "Attachments can't be added to a sensitive note.";
    public const string TooManyFilesText = "Drop at most 10 files.";

    public const string ImportInsideText = "That file is already in the Context folder. Nothing changed.";
    public const string ExportInsideText = "Export to a path outside the Context folder. Nothing was written.";

    public const string CalendarLinkInvalidText = "The calendar link must be an https:// or webcal:// address.";
    public const string CalendarLinkNotSavedText =
        "Can't save the calendar link. Check that the Context folder is writable.";
    public const string CalendarLinkNotRemovedText =
        "Can't remove the calendar link. Check that the Context folder is writable.";
    public const string CalendarLinkRemovedText = "Calendar link removed.";
    public const string CalendarLinkNotSetText = "No calendar link set.";

    public const string SearchNameBlankText = "Type a name for the search.";
    public const string SearchNameInvalidText =
        "A search name can be at most 50 characters, without control characters.";
    public const string SearchQueryInvalidText = "Can't save that search.";
    public const string SearchesFullText = "Can't save more than 50 searches.";
    public const string SearchesUnreadableText = "Can't read settings.json. Nothing was saved.";
    public const string SearchesWriteFailedText = "Can't write settings.json. Nothing was saved.";
    public const string SearchGoneText = "That saved search is gone. Nothing was done.";
    public const string SearchesNotRemovedText = "Can't read settings.json. Nothing was removed.";
    public const string SearchesRemoveFailedText = "Can't write settings.json. Nothing was removed.";

    /// <summary>
    /// The longest calendar link <see cref="SetCalendarLink"/> accepts, in characters as typed. Core has no such
    /// limit; the desktop field has the same <c>maxlength</c>.
    /// </summary>
    public const int MaxCalendarLinkLength = 2048;

    /// <summary>The longest todo text <see cref="AddTodo"/> accepts, as in the console.</summary>
    public const int MaxTodoLength = 200;

    /// <summary>How old a note's newest version must be before an automatic save keeps another one (5 minutes).</summary>
    public static readonly TimeSpan AutoVersionGap = TimeSpan.FromMinutes(5);

    /// <summary>The largest note text <see cref="SaveNote"/> writes (5 MB, as for imports).</summary>
    public const long MaxNoteBytes = NoteStore.MaxEditBytes;

    // What CalendarLinkView.Run takes as "remove the link and the cache".
    private const string RemoveArgument = "off";

    private readonly NoteStore notes;
    private readonly NoteAttachments attachments;
    private readonly TodoStore todos;
    private readonly DesktopState state;
    private readonly INoteLauncher launcher;
    private readonly CalendarLink? calendarLink;
    private readonly CalendarFeed? calendarFeed;
    private readonly Func<DateTime> localNow;
    private readonly CalendarStore calendar;
    private readonly DayEnd dayEnd;
    private readonly SettingsStore settings;
    private readonly string? timesFolder;
    private readonly INotifier? notifier;
    private int running;

    // Plain fields: only AutoBriefingNow touches them, and Run lets one action run at a time.
    // Set by the first AutoBriefing that runs (the start-up call); later calls are ticks.
    private bool briefingStarted;

    // The day whose briefing AutoBriefing created or found, so later ticks that day read nothing.
    private DateOnly? briefedDay;

    public DesktopActions(
        NoteStore notes,
        TodoStore todos,
        DesktopState state,
        INoteLauncher launcher,
        CalendarLink? calendarLink = null,
        CalendarFeed? calendarFeed = null,
        Func<DateTime>? localNow = null,
        CalendarStore? calendar = null,
        string? timesFolder = null,
        INotifier? notifier = null)
    {
        this.notifier = notifier;
        this.localNow = localNow ?? (() => DateTime.Now);
        this.calendar = calendar ?? new CalendarStore(notes.Root, TimeZoneInfo.Local);
        settings = new SettingsStore(notes.Root);
        dayEnd = new DayEnd(notes, todos, this.calendar, calendarFeed, timesFolder, settings);
        this.timesFolder = timesFolder;
        this.calendarLink = calendarLink;
        this.calendarFeed = calendarFeed;
        this.notes = notes;
        attachments = new NoteAttachments(notes);
        this.todos = todos;
        this.state = state;
        this.launcher = launcher;
    }

    /// <summary>The attachments store, for tests (its <see cref="NoteAttachments.BeforeMoveStep"/> hook).</summary>
    internal NoteAttachments Attachments => attachments;

    /// <summary>How often <c>Program.cs</c>'s day-end timer calls <see cref="AutoDayEnd"/>.</summary>
    public static readonly TimeSpan DayEndInterval = TimeSpan.FromSeconds(30);

    /// <summary>How often <c>Program.cs</c>'s reminder timer calls <see cref="AutoReminders"/>.</summary>
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(30);

    /// <summary>The title of every reminder notification.</summary>
    public const string ReminderTitle = "Context";

    /// <summary>The most reminders <see cref="AutoReminders"/> shows one by one; more are one <see cref="RemindersDueText"/>.</summary>
    public const int MaxSingleReminders = 3;

    /// <summary>The body of a reminder: the todo's time and its one-line text.</summary>
    public static string ReminderText(TodoItem todo) =>
        $"{(todo.Time ?? TimeOnly.MinValue).ToString(TodoStore.StoredTimeFormat, CultureInfo.InvariantCulture)} {DesktopText.OneLine(todo.Text)}";

    /// <summary>The one notification's body when more than <see cref="MaxSingleReminders"/> todos are due at once.</summary>
    public static string RemindersDueText(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} todos are due.";

    /// <summary>The grey line after a successful <see cref="EndDay"/>.</summary>
    public static string DayClosedText(int moved, DateOnly to) =>
        $"Day closed. Moved {moved} todo(s) to {DailyNote.NameFor(to)}.";

    /// <summary>The red line when a daily note the day's end needs is sensitive.</summary>
    public static string DayEndSensitiveText(string name) => $"{name} is sensitive. Nothing was written to it.";

    /// <summary>The red line when <c>todos.json</c> can't be read for the day's end.</summary>
    public const string DayEndTodosUnreadableText = "Can't read todos.json. The worked hours were written; no todos moved.";

    /// <summary>The red line when a file of the day's end can't be written.</summary>
    public static string DayEndWriteFailedText(string name) => $"Can't write {name}.";

    /// <summary>The answer of <see cref="AutoDayEnd"/> when <c>settings.json</c> can't be read or written.</summary>
    public const string SettingsUnreadableText = "Can't read settings.json. The day was not closed automatically.";

    /// <summary>The red line when the top-level <c>briefings</c> note is sensitive.</summary>
    public const string BriefingSensitiveText = Briefing.FolderName + " is sensitive, so no briefing can be written.";

    /// <summary>The red line when a briefing can't be written.</summary>
    public static string BriefingFailedText(string name) => $"Can't write {name}.";

    /// <summary>The red line when a note with that name exists (ignoring case).</summary>
    public static string ExistsText(string name) => $"Note {name} already exists.";

    /// <summary>The red line when the note to rename, delete or open is gone.</summary>
    public static string NoteMissingText(string name) => $"{name} no longer exists. Nothing was done.";

    /// <summary>The red line when a rename fails.</summary>
    public static string RenameFailedText(string name) => $"Can't rename {name}. Nothing was done.";

    /// <summary>The red line when the parent typed for a move names no note.</summary>
    public static string NoParentText(string parent) => $"No note called {parent}. Nothing was moved.";

    /// <summary>The red line when a person's page can't be opened or made (invalid name, sensitive people note).</summary>
    public static string PersonRefusedText(string name) => $"Can't make a page for {name}. Nothing was done.";

    /// <summary>The red line when a note can't go under that parent (or can't take a subpage).</summary>
    public static string NoSubpagesText(string parent) => $"{parent} can't have subpages. Nothing was done.";

    /// <summary>The red line when a note may not go under that parent (or to the top level, for an empty one).</summary>
    public static string CantMoveText(string name, string parent) =>
        parent.Length == 0 ? $"Can't move {name} to the top level. Nothing was moved." : $"Can't move {name} under {parent}. Nothing was moved.";

    /// <summary>The red line when a move fails.</summary>
    public static string MoveFailedText(string name) => $"Can't move {name}. Nothing was moved.";

    /// <summary>The red line when the attachments of a moved note couldn't be carried along (everything was undone).</summary>
    public static string AttachmentsMoveFailedText(string name) => $"Can't move the attachments of {name}. Nothing was moved.";

    /// <summary>The accent line for a stored attachment.</summary>
    public static string AttachedText(string name) => $"Attached {name}.";

    /// <summary>The red line for a dropped file over 10 MB.</summary>
    public static string AttachTooLargeText(string name) => $"{name} is over 10 MB. Nothing was attached.";

    /// <summary>The red line for a dropped file of another type.</summary>
    public static string AttachUnsupportedText(string name) =>
        $"{name} isn't a PNG, JPEG, GIF, WebP or PDF file. Nothing was attached.";

    /// <summary>The red line when a dropped file couldn't be stored.</summary>
    public static string AttachFailedText(string name) => $"Can't attach {name}.";

    /// <summary>The red line when an attachment link can't be opened.</summary>
    public static string OpenAttachmentText(string name) => $"Can't open {name}.";

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

    /// <summary>The red line when the old version of a note can't be kept, so the save writes nothing.</summary>
    public static string KeepFailedText(string name) => $"Can't keep the old version of {name}. Nothing was saved.";

    /// <summary>The red line when a kept version can't be read for a restore.</summary>
    public static string VersionUnreadableText => "Can't read this version.";

    /// <summary>The red line when the history of a note can't be sealed while marking it sensitive.</summary>
    public static string SealHistoryFailedText(string name) => $"Can't seal the history of {name}. Nothing was changed.";

    /// <summary>The red line when a note was marked sensitive but its old plain versions are still on disk.</summary>
    public static string PlainHistoryLeftText(string name) =>
        $"Marked {name} sensitive, but its old readable versions couldn't be removed. Delete .history/{name} in the data folder.";

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

    /// <summary>The line after an import; <paramref name="replaced"/> when it took an existing note's place.</summary>
    public static string ImportedText(string name, string path, bool replaced) =>
        $"{(replaced ? "Replaced" : "Imported")} {name}. The source is unchanged; delete it if you don't need the plain copy: {path}";

    /// <summary>The red line when the file's name is no valid title; <paramref name="reason"/> is the <see cref="NoteStore.ValidateTitle"/> text.</summary>
    public static string ImportTitleText(string reason) => $"{reason} Rename the file and import it again.";

    /// <summary>The red line when the file to import is missing (or is a folder).</summary>
    public static string NoSuchFileText(string path) => $"No such file: {path}";

    /// <summary>The red line when the file to import is over <see cref="NoteStore.MaxImportBytes"/>.</summary>
    public static string ImportTooLargeText(string path) => $"Can't import a file over 5 MB: {path}";

    /// <summary>The red line when a file or a note can't be read.</summary>
    public static string CantReadText(string pathOrName) => $"Can't read {pathOrName}.";

    /// <summary>The red line when the file's title names a sensitive note.</summary>
    public static string ImportSensitiveText(string name) => $"{name} is sensitive. Nothing was imported.";

    /// <summary>The red line when the imported note can't be stored.</summary>
    public static string ImportSaveFailedText(string name) => $"Can't save {name}. Nothing changed.";

    /// <summary>The line after an export.</summary>
    public static string ExportedText(string name, string path) => $"Exported {name} to {path}";

    /// <summary>The red line when the export target is already there.</summary>
    public static string ExportExistsText(string path) => $"That file already exists; choose a new name: {path}";

    /// <summary>The red line when a sensitive note is asked to be exported.</summary>
    public static string ExportSensitiveText(string name) => $"{name} is sensitive. Make it plain to export it.";

    /// <summary>The red line when the export target can't be written.</summary>
    public static string CantWriteText(string path) => $"Can't write {path}.";

    /// <summary>The red line when no template has the typed name; lists the templates there are, if any.</summary>
    public static string TemplateMissingText(string name, IReadOnlyList<string> templates) =>
        templates.Count == 0
            ? $"No template called {name}. Add one as {NoteTemplates.FolderName}/{name}.md."
            : $"No template called {name}. Templates: {string.Join(", ", templates)}.";

    /// <summary>The red line when the template is too large or can't be read.</summary>
    public static string TemplateUnreadableText(string name) => $"Can't read template {name}.";

    /// <summary>The red line when the filled template would be over <see cref="NoteStore.MaxEditBytes"/>.</summary>
    public static string TemplateTooBigText(string name) => $"Template {name} is too big.";

    /// <summary>The red line when the top-level <c>templates</c> note is sensitive.</summary>
    public const string TemplatesSensitiveText =
        NoteTemplates.FolderName + " is sensitive, so its templates can't be used.";

    /// <summary>The red line when the daily note can't be created.</summary>
    public static string DailyNoteFailedText(string name) => $"Can't write {name}. Nothing was done.";

    /// <summary>The red line when a todo's text is too long.</summary>
    public static string TodoTooLongText => $"A todo can be at most {MaxTodoLength} characters. Nothing was done.";

    /// <summary>The accent line after a triage line became the note <paramref name="name"/>.</summary>
    public static string TriageCreatedText(string name) => $"Created {name}.";

    /// <summary>The accent line after a triage line became a todo.</summary>
    public static string TriageTodoText(string text) => $"Added: {text}";

    /// <summary>The accent line after a triage line was added to the note <paramref name="name"/>.</summary>
    public static string TriageAddedText(string name) => $"Added to {name}.";

    /// <summary>The accent line after a triage line was snoozed.</summary>
    public static string TriageSnoozedText(DateOnly until) =>
        $"Snoozed until {until.ToString(DesktopText.DateFormat, CultureInfo.InvariantCulture)}.";

    /// <summary>The red line when the typed note for "Add to note" names no note.</summary>
    public static string NoNoteNamedText(string name) => $"No note named {name}.";

    /// <summary>The red line when a sensitive note can't be unlocked.</summary>
    public static string CantUnlockText(string name) => $"Can't unlock {name}.";

    /// <summary>The red line when a triage target was saved, but the line could not be removed from the inbox.</summary>
    public static string TriageKeptText(string target) => $"Saved to {target}, but the line is still in the inbox.";

    /// <summary>The red line when a capture's text is too long.</summary>
    public static string CaptureTooLongText => $"A capture can be at most {Inbox.MaxTextLength} characters.";

    /// <summary>The Done text of a rename that changed no other note.</summary>
    public static string RenamedText(string name) => $"Renamed to {name}.";

    /// <summary>The Done text of a rename that updated the links in <paramref name="count"/> notes.</summary>
    public static string RenamedLinksText(string name, int count) =>
        $"Renamed to {name}. Updated links in {NotesText(count)}.";

    /// <summary>
    /// The Done/<see cref="ActionReason.LinksLeft"/> text: the note was renamed, but the links in
    /// <paramref name="left"/> couldn't be updated (at most 3 names, then <c>and &lt;m&gt; more</c>).
    /// </summary>
    public static string LinksLeftText(string name, IReadOnlyList<string> left)
    {
        string shown = string.Join(", ", left.Take(MaxLinksLeftNames));
        string more = left.Count > MaxLinksLeftNames
            ? $" and {(left.Count - MaxLinksLeftNames).ToString(CultureInfo.InvariantCulture)} more"
            : string.Empty;
        return $"Renamed to {name}, but links in {NotesText(left.Count)} couldn't be updated: {shown}{more}.";
    }

    /// <summary>The Refused/<see cref="ActionReason.Invalid"/> text when a rewritten link would not read back.</summary>
    public static string LinksInvalidText(string oldName, string newName) =>
        $"Links to {oldName} can't be written with the name {newName}. Nothing was renamed.";

    /// <summary>The rename question when links in <paramref name="count"/> notes will change.</summary>
    public static string RenameQuestion(string oldName, string newName, int count) =>
        $"Rename {DesktopText.OneLine(oldName)} to {DesktopText.OneLine(newName)}? Links in {NotesText(count)} will be updated.";

    /// <summary><c>1 note</c> or <c>&lt;n&gt; notes</c>.</summary>
    public static string NotesText(int count) =>
        count == 1 ? "1 note" : $"{count.ToString(CultureInfo.InvariantCulture)} notes";

    /// <summary>The most note names <see cref="LinksLeftText"/> lists.</summary>
    public const int MaxLinksLeftNames = 3;

    /// <summary>How often a link update reads a note again when it changed on disk in between (tries in total).</summary>
    public const int LinkUpdateTries = 3;

    /// <summary>
    /// Test hook: runs before every save attempt of a link update in <see cref="RenameNote"/>, with the note's name
    /// (after the rename), after the note was read.
    /// </summary>
    internal Action<string>? BeforeLinkSave { get; set; }

    /// <summary>Test hook: runs on the worker thread right before an action's checks and write.</summary>
    internal Action? BeforeWrite { get; set; }

    /// <summary>Test hook: runs before every save attempt of <see cref="CaptureText"/>, after the inbox was read.</summary>
    internal Action? BeforeCaptureSave { get; set; }

    /// <summary>
    /// Test hook: runs before every save attempt of the inbox in a triage action (removing or snoozing the line),
    /// after the inbox was read.
    /// </summary>
    internal Action? BeforeTriageRemove { get; set; }

    /// <summary>Test hook: runs in <see cref="MarkSensitive"/> after the plain versions were sealed, before the note is.</summary>
    internal Action? AfterHistorySealed { get; set; }

    /// <summary>
    /// Appends <paramref name="text"/> as one dated line (<c>- yyyy-MM-dd HH:mm &lt;text&gt;</c>, local time) to the
    /// <c>inbox</c> note through <see cref="Inbox.Capture(NoteStore, string, DateTime)"/>, creating the note when it
    /// is missing. The text is cleaned to one line first; empty or over <see cref="Inbox.MaxTextLength"/> characters
    /// is refused. Nothing is written either when the inbox is a sensitive note (no key-store call), would grow past
    /// <see cref="MaxNoteBytes"/>, keeps changing on disk or can't be read or written. The result's text is
    /// <see cref="CapturedText"/> when done.
    /// </summary>
    public Task<ActionResult> CaptureText(string text) => Run(() => CaptureTextNow(text));

    /// <summary>
    /// Triage: creates the note <paramref name="title"/> as <c># &lt;Title&gt;\n\n&lt;text&gt;\n</c> through
    /// <see cref="NoteStore.CreateWithText"/>, then removes <paramref name="item"/>'s line from the inbox. Before the
    /// note is written the inbox must still hold the line (else <see cref="TriageLineGoneText"/>, nothing written). A bad
    /// or taken title gives the <see cref="CreateNote"/> texts and leaves the inbox as it is. When the note was created
    /// but the line can't be removed the result is <see cref="ActionKind.Failed"/>/<see cref="ActionReason.LineKept"/>.
    /// </summary>
    public Task<ActionResult> TriageToNote(InboxItem item, string title) => Run(() => TriageToNoteNow(item, title));

    /// <summary>
    /// Triage: adds the todo <paramref name="text"/> due <paramref name="due"/> (the <see cref="AddTodo"/> rules and
    /// texts), then removes the line, with the checks and results of <see cref="TriageToNote"/>.
    /// </summary>
    public Task<ActionResult> TriageToTodo(InboxItem item, string text, DateOnly due) =>
        Run(() => TriageToTodoNow(item, text, due));

    /// <summary>
    /// Triage: appends <c>- &lt;text&gt;\n</c> (after a missing final newline) to the note <paramref name="name"/>,
    /// then removes the line, with the checks and results of <see cref="TriageToNote"/>. A plain note is written through
    /// <see cref="NoteStore.ReadForEdit"/> and <see cref="NoteStore.SaveIfUnchanged"/> (read again on a change on disk);
    /// a sensitive one only when <paramref name="unlocked"/> is that note as the <see cref="DesktopState.Session"/> holds
    /// it, re-sealed through the <see cref="SaveNote"/> sensitive path (no plain bytes are written). The inbox itself is
    /// refused with <see cref="TriageInboxTargetText"/>.
    /// </summary>
    public Task<ActionResult> TriageAppend(InboxItem item, string name, EditableNote? unlocked) =>
        Run(() => TriageAppendNow(item, name, unlocked));

    /// <summary>
    /// Triage: removes the first inbox line equal to <paramref name="item"/>'s line, only when the user
    /// <paramref name="confirmed"/> it (else <see cref="ActionReason.NotConfirmed"/>, nothing written).
    /// </summary>
    public Task<ActionResult> TriageDrop(InboxItem item, Confirmation? confirmed) =>
        Run(() => TriageDropNow(item, confirmed));

    /// <summary>
    /// Triage: rewrites <paramref name="item"/>'s line in place with the token <c>snooze:yyyy-MM-dd</c> for
    /// <paramref name="until"/>, which must be after today (the injected local clock; else
    /// <see cref="TriagePastDayText"/>, nothing written).
    /// </summary>
    public Task<ActionResult> TriageSnooze(InboxItem item, DateOnly until) => Run(() => TriageSnoozeNow(item, until));

    /// <summary>
    /// Marks <paramref name="todo"/> done, which removes the stored todo with the same id through
    /// <see cref="TodoStore.Complete(Guid, DateOnly, out TodoItem?)"/> in one read and write, but only when the user
    /// <paramref name="confirmed"/> it. A recurring todo is added again in the same write; its due date comes back in
    /// <see cref="ActionResult.NextDue"/>. A todo that is no longer stored is refused as <see cref="ActionReason.Changed"/>.
    /// </summary>
    public Task<ActionResult> CompleteTodo(TodoItem todo, Confirmation? confirmed) =>
        Run(() => CompleteTodoNow(todo, confirmed));

    /// <summary>
    /// Ticks the todo inside a note, only when the user <paramref name="confirmed"/> it, through
    /// <see cref="NoteTodos.Complete(NoteStore, NoteTodo, NoteHistory?)"/>: only when the note's line is still the
    /// todo's line as indexed, its first <c>[ ]</c> becomes <c>[x]</c> (every other byte kept, the previous bytes kept
    /// as a version). A changed line is refused as <see cref="ActionReason.Changed"/>, a gone note as
    /// <see cref="ActionReason.Missing"/>; nothing is written then. The result's text is the todo's text when done.
    /// </summary>
    public Task<ActionResult> CompleteNoteTodo(NoteTodo todo, Confirmation? confirmed) =>
        Run(() => CompleteNoteTodoNow(todo, confirmed));

    /// <summary>
    /// Saves <paramref name="text"/> over the note <paramref name="opened"/> came from through
    /// <see cref="NoteStore.SaveIfUnchanged"/>, keeping its line endings and BOM. Writes nothing when the text is
    /// unchanged (<see cref="ActionReason.Unchanged"/>, done, no refresh), when the file changed on disk or is gone,
    /// or when the text is over <see cref="MaxNoteBytes"/>. The result's text is the note's name when done. An
    /// <paramref name="auto"/> save (the editor's pause after typing) keeps the previous bytes as a version only when
    /// the note has none or its newest is at least <see cref="AutoVersionGap"/> old on the history clock; every other
    /// check is the same.
    /// </summary>
    public Task<ActionResult> SaveNote(EditableNote opened, string text, bool auto = false) =>
        Run(() => SaveNoteNow(opened, text, auto));

    /// <summary>
    /// Restores the note <paramref name="name"/> to its kept version <paramref name="id"/>, only when the user
    /// <paramref name="confirmed"/> it: reads the version (<see cref="DesktopState.ReadVersionText"/>, one
    /// <see cref="IKeyStore.Agree"/> for a sealed one) and saves its text over <paramref name="opened"/> through the
    /// same path as <see cref="SaveNote"/>, so the current text is kept as a version first. A version that can't be
    /// read is <see cref="VersionUnreadableText"/>; without a confirmation nothing is read or written.
    /// </summary>
    public Task<ActionResult> RestoreVersion(string name, string id, EditableNote opened, Confirmation? confirmed) =>
        Run(() => RestoreVersionNow(name, id, opened, confirmed));

    /// <summary>
    /// Adds a todo due <paramref name="due"/> (recurring when <paramref name="repeat"/> is set) through
    /// <see cref="TodoStore.Add"/>. Blank text is refused with
    /// <see cref="EmptyTodoText"/> and text over <see cref="MaxTodoLength"/> characters is refused; both
    /// write nothing. The result's text is the todo's text when done.
    /// </summary>
    public Task<ActionResult> AddTodo(string text, DateOnly due, TodoRepeat? repeat = null, TimeOnly? time = null) =>
        Run(() => AddTodoNow(text, due, repeat, time));

    /// <summary>
    /// Creates the note <paramref name="title"/> through <see cref="NoteStore.Create"/>. An invalid title is refused
    /// with the <see cref="NoteStore.ValidateTitle"/> text, an existing note with <see cref="ExistsText"/>. The result's
    /// text is the new note's name when done.
    /// </summary>
    public Task<ActionResult> CreateNote(string title) => Run(() => CreateNoteNow(title));

    /// <summary>
    /// Creates the top-level note <paramref name="title"/> from the template <paramref name="template"/>
    /// (<see cref="NoteTemplates.Prepare"/>, filled from the injected local clock) through
    /// <see cref="NoteStore.CreateWithText"/>. The <see cref="CreateNote"/> texts for a bad or taken title (the template
    /// is then not read), else <see cref="TemplateMissingText"/>, <see cref="TemplateUnreadableText"/>,
    /// <see cref="TemplateTooBigText"/> or <see cref="TemplatesSensitiveText"/>; none of them writes anything and none
    /// calls the key store. The result's text is the new note's name when done.
    /// </summary>
    public Task<ActionResult> CreateNoteFromTemplate(string title, string template) =>
        Run(() => CreateNoteFromTemplateNow(title, template));

    /// <summary>
    /// Renames the note <paramref name="name"/> to <paramref name="newTitle"/> through <see cref="NoteStore.Rename"/>
    /// (the file is moved, its bytes are kept) and then rewrites the <c>[[links]]</c> to it and its subpages in the
    /// plain notes (<see cref="LinkRenamePlan"/>, <see cref="NoteLinkRewriter"/>). The same name gives
    /// <see cref="ActionReason.Unchanged"/> (done, nothing written, no refresh). When a rewritten link would not read
    /// back, Refused/<see cref="ActionReason.Invalid"/> <see cref="LinksInvalidText"/>; when links in other notes would
    /// change and <paramref name="confirmed"/> is null, Refused/<see cref="ActionReason.NotConfirmed"/>; both write
    /// nothing. Otherwise the note is renamed first (a refused or failed rename touches no other note), then every
    /// affected note is read, rewritten and saved through <see cref="NoteStore.SaveIfUnchanged"/> with the history (its
    /// previous bytes kept as a version), up to <see cref="LinkUpdateTries"/> tries when it changed on disk. Done
    /// texts: <see cref="RenamedText"/>, <see cref="RenamedLinksText"/>, or <see cref="ActionReason.LinksLeft"/> with
    /// <see cref="LinksLeftText"/>. The result's <see cref="ActionResult.Name"/> is the new full name when done.
    /// </summary>
    public Task<ActionResult> RenameNote(string name, string newTitle, Confirmation? confirmed) =>
        Run(() => RenameNoteNow(name, newTitle, confirmed));

    /// <summary>
    /// Creates the subpage <paramref name="title"/> of the note <paramref name="parent"/> through
    /// <see cref="NoteStore.CreateChild"/>, with the <see cref="CreateNote"/> texts. The result's text is the new
    /// note's full name when done.
    /// </summary>
    public Task<ActionResult> CreateSubpage(string parent, string title) => Run(() => CreateSubpageNow(parent, title));

    /// <summary>
    /// Opens the page of the person <paramref name="name"/> (<c>Anna Schmidt</c> or <c>@[Anna Schmidt]</c>, normalized
    /// like a mention by <see cref="PersonParser.NormalizeTyped"/>): the note <c>people/&lt;Name&gt;</c>. A missing
    /// top-level <c>people</c> note is created through <see cref="NoteStore.Create"/> (an existing one is reused, in any
    /// case), and a missing person page through <see cref="NoteStore.CreateChild"/>; an existing page is reused, so a
    /// second call writes nothing (<see cref="ActionReason.Unchanged"/>, done, no refresh). An invalid name, or a
    /// sensitive top-level <c>people</c> note, is refused with <see cref="PersonRefusedText"/> and nothing written; no
    /// key is asked for. The name is resolved through the person pages' aliases (<see cref="PersonAliases.Load"/>): a
    /// person's full name or the alias of one person opens that person's page and creates no other; an alias of more than
    /// one person is refused (<see cref="ActionReason.Ambiguous"/>) with <see cref="PersonAliases.AmbiguousText"/> and
    /// nothing written. A new page is <c># &lt;Name&gt;\n</c> plus, when the type <c>person</c>
    /// (<see cref="PageTypes.Load"/>) has attributes, a blank line and one <c>&lt;attr&gt;::</c> line per attribute;
    /// an existing page is never changed. The result's text is the person page's full name when done.
    /// </summary>
    public Task<ActionResult> OpenPerson(string name) => Run(() => OpenPersonNow(name));

    /// <summary>The name of today's daily note (<see cref="DailyNote.NameFor"/>), by the injected local clock.</summary>
    public string DailyNoteName() => DailyNote.NameFor(DateOnly.FromDateTime(localNow()));

    /// <summary>
    /// Makes sure the meeting note for <paramref name="appointment"/> (<see cref="MeetingNote.NameFor"/>) exists
    /// through <see cref="MeetingNote.Ensure"/>: a missing one is created with the appointment's title, time, location
    /// and attendees, then the state is refreshed. An existing plain or sensitive note is left as it is
    /// (<see cref="ActionReason.Unchanged"/>, done, nothing read or written, no key-store call, no refresh). The
    /// result's text is the note's name when done, else <see cref="MeetingNote.WriteFailedText"/>.
    /// </summary>
    public Task<ActionResult> OpenMeetingNote(Appointment appointment) => Run(() => OpenMeetingNoteNow(appointment));

    /// <summary>
    /// Makes sure today's daily note (<see cref="DailyNoteName"/>, the day from the injected local clock) exists
    /// through <see cref="DailyNote.Ensure"/>: a missing one is created with the day's appointments (calendar files
    /// and the feed) and the due todos, then the state is refreshed. An existing plain or sensitive note is left as
    /// it is (<see cref="ActionReason.Unchanged"/>, done, nothing read or written, no key-store call, no refresh). The
    /// result's text is the note's name when done, else <see cref="DailyNoteFailedText"/>.
    /// </summary>
    public Task<ActionResult> OpenDailyNote() => Run(OpenDailyNoteNow);

    /// <summary>
    /// Closes <paramref name="day"/> through <see cref="DayEnd.CloseDay"/>: Times' worked hours into its daily note,
    /// then its due todos moved to the next day and listed in both notes. Never reads or writes
    /// <c>settings.json</c>. The text is <see cref="DayClosedText"/> when done, else a fixed red text.
    /// </summary>
    public Task<ActionResult> EndDay(DateOnly day) => Run(() => EndDayNow(day));

    /// <summary>
    /// The automatic day's end, called only by <c>Program.cs</c>'s day-end timer (at start and every
    /// <see cref="DayEndInterval"/>): <see cref="DayEnd.AutoClose"/> with the injected local clock. Done with
    /// <see cref="ActionReason.Unchanged"/> (no refresh) when nothing was written; Refused while another action runs
    /// or when <c>settings.json</c> is broken. Never throws and never asks anything.
    /// </summary>
    public Task<ActionResult> AutoDayEnd() => Run(AutoDayEndNow);

    /// <summary>The name of today's briefing (<see cref="Briefing.NameFor"/>, the day from the injected local clock).</summary>
    public string BriefingName() => Briefing.NameFor(DateOnly.FromDateTime(localNow()));

    /// <summary>
    /// Makes sure today's briefing (<see cref="BriefingName"/>) exists through <see cref="Briefing.Ensure"/>,
    /// whatever the weekday and whether or not Times has data; a missing one is created, then the state is refreshed.
    /// An existing page is left as it is (<see cref="ActionReason.Unchanged"/>, done, no refresh). A sensitive
    /// <c>briefings</c> is Refused with <see cref="BriefingSensitiveText"/>, a failed write is Failed with
    /// <see cref="BriefingFailedText"/>. The result's text is the page's name when done.
    /// </summary>
    public Task<ActionResult> OpenBriefing() => Run(OpenBriefingNow);

    /// <summary>
    /// The automatic briefing, called only by <c>Program.cs</c> after <see cref="AutoDayEnd"/> (at start and on the
    /// day-end timer): creates today's briefing when <see cref="Briefing.ShouldCreate"/> holds (the first call that
    /// runs counts as the start) and then offers it through <see cref="DesktopState.OfferBriefing"/>. An existing
    /// page is never changed or offered; a sensitive <c>briefings</c> writes and shows nothing. Refused while another
    /// action runs (the next call tries again). Never throws and never asks anything.
    /// </summary>
    public Task<ActionResult> AutoBriefing() => Run(AutoBriefingNow);

    /// <summary>
    /// The timed-todo reminders, called only by <c>Program.cs</c> (once at start, before the automatic writes, then
    /// every <see cref="ReminderInterval"/>): reads <see cref="TodoStore.DueReminders"/> with the injected local clock,
    /// marks them all notified through <see cref="TodoStore.MarkNotified"/> in one write, and then shows one
    /// notification per todo (<see cref="ReminderTitle"/>, <see cref="ReminderText"/>), or one
    /// <see cref="RemindersDueText"/> when more than <see cref="MaxSingleReminders"/> are due. Marked before shown, so
    /// a todo reminds at most once, also when a notification fails (that failure is swallowed). Nothing due, or an
    /// unreadable <c>todos.json</c>, writes and shows nothing. Refused while another action runs (the next call tries
    /// again). Never refreshes, never throws and never asks anything.
    /// </summary>
    public Task<ActionResult> AutoReminders() => Run(AutoRemindersNow, refresh: false);

    /// <summary>
    /// Moves the note <paramref name="name"/> and its subpages under the note <paramref name="newParent"/> (matched
    /// through <see cref="NoteStore.TryFind"/> only, never a path; blank = the top level) through
    /// <see cref="NoteStore.Move"/>. Already there gives <see cref="ActionReason.Unchanged"/> (done, nothing written,
    /// no refresh). The result's text is the new full name when done.
    /// </summary>
    public Task<ActionResult> MoveNote(string name, string newParent) => Run(() => MoveNoteNow(name, newParent));

    /// <summary>
    /// Deletes the note <paramref name="name"/> and every subpage below it through <see cref="NoteStore.Delete"/>,
    /// but only when the user <paramref name="confirmed"/> it. There is no undo. The result's text is the name when
    /// done.
    /// </summary>
    public Task<ActionResult> DeleteNote(string name, Confirmation? confirmed) => Run(() => DeleteNoteNow(name, confirmed));

    /// <summary>
    /// Opens the note <paramref name="name"/> in the OS default app through the <see cref="INoteLauncher"/>. Only a path
    /// from the note list is launched, and only when it is inside the data folder, ends in <c>.md</c> and is a regular
    /// file (no link or reparse point, no folder, seekable). Never writes and never refreshes.
    /// </summary>
    public Task<ActionResult> OpenExternally(string name) => Run(() => OpenExternallyNow(name), refresh: false);

    /// <summary>
    /// Stores the files dropped onto the plain note <paramref name="name"/> through <see cref="NoteAttachments.Add"/>,
    /// each once, in drop order, and answers one <see cref="ActionResult.Attached"/> line per file. A sensitive note
    /// (checked first, without a key-store call) and a drop of more than <see cref="NoteAttachments.MaxFilesPerDrop"/>
    /// (<paramref name="count"/>, as the page counted them) are refused and nothing is written. Never touches the note
    /// file or <c>context.db</c> and never refreshes.
    /// </summary>
    public Task<ActionResult> AttachFiles(string name, int count, IReadOnlyList<DroppedFile> files) =>
        Run(() => AttachFilesNow(name, count, files), refresh: false);

    /// <summary>
    /// Opens the attachment PDF the link <paramref name="src"/> of the plain note <paramref name="name"/> points to in
    /// the system viewer: <see cref="INoteLauncher.Launch"/> once, only with a path <see cref="NoteAttachments.TryResolve"/>
    /// accepted as a PDF; anything else is Refused <c>Can't open &lt;name&gt;.</c> (Failed when the launcher throws).
    /// Never writes or refreshes.
    /// </summary>
    public Task<ActionResult> OpenAttachment(string name, string src) =>
        Run(() => OpenAttachmentNow(name, src), refresh: false);

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

    /// <summary>
    /// Stores the file at <paramref name="path"/> (fully qualified, outside the data folder, at most
    /// <see cref="NoteStore.MaxImportBytes"/>) as the note named after it, with its exact bytes, through
    /// <see cref="NoteStore.Save"/>; the source is only read. When a plain note with that title exists (ignoring case)
    /// it is replaced only when the user <paramref name="confirmed"/> it: without that the answer is Refused with
    /// <see cref="ActionReason.NotConfirmed"/> (no other answer of this action has that reason) and its text is the
    /// existing note's name, for the question. A title that names a sensitive note is always refused.
    /// </summary>
    public Task<ActionResult> ImportNote(string path, Confirmation? confirmed) => Run(() => ImportNoteNow(path, confirmed));

    /// <summary>
    /// Writes the saved text of the plain note <paramref name="name"/> (UTF-8, no BOM) to the new file
    /// <paramref name="path"/> (fully qualified, outside the data folder) through
    /// <see cref="NoteStore.WritePlainCopy"/>, which never overwrites. A sensitive note is refused, locked or open,
    /// without a private-key call. Changes nothing in the data folder and never refreshes.
    /// </summary>
    public Task<ActionResult> ExportNote(string name, string path) => Run(() => ExportNoteNow(name, path), refresh: false);

    /// <summary>The Refused/<see cref="ActionReason.Exists"/> text of <see cref="SaveSearch"/> (raw name).</summary>
    public static string SearchExistsText(string name) => $"A saved search named {name} already exists.";

    /// <summary>The Done text of <see cref="SaveSearch"/> (raw name).</summary>
    public static string SearchSavedText(string name) => $"Saved search {name}.";

    /// <summary>The Done text of <see cref="RemoveSavedSearch"/> (raw name).</summary>
    public static string SearchRemovedText(string name) => $"Removed saved search {name}.";

    /// <summary>
    /// Adds the search <paramref name="query"/> (trimmed) as the last entry of <c>settings.json</c>'s
    /// <c>savedSearches</c> under <paramref name="name"/>, trimmed with whitespace runs collapsed
    /// (<see cref="SavedSearches.NormalizeName"/>). Refused, writing nothing: a blank name
    /// (<see cref="SearchNameBlankText"/>), a name over 50 characters or with a control character
    /// (<see cref="SearchNameInvalidText"/>), a query that can't be searched (<see cref="SearchQueryInvalidText"/>), a
    /// name already stored ignoring case (<see cref="SearchExistsText"/>), a 51st entry (<see cref="SearchesFullText"/>).
    /// A broken <c>settings.json</c> is Failed with <see cref="SearchesUnreadableText"/> and left as it is.
    /// </summary>
    public Task<ActionResult> SaveSearch(string name, string query) => Run(() => SaveSearchNow(name, query));

    /// <summary>
    /// Removes the saved search named <paramref name="name"/> (ignoring case) from <c>settings.json</c>, keeping the
    /// others in order. A name no longer stored is Refused/<see cref="ActionReason.Missing"/>
    /// (<see cref="SearchGoneText"/>); a broken file is Failed with <see cref="SearchesNotRemovedText"/>.
    /// </summary>
    public Task<ActionResult> RemoveSavedSearch(string name) => Run(() => RemoveSavedSearchNow(name));

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

            bool changed = (result.Kind == ActionKind.Done && result.Reason != ActionReason.Unchanged)
                || result.Reason == ActionReason.LineKept;
            if (refresh && changed)
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

    // The checks on the input come first, so a bad name or query gets the same answer whatever the file holds.
    private ActionResult SaveSearchNow(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Refused(ActionReason.Invalid, SearchNameBlankText);
        }

        if (SavedSearches.NormalizeName(name) is not { } normalized)
        {
            return Refused(ActionReason.Invalid, SearchNameInvalidText);
        }

        string trimmed = query.Trim();
        if (!SavedSearches.IsValidQuery(trimmed))
        {
            return Refused(ActionReason.Invalid, SearchQueryInvalidText);
        }

        IReadOnlyList<SavedSearch> stored;
        try
        {
            stored = settings.ReadSavedSearches();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return Failed(ActionReason.Unreadable, SearchesUnreadableText);
        }

        if (stored.Any(s => string.Equals(s.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return Refused(ActionReason.Exists, SearchExistsText(normalized));
        }

        if (stored.Count >= SavedSearches.MaxCount)
        {
            return Refused(ActionReason.Invalid, SearchesFullText);
        }

        try
        {
            settings.SaveSavedSearches([.. stored, new SavedSearch(normalized, trimmed)]);
        }
        catch (SettingsStoreException)
        {
            return Failed(ActionReason.Unreadable, SearchesUnreadableText);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return Failed(ActionReason.WriteFailed, SearchesWriteFailedText);
        }

        return new ActionResult(ActionKind.Done, ActionReason.None, SearchSavedText(normalized));
    }

    private ActionResult RemoveSavedSearchNow(string name)
    {
        IReadOnlyList<SavedSearch> stored;
        try
        {
            stored = settings.ReadSavedSearches();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return Failed(ActionReason.Unreadable, SearchesNotRemovedText);
        }

        string key = SavedSearches.NormalizeName(name) ?? name;
        if (stored.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)) is not { } found)
        {
            return Refused(ActionReason.Missing, SearchGoneText);
        }

        try
        {
            settings.SaveSavedSearches(stored.Where(s => !ReferenceEquals(s, found)).ToList());
        }
        catch (SettingsStoreException)
        {
            return Failed(ActionReason.Unreadable, SearchesNotRemovedText);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return Failed(ActionReason.WriteFailed, SearchesRemoveFailedText);
        }

        return new ActionResult(ActionKind.Done, ActionReason.None, SearchRemovedText(found.Name));
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
            TodoItem? done = todos.Complete(todo.Id, DateOnly.FromDateTime(localNow()), out TodoItem? next);
            return done is null
                ? Refused(ActionReason.Changed, TodosChangedText)
                : new ActionResult(ActionKind.Done, ActionReason.None, done.Text, nextDue: next?.Due);
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

    private ActionResult CompleteNoteTodoNow(NoteTodo todo, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        // The note name comes from a file name: one safe line, as the CLI shows it.
        string note = DesktopText.OneLine(todo.Note);
        return NoteTodos.Complete(notes, todo, state.HistoryStore) switch
        {
            NoteTodoResult.Done => new ActionResult(ActionKind.Done, ActionReason.None, todo.Text),
            NoteTodoResult.Missing => Refused(ActionReason.Missing, NoteTodos.MissingText(note)),
            NoteTodoResult.Failed => Failed(ActionReason.WriteFailed, NoteTodos.FailedText(note)),
            _ => Refused(ActionReason.Changed, NoteTodos.ChangedText(note)),
        };
    }

    private ActionResult SaveNoteNow(EditableNote opened, string text, bool auto = false)
    {
        if (IsSealedPath(opened.Path))
        {
            try
            {
                return SaveSensitiveNow(opened, text, auto);
            }
            catch (NoteHistoryException)
            {
                return Failed(ActionReason.WriteFailed, KeepFailedText(opened.Name));
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
            NoteHistory? history = KeepsVersion(opened, auto) ? state.HistoryStore : null;
            NoteSaveResult saved = notes.SaveIfUnchanged(opened, text, out EditableNote? savedNote, history);
            return saved switch
            {
                NoteSaveResult.Saved => new ActionResult(ActionKind.Done, ActionReason.None, opened.Name, savedNote),
                NoteSaveResult.Unchanged => new ActionResult(ActionKind.Done, ActionReason.Unchanged, opened.Name, opened),
                NoteSaveResult.ChangedOnDisk => Refused(ActionReason.ChangedOnDisk, ChangedOnDiskText(opened.Name)),
                NoteSaveResult.TooLarge => Refused(ActionReason.Invalid, TooLargeText(opened.Name)),
                _ => Refused(ActionReason.Missing, MissingText(opened.Name)),
            };
        }
        catch (NoteHistoryException)
        {
            return Failed(ActionReason.WriteFailed, KeepFailedText(opened.Name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, NoteWriteFailedText(opened.Name));
        }
    }

    private ActionResult RestoreVersionNow(string name, string id, EditableNote opened, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        string? text = state.ReadVersionText(name, id);
        if (text is null)
        {
            return Failed(ActionReason.Unreadable, VersionUnreadableText);
        }

        return SaveNoteNow(opened, text);
    }

    private ActionResult AddTodoNow(string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time)
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
            TodoItem added = todos.Add(trimmed, due, repeat, time);
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

    private ActionResult CaptureTextNow(string? text)
    {
        string cleaned = Inbox.Clean(text ?? string.Empty);
        if (cleaned.Length == 0)
        {
            return Refused(ActionReason.Invalid, CaptureEmptyText);
        }

        if (cleaned.Length > Inbox.MaxTextLength)
        {
            return Refused(ActionReason.Invalid, CaptureTooLongText);
        }

        try
        {
            // A sealed inbox is never opened for a capture: no key is asked for and nothing is written.
            if (notes.TryFindSensitive(Inbox.NoteName, out _))
            {
                return Refused(ActionReason.Exists, CaptureSensitiveText);
            }

            return Inbox.Capture(notes, cleaned, localNow(), BeforeCaptureSave) switch
            {
                CaptureResult.Captured => new ActionResult(ActionKind.Done, ActionReason.None, CapturedText),
                CaptureResult.TooLarge => Refused(ActionReason.Invalid, CaptureTooLargeText),
                _ => Refused(ActionReason.ChangedOnDisk, CaptureChangedText),
            };
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CaptureFailedText);
        }
    }

    private ActionResult TriageToNoteNow(InboxItem item, string title)
    {
        string normalized = NoteStore.NormalizeTitle(title);
        string? invalid = NoteStore.ValidateTitle(normalized);
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, invalid);
        }

        if (TriageCheck(item) is { } refused)
        {
            return refused;
        }

        NoteInfo created;
        try
        {
            created = notes.CreateWithText(title, $"# {normalized}\n\n{item.Text}\n");
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CreateFailedText);
        }

        return TriageRemove(item, created.Name, TriageCreatedText(created.Name), null);
    }

    private ActionResult TriageToTodoNow(InboxItem item, string text, DateOnly due)
    {
        if (TriageCheck(item) is { } refused)
        {
            return refused;
        }

        ActionResult added = AddTodoNow(text, due, null, null);
        return added.Kind == ActionKind.Done
            ? TriageRemove(item, TodoStore.FileName, TriageTodoText(added.Text), null)
            : added;
    }

    private ActionResult TriageAppendNow(InboxItem item, string name, EditableNote? unlocked)
    {
        if (string.Equals(name.Trim(), Inbox.NoteName, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(ActionReason.Invalid, TriageInboxTargetText);
        }

        if (TriageCheck(item) is { } refused)
        {
            return refused;
        }

        if (unlocked is not null)
        {
            // Only the sensitive note the session holds, by its own name; SaveNoteNow re-seals it and never writes plain bytes.
            if (!IsSealedPath(unlocked.Path)
                || !string.Equals(unlocked.Name, name, StringComparison.Ordinal)
                || !ReferenceEquals(unlocked, state.Session.Note))
            {
                return Refused(ActionReason.Locked, CantUnlockText(name));
            }

            ActionResult sealedSave = SaveNoteNow(unlocked, Appended(unlocked.Text, item.Text));
            return sealedSave.Kind == ActionKind.Done
                ? TriageRemove(item, unlocked.Name, TriageAddedText(unlocked.Name), sealedSave.Note)
                : sealedSave;
        }

        try
        {
            if (notes.TryFindSensitive(name, out _))
            {
                return Refused(ActionReason.Locked, CantUnlockText(name));
            }

            for (int attempt = 0; attempt <= Inbox.MaxRetries; attempt++)
            {
                if (!notes.TryFind(name, out NoteInfo? found) || found is null)
                {
                    return Refused(ActionReason.Missing, NoNoteNamedText(name));
                }

                EditableNote opened = notes.ReadForEdit(found);
                NoteSaveResult result = notes.SaveIfUnchanged(opened, Appended(opened.Text, item.Text), out EditableNote? saved);
                switch (result)
                {
                    case NoteSaveResult.Saved:
                        return TriageRemove(item, found.Name, TriageAddedText(found.Name), saved);
                    case NoteSaveResult.TooLarge:
                        return Refused(ActionReason.Invalid, TooLargeText(found.Name));
                    default:
                        // ChangedOnDisk or Missing: read again.
                        continue;
                }
            }

            return Refused(ActionReason.ChangedOnDisk, ChangedText(name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, NoteWriteFailedText(name));
        }
    }

    private ActionResult TriageDropNow(InboxItem item, Confirmation? confirmed)
    {
        if (confirmed is null)
        {
            return Refused(ActionReason.NotConfirmed, NotConfirmedText);
        }

        return TriageInboxWrite(() => InboxTriage.Remove(notes, item.Line, BeforeTriageRemove), TriageDroppedText);
    }

    private ActionResult TriageSnoozeNow(InboxItem item, DateOnly until)
    {
        if (until <= DateOnly.FromDateTime(localNow()))
        {
            return Refused(ActionReason.Invalid, TriagePastDayText);
        }

        return TriageInboxWrite(() => InboxTriage.Snooze(notes, item.Line, until, BeforeTriageRemove), TriageSnoozedText(until));
    }

    // Drop and Later: the inbox is the only file written.
    private ActionResult TriageInboxWrite(Func<TriageResult> write, string doneText)
    {
        try
        {
            if (notes.TryFindSensitive(Inbox.NoteName, out _))
            {
                return Refused(ActionReason.Locked, TriageSensitiveText);
            }

            return write() switch
            {
                TriageResult.Done => new ActionResult(ActionKind.Done, ActionReason.None, doneText),
                TriageResult.Gone => Refused(ActionReason.Changed, TriageLineGoneText),
                TriageResult.TooLarge => Refused(ActionReason.Invalid, TriageTooLargeText),
                _ => Refused(ActionReason.ChangedOnDisk, TriageChangedText),
            };
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, TriageFailedText);
        }
    }

    // Before a triage target is written: the inbox is plain and still holds the exact line; else the refusal. A
    // sensitive inbox is never opened (no key-store call).
    private ActionResult? TriageCheck(InboxItem item)
    {
        try
        {
            if (notes.TryFindSensitive(Inbox.NoteName, out _))
            {
                return Refused(ActionReason.Locked, TriageSensitiveText);
            }

            if (!notes.TryFind(Inbox.NoteName, out NoteInfo? inbox)
                || inbox is null
                || !InboxTriage.Contains(notes.ReadForEdit(inbox).Text, item.Line))
            {
                return Refused(ActionReason.Changed, TriageLineGoneText);
            }

            return null;
        }
        catch (Exception)
        {
            return Failed(ActionReason.Unreadable, TriageUnreadableText);
        }
    }

    // After the target was saved: removes the line. When that fails the target is kept and the result says so.
    private ActionResult TriageRemove(InboxItem item, string target, string doneText, EditableNote? saved)
    {
        TriageResult removed;
        try
        {
            removed = InboxTriage.Remove(notes, item.Line, BeforeTriageRemove);
        }
        catch (Exception)
        {
            removed = TriageResult.ChangedOnDisk;
        }

        return removed == TriageResult.Done
            ? new ActionResult(ActionKind.Done, ActionReason.None, doneText, saved)
            : new ActionResult(ActionKind.Failed, ActionReason.LineKept, TriageKeptText(target), saved);
    }

    // The note text with "- <text>\n" appended, after a missing final newline.
    private static string Appended(string text, string line) =>
        (text.Length == 0 || text.EndsWith('\n') ? text : text + "\n") + "- " + line + "\n";

    private ActionResult OpenDailyNoteNow()
    {
        DateOnly day = DateOnly.FromDateTime(localNow());
        try
        {
            DailyNoteResult result = DailyNote.Ensure(notes, day, () => DailyNote.Compose(todos, calendar, calendarFeed, day));
            return new ActionResult(
                ActionKind.Done,
                result.Kind == DailyNoteKind.Created ? ActionReason.None : ActionReason.Unchanged,
                result.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, DailyNoteFailedText(DailyNote.NameFor(day)));
        }
    }

    private ActionResult OpenBriefingNow()
    {
        DateOnly day = DateOnly.FromDateTime(localNow());
        try
        {
            BriefingResult result = Briefing.Ensure(notes, day, () => ComposeBriefing(day));
            return result.Kind switch
            {
                BriefingKind.Created => new ActionResult(ActionKind.Done, ActionReason.None, result.Name),
                BriefingKind.Existing => new ActionResult(ActionKind.Done, ActionReason.Unchanged, result.Name),
                _ => Refused(ActionReason.Locked, BriefingSensitiveText),
            };
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, BriefingFailedText(Briefing.NameFor(day)));
        }
    }

    private ActionResult AutoBriefingNow()
    {
        bool atStart = !briefingStarted;
        briefingStarted = true;
        DateOnly day = DateOnly.FromDateTime(localNow());
        if (briefedDay == day || !Briefing.ShouldCreate(day, TimesDay.Read(timesFolder, day), atStart))
        {
            return new ActionResult(ActionKind.Done, ActionReason.Unchanged, Briefing.NameFor(day));
        }

        try
        {
            BriefingResult result = Briefing.Ensure(notes, day, () => ComposeBriefing(day));
            switch (result.Kind)
            {
                case BriefingKind.Created:
                    briefedDay = day;
                    state.OfferBriefing(result.Name);
                    return new ActionResult(ActionKind.Done, ActionReason.None, result.Name);
                case BriefingKind.Existing:
                    briefedDay = day;
                    return new ActionResult(ActionKind.Done, ActionReason.Unchanged, result.Name);
                default:
                    return Refused(ActionReason.Locked, BriefingSensitiveText);
            }
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, BriefingFailedText(Briefing.NameFor(day)));
        }
    }

    private ActionResult AutoRemindersNow()
    {
        IReadOnlyList<TodoItem> marked;
        try
        {
            IReadOnlyList<TodoItem> due = todos.DueReminders(localNow());
            if (due.Count == 0)
            {
                return new ActionResult(ActionKind.Done, ActionReason.Unchanged, string.Empty);
            }

            marked = todos.MarkNotified(due.Select(t => t.Id).ToList());
        }
        catch (TodoStoreException)
        {
            return Failed(ActionReason.Unreadable, TodosUnreadableText);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, TodosWriteFailedText);
        }

        if (marked.Count == 0)
        {
            return new ActionResult(ActionKind.Done, ActionReason.Unchanged, string.Empty);
        }

        if (marked.Count > MaxSingleReminders)
        {
            Notify(RemindersDueText(marked.Count));
        }
        else
        {
            foreach (TodoItem todo in marked)
            {
                Notify(ReminderText(todo));
            }
        }

        return new ActionResult(ActionKind.Done, ActionReason.None, marked.Count.ToString(CultureInfo.InvariantCulture));
    }

    // One notification; a notifier that fails is skipped (the todo stays notified).
    private void Notify(string body)
    {
        try
        {
            notifier?.Show(ReminderTitle, body);
        }
        catch (Exception)
        {
            // Shown at most once: the todo is already marked notified.
        }
    }

    private string ComposeBriefing(DateOnly day) => Briefing.Compose(todos, calendar, calendarFeed, timesFolder, day);

    private ActionResult EndDayNow(DateOnly day)
    {
        DayEndResult result = dayEnd.CloseDay(day);
        return DayEndAnswer(result, DayClosedText(result.Moved.Count, result.To));
    }

    private ActionResult AutoDayEndNow()
    {
        DateTime now = localNow();
        DayEndResult result = dayEnd.AutoClose(DateOnly.FromDateTime(now), now);
        if (result.Kind == DayEndKind.SettingsUnreadable)
        {
            return new ActionResult(
                result.Changed ? ActionKind.Done : ActionKind.Refused, ActionReason.Unreadable, SettingsUnreadableText);
        }

        if (result.Kind == DayEndKind.Done && !result.Changed)
        {
            return new ActionResult(ActionKind.Done, ActionReason.Unchanged, result.Name);
        }

        return DayEndAnswer(result, result.Name);
    }

    // The answer for a day's end result; a partial write still counts as a change, so the state is refreshed.
    private static ActionResult DayEndAnswer(DayEndResult result, string doneText)
    {
        ActionKind failed = result.Changed ? ActionKind.Done : ActionKind.Failed;
        return result.Kind switch
        {
            DayEndKind.Done => new ActionResult(
                ActionKind.Done, result.Changed ? ActionReason.None : ActionReason.Unchanged, doneText),
            DayEndKind.Sensitive => new ActionResult(
                result.Changed ? ActionKind.Done : ActionKind.Refused, ActionReason.Locked,
                DayEndSensitiveText(result.Name)),
            DayEndKind.TodosUnreadable => new ActionResult(failed, ActionReason.Unreadable, DayEndTodosUnreadableText),
            _ => new ActionResult(failed, ActionReason.WriteFailed, DayEndWriteFailedText(result.Name)),
        };
    }

    private ActionResult OpenMeetingNoteNow(Appointment appointment)
    {
        string name = MeetingNote.NameFor(appointment);
        try
        {
            MeetingNoteResult result = MeetingNote.Ensure(notes, appointment);
            return new ActionResult(
                ActionKind.Done,
                result.Kind == MeetingNoteKind.Created ? ActionReason.None : ActionReason.Unchanged,
                result.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MeetingNote.WriteFailedText(name));
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

    private ActionResult CreateNoteFromTemplateNow(string title, string template)
    {
        try
        {
            TemplateNote prepared = new NoteTemplates(notes).Prepare(title, template, localNow());
            switch (prepared.Kind)
            {
                case TemplateNoteKind.InvalidTitle:
                    return Refused(ActionReason.Invalid, prepared.Text);
                case TemplateNoteKind.Exists:
                    return Refused(ActionReason.Exists, ExistsText(prepared.Title));
                case TemplateNoteKind.TemplatesSensitive:
                    return Refused(ActionReason.Locked, TemplatesSensitiveText);
                case TemplateNoteKind.Missing:
                    return Refused(ActionReason.Missing, TemplateMissingText(prepared.Template, prepared.Templates));
                case TemplateNoteKind.Unreadable:
                    return Failed(ActionReason.Unreadable, TemplateUnreadableText(prepared.Template));
                case TemplateNoteKind.TooBig:
                    return Refused(ActionReason.Invalid, TemplateTooBigText(prepared.Template));
            }

            NoteInfo created = notes.CreateWithText(prepared.Title, prepared.Text);
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

    private ActionResult RenameNoteNow(string name, string newTitle, Confirmation? confirmed)
    {
        string title = NoteStore.NormalizeTitle(newTitle);
        string? invalid = NoteStore.ValidateTitle(title);
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, invalid);
        }

        try
        {
            bool plain = notes.TryFind(name, out NoteInfo? source) && source is not null;
            if (!plain && (!notes.TryFindSensitive(name, out source) || source is null))
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (string.Equals(source!.Title, title, StringComparison.Ordinal))
            {
                return new ActionResult(ActionKind.Done, ActionReason.Unchanged, source.Name);
            }

            LinkRenamePlan plan = LinkRenamePlan.For(notes, source.Name, title);
            if (plan.Invalid)
            {
                return Refused(ActionReason.Invalid, LinksInvalidText(plan.OldName, plan.NewName));
            }

            if (plan.Notes.Count > 0 && confirmed is null)
            {
                return Refused(ActionReason.NotConfirmed, NotConfirmedText);
            }

            string renamedName;
            if (plain)
            {
                NoteInfo? renamed = notes.Rename(source.Name, title);
                if (renamed is null)
                {
                    return Refused(ActionReason.Missing, NoteMissingText(name));
                }

                renamedName = renamed.Name;
            }
            else
            {
                ActionResult sealedResult = RenameSensitiveNow(source, title);
                if (sealedResult.Kind != ActionKind.Done || sealedResult.Reason == ActionReason.Unchanged)
                {
                    return sealedResult;
                }

                renamedName = sealedResult.Text;
            }

            return UpdateLinks(plan, renamedName);
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (ArgumentException)
        {
            // The title is valid (checked above), so the new full name of the note or a subpage is too long, or a
            // top-level note with subpages would be called calendar.
            return Refused(ActionReason.Invalid, RenameFailedText(name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, RenameFailedText(name));
        }
    }

    // After the rename: rewrites the links in every planned note (each under its name after the rename). Never throws;
    // a note that can't be updated is named in the LinksLeft text and the others are still updated.
    private ActionResult UpdateLinks(LinkRenamePlan plan, string renamedName)
    {
        int updated = 0;
        List<string> left = [];
        foreach (string planned in plan.Notes)
        {
            string current = NoteLinkRewriter.Moved(planned, plan.OldName, plan.NewName) ?? planned;
            switch (UpdateLinksIn(plan, current))
            {
                case NoteSaveResult.Saved:
                    updated++;
                    break;
                case NoteSaveResult.Unchanged:
                    break;
                default:
                    left.Add(current);
                    break;
            }
        }

        if (left.Count > 0)
        {
            return new ActionResult(
                ActionKind.Done, ActionReason.LinksLeft, LinksLeftText(renamedName, left), name: renamedName);
        }

        string text = updated == 0 ? RenamedText(renamedName) : RenamedLinksText(renamedName, updated);
        return new ActionResult(ActionKind.Done, ActionReason.None, text, name: renamedName);
    }

    // One note's link update: read, rewrite, save with the history; read again when it changed on disk in between.
    // Saved or Unchanged when done; any other value (a failed check counts as Missing) means the links are left.
    private NoteSaveResult UpdateLinksIn(LinkRenamePlan plan, string current)
    {
        try
        {
            for (int attempt = 0; attempt < LinkUpdateTries; attempt++)
            {
                if (!notes.TryFind(current, out NoteInfo? found) || found is null)
                {
                    return NoteSaveResult.Missing;
                }

                EditableNote opened = notes.ReadForEdit(found);
                if (!NoteLinkRewriter.TryRewrite(
                    opened.Text, plan.Before, plan.After, plan.OldName, plan.NewName, out string rewritten))
                {
                    return NoteSaveResult.Missing;
                }

                if (ReferenceEquals(rewritten, opened.Text))
                {
                    return NoteSaveResult.Unchanged;
                }

                BeforeLinkSave?.Invoke(current);
                NoteSaveResult result = notes.SaveIfUnchanged(opened, rewritten, out _, state.HistoryStore);
                if (result != NoteSaveResult.ChangedOnDisk)
                {
                    return result;
                }
            }

            return NoteSaveResult.ChangedOnDisk;
        }
        catch (Exception)
        {
            return NoteSaveResult.Missing;
        }
    }

    private ActionResult CreateSubpageNow(string parent, string title)
    {
        string normalized = NoteStore.NormalizeTitle(title);
        string? invalid = NoteStore.ValidateTitle(normalized);
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, invalid);
        }

        try
        {
            if (!notes.TryFind(parent, out NoteInfo? owner) || owner is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(parent));
            }

            if (owner.Name.Length + 1 + normalized.Length > NoteStore.MaxNameLength)
            {
                return Refused(ActionReason.Invalid, NameTooLongText);
            }

            NoteInfo created = notes.CreateChild(owner.Name, normalized);
            return new ActionResult(ActionKind.Done, ActionReason.None, created.Name);
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (ArgumentException)
        {
            return Refused(ActionReason.Invalid, NoSubpagesText(parent));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CreateFailedText);
        }
    }

    private ActionResult OpenPersonNow(string name)
    {
        string? person = PersonParser.NormalizeTyped(name);
        if (person is null)
        {
            return Refused(ActionReason.Invalid, PersonRefusedText(TagParser.CollapseWhitespace(name)));
        }

        try
        {
            // A sealed people note is never opened or added to: no key is asked for and nothing is written.
            if (notes.TryFindSensitive(People.FolderName, out _))
            {
                return Refused(ActionReason.Locked, PersonRefusedText(person));
            }

            // A full name or the alias of one person opens that person's page; a shared alias links to no one.
            AliasResolution resolved = PersonAliases.Load(notes).Resolve(person);
            if (resolved.Kind == AliasResolutionKind.Ambiguous)
            {
                return Refused(ActionReason.Ambiguous, PersonAliases.AmbiguousText(person, resolved.Titles));
            }

            person = resolved.Person ?? person;
            bool created = false;
            if (!notes.TryFind(People.FolderName, out NoteInfo? folder) || folder is null)
            {
                try
                {
                    folder = notes.Create(People.FolderName);
                    created = true;
                }
                catch (NoteExistsException)
                {
                    // Created by someone else between the check and the write.
                    if (!notes.TryFind(People.FolderName, out folder) || folder is null)
                    {
                        return Refused(ActionReason.Locked, PersonRefusedText(person));
                    }
                }
            }

            if (notes.TryFind(folder.Name + "/" + person, out NoteInfo? page) && page is not null)
            {
                return new ActionResult(ActionKind.Done, created ? ActionReason.None : ActionReason.Unchanged, page.Name);
            }

            if (notes.TryFindSensitive(folder.Name + "/" + person, out NoteInfo? sealedPage) && sealedPage is not null)
            {
                return new ActionResult(ActionKind.Done, created ? ActionReason.None : ActionReason.Unchanged, sealedPage.Name);
            }

            if (folder.Name.Length + 1 + person.Length > NoteStore.MaxNameLength)
            {
                return Refused(ActionReason.Invalid, PersonRefusedText(person));
            }

            try
            {
                // The new page comes with one empty line per attribute of the type person (types/person or the built-in).
                string body = PageTypes.Template(PageTypes.Find(PageTypes.Load(notes), PageTypes.Person));
                NoteInfo child = notes.CreateChild(folder.Name, person, body);
                return new ActionResult(ActionKind.Done, ActionReason.None, child.Name);
            }
            catch (NoteExistsException)
            {
                // Created by someone else between the check and the write.
                return notes.TryFind(folder.Name + "/" + person, out page) && page is not null
                    ? new ActionResult(ActionKind.Done, created ? ActionReason.None : ActionReason.Unchanged, page.Name)
                    : Refused(ActionReason.Exists, PersonRefusedText(person));
            }
        }
        catch (ArgumentException)
        {
            return Refused(ActionReason.Invalid, PersonRefusedText(person));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, CreateFailedText);
        }
    }

    private ActionResult MoveNoteNow(string name, string newParent)
    {
        try
        {
            if (!notes.TryFind(name, out NoteInfo? source) || source is null)
            {
                return Refused(ActionReason.Missing, NoteMissingText(name));
            }

            NoteInfo? owner = null;
            string typed = newParent.Trim();
            if (typed.Length > 0)
            {
                // The typed parent is only ever matched against the note list, never turned into a path.
                if (!notes.TryFind(typed, out owner) || owner is null)
                {
                    return Refused(ActionReason.Missing, NoParentText(typed));
                }

                if (string.Equals(owner.Name, source.Name, StringComparison.Ordinal)
                    || owner.Name.StartsWith(source.Name + "/", StringComparison.Ordinal))
                {
                    return Refused(ActionReason.Invalid, MoveUnderItselfText);
                }
            }

            if (string.Equals(owner?.Name, source.Parent, StringComparison.Ordinal))
            {
                return new ActionResult(ActionKind.Done, ActionReason.Unchanged, source.Name);
            }

            NoteInfo? moved = attachments.MoveWithNote(source.Name, owner?.Name);
            return moved is null
                ? Refused(ActionReason.Missing, NoteMissingText(name))
                : new ActionResult(ActionKind.Done, ActionReason.None, moved.Name);
        }
        catch (NoteExistsException e)
        {
            return Refused(ActionReason.Exists, ExistsText(e.Name));
        }
        catch (ArgumentException)
        {
            // Below the calendar note, or a full name over NoteStore.MaxNameLength for the note or a subpage.
            return Refused(ActionReason.Invalid, CantMoveText(name, newParent.Trim()));
        }
        catch (AttachmentMoveException)
        {
            return Failed(ActionReason.WriteFailed, AttachmentsMoveFailedText(name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MoveFailedText(name));
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
            // The names the note links are read before it goes; an unreadable note removes no attachment.
            notes.TryFind(name, out NoteInfo? plain);
            IReadOnlyList<string> linked = plain is null ? [] : attachments.LinkedNamesOf(plain.Name);
            if (notes.Delete(name, removeHistory: true, removeAttachments: true))
            {
                if (plain is not null)
                {
                    // Never throws; a file it can't remove stays and the delete is still done.
                    attachments.RemoveUnlinked(plain, linked);
                }

                return new ActionResult(ActionKind.Done, ActionReason.None, name);
            }

            // A sensitive note is deleted without being opened: no private-key call.
            if (notes.TryFindSensitive(name, out NoteInfo? locked) && locked is not null && notes.DeleteSensitive(locked.Name, removeHistory: true))
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

    private ActionResult AttachFilesNow(string name, int count, IReadOnlyList<DroppedFile> files)
    {
        try
        {
            if (!notes.TryFind(name, out NoteInfo? note) || note is null)
            {
                // A sensitive note is only ever matched by name: no key-store call.
                return notes.TryFindSensitive(name, out _)
                    ? Refused(ActionReason.Invalid, AttachSensitiveText)
                    : Refused(ActionReason.Missing, NoteMissingText(name));
            }
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, FailedText);
        }

        if (count > NoteAttachments.MaxFilesPerDrop || files.Count > NoteAttachments.MaxFilesPerDrop)
        {
            return Refused(ActionReason.Invalid, TooManyFilesText);
        }

        List<AttachmentLine> lines = [];
        foreach (DroppedFile file in files)
        {
            lines.Add(AttachOne(name, file));
        }

        bool any = lines.Any(l => l.Kind == ActionKind.Done);
        return new ActionResult(any ? ActionKind.Done : ActionKind.Refused, ActionReason.None, name, attached: lines);
    }

    private AttachmentLine AttachOne(string name, DroppedFile file)
    {
        string shown = file.Name;
        if (file.Size > NoteAttachments.MaxBytes || file.Bytes is { LongLength: > NoteAttachments.MaxBytes })
        {
            return new AttachmentLine(ActionKind.Refused, AttachTooLargeText(shown));
        }

        if (file.Bytes is null)
        {
            return new AttachmentLine(ActionKind.Failed, AttachFailedText(shown));
        }

        try
        {
            AttachmentAdd added = attachments.Add(name, file.Name, file.Bytes);
            return added.Kind switch
            {
                AttachmentAddKind.Added when added.Name is not null && added.Src is not null => new AttachmentLine(
                    ActionKind.Done,
                    AttachedText(added.Name),
                    added.Src,
                    added.Name,
                    NoteAttachments.IsImage(added.Type),
                    NoteAttachments.MimeOf(added.Type),
                    NoteAttachments.IsImage(added.Type) ? Convert.ToBase64String(file.Bytes) : null),
                AttachmentAddKind.TooLarge => new AttachmentLine(ActionKind.Refused, AttachTooLargeText(shown)),
                AttachmentAddKind.Unsupported => new AttachmentLine(ActionKind.Refused, AttachUnsupportedText(shown)),
                _ => new AttachmentLine(ActionKind.Refused, AttachFailedText(shown)),
            };
        }
        catch (Exception)
        {
            return new AttachmentLine(ActionKind.Failed, AttachFailedText(shown));
        }
    }

    private ActionResult OpenAttachmentNow(string name, string src)
    {
        int slash = src.LastIndexOfAny(['/', '\\']);
        string shown = src[(slash + 1)..];
        ResolvedAttachment? resolved;
        try
        {
            resolved = attachments.TryResolve(name, src);
        }
        catch (Exception)
        {
            resolved = null;
        }

        if (resolved is null || resolved.Type != AttachmentType.Pdf)
        {
            return Refused(ActionReason.Invalid, OpenAttachmentText(shown));
        }

        try
        {
            launcher.Launch(resolved.Path);
            return new ActionResult(ActionKind.Done, ActionReason.None, shown);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, OpenAttachmentText(shown));
        }
    }

    // The console's /import with the path from the OS dialog, which is still untrusted: every check runs again here.
    private ActionResult ImportNoteNow(string? path, Confirmation? confirmed)
    {
        string shown = path ?? string.Empty;
        string source;
        string title;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return Refused(ActionReason.Unreadable, CantReadText(shown));
            }

            source = Path.GetFullPath(path);
            shown = source;
            if (notes.Contains(source))
            {
                return Refused(ActionReason.Invalid, ImportInsideText);
            }

            title = NoteStore.NormalizeTitle(Path.GetFileName(source));
        }
        catch (Exception)
        {
            return Refused(ActionReason.Unreadable, CantReadText(shown));
        }

        string? invalid = NoteStore.ValidateTitle(title);
        if (invalid is not null)
        {
            return Refused(ActionReason.Invalid, ImportTitleText(invalid));
        }

        byte[] content;
        try
        {
            if (notes.TryFindSensitive(title, out NoteInfo? locked) && locked is not null)
            {
                return Refused(ActionReason.Exists, ImportSensitiveText(locked.Name));
            }

            content = NoteStore.ReadImport(source);
        }
        catch (FileNotFoundException)
        {
            return Refused(ActionReason.Missing, NoSuchFileText(shown));
        }
        catch (InvalidDataException)
        {
            return Refused(ActionReason.Invalid, ImportTooLargeText(shown));
        }
        catch (Exception)
        {
            return Failed(ActionReason.Unreadable, CantReadText(shown));
        }

        try
        {
            // Replacing a note destroys its text: only with the user's confirmation. The text is the note's name.
            if (confirmed is null && notes.TryFind(title, out NoteInfo? existing) && existing is not null)
            {
                return Refused(ActionReason.NotConfirmed, existing.Name);
            }

            (NoteInfo note, bool replaced) = notes.Save(title, content);
            return new ActionResult(ActionKind.Done, ActionReason.None, ImportedText(note.Name, shown, replaced));
        }
        catch (NoteExistsException e)
        {
            // A sensitive note with that name appeared in between.
            return Refused(ActionReason.Exists, ImportSensitiveText(e.Name));
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, ImportSaveFailedText(title));
        }
    }

    // The console's /export with the path from the OS save dialog, checked again here.
    private ActionResult ExportNoteNow(string name, string? path)
    {
        string shown = path ?? string.Empty;
        string target;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return Refused(ActionReason.Invalid, CantWriteText(shown));
            }

            target = Path.GetFullPath(path);
            shown = target;
            if (notes.Contains(target))
            {
                return Refused(ActionReason.Invalid, ExportInsideText);
            }
        }
        catch (Exception)
        {
            return Refused(ActionReason.Invalid, CantWriteText(shown));
        }

        NoteInfo? note;
        try
        {
            if (!notes.TryFind(name, out note) || note is null)
            {
                // A sensitive note is never written out as plain text, and never opened for it.
                return notes.TryFindSensitive(name, out NoteInfo? locked) && locked is not null
                    ? Refused(ActionReason.Locked, ExportSensitiveText(locked.Name))
                    : Refused(ActionReason.Missing, NoteMissingText(name));
            }

            if (File.Exists(target) || Directory.Exists(target))
            {
                return Refused(ActionReason.Exists, ExportExistsText(shown));
            }
        }
        catch (Exception)
        {
            return Failed(ActionReason.Unreadable, CantReadText(name));
        }

        string text;
        try
        {
            // Decoded like the console's File.ReadAllText (UTF-8 unless a byte order mark says otherwise), but bounded.
            byte[] bytes = BoundedFile.Read(note.Path, MaxNoteBytes);
            using MemoryStream stream = new MemoryStream(bytes, writable: false);
            using StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (Exception)
        {
            return Failed(ActionReason.Unreadable, CantReadText(note.Name));
        }

        try
        {
            NoteStore.WritePlainCopy(target, text);
            return new ActionResult(ActionKind.Done, ActionReason.None, ExportedText(note.Name, shown));
        }
        catch (Exception)
        {
            return File.Exists(target)
                ? Refused(ActionReason.Exists, ExportExistsText(shown))
                : Failed(ActionReason.WriteFailed, CantWriteText(shown));
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

            // Sensitive notes stay top-level: a sealed note's name is its file stem, and its file is never moved.
            if (source.Parent is not null)
            {
                return Refused(ActionReason.Invalid, ChildSensitiveText);
            }

            if (notes.Descendants(source.Name) > 0)
            {
                return Refused(ActionReason.Invalid, ParentSensitiveText);
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

            // The plain versions are sealed first, each under the id it has, so the history never stays readable.
            List<string> sealedVersions = [];
            if (!SealHistory(source, recipients, sealedVersions))
            {
                RemoveAll(sealedVersions);
                return Failed(ActionReason.WriteFailed, SealHistoryFailedText(source.Name));
            }

            bool written;
            try
            {
                AfterHistorySealed?.Invoke();
                written = WriteThroughTemp(
                    target,
                    TempName(source.Name, NoteStore.SensitiveExtension),
                    file,
                    overwrite: false,
                    () => IsVersion(source.Path, MaxNoteBytes, version));
            }
            catch (Exception)
            {
                RemoveAll(sealedVersions);
                throw;
            }

            if (!written)
            {
                RemoveAll(sealedVersions);
                return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
            }

            try
            {
                if (!IsVersion(source.Path, MaxNoteBytes, version) || !notes.Delete(source.Name))
                {
                    notes.DeleteSensitive(source.Name);
                    RemoveAll(sealedVersions);
                    return Refused(ActionReason.ChangedOnDisk, ChangedText(source.Name));
                }
            }
            catch (Exception)
            {
                // The plain file could not be removed: take the sealed copies away again, so there is one note.
                notes.DeleteSensitive(source.Name);
                RemoveAll(sealedVersions);
                throw;
            }

            // Only now the plain versions go; when that fails the sealed ones and the rest stay, and the user is told,
            // since readable copies of a note now sealed are still on disk.
            if (!RemoveHistoryQuietly(source.Path, ".md"))
            {
                return new ActionResult(ActionKind.Done, ActionReason.PlainHistoryLeft, source.Name);
            }

            return new ActionResult(ActionKind.Done, ActionReason.None, source.Name);
        }
        catch (Exception)
        {
            return Failed(ActionReason.WriteFailed, MarkFailedText(name));
        }
    }

    // Re-seals the open sensitive note with a new note key. Needs no private key; never writes plain text.
    // True when a save keeps the previous bytes as a version: always for an explicit save; for an automatic one only
    // when the newest version is at least AutoVersionGap old (or there is none). Throws NoteHistoryException like
    // NoteHistory.Versions.
    private bool KeepsVersion(EditableNote opened, bool auto) =>
        !auto || !state.HistoryStore.KeptWithin(opened.Path, AutoVersionGap);

    private ActionResult SaveSensitiveNow(EditableNote opened, string text, bool auto)
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
            () => KeepSealed(found, opened.Version, KeepsVersion(opened, auto)));
        if (!written)
        {
            return Refused(ActionReason.ChangedOnDisk, ChangedOnDiskText(opened.Name));
        }

        EditableNote saved = new EditableNote(
            found.Name, found.Path, plainText, EditableNote.Hash(file), opened.HadCrlf, opened.HadBom);
        state.Session.Open(saved);
        return new ActionResult(ActionKind.Done, ActionReason.None, opened.Name, saved);
    }

    // True when the sealed file still has the version it was opened with; then its bytes are kept as a version, sealed
    // under its current title (no key call) unless keep is false (an automatic save within AutoVersionGap). Throws
    // NoteHistoryException when they can't be kept.
    private bool KeepSealed(NoteInfo found, string version, bool keep)
    {
        byte[] current;
        try
        {
            current = BoundedFile.Read(found.Path, SealedNote.MaxFileBytes);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }

        if (!string.Equals(EditableNote.Hash(current), version, StringComparison.Ordinal))
        {
            return false;
        }

        if (keep)
        {
            state.HistoryStore.Keep(found.Path, current, found.Name);
        }

        return true;
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

        // The history moves with the note (its versions stay sealed under the titles they had); a history already
        // under the new title, or a move that fails, takes the new file away again.
        bool moved;
        try
        {
            moved = NoteHistory.MoveHistory(source.Path, target, caseOnly: false);
        }
        catch (Exception)
        {
            Remove(target);
            throw;
        }

        if (!moved)
        {
            Remove(target);
            return Refused(ActionReason.Exists, ExistsText(title));
        }

        notes.DeleteSensitive(source.Name);
        state.Session.Open(new EditableNote(title, target, open.Text, EditableNote.Hash(file), open.HadCrlf, open.HadBom));
        return new ActionResult(ActionKind.Done, ActionReason.None, title);
    }

    // Seals every plain version of source (the newest MaxVersionsPerNote, no key call) as <id>~<title>.ctxs next to
    // it, adding each written path to written. False when a version can't be read, isn't UTF-8 or can't be written.
    private bool SealHistory(NoteInfo source, SealedRecipient[] recipients, List<string> written)
    {
        NoteHistory history = state.HistoryStore;
        try
        {
            foreach (NoteVersion kept in history.Versions(source).Take(NoteHistory.MaxVersionsPerNote))
            {
                if (!history.TryRead(source, kept.Id, out byte[]? bytes) || bytes is null)
                {
                    return false;
                }

                string text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
                written.Add(history.KeepSealedCopy(source.Path, kept.Id, source.Name, SealedNote.Seal(source.Name, text, recipients)));
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void RemoveAll(List<string> paths)
    {
        foreach (string path in paths)
        {
            Remove(path);
        }
    }

    // Removes the versions of one kind; a failure leaves them (the note itself is already done) and gives false.
    private bool RemoveHistoryQuietly(string notePath, string extension)
    {
        try
        {
            state.HistoryStore.RemoveHistory(notePath, extension);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in place.
            return false;
        }
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

            RemoveHistoryQuietly(source.Path, NoteStore.SensitiveExtension);
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
