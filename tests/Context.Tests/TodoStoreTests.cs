using System.Text;

namespace Context.Tests;

public sealed class TodoStoreTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private DateTime now = Start;

    public TodoStoreTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-todostore-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string FilePath => Path.Combine(root, "todos.json");

    // Each call moves the clock on by a minute, so Created is increasing but never the real time.
    private TodoStore Store() => new(root, () => now = now.AddMinutes(1));

    // A store whose clock never moves, so todos only differ by the order they were added.
    private TodoStore FrozenStore() => new(root, () => Start);

    // AC-1
    [Fact]
    public void Add_CreatesFolderAndFile_Utf8WithoutBom()
    {
        TodoItem todo = Store().Add("Buy milk", new DateOnly(2026, 11, 3));

        Assert.True(File.Exists(FilePath));
        byte[] bytes = File.ReadAllBytes(FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("Buy milk", todo.Text);
        Assert.Equal(new DateOnly(2026, 11, 3), todo.Due);
        Assert.Equal(DateTimeKind.Utc, todo.Created.Kind);
        string json = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"text\": \"Buy milk\"", json);
        Assert.Contains("\"due\": \"2026-11-03\"", json);
        Assert.Single(Directory.GetFiles(root));
    }

    // AC-1: the temp file is moved into place, none is left behind.
    [Fact]
    public void Add_LeavesNoTempFile()
    {
        TodoStore store = Store();
        store.Add("a", new DateOnly(2026, 11, 3));
        store.Add("b", new DateOnly(2026, 11, 4));
        store.Complete(store.List()[0].Id);

        Assert.Equal([FilePath], Directory.GetFiles(root));
    }

    [Fact]
    public void MissingFile_ListsEmpty_CreatesNothing()
    {
        Assert.Empty(Store().List());
        Assert.False(Directory.Exists(root));
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    // AC-3
    [Fact]
    public void List_SortsByDue_ThenByOrderAdded()
    {
        TodoStore store = Store();
        store.Add("later", new DateOnly(2026, 12, 1));
        store.Add("first of two", new DateOnly(2026, 11, 3));
        store.Add("overdue", new DateOnly(2026, 10, 1));
        store.Add("second of two", new DateOnly(2026, 11, 3));

        Assert.Equal(
            ["overdue", "first of two", "second of two", "later"],
            store.List().Select(t => t.Text));
    }

    // AC-3: with the same Created time, the order added still decides.
    [Fact]
    public void List_SameDueAndCreated_KeepsInsertionOrder()
    {
        TodoStore store = FrozenStore();
        foreach (string text in new[] { "a", "b", "c", "d" })
        {
            store.Add(text, new DateOnly(2026, 11, 3));
        }

        Assert.Equal(["a", "b", "c", "d"], new TodoStore(root).List().Select(t => t.Text));
    }

    // AC-5
    [Fact]
    public void NewStore_ReadsTodosBack()
    {
        TodoStore store = Store();
        TodoItem a = store.Add("[bold]x[/]", new DateOnly(2026, 11, 3));
        TodoItem b = store.Add("Buy milk", new DateOnly(2026, 10, 29));

        IReadOnlyList<TodoItem> read = new TodoStore(root).List();

        Assert.Equal([b, a], read);
    }

    // AC-6 / 080: Complete removes the todo with that id and returns it.
    [Fact]
    public void Complete_RemovesThatId_ReturnsIt()
    {
        TodoStore store = Store();
        store.Add("c", new DateOnly(2026, 11, 5));
        store.Add("a", new DateOnly(2026, 11, 3));
        TodoItem b = store.Add("b", new DateOnly(2026, 11, 4));

        TodoItem? done = store.Complete(b.Id);

        Assert.Equal(b, done);
        Assert.Equal(["a", "c"], new TodoStore(root).List().Select(t => t.Text));
    }

    // 080: an id no todo has returns null and changes nothing.
    [Fact]
    public void Complete_UnknownId_ReturnsNull_ChangesNothing()
    {
        TodoStore store = Store();
        store.Add("a", new DateOnly(2026, 11, 3));
        TodoItem b = store.Add("b", new DateOnly(2026, 11, 4));
        store.Complete(b.Id);
        byte[] before = File.ReadAllBytes(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.Null(store.Complete(b.Id));
        Assert.Null(store.Complete(Guid.NewGuid()));
        Assert.Null(store.Complete(Guid.Empty));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
    }

    // 080: no file: null, and nothing is created.
    [Fact]
    public void Complete_NoFile_ReturnsNull_CreatesNothing()
    {
        Assert.Null(Store().Complete(Guid.NewGuid()));
        Assert.False(Directory.Exists(root));
    }

    // 080: the list changed after it was read (a sooner todo was added), so the old position now holds another
    // todo; completing by id still removes the one that was meant.
    [Fact]
    public void Complete_ListChangedSinceRead_RemovesTheMeantTodo()
    {
        TodoStore store = Store();
        TodoItem later = store.Add("later", new DateOnly(2026, 11, 5));
        TodoItem shown = store.List()[0];
        new TodoStore(root, () => Start).Add("sooner", new DateOnly(2026, 11, 1));

        TodoItem? done = store.Complete(shown.Id);

        Assert.Equal(later, done);
        Assert.Equal(["sooner"], store.List().Select(t => t.Text));
    }

    // 080: each new todo gets a new, non-empty id that is written to the file and read back.
    [Fact]
    public void Add_StoresUniqueIds()
    {
        TodoStore store = FrozenStore();
        TodoItem a = store.Add("same", new DateOnly(2026, 11, 3));
        TodoItem b = store.Add("same", new DateOnly(2026, 11, 3));

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Contains($"\"id\": \"{a.Id}\"", File.ReadAllText(FilePath));
        Assert.Equal([a.Id, b.Id], new TodoStore(root).List().Select(t => t.Id));
    }

    // 080: an injected id is used unless it is empty or already taken.
    [Fact]
    public void Add_InjectedId_UsedUnlessEmptyOrTaken()
    {
        Guid fixedId = new Guid("11111111-2222-3333-4444-555555555555");
        TodoStore store = new TodoStore(root, () => Start, () => fixedId);

        TodoItem first = store.Add("a", new DateOnly(2026, 11, 3));
        TodoItem second = store.Add("b", new DateOnly(2026, 11, 3));
        TodoItem third = new TodoStore(root, () => Start, () => Guid.Empty).Add("c", new DateOnly(2026, 11, 3));

        Assert.Equal(fixedId, first.Id);
        Assert.NotEqual(fixedId, second.Id);
        Assert.NotEqual(Guid.Empty, third.Id);
    }

    // 080 migration: a file from before ids still reads; every todo gets an id that is the same on every read, and
    // reading writes nothing.
    [Fact]
    public void LegacyFile_NoIds_StableDerivedIds_ReadWritesNothing()
    {
        Directory.CreateDirectory(root);
        string legacy = "[\n  {\"text\": \"twin\", \"due\": \"2026-11-03\", \"created\": \"2026-10-01T09:00:00Z\"},\n"
            + "  {\"text\": \"twin\", \"due\": \"2026-11-03\", \"created\": \"2026-10-01T09:00:00Z\"},\n"
            + "  {\"text\": \"other\", \"due\": \"2026-11-04\"}\n]\n";
        File.WriteAllText(FilePath, legacy);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        IReadOnlyList<TodoItem> first = new TodoStore(root).List();
        IReadOnlyList<TodoItem> second = new TodoStore(root).List();

        Assert.Equal(first, second);
        Assert.Equal(3, first.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, first.Select(t => t.Id));
        Assert.Equal(legacy, File.ReadAllText(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
    }

    // 080 migration: completing one legacy twin removes exactly that one and stores the ids, which stay the same.
    [Fact]
    public void LegacyFile_CompleteOneTwin_KeepsTheOtherWithTheSameId()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"twin\",\"due\":\"2026-11-03\"},{\"text\":\"twin\",\"due\":\"2026-11-03\"},"
            + "{\"text\":\"other\",\"due\":\"2026-11-04\"}]");
        TodoStore store = new TodoStore(root);
        IReadOnlyList<TodoItem> before = store.List();

        Assert.Equal(before[1], store.Complete(before[1].Id));

        Assert.Equal([before[0], before[2]], store.List());
        Assert.Contains($"\"id\": \"{before[0].Id}\"", File.ReadAllText(FilePath));
        Assert.Contains($"\"id\": \"{before[2].Id}\"", File.ReadAllText(FilePath));
    }

    // 080: an empty or repeated stored id gets a derived one; a stored id is never taken by a derived one.
    [Fact]
    public void StoredIds_EmptyOrRepeated_GetDerivedUniqueIds()
    {
        Directory.CreateDirectory(root);
        Guid kept = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        File.WriteAllText(FilePath,
            $"[{{\"id\":\"{kept}\",\"text\":\"a\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{kept}\",\"text\":\"b\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{Guid.Empty}\",\"text\":\"c\",\"due\":\"2026-11-03\"}}]");

        IReadOnlyList<TodoItem> todos = new TodoStore(root).List();

        Assert.Equal(kept, todos[0].Id);
        Assert.Equal(3, todos.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, todos.Select(t => t.Id));
        Assert.Equal(todos, new TodoStore(root).List());
    }

    // 080: an id that is not a GUID makes the file unreadable, like any other bad field.
    [Fact]
    public void StoredId_NotAGuid_Unreadable()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "[{\"id\":\"nope\",\"text\":\"a\",\"due\":\"2026-11-03\"}]");

        Assert.Throws<TodoStoreException>(() => new TodoStore(root).List());
    }

    // AC-8
    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{\"due\":\"2026-11-03\"}]")]
    [InlineData("[{\"text\":\"x\"}]")]
    [InlineData("[{\"text\":\"x\",\"due\":\"03.11.2026\"}]")]
    [InlineData("[{\"text\":5,\"due\":\"2026-11-03\"}]")]
    public void CorruptFile_ListThrows_AddAndCompleteLeaveBytesUnchanged(string content)
    {
        Directory.CreateDirectory(root);
        byte[] original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(FilePath, original);
        TodoStore store = Store();

        Assert.Throws<TodoStoreException>(() => store.List());
        Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3)));
        Assert.Throws<TodoStoreException>(() => store.Complete(Guid.NewGuid()));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    // 132 AC-7: a todos.json that starts with the old magic bytes is just a file that isn't JSON.
    [Fact]
    public void FileStartingWithTheOldMagicBytes_IsUnreadableLikeAnyCorruptFile_AndNeverOverwritten()
    {
        Directory.CreateDirectory(root);
        byte[] original = [0x00, 0x43, 0x54, 0x58, 0x45, 0x01, 0x02, 0x03];
        File.WriteAllBytes(FilePath, original);
        TodoStore store = Store();

        Assert.Throws<TodoStoreException>(() => store.List());
        Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3)));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    // 132 AC-9: todos.json is the plain JSON text, byte for byte.
    [Fact]
    public void Add_WritesPlainJsonBytes()
    {
        Guid id = new Guid("11111111-2222-3333-4444-555555555555");
        new TodoStore(root, () => Start, () => id).Add("Buy milk", new DateOnly(2026, 11, 3));

        byte[] bytes = File.ReadAllBytes(FilePath);

        Assert.Equal((byte)'[', bytes[0]);
        Assert.Equal(Encoding.UTF8.GetBytes(File.ReadAllText(FilePath)), bytes);
        TodoItem todo = Assert.Single(Store().List());
        Assert.Equal(id, todo.Id);
        Assert.Equal("Buy milk", todo.Text);
    }
}
