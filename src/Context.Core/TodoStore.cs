using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// A todo: its id, its text, the day it is due and when it was added (UTC). The id is never shown; it is how a
/// todo is addressed for <see cref="TodoStore.Complete"/>, so a list that changed since it was shown can't make
/// another todo go away.
/// </summary>
internal sealed record TodoItem
{
    public TodoItem(Guid id, string text, DateOnly due, DateTime created)
    {
        Id = id;
        Text = text;
        Due = due;
        Created = created;
    }

    /// <summary>Unique within the list, stable across reads; never shown to the user.</summary>
    public Guid Id { get; }

    public string Text { get; }

    public DateOnly Due { get; }

    /// <summary>When the todo was added, UTC. Breaks ties between todos due on the same day.</summary>
    public DateTime Created { get; }
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
/// fallback for a file changed while the app runs.
/// </summary>
internal sealed class TodoStore
{
    /// <summary>The file name of the list inside the root.</summary>
    public const string FileName = "todos.json";

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
    /// <param name="utcNow">The clock for <see cref="TodoItem.Created"/>; <see cref="DateTime.UtcNow"/> if null.</param>
    /// <param name="newId">The ids of new todos; <see cref="Guid.NewGuid"/> if null (tests pass fixed ones).</param>
    public TodoStore(string root, Func<DateTime>? utcNow = null, Func<Guid>? newId = null)
    {
        this.root = Path.GetFullPath(root);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        this.newId = newId ?? Guid.NewGuid;
    }

    /// <summary>The full path of <c>todos.json</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// Every todo, sorted by <see cref="TodoItem.Due"/>, then <see cref="TodoItem.Created"/>, then file order.
    /// A missing file lists as empty and nothing is created. Throws <see cref="TodoStoreException"/>
    /// if the file can't be read or isn't a valid todo list.
    /// </summary>
    public IReadOnlyList<TodoItem> List() =>
        Read().OrderBy(t => t.Due).ThenBy(t => t.Created).ToList();

    /// <summary>
    /// Adds a todo, creating the root folder if needed, and returns it. Throws
    /// <see cref="TodoStoreException"/> (and writes nothing) if the existing file can't be read.
    /// </summary>
    public TodoItem Add(string text, DateOnly due)
    {
        List<TodoItem> todos = Read();
        Guid id = newId();
        while (id == Guid.Empty || todos.Any(t => t.Id == id))
        {
            id = Guid.NewGuid();
        }

        TodoItem todo = new TodoItem(id, text, due, DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc));
        todos.Add(todo);
        Directory.CreateDirectory(root);
        Write(todos);
        return todo;
    }

    /// <summary>
    /// Removes the todo with <paramref name="id"/> in one read and write and returns it, or returns <c>null</c>
    /// (and writes nothing) when no todo has that id. Throws <see cref="TodoStoreException"/> if the file can't be
    /// read; nothing is written then.
    /// </summary>
    public TodoItem? Complete(Guid id)
    {
        List<TodoItem> todos = Read();
        int index = todos.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return null;
        }

        TodoItem done = todos[index];
        todos.RemoveAt(index);
        Write(todos);
        return done;
    }

    /// <summary>
    /// Stores the ids <see cref="List"/> would return when some todo has no stored, non-empty, unique id: writes
    /// the list once, in file order with the same text, due and created, and returns <c>true</c>. Returns
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

    /// <param name="allStored"><c>true</c> when every todo has a stored, non-empty, unique id (or the file is missing).</param>
    /// <param name="badIds">The number of todos whose <c>id</c> is present but not a GUID string.</param>
    private List<TodoItem> Read(out bool allStored, out int badIds)
    {
        allStored = true;
        badIds = 0;
        string path = FilePath;
        if (!File.Exists(path))
        {
            return [];
        }

        List<TodoJson?>? items;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            items = JsonSerializer.Deserialize<List<TodoJson?>>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            throw new TodoStoreException($"Can't read {FileName}.", ex);
        }

        if (items is null)
        {
            throw new TodoStoreException($"Can't read {FileName}.");
        }

        List<TodoItem> todos = new List<TodoItem>(items.Count);
        List<Guid> ids = new List<Guid>(items.Count);
        HashSet<Guid> used = [];
        foreach (TodoJson? item in items)
        {
            if (item?.Text is null || item.Due is null)
            {
                throw new TodoStoreException($"Can't read {FileName}.");
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

            todos.Add(new TodoItem(id, item.Text!, item.Due!.Value, created));
        }

        return todos;
    }

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

    private void Write(List<TodoItem> todos)
    {
        List<TodoJson> items = todos
            .Select(t => new TodoJson { Id = JsonSerializer.SerializeToElement(t.Id), Text = t.Text, Due = t.Due, Created = t.Created })
            .ToList();
        string json = JsonSerializer.Serialize(items, JsonOptions) + "\n";
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Utf8NoBom.GetBytes(json));
            }

            File.Move(temp, FilePath, overwrite: true);
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
    }
}
