using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Context;

/// <summary>
/// A todo: its id, its text, the day it is due, when it was added (UTC), when it was done (UTC, <c>null</c> while
/// it is open), how it repeats (<c>null</c> for a todo that doesn't), its time of day (<c>null</c> for an untimed
/// todo), when its reminder was shown (UTC, <c>null</c> until then) and the meeting occurrence it is tied to
/// (<c>null</c> for an untied todo). The id is never shown; it is how a todo is
/// addressed for <see cref="TodoStore.Complete(Guid)"/>, so a list that changed since it was shown can't make another
/// todo done.
/// </summary>
internal sealed record TodoItem
{
    public TodoItem(
        Guid id,
        string text,
        DateOnly due,
        DateTime created,
        DateTime? done = null,
        TodoRepeat? repeat = null,
        TimeOnly? time = null,
        DateTime? notified = null,
        MeetingLink? meeting = null)
    {
        Id = id;
        Text = text;
        Due = due;
        Created = created;
        Done = done;
        Repeat = repeat;
        Time = time;
        Notified = notified;
        Meeting = meeting;
    }

    /// <summary>Unique within the list, stable across reads; never shown to the user.</summary>
    public Guid Id { get; }

    public string Text { get; }

    public DateOnly Due { get; }

    /// <summary>When the todo was added, UTC. Breaks ties between todos due on the same day.</summary>
    public DateTime Created { get; }

    /// <summary>When the todo was marked done, UTC; <c>null</c> for an open todo.</summary>
    public DateTime? Done { get; }

    /// <summary>How the todo repeats; <c>null</c> for a todo that doesn't. Not part of the id.</summary>
    public TodoRepeat? Repeat { get; }

    /// <summary>The local time of day the todo is due at (whole minutes); <c>null</c> for an untimed todo. Not part of the id.</summary>
    public TimeOnly? Time { get; }

    /// <summary>When the todo's reminder was shown, UTC; <c>null</c> until then. Not part of the id.</summary>
    public DateTime? Notified { get; }

    /// <summary>The meeting occurrence the todo is tied to (job 287); <c>null</c> for an untied todo. Not part of the id.</summary>
    public MeetingLink? Meeting { get; }
}

/// <summary>Thrown when <c>todos.json</c> exists but can't be read or parsed. The file is never overwritten then.</summary>
internal sealed class TodoStoreException : IOException
{
    public TodoStoreException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The todo list, kept as JSON in <c>&lt;root&gt;/todos.json</c> (UTF-8, no BOM). Every write goes to a
/// temp file in the root first and then replaces <c>todos.json</c>, so the file is never half-written.
/// Each todo has an <c>id</c>. A todo stored without one (files from before ids), with an empty or repeated one, or
/// with a bad one (any value that is not a GUID string, such as <c>"nope"</c> or <c>42</c>), gets an id derived from
/// its content and position among identical todos. <see cref="Migrate()"/>, called once when the desktop app or a
/// todo-reading command starts, writes those derived ids into the file; reads never write, and derivation stays the
/// fallback for a file changed while the app runs. A done todo stays in the file with a <c>done</c> field (a todo
/// without one is open); <see cref="List"/> gives the open todos and <see cref="ListDone"/> the done ones. Job 361:
/// every write moves the todos done more than <see cref="ArchiveAfter"/> ago into <c>todos-archive.json</c> (same
/// format), written first; <see cref="ListAllDone"/> reads the done todos of both files.
/// </summary>
internal sealed class TodoStore
{
    /// <summary>The file name of the list inside the root.</summary>
    public const string FileName = "todos.json";

    /// <summary>How <see cref="TodoItem.Time"/> is stored and shown.</summary>
    public const string StoredTimeFormat = "HH:mm";

    /// <summary>How <see cref="MeetingLink.Start"/> is stored.</summary>
    public const string StoredMeetingStartFormat = "yyyy-MM-dd'T'HH:mm";

    /// <summary>The largest <c>todos.json</c> that is read (5 MB); a bigger one can't be read.</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;

    /// <summary>The file name of the archive of old done todos inside the root (job 361).</summary>
    public const string ArchiveFileName = "todos-archive.json";

    /// <summary>The largest <c>todos-archive.json</c> that is read or written (20 MB); a bigger one can't be read.</summary>
    public const long MaxArchiveBytes = 20L * 1024 * 1024;

    /// <summary>A todo done longer ago than this (strictly) moves to the archive on the next write.</summary>
    public static readonly TimeSpan ArchiveAfter = TimeSpan.FromDays(30);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string root;
    private readonly Func<DateTime> utcNow;
    private readonly Func<Guid> newId;

    /// <param name="root">The folder <c>todos.json</c> lives in.</param>
    /// <param name="utcNow">The clock for <see cref="TodoItem.Created"/> and <see cref="TodoItem.Done"/>; <see cref="DateTime.UtcNow"/> if null.</param>
    /// <param name="newId">The ids of new todos; <see cref="Guid.NewGuid"/> if null (tests pass fixed ones).</param>
    public TodoStore(string root, Func<DateTime>? utcNow = null, Func<Guid>? newId = null)
    {
        this.root = Path.GetFullPath(root);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        this.newId = newId ?? Guid.NewGuid;
    }

    /// <summary>The full path of <c>todos.json</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>The full path of <c>todos-archive.json</c>.</summary>
    public string ArchivePath => Path.Combine(root, ArchiveFileName);

    /// <summary>
    /// Test hook: called with the target path (<see cref="ArchivePath"/> or <see cref="FilePath"/>) just before that
    /// file's temp write. A test makes it throw to simulate a failed write.
    /// </summary>
    internal Action<string>? BeforeWrite { get; init; }

    /// <summary>
    /// Every open todo, sorted by <see cref="TodoItem.Due"/>; within one date the untimed todos first, then the timed
    /// ones by <see cref="TodoItem.Time"/>; then <see cref="TodoItem.Created"/>, then file order.
    /// A missing file lists as empty and nothing is created. Throws <see cref="TodoStoreException"/>
    /// if the file can't be read or isn't a valid todo list.
    /// </summary>
    public IReadOnlyList<TodoItem> List() => InListOrder(Read().Where(t => t.Done is null));

    /// <summary>
    /// The open todos whose reminder is due: they have a <see cref="TodoItem.Time"/>, no
    /// <see cref="TodoItem.Notified"/>, and their due date at that time is at or before <paramref name="localNow"/>
    /// (local time), in <see cref="List"/> order. Reads only. Throws <see cref="TodoStoreException"/> like
    /// <see cref="List"/>.
    /// </summary>
    public IReadOnlyList<TodoItem> DueReminders(DateTime localNow) =>
        List().Where(t => t.Time is { } time && t.Notified is null && t.Due.ToDateTime(time) <= localNow).ToList();

    /// <summary>
    /// Marks the open, not yet notified todos with one of <paramref name="ids"/> as notified
    /// (<see cref="TodoItem.Notified"/> from the UTC clock) in one read and one write, and returns them (marked) in
    /// <see cref="List"/> order. Ids of todos that are gone, done or already notified are skipped; when none is
    /// left, nothing is written. Throws <see cref="TodoStoreException"/> (and writes nothing) if the file can't be
    /// read.
    /// </summary>
    public IReadOnlyList<TodoItem> MarkNotified(IReadOnlyList<Guid> ids)
    {
        HashSet<Guid> wanted = ids.ToHashSet();
        List<TodoItem> todos = Read();
        DateTime now = DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc);
        List<TodoItem> marked = [];
        for (int i = 0; i < todos.Count; i++)
        {
            TodoItem todo = todos[i];
            if (todo.Done is null && todo.Notified is null && wanted.Contains(todo.Id))
            {
                todos[i] = new TodoItem(
                    todo.Id, todo.Text, todo.Due, todo.Created, null, todo.Repeat, todo.Time, now, todo.Meeting);
                marked.Add(todos[i]);
            }
        }

        if (marked.Count == 0)
        {
            return [];
        }

        Write(todos);
        return InListOrder(marked);
    }

    /// <summary>
    /// Every done todo, sorted by <see cref="TodoItem.Done"/>, latest first, then <see cref="TodoItem.Created"/>,
    /// then file order. A missing file lists as empty. Throws <see cref="TodoStoreException"/> like <see cref="List"/>.
    /// </summary>
    public IReadOnlyList<TodoItem> ListDone() =>
        Read().Where(t => t.Done is not null).OrderByDescending(t => t.Done).ThenBy(t => t.Created).ToList();

    /// <summary>
    /// The done todos of <c>todos.json</c> and <c>todos-archive.json</c> (job 361) in <see cref="ListDone"/> order:
    /// <see cref="TodoItem.Done"/> latest first, then <see cref="TodoItem.Created"/>, then <c>todos.json</c> before the
    /// archive, then file order. A todo in both files is listed once, as its <c>todos.json</c> copy. A missing archive
    /// lists nothing from it; one that can't be read (see <see cref="ArchivePath"/>) sets
    /// <paramref name="archiveUnreadable"/> and only the <c>todos.json</c> done todos are returned. Throws
    /// <see cref="TodoStoreException"/> like <see cref="ListDone"/> for a bad <c>todos.json</c>. Reads only.
    /// </summary>
    public IReadOnlyList<TodoItem> ListAllDone(out bool archiveUnreadable)
    {
        List<TodoItem> done = Read().Where(t => t.Done is not null).ToList();
        archiveUnreadable = !TryReadArchive(out List<TodoItem> archive);
        HashSet<Guid> ids = done.Select(t => t.Id).ToHashSet();

        // OrderBy is stable, so the concatenation keeps todos.json before the archive, each in file order.
        return done.Concat(archive.Where(t => !ids.Contains(t.Id)))
            .OrderByDescending(t => t.Done).ThenBy(t => t.Created).ToList();
    }

    /// <summary>
    /// Adds a todo (recurring when <paramref name="repeat"/> is set), creating the root folder if needed, and returns
    /// it. Throws <see cref="TodoStoreException"/> (and writes nothing) if the existing file can't be read.
    /// </summary>
    public TodoItem Add(string text, DateOnly due, TodoRepeat? repeat = null) => Add(text, due, repeat, null);

    /// <summary>
    /// <see cref="Add(string, DateOnly, TodoRepeat?)"/> with a time of day (<c>null</c> for an untimed todo); the
    /// todo has no <see cref="TodoItem.Notified"/> yet.
    /// </summary>
    public TodoItem Add(string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time) =>
        Add(text, due, repeat, time, null);

    /// <summary>
    /// <see cref="Add(string, DateOnly, TodoRepeat?, TimeOnly?)"/> tied to <paramref name="meeting"/> (<c>null</c>: an
    /// untied todo).
    /// </summary>
    public TodoItem Add(string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time, MeetingLink? meeting)
    {
        List<TodoItem> todos = Read();
        TodoItem todo = NewTodo(todos, text, due, repeat, time, meeting);
        todos.Add(todo);
        PrivateFile.CreateFolder(root);
        Write(todos);
        return todo;
    }

    /// <summary>
    /// Marks the open todo with <paramref name="id"/> done (<see cref="TodoItem.Done"/> from the clock) in one read
    /// and write and returns it; it stays in the file. Returns <c>null</c> (and writes nothing) when no open todo has
    /// that id, including one that is already done. Throws <see cref="TodoStoreException"/> if the file can't be
    /// read; nothing is written then. A recurring todo still gets its next todo, with <see cref="DateOnly.MinValue"/>
    /// as today (so the next due date follows only its due date); callers that know today use the other overload.
    /// </summary>
    public TodoItem? Complete(Guid id) => Complete(id, DateOnly.MinValue, out _);

    /// <summary>
    /// <see cref="Complete(Guid)"/>; when the todo repeats, the same write also appends one new open todo with the
    /// same text, repeat and time (not notified), a new id, <see cref="TodoItem.Created"/> from the clock and the due date
    /// <see cref="TodoInput.NextDue"/> gives for <paramref name="today"/>, and returns it in <paramref name="next"/>
    /// (<c>null</c> for a todo that doesn't repeat, and whenever <c>null</c> is returned).
    /// </summary>
    public TodoItem? Complete(Guid id, DateOnly today, out TodoItem? next)
    {
        next = null;
        List<TodoItem> todos = Read();
        int index = todos.FindIndex(t => t.Id == id && t.Done is null);
        if (index < 0)
        {
            return null;
        }

        TodoItem open = todos[index];
        TodoItem done = new TodoItem(
            open.Id,
            open.Text,
            open.Due,
            open.Created,
            DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc),
            open.Repeat,
            open.Time,
            open.Notified,
            open.Meeting);
        todos[index] = done;
        TodoItem? again = null;
        if (open.Repeat is { } repeat && TryNextDue(repeat, open.Due, today, out DateOnly nextDue))
        {
            again = NewTodo(todos, open.Text, nextDue, repeat, open.Time, null);
            todos.Add(again);
        }

        Write(todos);
        next = again;
        return done;
    }

    /// <summary>
    /// Moves every open todo due on or before <paramref name="through"/> to <paramref name="to"/> in one read and
    /// one write, keeping its id, text, <see cref="TodoItem.Created"/>, <see cref="TodoItem.Repeat"/> and
    /// <see cref="TodoItem.Time"/> and clearing <see cref="TodoItem.Notified"/> (so it reminds again), and returns
    /// the moved todos (with their new due date) in <see cref="List"/> order. Done todos and todos due later are not
    /// changed. When nothing is due, nothing is written. Throws <see cref="TodoStoreException"/> (and writes nothing)
    /// if the file can't be read.
    /// </summary>
    public IReadOnlyList<TodoItem> MoveDue(DateOnly through, DateOnly to)
    {
        List<TodoItem> todos = Read();
        List<TodoItem> due = InListOrder(todos.Where(t => t.Done is null && t.Due <= through));
        if (due.Count == 0)
        {
            return [];
        }

        HashSet<Guid> ids = due.Select(t => t.Id).ToHashSet();
        for (int i = 0; i < todos.Count; i++)
        {
            TodoItem todo = todos[i];
            if (todo.Done is null && ids.Contains(todo.Id))
            {
                todos[i] = new TodoItem(todo.Id, todo.Text, to, todo.Created, null, todo.Repeat, todo.Time, null, todo.Meeting);
            }
        }

        Write(todos);
        return due.Select(t => new TodoItem(t.Id, t.Text, to, t.Created, null, t.Repeat, t.Time, null, t.Meeting)).ToList();
    }

    /// <summary>
    /// Ties the open todo with <paramref name="id"/> to <paramref name="link"/> in one read and one write: sets
    /// <see cref="TodoItem.Meeting"/>, sets <see cref="TodoItem.Due"/> to <see cref="MeetingLink.Day"/> and clears
    /// <see cref="TodoItem.Notified"/>, keeping id, text, created, repeat and time, and returns it. Returns <c>null</c>
    /// (and writes nothing) when no open todo has that id. Throws <see cref="TodoStoreException"/> (and writes nothing)
    /// if the file can't be read.
    /// </summary>
    public TodoItem? Tie(Guid id, MeetingLink link)
    {
        List<TodoItem> todos = Read();
        int index = todos.FindIndex(t => t.Id == id && t.Done is null);
        if (index < 0)
        {
            return null;
        }

        TodoItem open = todos[index];
        TodoItem tied = new TodoItem(open.Id, open.Text, link.Day, open.Created, null, open.Repeat, open.Time, null, link);
        todos[index] = tied;
        Write(todos);
        return tied;
    }

    /// <summary>
    /// Stores the ids <see cref="List"/> would return when some todo has no stored, non-empty, unique id: writes
    /// the list once, in file order with the same text, due, created, done, repeat, time and notified, and returns <c>true</c>. Returns
    /// <c>false</c> and writes nothing when every id is already stored, the file is missing, or it can't be read,
    /// parsed or written (an unreadable file is never touched; the next <see cref="List"/> reports it). Never throws
    /// for those.
    /// </summary>
    public bool Migrate() => Migrate(out _);

    /// <summary>
    /// <see cref="Migrate()"/>, also giving <paramref name="badIds"/>: the number of todos whose stored id was bad
    /// (not a GUID string) and whose derived ids were written. It is 0 whenever nothing was written.
    /// </summary>
    public bool Migrate(out int badIds)
    {
        badIds = 0;
        if (!File.Exists(FilePath))
        {
            return false;
        }

        List<TodoItem> todos;
        bool stored;
        int bad;
        try
        {
            todos = Read(out stored, out bad);
        }
        catch (TodoStoreException)
        {
            return false;
        }

        if (stored)
        {
            return false;
        }

        try
        {
            Write(todos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        badIds = bad;
        return true;
    }

    /// <summary>The order of <see cref="List"/>: due date, untimed before timed, time, created, then the given order.</summary>
    private static List<TodoItem> InListOrder(IEnumerable<TodoItem> todos) =>
        todos.OrderBy(t => t.Due).ThenBy(t => t.Time.HasValue).ThenBy(t => t.Time ?? TimeOnly.MinValue)
            .ThenBy(t => t.Created).ToList();

    /// <summary>A new open todo with an id no todo in <paramref name="todos"/> has and the clock's time.</summary>
    private TodoItem NewTodo(
        List<TodoItem> todos, string text, DateOnly due, TodoRepeat? repeat, TimeOnly? time, MeetingLink? meeting)
    {
        Guid id = newId();
        while (id == Guid.Empty || todos.Any(t => t.Id == id))
        {
            id = Guid.NewGuid();
        }

        return new TodoItem(
            id, text, due, DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc), repeat: repeat, time: time, meeting: meeting);
    }

    /// <summary><see cref="TodoInput.NextDue"/>, or <c>false</c> at the end of the calendar (no next todo then).</summary>
    private static bool TryNextDue(TodoRepeat repeat, DateOnly due, DateOnly today, out DateOnly next)
    {
        try
        {
            next = TodoInput.NextDue(repeat, due, today);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            next = default;
            return false;
        }
    }

    /// <summary>
    /// The id of a todo stored without a usable one: the first 16 bytes of SHA-256 over its created ticks, due
    /// date, text and <paramref name="attempt"/> (the n-th identical todo in file order, then retries on a clash).
    /// </summary>
    internal static Guid DerivedId(string text, DateOnly due, DateTime created, int attempt)
    {
        string key = string.Join(
            "\n",
            attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            created.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            due.DayNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            text);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    private List<TodoItem> Read() => Read(out _, out _);

    private List<TodoItem> Read(out bool allStored, out int badIds) =>
        Read(FilePath, FileName, MaxFileBytes, out allStored, out badIds);

    /// <summary>
    /// The archive's todos: <c>true</c> with an empty list when it is missing, <c>false</c> (and an empty list) when it
    /// can't be read or parsed, is over <see cref="MaxArchiveBytes"/> or holds a todo without a valid <c>done</c>.
    /// </summary>
    private bool TryReadArchive(out List<TodoItem> archive)
    {
        try
        {
            archive = Read(ArchivePath, ArchiveFileName, MaxArchiveBytes, out _, out _);
        }
        catch (TodoStoreException)
        {
            archive = [];
            return false;
        }

        if (archive.Any(t => t.Done is null))
        {
            archive = [];
            return false;
        }

        return true;
    }

    /// <param name="path">The file to read.</param>
    /// <param name="fileName">Its name, for the exception text.</param>
    /// <param name="maxBytes">The largest file that is read.</param>
    /// <param name="allStored"><c>true</c> when every todo has a stored, non-empty, unique id (or the file is missing).</param>
    /// <param name="badIds">The number of todos whose <c>id</c> is present but not a GUID string.</param>
    private static List<TodoItem> Read(string path, string fileName, long maxBytes, out bool allStored, out int badIds)
    {
        allStored = true;
        badIds = 0;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return [];
        }

        List<TodoJson?>? items;
        try
        {
            byte[] bytes = BoundedFile.Read(path, maxBytes);
            items = JsonSerializer.Deserialize<List<TodoJson?>>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            throw new TodoStoreException($"Can't read {fileName}.", ex);
        }

        if (items is null)
        {
            throw new TodoStoreException($"Can't read {fileName}.");
        }

        List<TodoItem> todos = new List<TodoItem>(items.Count);
        List<Guid> ids = new List<Guid>(items.Count);
        HashSet<Guid> used = [];
        foreach (TodoJson? item in items)
        {
            if (item?.Text is null || item.Due is null)
            {
                throw new TodoStoreException($"Can't read {fileName}.");
            }

            Guid stored = StoredId(item.Id, out bool bad);
            if (bad)
            {
                badIds++;
            }

            ids.Add(stored);
            if (stored != Guid.Empty)
            {
                used.Add(stored);
            }
        }

        // Stored ids first, so a todo without one can never take the id of a later one that has it.
        HashSet<Guid> taken = [];
        for (int i = 0; i < items.Count; i++)
        {
            TodoJson item = items[i]!;
            DateTime created = item.Created ?? DateTime.MinValue;
            Guid id = ids[i];
            if (id == Guid.Empty || !taken.Add(id))
            {
                allStored = false;
                int attempt = 0;
                do
                {
                    id = DerivedId(item.Text!, item.Due!.Value, created, attempt++);
                }
                while (id == Guid.Empty || used.Contains(id) || !taken.Add(id));
            }

            TodoRepeat? repeat = null;
            if (item.Repeat is { } stored)
            {
                if (!TodoRepeat.TryParseStored(stored, out TodoRepeat parsed))
                {
                    throw new TodoStoreException($"Can't read {fileName}.");
                }

                repeat = parsed;
            }

            TimeOnly? time = null;
            if (item.Time is { } storedTime)
            {
                if (!TryParseStoredTime(storedTime, out TimeOnly parsedTime))
                {
                    throw new TodoStoreException($"Can't read {fileName}.");
                }

                time = parsedTime;
            }

            MeetingLink? meeting = null;
            if (item.Meeting is { } storedMeeting && storedMeeting.ValueKind != JsonValueKind.Null)
            {
                meeting = ReadMeeting(storedMeeting) ?? throw new TodoStoreException($"Can't read {fileName}.");
            }

            todos.Add(new TodoItem(
                id, item.Text!, item.Due!.Value, created, AsUtc(item.Done), repeat, time, AsUtc(item.Notified), meeting));
        }

        return todos;
    }

    /// <summary>
    /// A stored meeting: an object with a non-empty string <c>uid</c> of at most <see cref="MeetingLink.MaxUidLength"/>
    /// characters, a string <c>start</c> in exactly <see cref="StoredMeetingStartFormat"/> and an optional string
    /// <c>title</c> (missing: empty). <c>null</c> for anything else.
    /// </summary>
    private static MeetingLink? ReadMeeting(JsonElement stored)
    {
        if (stored.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!stored.TryGetProperty("uid", out JsonElement uidValue) || uidValue.ValueKind != JsonValueKind.String
            || uidValue.GetString() is not { Length: > 0 and <= MeetingLink.MaxUidLength } uid)
        {
            return null;
        }

        if (!stored.TryGetProperty("start", out JsonElement startValue) || startValue.ValueKind != JsonValueKind.String
            || !DateTime.TryParseExact(
                startValue.GetString(),
                StoredMeetingStartFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime start))
        {
            return null;
        }

        string title = string.Empty;
        if (stored.TryGetProperty("title", out JsonElement titleValue))
        {
            if (titleValue.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            title = titleValue.GetString()!;
        }

        return new MeetingLink(uid, start, title);
    }

    /// <summary>A stored time of day: exactly <c>HH:mm</c>, two ASCII digits each, 00:00 to 23:59.</summary>
    private static bool TryParseStoredTime(string stored, out TimeOnly time)
    {
        time = default;
        if (stored.Length != 5 || stored[2] != ':' || !char.IsAsciiDigit(stored[0]) || !char.IsAsciiDigit(stored[1])
            || !char.IsAsciiDigit(stored[3]) || !char.IsAsciiDigit(stored[4]))
        {
            return false;
        }

        int hour = ((stored[0] - '0') * 10) + (stored[1] - '0');
        int minute = ((stored[3] - '0') * 10) + (stored[4] - '0');
        if (hour > 23 || minute > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }

    /// <summary>
    /// A stored done or notified time as UTC: a value with an offset (read as local time) is converted, one with no zone is taken
    /// as UTC, so <see cref="TodoItem.Done"/> is always UTC and is written back with <c>Z</c>.
    /// </summary>
    private static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
    };

    /// <summary>
    /// The stored id, or <see cref="Guid.Empty"/> when it is absent, null or bad. <paramref name="bad"/> is
    /// <c>true</c> for any value other than null or a string System.Text.Json reads as a <see cref="Guid"/>.
    /// </summary>
    private static Guid StoredId(JsonElement? value, out bool bad)
    {
        bad = false;
        if (value is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Guid.Empty;
        }

        if (element.ValueKind == JsonValueKind.String && element.TryGetGuid(out Guid id))
        {
            return id;
        }

        bad = true;
        return Guid.Empty;
    }

    /// <summary>
    /// Writes <paramref name="todos"/> to <c>todos.json</c>, first moving every todo done more than
    /// <see cref="ArchiveAfter"/> ago into <c>todos-archive.json</c> (job 361). The archive is written first; when it
    /// can't be read, would be over <see cref="MaxArchiveBytes"/> or its write fails, those todos stay in
    /// <c>todos.json</c> and the archive is not touched. A failed <c>todos.json</c> write throws as before; a todo
    /// moved by then is in both files, never in neither.
    /// </summary>
    private void Write(List<TodoItem> todos)
    {
        DateTime cutoff = DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc) - ArchiveAfter;
        List<TodoItem> old = todos.Where(t => t.Done is { } done && done < cutoff).ToList();
        List<TodoItem> kept = todos;
        if (old.Count > 0 && TryReadArchive(out List<TodoItem> archive))
        {
            Dictionary<Guid, int> at = [];
            for (int i = 0; i < archive.Count; i++)
            {
                at.TryAdd(archive[i].Id, i);
            }

            foreach (TodoItem todo in old)
            {
                if (at.TryGetValue(todo.Id, out int index))
                {
                    archive[index] = todo;
                }
                else
                {
                    at[todo.Id] = archive.Count;
                    archive.Add(todo);
                }
            }

            byte[] bytes = Serialize(archive);
            if (bytes.LongLength <= MaxArchiveBytes)
            {
                try
                {
                    WriteFile(ArchivePath, ArchiveFileName, bytes);
                    HashSet<Guid> moved = old.Select(t => t.Id).ToHashSet();
                    kept = todos.Where(t => !(t.Done is not null && moved.Contains(t.Id))).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The old done todos stay in todos.json; the next write tries again.
                }
            }
        }

        WriteFile(FilePath, FileName, Serialize(kept));
    }

    private static byte[] Serialize(List<TodoItem> todos)
    {
        List<TodoJson> items = todos
            .Select(t => new TodoJson
            {
                Id = JsonSerializer.SerializeToElement(t.Id),
                Text = t.Text,
                Due = t.Due,
                Created = t.Created,
                Done = t.Done,
                Repeat = t.Repeat?.Stored,
                Time = t.Time?.ToString(StoredTimeFormat, System.Globalization.CultureInfo.InvariantCulture),
                Notified = t.Notified,
                Meeting = t.Meeting is { } meeting ? JsonSerializer.SerializeToElement(
                    new MeetingJson
                    {
                        Uid = meeting.Uid,
                        Start = meeting.Start.ToString(StoredMeetingStartFormat, System.Globalization.CultureInfo.InvariantCulture),
                        Title = meeting.Title,
                    },
                    JsonOptions) : null,
            })
            .ToList();
        string json = JsonSerializer.Serialize(items, JsonOptions) + "\n";
        return Utf8NoBom.GetBytes(json);
    }

    /// <summary>Writes <paramref name="bytes"/> to a temp file in the root, then moves it over <paramref name="path"/>.</summary>
    private void WriteFile(string path, string fileName, byte[] bytes)
    {
        BeforeWrite?.Invoke(path);
        string temp = Path.Combine(root, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(bytes);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>The JSON shape of one todo. Every field is nullable so a missing one is caught, not defaulted.</summary>
    private sealed class TodoJson
    {
        /// <summary>Read as raw JSON so a value that is not a GUID is a bad id, not an unreadable file.</summary>
        public JsonElement? Id { get; set; }

        public string? Text { get; set; }

        public DateOnly? Due { get; set; }

        public DateTime? Created { get; set; }

        /// <summary>
        /// When the todo was done; left out of the file for an open todo, so a list with no done todo is written as
        /// before. A value that is not null and not a date-time string makes the file unreadable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? Done { get; set; }

        /// <summary>
        /// How the todo repeats (<see cref="TodoRepeat.Stored"/>); left out of the file for a todo that doesn't, so a
        /// list without recurring todos is written as before. A value that is not null and not one of the stored
        /// strings (including a number or an object) makes the file unreadable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Repeat { get; set; }

        /// <summary>
        /// The time of day, exactly <c>HH:mm</c>; left out for an untimed todo, so a list without timed todos is
        /// written as before. Any other value (<c>"9:00"</c>, <c>"14:60"</c>, a number) makes the file unreadable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Time { get; set; }

        /// <summary>
        /// When the reminder was shown (UTC); left out until then. Read like <see cref="Done"/>: a value that is not
        /// null and not a date-time string makes the file unreadable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? Notified { get; set; }

        /// <summary>
        /// The tied meeting (job 287), <c>{"uid","start","title"}</c>; left out for an untied todo, so a list without
        /// tied todos is written as before. Read as raw JSON and checked by <c>ReadMeeting</c>: a non-object value, a
        /// missing, empty or too long <c>uid</c> or a bad <c>start</c> makes the file unreadable.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Meeting { get; set; }
    }

    /// <summary>The JSON shape of a stored meeting, in written order.</summary>
    private sealed class MeetingJson
    {
        public string? Uid { get; set; }

        public string? Start { get; set; }

        public string? Title { get; set; }
    }
}
