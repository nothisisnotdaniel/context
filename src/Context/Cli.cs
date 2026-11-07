using System.Globalization;
using System.Text;

namespace Context;

/// <summary>
/// The scriptable command line: <c>Context &lt;command&gt; [args]</c> runs one command against the data folder and
/// returns. Output is plain text: results on stdout, one error line on stderr. Exit codes: 0 success, 1 error,
/// 2 usage error. With <c>encryption.json</c> the passphrase comes from <c>--passphrase-stdin</c> (the first line of
/// stdin) or <see cref="PassphraseVariable"/>, never from an argument and never by prompting. This class writes
/// through the given <see cref="TextWriter"/>s only, so it needs no terminal library.
/// </summary>
internal sealed class Cli
{
    /// <summary>The environment variable that holds the passphrase.</summary>
    public const string PassphraseVariable = "CONTEXT_PASSPHRASE";

    /// <summary>The option that reads the passphrase from the first line of stdin; only before the command.</summary>
    public const string PassphraseStdin = "--passphrase-stdin";

    /// <summary>Exit code for success.</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code for an error (unreadable data, not found, wrong or missing passphrase).</summary>
    public const int ExitError = 1;

    /// <summary>Exit code for a usage error (unknown command, missing or bad arguments).</summary>
    public const int ExitUsage = 2;

    /// <summary>The longest passphrase read from stdin; a longer first line is refused.</summary>
    public const int MaxPassphraseLength = 1024;

    private const string DateFormat = "yyyy-MM-dd";
    private const string HelpHint = "Run 'context help'.";
    private const string TodosUnreadable = "Can't read todos.json.";
    private const string IndexUnreadable = "Can't read context.db.";

    // Every command with its usage and what it does, in the order help lists them.
    private static readonly (string Usage, string Description)[] Commands =
    [
        ("help", "Show this list."),
        ("capture <text...>", "Append a dated line to the inbox note."),
        ("todos", "List todos: number, date, text."),
        ("todo add <yyyy-MM-dd|today|tomorrow> <text...>", "Add a todo."),
        ("todo done <n>", "Mark the n-th todo of 'todos' done."),
        ("notes", "List note names."),
        ("note show <name...>", "Print a note."),
        ("tags [tag...]", "List tags with counts, or the notes with a tag."),
        ("links <note...>", "List the notes linked to a note."),
        ("current", "Today's appointments, due todos and linked notes."),
        ("import <path>", "Store a Markdown file as a note."),
        ("export <note> <path>", "Write a note to a new plain file."),
    ];

    private readonly TextWriter stdout;
    private readonly TextWriter stderr;
    private readonly TextReader stdin;
    private readonly Func<string, string?> env;
    private readonly string folder;
    private readonly Func<DateOnly> today;
    private readonly Func<DateTime> now;
    private readonly HttpMessageHandler http;
    private readonly int minIterations;
    private readonly TimeZoneInfo zone;

    /// <param name="stdout">Where results go.</param>
    /// <param name="stderr">Where error and warning lines go.</param>
    /// <param name="stdin">Read only for <c>--passphrase-stdin</c>.</param>
    /// <param name="env">Reads an environment variable (null when unset).</param>
    /// <param name="folder">The data folder.</param>
    /// <param name="today">The local date.</param>
    /// <param name="now">The local time, for capture lines and the calendar download.</param>
    /// <param name="http">The handler the Exchange calendar download goes through.</param>
    public Cli(
        TextWriter stdout,
        TextWriter stderr,
        TextReader stdin,
        Func<string, string?> env,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http)
        : this(stdout, stderr, stdin, env, folder, today, now, http, Vault.MinUnlockIterations, TimeZoneInfo.Local)
    {
    }

    /// <summary>Like the public constructor with a lower key-file minimum and a fixed zone (tests).</summary>
    internal Cli(
        TextWriter stdout,
        TextWriter stderr,
        TextReader stdin,
        Func<string, string?> env,
        string folder,
        Func<DateOnly> today,
        Func<DateTime> now,
        HttpMessageHandler http,
        int minIterations,
        TimeZoneInfo zone)
    {
        this.stdout = stdout;
        this.stderr = stderr;
        this.stdin = stdin;
        this.env = env;
        this.folder = folder;
        this.today = today;
        this.now = now;
        this.http = http;
        this.minIterations = minIterations;
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

    private int RunCommand(string[] args)
    {
        // A passphrase in an argument would be visible in process lists and shell history: refuse it before
        // anything is read, and never repeat it.
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--passphrase", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--passphrase=", StringComparison.OrdinalIgnoreCase))
            {
                return Usage(
                    $"A passphrase can't be given as an argument. Set {PassphraseVariable} or use {PassphraseStdin}.");
            }
        }

        int start = 0;
        bool passphraseFromStdin = false;
        if (args.Length > 0 && args[0] == PassphraseStdin)
        {
            passphraseFromStdin = true;
            start = 1;
        }

        for (int i = start + 1; i < args.Length; i++)
        {
            if (args[i] == PassphraseStdin)
            {
                return Usage($"{PassphraseStdin} goes before the command.");
            }
        }

        if (args.Length <= start)
        {
            return Usage("No command given.");
        }

        string name = args[start].ToLowerInvariant();
        string[] rest = args[(start + 1)..];
        if (name is "help" or "--help" or "-h")
        {
            return Help();
        }

        Func<Session, int>? action = Parse(name, rest, out string? usage);
        if (action is null)
        {
            // An unknown option is not repeated: it could be a mistyped secret.
            string shown = args[start].StartsWith('-') ? "that option" : OneLine(args[start]);
            return Usage(usage ?? $"Unknown command: {shown}.");
        }

        Encryption encryption = new Encryption(folder, minIterations);
        try
        {
            if (encryption.IsConfigured)
            {
                int? failed = Unlock(encryption, passphraseFromStdin);
                if (failed is not null)
                {
                    return failed.Value;
                }
            }
            else
            {
                int orphaned = encryption.EncryptedFilesWithoutKey();
                if (orphaned > 0)
                {
                    string files = orphaned == 1 ? "1 encrypted file" : $"{orphaned} encrypted files";
                    Warn($"{files} but no {Vault.FileName}: new data is NOT encrypted. Restore it from a backup.");
                }
            }

            using Session session = new Session(this, encryption);
            return action(session);
        }
        finally
        {
            encryption.Use(null);
        }
    }

    // Returns null once unlocked, or the exit code after writing the error line. Nothing is written to disk.
    private int? Unlock(Encryption encryption, bool fromStdin)
    {
        string? passphrase;
        if (fromStdin)
        {
            passphrase = ReadPassphraseLine(out bool tooLong);
            if (tooLong)
            {
                return Error($"The passphrase on stdin is longer than {MaxPassphraseLength} characters.");
            }
        }
        else
        {
            passphrase = env(PassphraseVariable);
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            return Error($"Encryption is on: set {PassphraseVariable} or use {PassphraseStdin}.");
        }

        try
        {
            encryption.Unlock(passphrase);
            return null;
        }
        catch (WrongPassphraseException)
        {
            return Error("Wrong passphrase.");
        }
        catch (VaultException ex)
        {
            // A fixed text from Vault (bad or weak key file), never file content or the passphrase.
            return Error(OneLine(ex.Message));
        }
    }

    // The first line of stdin without its line break, read one character at a time so a huge input without a line
    // break is never buffered: more than MaxPassphraseLength characters stops reading and sets tooLong.
    private string? ReadPassphraseLine(out bool tooLong)
    {
        tooLong = false;
        StringBuilder line = new StringBuilder();
        while (true)
        {
            int c = stdin.Read();
            if (c == -1)
            {
                if (line.Length == 0)
                {
                    return null;
                }

                break;
            }

            if (c == '\n')
            {
                break;
            }

            if (line.Length > MaxPassphraseLength)
            {
                tooLong = true;
                return null;
            }

            line.Append((char)c);
        }

        if (line.Length > 0 && line[^1] == '\r')
        {
            line.Length--;
        }

        if (line.Length > MaxPassphraseLength)
        {
            tooLong = true;
            return null;
        }

        return line.ToString();
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
            if (args.Length != 2
                || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < 1)
            {
                usage = "Type: context todo done <n> with a number from 'context todos'.";
                return null;
            }

            return s => DoneTodo(s, number);
        }

        usage = "Type: context todo add <date> <text> or context todo done <n>.";
        return null;
    }

    private int Help()
    {
        int width = Commands.Max(c => c.Usage.Length);
        foreach ((string usage, string description) in Commands)
        {
            Out($"context {usage.PadRight(width)}  {description}");
        }

        Out($"With encryption on, set {PassphraseVariable} or put {PassphraseStdin} before the command.");
        return ExitOk;
    }

    private int Capture(Session session, string text)
    {
        CaptureResult result;
        try
        {
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

    private int Todos(Session session)
    {
        IReadOnlyList<TodoItem> todos;
        try
        {
            todos = session.Todos.List();
        }
        catch (TodoStoreException)
        {
            return Error(TodosUnreadable);
        }

        for (int i = 0; i < todos.Count; i++)
        {
            Out($"{(i + 1).ToString(CultureInfo.InvariantCulture)}\t{Date(todos[i].Due)}\t{OneLine(todos[i].Text)}");
        }

        return ExitOk;
    }

    private int AddTodo(Session session, string text, DateOnly due)
    {
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

    private int DoneTodo(Session session, int number)
    {
        TodoItem? done;
        try
        {
            IReadOnlyList<TodoItem> todos = session.Todos.List();
            if (number > todos.Count)
            {
                return Error($"There is no todo {number.ToString(CultureInfo.InvariantCulture)}.");
            }

            // The number only picks the todo; it is removed by its id, so a list that changed in between can't
            // make another todo go away.
            done = session.Todos.Complete(todos[number - 1].Id);
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
        foreach (NoteInfo note in session.Notes.List())
        {
            Out(OneLine(note.Name));
        }

        return ExitOk;
    }

    private int ShowNote(Session session, string name)
    {
        Note? note;
        try
        {
            if (!session.Notes.TryOpen(name, out note) || note is null)
            {
                return Error($"No note named {OneLine(name.Trim())}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Can't read {OneLine(name.Trim())}.");
        }

        string text = SafeText.Clean(note.Text);
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
            case TagsViewKind.AllTags:
                foreach (TagCount tag in view.Tags)
                {
                    Out($"{OneLine(TagParser.Format(tag.Tag))}\t{tag.Count.ToString(CultureInfo.InvariantCulture)}");
                }

                return ExitOk;
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
                return Error($"No note named {OneLine(view.Typed)}.");
            default:
                foreach (NoteLink link in view.Links)
                {
                    string tags = string.Join(' ', link.SharedTags.Select(TagParser.Format));
                    Out($"{OneLine(link.Note)}\t{LinkText(link)}\t{OneLine(tags)}");
                }

                return ExitOk;
        }
    }

    // The console's Link column text; empty for a link by shared tags only.
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
        CurrentView view = CurrentView.Build(
            session.Todos, session.Index, session.Notes, session.Calendar, session.Feed, today());
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
            Out($"{todo.Number.ToString(CultureInfo.InvariantCulture)}\t{Date(todo.Due)}\t{OneLine(todo.Text)}");
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
            return Error($"Can't import a file over 5 MB or an encrypted one: {shownSource}");
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
            return Error($"No note named {OneLine(name.Trim())}.");
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

        if (session.Encryption.IsOn)
        {
            Warn("This copy is not encrypted.");
        }

        Out($"Exported {shownName}.");
        return ExitOk;
    }

    // Text from disk or the user as one safe line: controls replaced, line breaks turned into spaces.
    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');

    private static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

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

    private void Warn(string line) => Err(line);

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

    // The stores of one run, all sharing the run's Encryption, as in Program.cs.
    private sealed class Session : IDisposable
    {
        public Session(Cli cli, Encryption encryption)
        {
            Encryption = encryption;
            Notes = new NoteStore(cli.folder, encryption);
            Todos = new TodoStore(cli.folder, () => cli.now().ToUniversalTime(), encryption);
            Index = new TagIndex(cli.folder, encryption);
            Calendar = new CalendarStore(cli.folder, cli.zone, encryption);
            CalendarLink link = new CalendarLink(cli.folder, encryption);
            Feed = new CalendarFeed(cli.folder, link, cli.http, () => cli.now().ToUniversalTime(), encryption: encryption);
        }

        public Encryption Encryption { get; }

        public NoteStore Notes { get; }

        public TodoStore Todos { get; }

        public TagIndex Index { get; }

        public CalendarStore Calendar { get; }

        public CalendarFeed Feed { get; }

        public void Dispose() => Index.Dispose();
    }
}
