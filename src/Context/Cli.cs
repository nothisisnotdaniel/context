using System.Globalization;
using System.Text.Json;

namespace Context;

/// <summary>
/// The scriptable command line: <c>Context &lt;command&gt; [args]</c> runs one command against the data folder and
/// returns. Output is plain text: results on stdout, one error line on stderr. Exit codes: 0 success, 1 error,
/// 2 usage error. It never reads stdin or the environment and never prompts. This class writes
/// through the given <see cref="TextWriter"/>s only, so it needs no terminal library.
/// </summary>
internal sealed class Cli
{
    /// <summary>Exit code for success.</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code for an error (unreadable data, not found).</summary>
    public const int ExitError = 1;

    /// <summary>Exit code for a usage error (unknown command, missing or bad arguments).</summary>
    public const int ExitUsage = 2;

    private const string DateFormat = "yyyy-MM-dd";
    private const string HelpHint = "Run 'context help'.";
    private const string TodosUnreadable = "Can't read todos.json.";
    private const string IndexUnreadable = "Can't read context.db.";
    private const string JsonOnly = "--json works only with version, todos, notes, note show, tags, links and current.";

    // Every command with its usage and what it does, in the order help lists them.
    private static readonly (string Usage, string Description)[] Commands =
    [
        ("help", "Show this list."),
        ("version", "Show the version."),
        ("capture <text...>", "Append a dated line to the inbox note."),
        ("todos", "List todos: date, text."),
        ("todo add <yyyy-MM-dd|today|tomorrow> <text...>", "Add a todo."),
        ("todo done <text...>", "Mark the todo with that text done."),
        ("notes", "List note names."),
        ("note show <name...>", "Print a note."),
        ("tags [tag...]", "List tags with counts, or the notes with a tag."),
        ("links <note...>", "List the notes linked to a note."),
        ("current", "Today's appointments, due todos and linked notes."),
        ("import <path>", "Store a Markdown file as a note."),
        ("export <note> <path>", "Write a note to a new plain file."),
        ("<command> --json", "Print one JSON object: version, todos, notes, note show, tags, links, current."),
    ];

    private readonly TextWriter stdout;
    private readonly TextWriter stderr;
    private readonly string folder;
    private readonly Func<DateOnly> today;
    private readonly Func<DateTime> now;
    private readonly HttpMessageHandler http;
    private readonly TimeZoneInfo zone;

    /// <param name="stdout">Where results go.</param>
    /// <param name="stderr">Where error and warning lines go.</param>
    /// <param name="folder">The data folder.</param>
    /// <param name="today">The local date.</param>
    /// <param name="now">The local time, for capture lines and the calendar download.</param>
    /// <param name="http">The handler the Exchange calendar download goes through.</param>
    public Cli(
        TextWriter stdout,
        TextWriter stderr,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http)
        : this(stdout, stderr, folder, today, now, http, TimeZoneInfo.Local)
    {
    }

    /// <summary>Like the public constructor with a fixed zone (tests).</summary>
    internal Cli(
        TextWriter stdout,
        TextWriter stderr,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http,
        TimeZoneInfo zone)
    {
        this.stdout = stdout;
        this.stderr = stderr;
        this.folder = folder;
        this.today = today;
        this.now = now;
        this.http = http;
        this.zone = zone;
    }

    /// <summary>Runs one command and returns its exit code. Never throws.</summary>
    public int Run(string[] args)
    {
        try
        {
            return RunCommand(args);
        }
        catch (Exception)
        {
            // Never a stack trace or an exception message: they could hold note text or paths.
            try
            {
                return Error("Something went wrong. Nothing more was done.");
            }
            catch (Exception)
            {
                // stderr itself is gone: only the exit code is left.
                return ExitError;
            }
        }
    }

    private int RunCommand(string[] given)
    {
        // --json may stand anywhere and repeat; it is removed before the command and its arguments are parsed.
        bool json = given.Any(CliJson.IsFlag);
        string[] args = json ? [.. given.Where(a => !CliJson.IsFlag(a))] : given;
        if (args.Length == 0)
        {
            return Usage("No command given.");
        }

        string name = args[0].ToLowerInvariant();
        string[] rest = args[1..];
        if (json && (name is "help" or "--help" or "-h" or "capture" or "todo" or "import" or "export"))
        {
            // Checked before any argument: nothing is written and the folder is not created.
            return Usage(JsonOnly);
        }

        if (name is "help" or "--help" or "-h")
        {
            return Help();
        }

        if (name is "version" or "--version")
        {
            // Like help: answered before the data folder is looked at.
            if (json)
            {
                stdout.Write(CliJson.Document(w => w.WriteString("version", AppVersion.Current)));
                return ExitOk;
            }

            Out($"Context {AppVersion.Current}");
            return ExitOk;
        }

        Func<Session, int>? action = Parse(name, rest, out string? usage);
        if (action is null)
        {
            // An unknown option is not repeated: it could be a mistyped secret.
            string shown = args[0].StartsWith('-') ? "that option" : OneLine(args[0]);
            return Usage(usage ?? $"Unknown command: {shown}.");
        }

        return action(new Session(this, json));
    }

    // Checks the arguments of a data command and returns what to run, or null with a usage message (null when the
    // command is unknown). Nothing here reads the data folder.
    private Func<Session, int>? Parse(string name, string[] args, out string? usage)
    {
        usage = null;
        switch (name)
        {
            case "capture":
                return ParseCapture(args, out usage);
            case "todos":
                return NoArguments("todos", args, out usage, Todos);
            case "todo":
                return ParseTodo(args, out usage);
            case "notes":
                return NoArguments("notes", args, out usage, Notes);
            case "note":
                if (args.Length < 2 || !string.Equals(args[0], "show", StringComparison.OrdinalIgnoreCase))
                {
                    usage = "Type: context note show <name>.";
                    return null;
                }

                string note = string.Join(' ', args[1..]);
                return s => ShowNote(s, note);
            case "tags":
                string tag = string.Join(' ', args);
                return s => Tags(s, tag);
            case "links":
                if (args.Length == 0)
                {
                    usage = "Type: context links <note>.";
                    return null;
                }

                string linked = string.Join(' ', args);
                return s => Links(s, linked);
            case "current":
                return NoArguments("current", args, out usage, Current);
            case "import":
                if (args.Length != 1)
                {
                    usage = "Type: context import <path>.";
                    return null;
                }

                return s => Import(s, args[0]);
            case "export":
                if (args.Length != 2)
                {
                    usage = "Type: context export <note> <path> (quote a name or path with spaces).";
                    return null;
                }

                return s => Export(s, args[0], args[1]);
            default:
                return null;
        }
    }

    private static Func<Session, int>? NoArguments(
        string name, string[] args, out string? usage, Func<Session, int> action)
    {
        usage = null;
        if (args.Length > 0)
        {
            usage = $"{name} takes no arguments.";
            return null;
        }

        return action;
    }

    private Func<Session, int>? ParseCapture(string[] args, out string? usage)
    {
        usage = null;
        string text = Inbox.Clean(string.Join(' ', args));
        if (text.Length == 0)
        {
            usage = "Type: context capture <text>.";
            return null;
        }

        if (text.Length > Inbox.MaxTextLength)
        {
            usage = $"A capture can be at most {Inbox.MaxTextLength} characters.";
            return null;
        }

        return s => Capture(s, text);
    }

    private Func<Session, int>? ParseTodo(string[] args, out string? usage)
    {
        usage = null;
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        if (sub == "add")
        {
            const string AddForm = "Type: context todo add <yyyy-MM-dd|today|tomorrow> <text>.";
            string text = args.Length > 2 ? string.Join(' ', args[2..]).Trim() : string.Empty;
            if (text.Length == 0)
            {
                usage = $"A todo needs a date and text. {AddForm}";
                return null;
            }

            if (!TodoInput.TryParseDate(args[1], today(), out DateOnly due))
            {
                usage = $"That date isn't valid. {AddForm}";
                return null;
            }

            if (text.Length > TodoInput.MaxTextLength)
            {
                usage = $"A todo can be at most {TodoInput.MaxTextLength} characters.";
                return null;
            }

            return s => AddTodo(s, text, due);
        }

        if (sub == "done")
        {
            string query = string.Join(' ', args[1..]).Trim();
            if (query.Length == 0)
            {
                usage = "Type: context todo done <text> with the todo's text, or a part of it.";
                return null;
            }

            return s => DoneTodo(s, query);
        }

        usage = "Type: context todo add <date> <text> or context todo done <text>.";
        return null;
    }

    private int Help()
    {
        int width = Commands.Max(c => c.Usage.Length);
        foreach ((string usage, string description) in Commands)
        {
            Out($"context {usage.PadRight(width)}  {description}");
        }

        return ExitOk;
    }

    private int Capture(Session session, string text)
    {
        CaptureResult result;
        try
        {
            if (Sensitive(session, Inbox.NoteName) is { } sensitive)
            {
                return Error(sensitive);
            }

            result = Inbox.Capture(session.Notes, text, now());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't write {Inbox.NoteName}. Nothing was captured.");
        }

        return result switch
        {
            CaptureResult.Captured => Done("Captured."),
            CaptureResult.TooLarge => Error($"{Inbox.NoteName} is too large. Nothing was captured."),
            _ => Error($"{Inbox.NoteName} kept changing on disk. Nothing was captured."),
        };
    }

    /// <summary>
    /// Stores missing todo ids before the first read; when bad ids were replaced, says so in one stderr line
    /// (a fixed text with the count, never the bad value).
    /// </summary>
    private void MigrateTodos(Session session)
    {
        session.Todos.Migrate(out int badIds);
        if (badIds == 1)
        {
            Err($"Fixed a bad todo id in {TodoStore.FileName}.");
        }
        else if (badIds > 1)
        {
            Err($"Fixed {badIds.ToString(CultureInfo.InvariantCulture)} bad todo ids in {TodoStore.FileName}.");
        }
    }

    private int Todos(Session session)
    {
        MigrateTodos(session);
        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = session.Todos.List();
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        if (session.Json)
        {
            return Json(w =>
            {
                w.WriteStartArray("todos");
                foreach (TodoItem todo in todos)
                {
                    w.WriteStartObject();
                    w.WriteString("due", Date(todo.Due));
                    w.WriteString("text", OneLine(todo.Text));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        }

        foreach (TodoItem todo in todos)
        {
            Out($"{Date(todo.Due)}\t{OneLine(todo.Text)}");
        }

        return ExitOk;
    }

    private int AddTodo(Session session, string text, DateOnly due)
    {
        MigrateTodos(session);
        TodoItem todo;
        try
        {
            todo = session.Todos.Add(text, due);
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        return Done($"Added: {Date(todo.Due)} {OneLine(todo.Text)}");
    }

    private int DoneTodo(Session session, string query)
    {
        MigrateTodos(session);
        TodoItem? done;
        try
        {
            IReadOnlyList<TodoItem> todos = session.Todos.List();
            if (todos.Count == 0)
            {
                return Error("There are no todos to mark done.");
            }

            TodoPick pick = TodoPicker.Pick(todos, query);
            if (pick.Todo is null)
            {
                string shown = OneLine(query);
                return Error(pick.Matches == 0
                    ? $"No todo matches \"{shown}\"."
                    : $"{pick.Matches.ToString(CultureInfo.InvariantCulture)} todos match \"{shown}\". Type more of the todo's text.");
            }

            // The text only picks the todo; it is removed by its id, so a list that changed in between can't
            // make another todo go away.
            done = session.Todos.Complete(pick.Todo.Id);
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        if (done is null)
        {
            return Error("The todo list changed. Nothing was done.");
        }

        return Done($"Done: {OneLine(done.Text)}");
    }

    private int Notes(Session session)
    {
        // Sensitive notes are listed by name only, marked locked; their text is never read here.
        IEnumerable<(NoteInfo Note, bool Locked)> all = session.Notes.List().Select(n => (n, false))
            .Concat(session.Notes.ListSensitive().Select(n => (n, true)))
            .OrderBy(r => r.Item1.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Item1.Name, StringComparer.Ordinal);
        if (session.Json)
        {
            return Json(w =>
            {
                w.WriteStartArray("notes");
                foreach ((NoteInfo note, bool locked) in all)
                {
                    w.WriteStartObject();
                    w.WriteString("name", OneLine(note.Name));
                    w.WriteBoolean("locked", locked);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        }

        foreach ((NoteInfo note, bool locked) in all)
        {
            Out(locked ? $"{OneLine(note.Name)}\tlocked" : OneLine(note.Name));
        }

        return ExitOk;
    }

    /// <summary>The error line for a command that would need the text of a sensitive note.</summary>
    internal static string SensitiveText(string name) => $"{name} is sensitive. Open it in the desktop app.";

    // The error for a sensitive note called name, or null when there is none.
    private string? Sensitive(Session session, string name) =>
        session.Notes.TryFindSensitive(name, out NoteInfo? locked) && locked is not null
            ? SensitiveText(OneLine(locked.Name))
            : null;

    private int ShowNote(Session session, string name)
    {
        Note? note;
        try
        {
            if (!session.Notes.TryOpen(name, out note) || note is null)
            {
                return Error(Sensitive(session, name) ?? $"No note named {OneLine(name.Trim())}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't read {OneLine(name.Trim())}.");
        }

        string text = SafeText.Clean(note.Text);
        if (session.Json)
        {
            string shown = OneLine(note.Name);
            return Json(w =>
            {
                w.WriteString("name", shown);
                w.WriteString("text", text);
            });
        }

        stdout.Write(text.Length == 0 || text.EndsWith('\n') ? text : text + "\n");
        return ExitOk;
    }

    private int Tags(Session session, string argument)
    {
        TagsView view = TagsView.Build(session.Index, session.Notes, argument);
        switch (view.Kind)
        {
            case TagsViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            case TagsViewKind.AllTags when session.Json:
                return Json(w =>
                {
                    w.WriteStartArray("tags");
                    foreach (TagCount tag in view.Tags)
                    {
                        w.WriteStartObject();
                        w.WriteString("tag", OneLine(tag.Tag));
                        w.WriteNumber("count", tag.Count);
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                });
            case TagsViewKind.AllTags:
                foreach (TagCount tag in view.Tags)
                {
                    Out($"{OneLine(TagParser.Format(tag.Tag))}\t{tag.Count.ToString(CultureInfo.InvariantCulture)}");
                }

                return ExitOk;
            case TagsViewKind.NotesWithTag or TagsViewKind.NoSuchTag when session.Json:
                return Json(w =>
                {
                    w.WriteString("tag", OneLine(view.Tag));
                    CliJson.Strings(w, "notes", view.Notes.Select(OneLine));
                });
            default:
                // NoSuchTag lists nothing: an unknown tag simply has no notes.
                foreach (string note in view.Notes)
                {
                    Out(OneLine(note));
                }

                return ExitOk;
        }
    }

    private int Links(Session session, string argument)
    {
        LinksView view = LinksView.Build(session.Index, session.Notes, argument);
        switch (view.Kind)
        {
            case LinksViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            case LinksViewKind.NoSuchNote:
            case LinksViewKind.MissingArgument:
                return Error(Sensitive(session, view.Typed) ?? $"No note named {OneLine(view.Typed)}.");
            case LinksViewKind.Links or LinksViewKind.NoLinks when session.Json:
                return Json(w =>
                {
                    w.WriteString("note", OneLine(view.Note ?? string.Empty));
                    w.WriteStartArray("links");
                    foreach (NoteLink link in view.Links)
                    {
                        w.WriteStartObject();
                        w.WriteString("note", OneLine(link.Note));
                        w.WriteString("direction", CliJson.Direction(link.Direction));
                        w.WriteBoolean("missing", link.Missing);
                        CliJson.Strings(w, "sharedTags", link.SharedTags.Select(OneLine));
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                });
            default:
                foreach (NoteLink link in view.Links)
                {
                    string tags = string.Join(' ', link.SharedTags.Select(TagParser.Format));
                    Out($"{OneLine(link.Note)}\t{LinkText(link)}\t{OneLine(tags)}");
                }

                return ExitOk;
        }
    }

    // The text of the link column; empty for a link by shared tags only.
    private static string LinkText(NoteLink link)
    {
        if (link.Missing)
        {
            return "links to (missing)";
        }

        return link.Direction switch
        {
            NoteLinkDirection.To => "links to",
            NoteLinkDirection.From => "linked from",
            NoteLinkDirection.Both => "both ways",
            _ => string.Empty,
        };
    }

    private int Current(Session session)
    {
        MigrateTodos(session);
        CurrentView view = CurrentView.Build(
            session.Todos, session.Index, session.Notes, session.Calendar, session.Feed, today());
        if (session.Json)
        {
            return CurrentJson(view);
        }

        bool failed = false;
        Out($"Today, {Date(view.Today)}");

        Out("Appointments");
        if (view.Appointments.Count == 0)
        {
            Out($"No appointments today. Put .ics files in {OneLine(view.Folder)} to see them here.");
        }

        foreach (Appointment appointment in view.Appointments)
        {
            string title = appointment.Title.Length == 0 ? "(no title)" : appointment.Title;
            Out($"{view.TimeText(appointment)}\t{OneLine(title)}\t{OneLine(appointment.Location ?? string.Empty)}");
        }

        foreach (string file in view.UnreadableCalendarFiles)
        {
            failed = true;
            Err($"Can't read {CalendarStore.FolderName}/{OneLine(file)}");
        }

        if (view.Status == CalendarFeedStatus.Stale && view.CachedAtUtc is not null)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), view.Zone);
            Out($"Exchange calendar offline, showing the copy from {local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}.");
        }
        else if (view.Status == CalendarFeedStatus.Failed)
        {
            failed = true;
            Err($"Can't download the Exchange calendar ({OneLine(view.Reason ?? string.Empty)}).");
        }

        if (view.TodosUnreadable)
        {
            Err(TodosUnreadable);
            return ExitError;
        }

        Out("Due");
        if (view.Due.Count == 0)
        {
            Out("Nothing due today. Add one with /todo today <text>.");
            return failed ? ExitError : ExitOk;
        }

        foreach (DueTodo todo in view.Due)
        {
            Out($"{Date(todo.Due)}\t{OneLine(todo.Text)}");
        }

        Out("Linked notes");
        if (view.IndexUnreadable)
        {
            Err(IndexUnreadable);
            return ExitError;
        }

        if (view.LinkedNotes.Count == 0)
        {
            Out("No linked notes. Add the same #tag to a todo and a note to link them.");
        }

        foreach (LinkedNote note in view.LinkedNotes)
        {
            Out($"{OneLine(note.Name)}\t{OneLine(string.Join(' ', note.Tags.Select(TagParser.Format)))}");
        }

        return failed ? ExitError : ExitOk;
    }

    // current as one JSON document: the same stderr lines and exit code as plain, no hint texts.
    private int CurrentJson(CurrentView view)
    {
        bool failed = false;
        foreach (string file in view.UnreadableCalendarFiles)
        {
            failed = true;
            Err($"Can't read {CalendarStore.FolderName}/{OneLine(file)}");
        }

        if (view.Status == CalendarFeedStatus.Failed)
        {
            failed = true;
            Err($"Can't download the Exchange calendar ({OneLine(view.Reason ?? string.Empty)}).");
        }

        // As in plain: the index is only looked at when something is due.
        bool indexFailed = !view.TodosUnreadable && view.Due.Count > 0 && view.IndexUnreadable;
        if (view.TodosUnreadable)
        {
            failed = true;
            Err(TodosUnreadable);
        }
        else if (indexFailed)
        {
            failed = true;
            Err(IndexUnreadable);
        }

        TimeZoneInfo shownZone = view.Zone;
        string? cachedAt = view.CachedAtUtc is null || view.Status is not (CalendarFeedStatus.Stale or CalendarFeedStatus.Fresh)
            ? null
            : CliJson.Time(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), shownZone),
                shownZone);
        stdout.Write(CliJson.Document(w =>
        {
            w.WriteString("today", Date(view.Today));
            w.WriteStartArray("appointments");
            foreach (Appointment appointment in view.Appointments)
            {
                w.WriteStartObject();
                w.WriteString("start", CliJson.Time(appointment.Start, shownZone));
                w.WriteString("end", CliJson.Time(appointment.End, shownZone));
                w.WriteBoolean("allDay", appointment.AllDay);
                w.WriteString("title", OneLine(appointment.Title));
                if (appointment.Location is null)
                {
                    w.WriteNull("location");
                }
                else
                {
                    w.WriteString("location", OneLine(appointment.Location));
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("calendar");
            w.WriteString("status", CliJson.Status(view.Status));
            if (cachedAt is null)
            {
                w.WriteNull("cachedAt");
            }
            else
            {
                w.WriteString("cachedAt", cachedAt);
            }

            CliJson.Strings(w, "unreadableFiles", view.UnreadableCalendarFiles.Select(OneLine));
            w.WriteEndObject();
            if (view.TodosUnreadable)
            {
                w.WriteNull("due");
            }
            else
            {
                w.WriteStartArray("due");
                foreach (DueTodo todo in view.Due)
                {
                    w.WriteStartObject();
                    w.WriteString("due", Date(todo.Due));
                    w.WriteString("text", OneLine(todo.Text));
                    w.WriteBoolean("overdue", todo.Overdue);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            if (view.TodosUnreadable || indexFailed)
            {
                w.WriteNull("linkedNotes");
            }
            else
            {
                w.WriteStartArray("linkedNotes");
                foreach (LinkedNote note in view.LinkedNotes)
                {
                    w.WriteStartObject();
                    w.WriteString("name", OneLine(note.Name));
                    CliJson.Strings(w, "tags", note.Tags.Select(OneLine));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
        }));
        return failed ? ExitError : ExitOk;
    }

    private int Import(Session session, string argument)
    {
        string source = Path.GetFullPath(argument);
        string shownSource = OneLine(source);
        NoteStore store = session.Notes;
        if (store.Contains(source))
        {
            return Error("That file is already in the Context folder. Nothing changed.");
        }

        string title = NoteStore.NormalizeTitle(Path.GetFileName(source));
        string? invalid = NoteStore.ValidateTitle(title);
        if (invalid is not null)
        {
            return Error($"{OneLine(invalid)} Rename the file and import it again.");
        }

        if (Sensitive(session, title) is { } sensitive)
        {
            return Error(sensitive);
        }

        byte[] content;
        try
        {
            content = NoteStore.ReadImport(source);
        }
        catch (FileNotFoundException)
        {
            return Error($"No such file: {shownSource}");
        }
        catch (InvalidDataException)
        {
            return Error($"Can't import a file over 5 MB: {shownSource}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't read {shownSource}.");
        }

        NoteInfo note;
        bool replaced;
        try
        {
            (note, replaced) = store.Save(title, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't save {OneLine(title)}. Nothing changed.");
        }

        Out($"{(replaced ? "Replaced" : "Imported")} {OneLine(note.Name)}.");
        Out($"The source is unchanged; delete it if you don't need the plain copy: {shownSource}");
        return ExitOk;
    }

    private int Export(Session session, string name, string path)
    {
        NoteStore store = session.Notes;
        if (!store.TryFind(name, out NoteInfo? match) || match is null)
        {
            return Error(Sensitive(session, name) ?? $"No note named {OneLine(name.Trim())}.");
        }

        string target = Path.GetFullPath(path);
        string shownTarget = OneLine(target);
        string shownName = OneLine(match.Name);
        if (store.Contains(target))
        {
            return Error("Export to a path outside the Context folder. Nothing was written.");
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return Error($"That file already exists; choose a new name: {shownTarget}");
        }

        string text;
        try
        {
            text = store.Read(match);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't read {shownName}.");
        }

        try
        {
            NoteStore.WritePlainCopy(target, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return File.Exists(target)
                ? Error($"That file already exists; choose a new name: {shownTarget}")
                : Error($"Can't write {shownTarget}.");
        }

        Out($"Exported {shownName}.");
        return ExitOk;
    }

    // Text from disk or the user as one safe line: controls replaced, line breaks turned into spaces.
    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');

    private static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private int Json(Action<Utf8JsonWriter> body)
    {
        stdout.Write(CliJson.Document(body));
        return ExitOk;
    }

    private void Out(string line)
    {
        stdout.Write(line);
        stdout.Write('\n');
    }

    private void Err(string line)
    {
        stderr.Write(line);
        stderr.Write('\n');
    }

    private int Done(string line)
    {
        Out(line);
        return ExitOk;
    }

    private int Error(string line)
    {
        Err(line);
        return ExitError;
    }

    private int Usage(string line)
    {
        Err($"{line} {HelpHint}");
        return ExitUsage;
    }

    // The stores of one run, built as in Program.cs.
    private sealed class Session
    {
        public Session(Cli cli, bool json)
        {
            Json = json;
            Notes = new NoteStore(cli.folder);
            Todos = new TodoStore(cli.folder, () => cli.now().ToUniversalTime());
            Index = new TagIndex(cli.folder);
            Calendar = new CalendarStore(cli.folder, cli.zone);
            CalendarLink link = new CalendarLink(cli.folder);
            Feed = new CalendarFeed(cli.folder, link, cli.http, () => cli.now().ToUniversalTime());
        }

        /// <summary>True when the command writes one JSON document instead of plain text.</summary>
        public bool Json { get; }

        public NoteStore Notes { get; }

        public TodoStore Todos { get; }

        public TagIndex Index { get; }

        public CalendarStore Calendar { get; }

        public CalendarFeed Feed { get; }
    }
}
