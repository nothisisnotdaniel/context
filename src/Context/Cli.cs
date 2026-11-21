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

    private const string StandupForm = "Type: context standup.";
    private const string TodosUnreadable = "Can't read todos.json.";
    private const string ArchiveUnreadable = "Can't read todos-archive.json.";
    private const string DoneLogFailed = "Couldn't add it to the done log.";
    private const string DoneLogUnreadable = "Can't read the done log.";
    private const string IndexUnreadable = "Can't read context.db.";
    private const string FindForm = "Type: context find <attribute> <value>.";
    private const string SearchForm = "Type: context search <text>.";
    private const string DecisionsForm = "Type: context decisions [--full] [tag] or context decisions <reference>.";
    private const string ItemForm = "Type: context item <n> [--json] [--open] with the work item's number.";
    private const string OpenFlag = "--open";
    private const string LaunchFailed = "Can't start the desktop app.";

    // Job 273: refused anywhere on the line. Spelled in pieces: the whole word must not appear in the source.
    private const string RemovedSecretOption = "--pass" + "phrase";

    private const string JsonOnly =
        "--json works only with version, todos, notes, note show, tags, find, search, current, review and item.";

    // Every command with its usage and what it does, in the order help lists them.
    private static readonly (string Usage, string Description)[] Commands =
    [
        ("help", "Show this list."),
        ("version", "Show the version."),
        ("capture <text...>", "Append a dated line to the inbox note."),
        ("todos", "List todos: date, text, and the note for todos inside notes."),
        ("todo add <yyyy-MM-dd|today|tomorrow> [HH:mm] <text...>", "Add a todo, with a time to be reminded at."),
        ("todo add every <weekday|day|month> <text...>", "Add a todo that comes back when it is done."),
        ("todo done <text...>", "Mark the todo with that text done."),
        ("notes", "List note names."),
        ("note show <name...>", "Print a note."),
        ("tags [tag...]", "List tags with counts, or the notes with a tag."),
        ("find <attribute> <value...>", "List the notes with an attribute line, e.g. find role Bereichsleiter."),
        ("search <text...>", "List the notes whose text contains the text, with the first matching line."),
        ("decisions [--full] [tag...]", "List the !d decisions of all notes, or of the notes with a tag, newest first; --full prints the whole text."),
        ("decisions <reference>", "Show decision #!d1234 or #!dab12-1234 with its note, date, people, copies and whole text."),
        ("current", "Today's Times bookings, appointments, due todos and linked notes."),
        ("review", "The last 7 days' changed notes and done todos, overdue todos and the next 7 days' appointments."),
        ("today", "Print today's note, creating it with the day's appointments and due todos."),
        ("dayend [today|yyyy-MM-dd]", "Write Times' worked hours into the day's note and move its due todos to the next day."),
        ("standup", "Print a standup: the last workday's bookings and done todos, today's plan and what is blocked."),
        ("meeting <n>", "Print the note for today's appointment n (as listed by current), creating it first."),
        ("new <title...> [--template <name>]", "Create a note, from templates/<name>.md if given."),
        ("import <path>", "Store a Markdown file as a note."),
        ("export <note> <path>", "Write a note to a new plain file."),
        ("item <n> [--open]", "What mentions work item #n: notes, todos, people, meetings, Times hours, commits; --open opens its page in the desktop app."),
        ("<command> --json", "Print one JSON object: version, todos, notes, note show, tags, find, search, current, review, item."),
    ];

    private readonly TextWriter stdout;
    private readonly TextWriter stderr;
    private readonly string folder;
    private readonly Func<DateOnly> today;
    private readonly Func<DateTime> now;
    private readonly HttpMessageHandler http;
    private readonly TimeZoneInfo zone;
    private readonly string? timesFolder;
    private readonly IDesktopLauncher? launcher;
    private readonly ICommitLog commitLog;

    /// <param name="stdout">Where results go.</param>
    /// <param name="stderr">Where error and warning lines go.</param>
    /// <param name="folder">The data folder.</param>
    /// <param name="today">The local date.</param>
    /// <param name="now">The local time, for capture lines and the calendar download.</param>
    /// <param name="http">The handler the Exchange calendar download goes through.</param>
    /// <param name="timesFolder">Times' data folder (<see cref="TimesFolder.Resolve(string?)"/>), only ever read; <c>null</c> for none.</param>
    /// <param name="launcher">Starts the desktop app for <c>item &lt;n&gt; --open</c>.</param>
    internal Cli(
        TextWriter stdout,
        TextWriter stderr,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http,
        string? timesFolder,
        IDesktopLauncher launcher)
        : this(stdout, stderr, folder, today, now, http, TimeZoneInfo.Local, timesFolder, launcher, new GitCommitLog())
    {
    }

    /// <summary>
    /// Like the other constructor with a fixed zone (tests). Without a launcher <c>--open</c> can't start the desktop
    /// app; without a commit log <c>git</c> is run.
    /// </summary>
    internal Cli(
        TextWriter stdout,
        TextWriter stderr,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http,
        TimeZoneInfo zone,
        string? timesFolder = null,
        IDesktopLauncher? launcher = null,
        ICommitLog? commitLog = null)
    {
        this.launcher = launcher;
        this.commitLog = commitLog ?? new GitCommitLog();
        this.stdout = stdout;
        this.stderr = stderr;
        this.folder = folder;
        this.today = today;
        this.now = now;
        this.http = http;
        this.zone = zone;
        this.timesFolder = timesFolder;
    }

    /// <summary>The random source of a new <c>decision-folder.json</c> prefix (job 349); tests set a fake.</summary>
    internal Func<int, int> DecisionRandom { get; init; } = DecisionFolder.RandomBelow;

    /// <summary>The GUID source of a new <c>decision-folder.json</c> (job 349); tests set a fake.</summary>
    internal Func<Guid> DecisionGuid { get; init; } = Guid.NewGuid;

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
        // The removed secret option is refused anywhere on the line, before every other rule, and never echoed,
        // so an old script can't write a secret into a note as text.
        if (given.Any(a => a.StartsWith(RemovedSecretOption, StringComparison.OrdinalIgnoreCase)))
        {
            return Usage("That option was removed.");
        }

        // --json may stand anywhere and repeat; it is removed before the command and its arguments are parsed.
        bool json = given.Any(CliJson.IsFlag);
        string[] args = json ? [.. given.Where(a => !CliJson.IsFlag(a))] : given;
        if (args.Length == 0)
        {
            return Usage("No command given.");
        }

        string name = args[0].ToLowerInvariant();
        string[] rest = args[1..];
        if (name == "standup" && (json || rest.Length > 0))
        {
            // Checked before the json rule: any argument, --json included, is this usage error, before the folder is read.
            return Usage(StandupForm);
        }

        if (json && name == "item" && rest.Any(IsOpenFlag))
        {
            // The page opens in the desktop app; there is nothing to print as JSON.
            return Usage(ItemForm);
        }

        if (json && (name is "help" or "--help" or "-h" or "capture" or "todo" or "today" or "decisions" or "dayend" or "meeting" or "new" or "import" or "export"))
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
            case "find":
                if (args.Length < 2 || AttributeParser.NormalizeKey(args[0]) is null)
                {
                    usage = FindForm;
                    return null;
                }

                string findKey = args[0];
                string findValue = string.Join(' ', args[1..]);
                if (TagParser.CollapseWhitespace(findValue).Length == 0)
                {
                    usage = FindForm;
                    return null;
                }

                return s => Find(s, findKey, findValue);
            case "search":
                string query = string.Join(' ', args);
                if (query.Trim().Length == 0)
                {
                    usage = SearchForm;
                    return null;
                }

                return s => Search(s, query);
            case "decisions":
                return ParseDecisions(args, out usage);
            case "current":
                return NoArguments("current", args, out usage, Current);
            case "review":
                return NoArguments("review", args, out usage, Review);
            case "today":
                return NoArguments("today", args, out usage, Today);
            case "dayend":
                return ParseDayEnd(args, out usage);
            case "standup":
                // Arguments were refused in RunCommand.
                return Standup;
            case "meeting":
                if (args.Length != 1 || !MeetingNote.TryParseNumber(args[0], out int number))
                {
                    usage = "Type: context meeting <n> with the appointment's number in context current.";
                    return null;
                }

                return session => Meeting(session, number);
            case "new":
                return ParseNew(args, out usage);
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
            case "item":
                return ParseItem(args, out usage);
            default:
                return null;
        }
    }

    // dayend takes nothing, "today" (any case) or one yyyy-MM-dd date.
    private Func<Session, int>? ParseDayEnd(string[] args, out string? usage)
    {
        usage = null;
        if (args.Length == 0 || (args.Length == 1 && args[0].Equals("today", StringComparison.OrdinalIgnoreCase)))
        {
            return session => DayEndCommand(session, today());
        }

        if (args.Length == 1 && DateOnly.TryParseExact(
            args[0], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            && day < DateOnly.MaxValue)
        {
            // The last date has no next day to move todos to, so it is a usage error like any other bad date.
            return session => DayEndCommand(session, day);
        }

        usage = "Type: context dayend [today|yyyy-MM-dd].";
        return null;
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

    // context new <title...> [--template <name...>]: the words before --template (ignoring case) are the title, the
    // words after it the template name. A missing title or template name is a usage error.
    private Func<Session, int>? ParseNew(string[] args, out string? usage)
    {
        const string NewForm = "Type: context new <title> [--template <name>].";
        usage = null;
        int flag = Array.FindIndex(args, a => string.Equals(a, "--template", StringComparison.OrdinalIgnoreCase));
        string title = string.Join(' ', flag < 0 ? args : args[..flag]).Trim();
        string? template = flag < 0 ? null : string.Join(' ', args[(flag + 1)..]).Trim();
        if (title.Length == 0 || template is { Length: 0 })
        {
            usage = NewForm;
            return null;
        }

        return template is null ? s => NewNote(s, title) : s => NewNoteFromTemplate(s, title, template);
    }

    private Func<Session, int>? ParseTodo(string[] args, out string? usage)
    {
        usage = null;
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        if (sub == "add" && args.Length > 1 && string.Equals(args[1], "every", StringComparison.OrdinalIgnoreCase))
        {
            const string EveryForm = "Type: context todo add every <weekday|day|month> <text>.";
            string text = args.Length > 3 ? string.Join(' ', args[3..]).Trim() : string.Empty;
            if (text.Length == 0)
            {
                usage = $"A repeating todo needs a repeat and text. {EveryForm}";
                return null;
            }

            if (!TodoInput.TryParseRepeat(args[2], today(), out TodoRepeat repeat, out DateOnly firstDue))
            {
                usage = $"That repeat isn't valid. {EveryForm}";
                return null;
            }

            if (text.Length > TodoInput.MaxTextLength)
            {
                usage = $"A todo can be at most {TodoInput.MaxTextLength} characters.";
                return null;
            }

            return s => AddTodo(s, text, firstDue, repeat, null);
        }

        if (sub == "add")
        {
            const string AddForm = "Type: context todo add <yyyy-MM-dd|today|tomorrow> [HH:mm] <text>.";

            // A time-shaped word right after the date is the time, never the start of the text.
            bool timed = args.Length > 2 && TodoInput.LooksLikeTime(args[2]);
            int textStart = timed ? 3 : 2;
            string text = args.Length > textStart ? string.Join(' ', args[textStart..]).Trim() : string.Empty;
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

            TimeOnly? time = null;
            if (timed)
            {
                if (!TodoInput.TryParseTime(args[2], out TimeOnly parsedTime))
                {
                    usage = $"That time isn't valid. {AddForm}";
                    return null;
                }

                time = parsedTime;
            }

            if (text.Length > TodoInput.MaxTextLength)
            {
                usage = $"A todo can be at most {TodoInput.MaxTextLength} characters.";
                return null;
            }

            return s => AddTodo(s, text, due, null, time);
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

        if (result == CaptureResult.Captured)
        {
            NumberDecisions(session, Inbox.NoteName);
        }

        return result switch
        {
            CaptureResult.Captured => Done("Captured."),
            CaptureResult.TooLarge => Error($"{Inbox.NoteName} is too large. Nothing was captured."),
            _ => Error($"{Inbox.NoteName} kept changing on disk. Nothing was captured."),
        };
    }

    /// <summary>
    /// Before the first read: converts the old <c>- [ ]</c> note todos to <c>- { }</c> once per data folder (job 329,
    /// <see cref="TodoMarkerConversion"/>; its report lines on stderr, the exit code unchanged), then stores missing
    /// todo ids; when bad ids were replaced, says so in one stderr line (a fixed text with the count, never the bad value).
    /// </summary>
    private void MigrateTodos(Session session)
    {
        NoteHistory history = new NoteHistory(session.Notes, () => now().ToUniversalTime());
        TodoMarkerConversionResult converted =
            TodoMarkerConversion.Run(session.Notes, history, () => now().ToUniversalTime());
        foreach (string line in TodoMarkerConversion.Report(converted, OneLine))
        {
            Err(line);
        }

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

        // A bad context.db still lists the todos.json todos, then says so.
        IReadOnlyList<NoteTodo> noteTodos;
        bool indexFailed = false;
        try
        {
            noteTodos = NoteTodos.Load(session.Notes, session.Index);
        }
        catch (TagIndexException)
        {
            noteTodos = [];
            indexFailed = true;
        }

        IReadOnlyList<TodoEntry> entries = NoteTodos.Merge(todos, noteTodos);
        if (session.Json)
        {
            Json(w =>
            {
                w.WriteStartArray("todos");
                foreach (TodoEntry entry in entries)
                {
                    w.WriteStartObject();
                    if (entry.Due is { } due)
                    {
                        w.WriteString("due", Date(due));
                    }
                    else
                    {
                        w.WriteNull("due");
                    }

                    CliJson.WriteTime(w, "time", entry.Stored?.Time);
                    w.WriteString("text", OneLine(entry.Text));
                    if (entry.Note is { } note)
                    {
                        w.WriteString("note", OneLine(note.Note));
                    }
                    else
                    {
                        w.WriteNull("note");
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
            });
        }
        else
        {
            foreach (TodoEntry entry in entries)
            {
                if (entry.Stored is { } todo)
                {
                    Out($"{When(todo)}\t{OneLine(todo.Text)}");
                }
                else
                {
                    NoteTodo note = entry.Note!;
                    string when = note.Due is { } due ? Date(due) : "-";
                    Out($"{when}\t{OneLine(note.Text)}\t{OneLine(note.Note)}");
                }
            }
        }

        return indexFailed ? Error(IndexUnreadable) : ExitOk;
    }

    private int AddTodo(Session session, string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time)
    {
        MigrateTodos(session);
        TodoItem todo;
        try
        {
            todo = session.Todos.Add(text, due, repeat, time);
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        return Done($"Added: {When(todo)} {OneLine(todo.Text)}");
    }

    private int DoneTodo(Session session, string query)
    {
        MigrateTodos(session);
        TodoItem? done;
        TodoItem? next;
        try
        {
            IReadOnlyList<TodoItem> todos = session.Todos.List();
            IReadOnlyList<NoteTodo> noteTodos;
            try
            {
                noteTodos = NoteTodos.Load(session.Notes, session.Index);
            }
            catch (TagIndexException)
            {
                return Error(IndexUnreadable);
            }

            IReadOnlyList<TodoEntry> entries = NoteTodos.Merge(todos, noteTodos);
            if (entries.Count == 0)
            {
                return Error(TodoPicker.NoTodosText);
            }

            TodoPick pick = TodoPicker.PickEntry(entries, query);
            if (pick.Entry is null)
            {
                return Error(TodoPicker.NoPickText(pick, OneLine(query)));
            }

            if (pick.Entry.Note is { } noteTodo)
            {
                return DoneNoteTodo(session, noteTodo);
            }

            // The text only picks the todo; it is removed by its id, so a list that changed in between can't
            // make another todo go away.
            done = session.Todos.Complete(pick.Entry.Stored!.Id, today(), out next);
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        if (done is null)
        {
            return Error("The todo list changed. Nothing was done.");
        }

        LogDone(session, done.Text, done.Due);
        if (next is null)
        {
            return Done($"Done: {OneLine(done.Text)}");
        }

        Out($"Done: {OneLine(done.Text)}");
        return Done($"Next: {Date(next.Due)}");
    }

    // Ticks a todo inside a note, only when its line is still as indexed.
    private int DoneNoteTodo(Session session, NoteTodo todo)
    {
        string note = OneLine(todo.Note);
        NoteTodoResult result = NoteTodos.Complete(session.Notes, todo);
        if (result == NoteTodoResult.Done)
        {
            LogDone(session, todo.Text, todo.Due);
        }

        return result switch
        {
            NoteTodoResult.Done => Done($"Done: {OneLine(todo.Text)}"),
            NoteTodoResult.Missing => Error(NoteTodos.MissingText(note)),
            NoteTodoResult.Failed => Error(NoteTodos.FailedText(note)),
            _ => Error(NoteTodos.ChangedText(note)),
        };
    }

    // Appends a completed todo to the done log; a failure is one stderr line and keeps the exit code.
    private void LogDone(Session session, string text, DateOnly? due)
    {
        if (DoneLog.Append(session.Notes, today(), text, due) != DoneLogResult.Appended)
        {
            Err(DoneLogFailed);
        }
    }

    private int Notes(Session session)
    {
        // Sensitive notes are listed by name only, marked locked; their text is never read here. They are top-level
        // only, so they sort among the top-level notes, and each plain note keeps its subpages right after it.
        List<List<(NoteInfo Note, bool Locked)>> blocks = [];
        foreach (NoteInfo note in session.Notes.List())
        {
            if (note.Parent is null || blocks.Count == 0)
            {
                blocks.Add([]);
            }

            blocks[^1].Add((note, false));
        }

        blocks.AddRange(session.Notes.ListSensitive().Select(n => new List<(NoteInfo Note, bool Locked)> { (n, true) }));
        IEnumerable<(NoteInfo Note, bool Locked)> all = blocks
            .OrderBy(b => b[0].Note.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b[0].Note.Name, StringComparer.Ordinal)
            .SelectMany(b => b);
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

    // Prints today's note like note show, creating it first (title, appointments, due todos) when it is missing.
    private int Today(Session session)
    {
        DateOnly day = today();
        DailyNoteResult result;
        try
        {
            result = DailyNote.Ensure(
                session.Notes, day, () => DailyNote.Compose(session.Todos, session.Calendar, session.Feed, day));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't write {DailyNote.NameFor(day)}.");
        }

        if (result.Kind == DailyNoteKind.Sensitive)
        {
            return Error(SensitiveText(OneLine(result.Name)));
        }

        return ShowNote(session, result.Name);
    }

    // Closes the day: Times' worked hours into its daily note, then its due todos moved to the next day. Never reads
    // or writes settings.json.
    private int DayEndCommand(Session session, DateOnly day)
    {
        DayEnd dayEnd = new DayEnd(session.Notes, session.Todos, session.Calendar, session.Feed, timesFolder, null);
        DayEndResult result = dayEnd.CloseDay(day);
        switch (result.Kind)
        {
            case DayEndKind.Sensitive:
                return Error(SensitiveText(OneLine(result.Name)));
            case DayEndKind.TodosUnreadable:
                return Error("Can't read todos.json.");
            case DayEndKind.Done:
                NumberDecisions(session, result.Name);
                Out($"Updated {Date(day)}.");
                Out($"Moved {result.Moved.Count} todo(s) to {Date(result.To)}.");
                return ExitOk;
            default:
                return Error($"Can't write {OneLine(result.Name)}.");
        }
    }

    // Prints today's standup (job 265). Writes nothing apart from Migrate; never opens context.db or settings.json.
    private int Standup(Session session)
    {
        MigrateTodos(session);
        string text = Context.Standup.Compose(
            session.Todos, session.Calendar, session.Feed, timesFolder, today(), zone, out bool todosUnreadable);
        stdout.Write(text);
        return todosUnreadable ? Error(TodosUnreadable) : ExitOk;
    }

    // Prints the note for today's appointment number (1-based, current view order) like note show, creating it first
    // with the appointment's title, time, location and attendees when it is missing.
    private int Meeting(Session session, int number)
    {
        IReadOnlyList<Appointment> appointments = MeetingNote.Today(session.Calendar, session.Feed, today());
        if (MeetingNote.Pick(appointments, number, out string? error) is not { } appointment)
        {
            return Error(error ?? MeetingNote.NoAppointmentsText);
        }

        MeetingNoteResult result;
        try
        {
            result = MeetingNote.Ensure(session.Notes, appointment);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error(MeetingNote.WriteFailedText(MeetingNote.NameFor(appointment)));
        }

        if (result.Kind == MeetingNoteKind.Sensitive)
        {
            return Error(SensitiveText(OneLine(result.Name)));
        }

        return ShowNote(session, result.Name);
    }

    /// <summary>The error line for a command that would need the text of a sensitive note.</summary>
    internal static string SensitiveText(string name) => $"{name} is sensitive. Open it in the desktop app.";

    // The error for a note over NoteStore.MaxNoteBytes; name is already passed through OneLine.
    private static string TooLargeText(string name) => $"Can't read {name}: it is over 5 MB.";

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
        catch (FileTooLargeException)
        {
            return Error(TooLargeText(OneLine(name.Trim())));
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

    private int Find(Session session, string key, string value)
    {
        FindView view = FindView.Build(session.Index, session.Notes, key, value);
        switch (view.Kind)
        {
            case FindViewKind.InvalidQuery:
                return Usage(FindForm);
            case FindViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            case FindViewKind.Found or FindViewKind.NoMatches when session.Json:
                return Json(w =>
                {
                    w.WriteString("key", OneLine(view.Key));
                    w.WriteString("value", OneLine(view.Value));
                    CliJson.Strings(w, "notes", view.Notes.Select(OneLine));
                });
            default:
                // NoMatches lists nothing, like an unknown tag.
                foreach (string note in view.Notes)
                {
                    Out(OneLine(note));
                }

                return ExitOk;
        }
    }

    // decisions [--full] [tag...] | decisions <ref>: 1234, #!d1234, ab12-1234 or #!dab12-1234 (any case); a reference
    // never comes with anything else.
    private Func<Session, int>? ParseDecisions(string[] args, out string? usage)
    {
        usage = null;
        if (args.Any(a => DecisionParser.TryParseReference(a, out _)))
        {
            if (args.Length != 1 || !DecisionParser.TryParseReference(args[0], out DecisionRef reference))
            {
                usage = DecisionsForm;
                return null;
            }

            return s => ShowDecision(s, reference);
        }

        bool full = args.Any(a => string.Equals(a, "--full", StringComparison.OrdinalIgnoreCase));
        string tag = string.Join(' ', args.Where(a => !string.Equals(a, "--full", StringComparison.OrdinalIgnoreCase)));
        if (!DecisionsView.TryNormalizeTag(tag, out _))
        {
            usage = DecisionsForm;
            return null;
        }

        return s => Decisions(s, tag, full);
    }

    private int ShowDecision(Session session, DecisionRef reference)
    {
        DecisionFolder.Load(folder, DecisionRandom, DecisionGuid);
        DecisionsView view = DecisionsView.Find(session.Index, session.Notes, reference, zone);
        switch (view.Kind)
        {
            case DecisionsViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            case DecisionsViewKind.Found:
                Decision decision = view.Decisions[0];
                Out(decision.Reference ?? DecisionParser.Format(reference.Prefix, reference.Number));
                Out($"Note: {OneLine(decision.Note)}");
                Out($"Date: {Date(decision.Date)}");
                Out($"People: {string.Join(", ", decision.People.Select(OneLine))}");
                if (decision.Places.Count > 0)
                {
                    Out($"Also in: {string.Join(", ", decision.Places.Select(p => OneLine(p.Note)).Distinct(StringComparer.Ordinal))}");
                }

                Out(string.Empty);
                foreach (string line in decision.Text.Split('\n'))
                {
                    Out(OneLine(line));
                }

                return ExitOk;
            default:
                string? own = DecisionFolder.Read(session.Notes.Root)?.Prefix;
                return Error($"No decision {DecisionParser.Token}{DecisionsView.LabelOf(reference.Prefix, reference.Number, own)}.");
        }
    }

    // Job 349: after a command wrote the plain note `name`, its !d markers are numbered in a second write
    // (DecisionNumbering.NumberWritten), once decision-folder.json reads fine (created when missing), its previous bytes
    // kept as a history version first (job 351). Any failure (also of the history) leaves
    // the text as written and the exit code unchanged.
    private void NumberDecisions(Session session, string name)
    {
        try
        {
            if (DecisionFolder.Load(folder, DecisionRandom, DecisionGuid) is { } decisionFolder)
            {
                NoteHistory history = new NoteHistory(session.Notes, () => now().ToUniversalTime());
                DecisionNumbering.NumberWritten(session.Index, session.Notes, history, decisionFolder, name);
            }
        }
        catch (Exception)
        {
            // Left to the next save.
        }
    }

    private int Decisions(Session session, string tag, bool full)
    {
        DecisionFolder.Load(folder, DecisionRandom, DecisionGuid);
        DecisionsView view = DecisionsView.Build(session.Index, session.Notes, tag, zone);
        switch (view.Kind)
        {
            case DecisionsViewKind.InvalidTag:
                return Usage(DecisionsForm);
            case DecisionsViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            default:
                // NoDecisions lists nothing, like search.
                foreach (Decision decision in view.Decisions)
                {
                    string people = string.Join(", ", decision.People.Select(OneLine));
                    string number = decision.Reference ?? string.Empty;
                    string text = OneLine(full ? decision.Text : decision.Headline);
                    Out($"{Date(decision.Date)}\t{number}\t{OneLine(decision.Note)}\t{people}\t{text}");
                }

                return ExitOk;
        }
    }

    private int Search(Session session, string text)
    {
        SearchView view = SearchView.Build(session.Index, session.Notes, text);
        switch (view.Kind)
        {
            case SearchViewKind.TooShort:
                return Error($"Type at least {SearchView.MinQueryLength} characters to search.");
            case SearchViewKind.TooLong:
                return Error($"Search text can be at most {SearchView.MaxQueryLength} characters.");
            case SearchViewKind.IndexUnreadable:
                return Error(IndexUnreadable);
            case SearchViewKind.Found or SearchViewKind.NoMatches when session.Json:
                return Json(w =>
                {
                    w.WriteString("query", OneLine(view.Query));
                    w.WriteStartArray("results");
                    foreach (SearchHit hit in view.Hits)
                    {
                        w.WriteStartObject();
                        w.WriteString("note", OneLine(hit.Note));
                        w.WriteNumber("line", hit.LineNumber);
                        w.WriteString("text", OneLine(hit.Line));
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                });
            default:
                // NoMatches lists nothing, like find.
                foreach (SearchHit hit in view.Hits)
                {
                    Out($"{OneLine(hit.Note)}\t{hit.LineNumber.ToString(CultureInfo.InvariantCulture)}\t{OneLine(hit.Line)}");
                }

                return ExitOk;
        }
    }

    // One appointment as a JSON object: start, end, allDay, title, location (null when missing).
    private static void WriteAppointment(
        Utf8JsonWriter w, Appointment appointment, TimeZoneInfo zone, Action<Utf8JsonWriter>? extra = null)
    {
        w.WriteStartObject();
        w.WriteString("start", CliJson.Time(appointment.Start, zone));
        w.WriteString("end", CliJson.Time(appointment.End, zone));
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

        extra?.Invoke(w);
        w.WriteEndObject();
    }

    // current's related:[{name,tags,words}] of one appointment, or null when the index can't be read.
    private static void WriteRelated(Utf8JsonWriter w, IReadOnlyList<RelatedNote>? related)
    {
        if (related is null)
        {
            w.WriteNull("related");
            return;
        }

        w.WriteStartArray("related");
        foreach (RelatedNote note in related)
        {
            w.WriteStartObject();
            w.WriteString("name", OneLine(note.Name));
            CliJson.Strings(w, "tags", note.Tags.Select(OneLine));
            CliJson.Strings(w, "words", note.Words.Select(OneLine));
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    // The third column of a related line: the tags as #tag or #[a b], then the shared title words.
    private static string RelatedMatches(RelatedNote note) =>
        OneLine(string.Join(' ', note.Tags.Select(TagParser.Format).Concat(note.Words)));

    private int Current(Session session)
    {
        MigrateTodos(session);
        CurrentView view = CurrentView.Build(
            session.Todos, session.Index, session.Notes, session.Calendar, session.Feed, today());
        TimesNow times = TimesNow.Read(timesFolder, view.Today, TimeOnly.FromDateTime(now()));
        if (session.Json)
        {
            return CurrentJson(view, times);
        }

        bool failed = false;
        Out($"Today, {Date(view.Today)}");
        if (times.Kind == TimesNowKind.Booked)
        {
            Out("Times");
            Out($"arrival\t{TimesTime(times.Arrival)}");
            if (times.Leaving is not null)
            {
                Out($"leaving\t{TimesTime(times.Leaving)}");
            }

            Out($"left\t{TimesNow.FormatLeft(times.Left)}");
            if (times.Checkpoint is { } checkpoint)
            {
                Out($"checkpoint\t{TimesCheckpointText(checkpoint)}");
            }
        }
        else if (times.Kind == TimesNowKind.Unreadable)
        {
            failed = true;
            Err(TimesUnreadable(view.Today));
        }

        Out("Appointments");
        if (view.Appointments.Count == 0)
        {
            Out($"No appointments today. Put .ics files in {OneLine(view.Folder)} to see them here.");
        }

        for (int i = 0; i < view.Appointments.Count; i++)
        {
            Appointment appointment = view.Appointments[i];
            string title = appointment.Title.Length == 0 ? "(no title)" : appointment.Title;
            Out($"{view.TimeText(appointment)}\t{OneLine(title)}\t{OneLine(appointment.Location ?? string.Empty)}");
            foreach (RelatedNote note in view.Related[i])
            {
                Out($"related\t{OneLine(note.Name)}\t{RelatedMatches(note)}");
            }
        }

        // The index is read for the appointments too, so a bad context.db is reported once, also with nothing due.
        bool relatedFailed = view.IndexUnreadable && view.Appointments.Count > 0;

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
            if (relatedFailed)
            {
                Err(IndexUnreadable);
            }

            return ExitError;
        }

        Out("Due");
        if (view.Due.Count == 0)
        {
            Out("Nothing due today. Add one with /todo today <text>.");
            if (relatedFailed)
            {
                Err(IndexUnreadable);
                return ExitError;
            }

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

    // Today's Times line texts (job 277): a booked time as HH:mm, a checkpoint as [#<n> ]<name> on one line.
    private static string TimesTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string TimesCheckpointText(TimesCheckpoint checkpoint) =>
        (checkpoint.WorkItem is { } n ? "#" + n.ToString(CultureInfo.InvariantCulture) + " " : string.Empty)
        + OneLine(checkpoint.Name);

    private static string TimesUnreadable(DateOnly day) => $"Can't read Times' day file for {Date(day)}.";

    // current as one JSON document: the same stderr lines and exit code as plain, no hint texts.
    private int CurrentJson(CurrentView view, TimesNow times)
    {
        bool failed = false;
        if (times.Kind == TimesNowKind.Unreadable)
        {
            failed = true;
            Err(TimesUnreadable(view.Today));
        }

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

        // As in plain: the index is looked at when something is due or there is an appointment; reported once.
        bool indexFailed = !view.TodosUnreadable && view.Due.Count > 0 && view.IndexUnreadable;
        bool relatedFailed = view.Appointments.Count > 0 && view.IndexUnreadable;
        if (view.TodosUnreadable)
        {
            failed = true;
            Err(TodosUnreadable);
        }

        if (indexFailed || relatedFailed)
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
            for (int i = 0; i < view.Appointments.Count; i++)
            {
                IReadOnlyList<RelatedNote>? related = relatedFailed ? null : view.Related[i];
                WriteAppointment(w, view.Appointments[i], shownZone, writer => WriteRelated(writer, related));
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

            WriteTimes(w, times);
        }));
        return failed ? ExitError : ExitOk;
    }

    // "times": null unless booked, else {arrival, leaving, leftMinutes, checkpoint:{workItem,name}|null} (job 277).
    private static void WriteTimes(Utf8JsonWriter w, TimesNow times)
    {
        if (times.Kind != TimesNowKind.Booked)
        {
            w.WriteNull("times");
            return;
        }

        w.WriteStartObject("times");
        w.WriteString("arrival", TimesTime(times.Arrival));
        if (times.Leaving is null)
        {
            w.WriteNull("leaving");
        }
        else
        {
            w.WriteString("leaving", TimesTime(times.Leaving));
        }

        w.WriteNumber("leftMinutes", times.Left.Ticks / TimeSpan.TicksPerMinute);
        if (times.Checkpoint is { } checkpoint)
        {
            w.WriteStartObject("checkpoint");
            if (checkpoint.WorkItem is { } n)
            {
                w.WriteNumber("workItem", n);
            }
            else
            {
                w.WriteNull("workItem");
            }

            w.WriteString("name", OneLine(checkpoint.Name));
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("checkpoint");
        }

        w.WriteEndObject();
    }

    private int Review(Session session)
    {
        MigrateTodos(session);
        ReviewView view = ReviewView.Build(session.Notes, session.Todos, session.Calendar, session.Feed, today());
        if (session.Json)
        {
            return ReviewJson(view);
        }

        Out($"Review, {Date(view.From)} to {Date(view.To)}");
        Out("Changed notes");
        foreach (ChangedNote note in view.ChangedNotes)
        {
            Out($"{note.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}\t{OneLine(note.Name)}");
        }

        if (!view.DoneLogUnreadable)
        {
            Out("Done todos");
            foreach (DoneEntry entry in view.Done)
            {
                Out($"{Date(entry.Date)}\t{OneLine(entry.Text)}");
            }

            if (view.Done.Count == 0)
            {
                Out("No todos done.");
            }
        }

        Out("Overdue");
        foreach (TodoItem todo in view.Overdue)
        {
            Out($"{Date(todo.Due)}\t{OneLine(todo.Text)}");
        }

        Out("Next week");
        foreach (ReviewDay day in view.Days)
        {
            foreach (Appointment appointment in day.Appointments)
            {
                string title = appointment.Title.Length == 0 ? "(no title)" : appointment.Title;
                string where = appointment.Location is null ? string.Empty : $" ({appointment.Location})";
                Out($"{Date(day.Date)}\t{day.TimeText(appointment)}\t{OneLine(title + where)}");
            }
        }

        if (view.Status == CalendarFeedStatus.Stale && view.CachedAtUtc is not null)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), view.Zone);
            Out($"Exchange calendar offline, showing the copy from {local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}.");
        }

        return ReviewErrors(view);
    }

    // review's stderr lines (calendar files, a failed feed, todos.json, the done log), in that order, and its exit code.
    private int ReviewErrors(ReviewView view)
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

        if (view.TodosUnreadable)
        {
            failed = true;
            Err(TodosUnreadable);
        }

        if (view.DoneLogUnreadable)
        {
            failed = true;
            Err(DoneLogUnreadable);
        }

        return failed ? ExitError : ExitOk;
    }

    // review as one JSON document: the same stderr lines and exit code as plain.
    private int ReviewJson(ReviewView view)
    {
        int exit = ReviewErrors(view);
        TimeZoneInfo shownZone = view.Zone;
        string? cachedAt = view.CachedAtUtc is null || view.Status is not (CalendarFeedStatus.Stale or CalendarFeedStatus.Fresh)
            ? null
            : CliJson.Time(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(view.CachedAtUtc.Value, DateTimeKind.Utc), shownZone),
                shownZone);
        stdout.Write(CliJson.Document(w =>
        {
            w.WriteString("from", Date(view.From));
            w.WriteString("to", Date(view.To));
            w.WriteStartArray("changedNotes");
            foreach (ChangedNote note in view.ChangedNotes)
            {
                w.WriteStartObject();
                w.WriteString("name", OneLine(note.Name));
                w.WriteString("modified", CliJson.Time(note.Modified, shownZone));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            if (view.DoneLogUnreadable)
            {
                w.WriteNull("doneTodos");
            }
            else
            {
                w.WriteStartArray("doneTodos");
                foreach (DoneEntry entry in view.Done)
                {
                    w.WriteStartObject();
                    if (entry.Due is { } due)
                    {
                        w.WriteString("due", Date(due));
                    }
                    else
                    {
                        w.WriteNull("due");
                    }

                    w.WriteString("done", Date(entry.Date));
                    w.WriteString("text", OneLine(entry.Text));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            if (view.TodosUnreadable)
            {
                w.WriteNull("overdue");
            }
            else
            {
                w.WriteStartArray("overdue");
                foreach (TodoItem todo in view.Overdue)
                {
                    w.WriteStartObject();
                    w.WriteString("due", Date(todo.Due));
                    w.WriteString("text", OneLine(todo.Text));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteStartArray("appointments");
            foreach (ReviewDay day in view.Days)
            {
                foreach (Appointment appointment in day.Appointments)
                {
                    WriteAppointment(w, appointment, shownZone);
                }
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
        }));
        return exit;
    }

    private static bool IsOpenFlag(string arg) => string.Equals(arg, OpenFlag, StringComparison.OrdinalIgnoreCase);

    // item <n> [--open]: exactly one number (1 to 9 digits, first 1-9, an optional #) and the flag at most once.
    private Func<Session, int>? ParseItem(string[] args, out string? usage)
    {
        usage = null;
        bool open = args.Count(IsOpenFlag) == 1;
        string[] numbers = [.. args.Where(a => !IsOpenFlag(a))];
        if (args.Count(IsOpenFlag) > 1 || numbers.Length != 1 || !WorkItemParser.TryParseNumber(numbers[0], out int item))
        {
            usage = ItemForm;
            return null;
        }

        return open ? s => OpenItem(s, item) : s => Item(s, item);
    }

    private int Item(Session session, int item)
    {
        MigrateTodos(session);
        IReadOnlyList<string> repositories;
        bool settingsUnreadable = false;
        try
        {
            repositories = session.Settings.ReadRepositories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            repositories = [];
            settingsUnreadable = true;
        }

        WorkItemView view = WorkItemView.Build(
            session.Index, session.Notes, session.Todos, session.Calendar, session.Feed, timesFolder, repositories, commitLog, item);
        int exit = view.IndexUnreadable || view.TodosUnreadable || view.ArchiveUnreadable || settingsUnreadable
            || view.Hours.SkippedFiles > 0
            || view.CommitsKind == WorkItemCommitsKind.GitMissing || view.FailedRepos.Count > 0
            ? ExitError
            : ExitOk;
        if (session.Json)
        {
            ItemJson(view);
        }
        else
        {
            ItemText(view, settingsUnreadable);
        }

        // The error lines, after what could be read; the same for both outputs.
        if (view.IndexUnreadable)
        {
            Err(IndexUnreadable);
        }

        if (view.TodosUnreadable)
        {
            Err(TodosUnreadable);
        }

        if (view.ArchiveUnreadable)
        {
            Err(ArchiveUnreadable);
        }

        if (view.Hours.SkippedFiles > 0)
        {
            Err($"Skipped {view.Hours.SkippedFiles.ToString(CultureInfo.InvariantCulture)} unreadable Times day file(s).");
        }

        if (settingsUnreadable)
        {
            Err("Can't read settings.json.");
        }

        if (view.CommitsKind == WorkItemCommitsKind.GitMissing)
        {
            Err("Can't run git.");
        }

        foreach (string repo in view.FailedRepos)
        {
            Err($"Can't read commits in {OneLine(repo)}.");
        }

        return exit;
    }

    private void ItemText(WorkItemView view, bool settingsUnreadable)
    {
        Out($"Item #{view.Item.ToString(CultureInfo.InvariantCulture)}");
        Out("Notes");
        foreach (string note in view.Notes)
        {
            Out(OneLine(note));
        }

        Out("Todos");
        foreach (TodoItem todo in view.OpenTodos)
        {
            Out($"{Date(todo.Due)}\t{OneLine(todo.Text)}");
        }

        foreach (TodoItem todo in view.DoneTodos)
        {
            Out($"done\t{Date(view.DoneDate(todo))}\t{OneLine(todo.Text)}");
        }

        Out("People");
        foreach (WorkItemPerson person in view.People)
        {
            Out($"{OneLine(person.Name)}\t{person.Count.ToString(CultureInfo.InvariantCulture)}");
        }

        Out("Meetings");
        foreach (Appointment meeting in view.Meetings)
        {
            string when = meeting.AllDay
                ? meeting.Start.ToString(DateFormat, CultureInfo.InvariantCulture)
                : meeting.Start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string title = meeting.Title.Length == 0 ? "(no title)" : meeting.Title;
            string where = meeting.Location is null ? string.Empty : $" ({meeting.Location})";
            Out($"{when}\t{OneLine(title + where)}");
        }

        Out("Hours");
        if (view.TimesKind == WorkItemTimesKind.NotSetUp)
        {
            Out("- Times isn't set up.");
        }
        else if (view.TimesKind == WorkItemTimesKind.Read)
        {
            foreach (WorkItemWeek week in view.Hours.Weeks)
            {
                Out($"{week.Week}\t{TimesNow.FormatLeft(week.Total)}");
            }

            Out($"total\t{TimesNow.FormatLeft(view.Hours.Total)}");
        }

        Out("Commits");
        if (view.CommitsKind == WorkItemCommitsKind.NotSetUp && !settingsUnreadable)
        {
            Out("- No repository folders set.");
        }

        foreach (WorkItemCommit commit in view.Commits)
        {
            string hash = commit.Hash.Length > 7 ? commit.Hash[..7] : commit.Hash;
            Out($"{Date(commit.Date)}\t{OneLine(commit.Repo)}\t{OneLine(hash)}\t{OneLine(commit.Subject)}");
        }
    }

    // {item,notes,todos,people,meetings,hours,commits}; a part that couldn't be read is null.
    private void ItemJson(WorkItemView view)
    {
        stdout.Write(CliJson.Document(w =>
        {
            w.WriteNumber("item", view.Item);
            if (view.IndexUnreadable)
            {
                w.WriteNull("notes");
            }
            else
            {
                CliJson.Strings(w, "notes", view.Notes.Select(OneLine));
            }

            if (view.TodosUnreadable)
            {
                w.WriteNull("todos");
            }
            else
            {
                w.WriteStartArray("todos");
                foreach (TodoItem todo in view.OpenTodos.Concat(view.DoneTodos))
                {
                    w.WriteStartObject();
                    w.WriteString("due", Date(todo.Due));
                    CliJson.WriteTime(w, "time", todo.Time);
                    w.WriteString("text", OneLine(todo.Text));
                    if (todo.Done is null)
                    {
                        w.WriteNull("done");
                    }
                    else
                    {
                        w.WriteString("done", Date(view.DoneDate(todo)));
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            if (view.IndexUnreadable)
            {
                w.WriteNull("people");
            }
            else
            {
                w.WriteStartArray("people");
                foreach (WorkItemPerson person in view.People)
                {
                    w.WriteStartObject();
                    w.WriteString("name", OneLine(person.Name));
                    w.WriteNumber("count", person.Count);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteStartArray("meetings");
            foreach (Appointment meeting in view.Meetings)
            {
                WriteAppointment(w, meeting, view.Zone);
            }

            w.WriteEndArray();
            if (view.TimesKind != WorkItemTimesKind.Read)
            {
                w.WriteNull("hours");
            }
            else
            {
                w.WriteStartObject("hours");
                w.WriteStartArray("weeks");
                foreach (WorkItemWeek week in view.Hours.Weeks)
                {
                    w.WriteStartObject();
                    w.WriteString("week", week.Week);
                    w.WriteNumber("minutes", week.Total.Ticks / TimeSpan.TicksPerMinute);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteNumber("totalMinutes", view.Hours.Total.Ticks / TimeSpan.TicksPerMinute);
                w.WriteNumber("skippedFiles", view.Hours.SkippedFiles);
                w.WriteEndObject();
            }

            if (view.CommitsKind != WorkItemCommitsKind.Read)
            {
                w.WriteNull("commits");
            }
            else
            {
                w.WriteStartArray("commits");
                foreach (WorkItemCommit commit in view.Commits)
                {
                    w.WriteStartObject();
                    w.WriteString("repo", OneLine(commit.Repo));
                    w.WriteString("hash", OneLine(commit.Hash));
                    w.WriteString("date", Date(commit.Date));
                    w.WriteString("subject", OneLine(commit.Subject));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
        }));
    }

    // item <n> --open: the page is created when missing, the request written, and the desktop app started when it
    // doesn't hold its lock.
    private int OpenItem(Session session, int item)
    {
        WorkItemPage page;
        try
        {
            page = WorkItems.EnsurePage(session.Notes, item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Error($"Can't write {OneLine(WorkItems.PageNote(item))}.");
        }

        if (page.Kind == WorkItemPageKind.Sensitive)
        {
            return Error(SensitiveText(OneLine(page.Name)));
        }

        if (page.Kind == WorkItemPageKind.Created)
        {
            Out($"Created {OneLine(page.Name)}.");
        }

        bool start;
        try
        {
            DesktopRequest.WriteItem(folder, item);
            start = DesktopRequest.IsLockFree(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error(LaunchFailed);
        }

        if (start)
        {
            try
            {
                (launcher ?? throw new PlatformNotSupportedException()).Launch();
            }
            catch (Exception)
            {
                // The request stays: the desktop app opens the page when it is started by hand.
                return Error(LaunchFailed);
            }
        }

        return Done($"Opened #{item.ToString(CultureInfo.InvariantCulture)}.");
    }

    private int NewNote(Session session, string title)
    {
        string? invalid = NoteStore.ValidateTitle(NoteStore.NormalizeTitle(title));
        if (invalid is not null)
        {
            return Error(invalid);
        }

        try
        {
            NoteInfo created = session.Notes.Create(title);
            NumberDecisions(session, created.Name);
            Out($"Created {OneLine(created.Name)}");
            return ExitOk;
        }
        catch (NoteExistsException e)
        {
            return Error($"Note {OneLine(e.Name)} already exists.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't write {OneLine(NoteStore.NormalizeTitle(title))}.");
        }
    }

    private int NewNoteFromTemplate(Session session, string title, string template)
    {
        TemplateNote prepared = new NoteTemplates(session.Notes).Prepare(title, template, now());
        string name = OneLine(prepared.Template);
        switch (prepared.Kind)
        {
            case TemplateNoteKind.InvalidTitle:
                return Error(prepared.Text);
            case TemplateNoteKind.Exists:
                return Error($"Note {OneLine(prepared.Title)} already exists.");
            case TemplateNoteKind.TemplatesSensitive:
                return Error($"{NoteTemplates.FolderName} is sensitive, so its templates can't be used.");
            case TemplateNoteKind.Missing:
                return Error(prepared.Templates.Count == 0
                    ? $"No template called {name}. Add one as {NoteTemplates.FolderName}/{name}.md."
                    : $"No template called {name}. Templates: {string.Join(", ", prepared.Templates.Select(OneLine))}.");
            case TemplateNoteKind.Unreadable:
                return Error($"Can't read template {name}.");
            case TemplateNoteKind.TooBig:
                return Error($"Template {name} is too big.");
        }

        try
        {
            NoteInfo created = session.Notes.CreateWithText(prepared.Title, prepared.Text);
            NumberDecisions(session, created.Name);
            Out($"Created {OneLine(created.Name)}");
            return ExitOk;
        }
        catch (NoteExistsException e)
        {
            return Error($"Note {OneLine(e.Name)} already exists.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't write {OneLine(prepared.Title)}.");
        }
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

        NumberDecisions(session, note.Name);
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
        catch (FileTooLargeException)
        {
            return Error(TooLargeText(shownName));
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

    // A todo's due date, and its time after one space when it has one.
    private static string When(TodoItem todo) => todo.Time is { } time
        ? $"{Date(todo.Due)} {time.ToString(TodoStore.StoredTimeFormat, CultureInfo.InvariantCulture)}"
        : Date(todo.Due);

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
            Settings = new SettingsStore(cli.folder);
        }

        /// <summary>True when the command writes one JSON document instead of plain text.</summary>
        public bool Json { get; }

        public NoteStore Notes { get; }

        public TodoStore Todos { get; }

        public TagIndex Index { get; }

        public CalendarStore Calendar { get; }

        public CalendarFeed Feed { get; }

        public SettingsStore Settings { get; }
    }
}
