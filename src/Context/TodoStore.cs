using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>A todo: its text, the day it is due and when it was added (UTC).</summary>
internal sealed record TodoItem
{
    public TodoItem(string text, DateOnly due, DateTime created)
    {
        Text = text;
        Due = due;
        Created = created;
    }

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

    /// <param name="root">The folder <c>todos.json</c> lives in.</param>
    /// <param name="utcNow">The clock for <see cref="TodoItem.Created"/>; <see cref="DateTime.UtcNow"/> if null.</param>
    public TodoStore(string root, Func<DateTime>? utcNow = null)
    {
        this.root = Path.GetFullPath(root);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
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
        TodoItem todo = new TodoItem(text, due, DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc));
        todos.Add(todo);
        Directory.CreateDirectory(root);
        Write(todos);
        return todo;
    }

    /// <summary>
    /// Removes the todo at 1-based position <paramref name="number"/> in <see cref="List"/> and returns it.
    /// Throws <see cref="ArgumentOutOfRangeException"/> if there is no such todo, and
    /// <see cref="TodoStoreException"/> if the file can't be read. Nothing is written then.
    /// </summary>
    public TodoItem Complete(int number)
    {
        List<TodoItem> todos = List().ToList();
        if (number < 1 || number > todos.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"There is no todo {number}.");
        }

        TodoItem done = todos[number - 1];
        todos.RemoveAt(number - 1);
        Write(todos);
        return done;
    }

    private List<TodoItem> Read()
    {
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
        foreach (TodoJson? item in items)
        {
            if (item?.Text is null || item.Due is null)
            {
                throw new TodoStoreException($"Can't read {FileName}.");
            }

            todos.Add(new TodoItem(item.Text, item.Due.Value, item.Created ?? DateTime.MinValue));
        }

        return todos;
    }

    private void Write(List<TodoItem> todos)
    {
        List<TodoJson> items = todos
            .Select(t => new TodoJson { Text = t.Text, Due = t.Due, Created = t.Created })
            .ToList();
        string json = JsonSerializer.Serialize(items, JsonOptions) + "\n";
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, json, Utf8NoBom);
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
        public string? Text { get; set; }

        public DateOnly? Due { get; set; }

        public DateTime? Created { get; set; }
    }
}
