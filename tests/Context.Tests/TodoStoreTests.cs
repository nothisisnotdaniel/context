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

    // AC-6 / 080 / 159 AC-2: Complete takes the todo with that id off the open list and returns it, done.
    [Fact]
    public void Complete_RemovesThatId_ReturnsIt()
    {
        TodoStore store = Store();
        store.Add("c", new DateOnly(2026, 11, 5));
        store.Add("a", new DateOnly(2026, 11, 3));
        TodoItem b = store.Add("b", new DateOnly(2026, 11, 4));

        TodoItem? done = store.Complete(b.Id);

        AssertDoneOf(b, done);
        Assert.Equal(["a", "c"], new TodoStore(root).List().Select(t => t.Text));
    }

    // The done copy of open: the same id, text, due and created, with Done set.
    private static void AssertDoneOf(TodoItem open, TodoItem? done)
    {
        Assert.NotNull(done);
        Assert.Equal(open.Id, done.Id);
        Assert.Equal(open.Text, done.Text);
        Assert.Equal(open.Due, done.Due);
        Assert.Equal(open.Created, done.Created);
        Assert.Null(open.Done);
        Assert.NotNull(done.Done);
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

        AssertDoneOf(later, done);
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

        AssertDoneOf(before[1], store.Complete(before[1].Id));

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

    // 151: stored id values that are not a GUID string.
    public static TheoryData<string> BadIds => new()
    {
        "\"nope\"",
        "\"\"",
        "42",
        "true",
        "{}",
        "[]",
    };

    private static readonly DateTime Created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    // 151 AC-1: a bad id counts as missing; the todo keeps its fields and gets the derived id, ranked among twins.
    [Theory]
    [MemberData(nameof(BadIds))]
    public void StoredId_Bad_GetsDerivedId_FieldsKept(string bad)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            $"[{{\"id\":{bad},\"text\":\"twin\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"}},"
            + "{\"text\":\"twin\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"},"
            + $"{{\"id\":{bad},\"text\":\"other\",\"due\":\"2026-11-04\"}}]");

        IReadOnlyList<TodoItem> todos = new TodoStore(root).List();

        DateOnly due = new DateOnly(2026, 11, 3);
        Assert.Equal(
            [
                new TodoItem(TodoStore.DerivedId("twin", due, Created, 0), "twin", due, Created),
                new TodoItem(TodoStore.DerivedId("twin", due, Created, 1), "twin", due, Created),
                new TodoItem(
                    TodoStore.DerivedId("other", new DateOnly(2026, 11, 4), DateTime.MinValue, 0),
                    "other", new DateOnly(2026, 11, 4), DateTime.MinValue),
            ],
            todos);
        Assert.Equal(todos, new TodoStore(root).List());
    }

    // 151 AC-2: valid ids stay; a bad-id todo never takes an id a later todo has stored; all distinct, none empty.
    [Fact]
    public void StoredIds_ValidMissingAndBad_ValidKept_NoClash()
    {
        Directory.CreateDirectory(root);
        DateOnly due = new DateOnly(2026, 11, 3);
        Guid kept = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        Guid later = TodoStore.DerivedId("a", due, DateTime.MinValue, 0);
        File.WriteAllText(FilePath,
            $"[{{\"id\":\"nope\",\"text\":\"a\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{kept}\",\"text\":\"b\",\"due\":\"2026-11-03\"}},"
            + "{\"text\":\"c\",\"due\":\"2026-11-03\"},"
            + $"{{\"id\":42,\"text\":\"d\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{later}\",\"text\":\"e\",\"due\":\"2026-11-03\"}}]");

        IReadOnlyList<TodoItem> todos = new TodoStore(root).List();

        Assert.Equal(["a", "b", "c", "d", "e"], todos.Select(t => t.Text));
        Assert.Equal(kept, todos[1].Id);
        Assert.Equal(later, todos[4].Id);
        Assert.Equal(TodoStore.DerivedId("a", due, DateTime.MinValue, 1), todos[0].Id);
        Assert.Equal(5, todos.Select(t => t.Id).Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, todos.Select(t => t.Id));
    }

    // 151 AC-3: other bad fields still make the file unreadable when the id is bad too.
    [Theory]
    [InlineData("[{\"id\":\"nope\",\"due\":\"2026-11-03\"}]")]
    [InlineData("[{\"id\":42}]")]
    [InlineData("[{\"id\":{},\"text\":\"x\"}]")]
    [InlineData("[{\"id\":true,\"text\":\"x\",\"due\":\"03.11.2026\"}]")]
    [InlineData("[{\"id\":[],\"text\":5,\"due\":\"2026-11-03\"}]")]
    [InlineData("[{\"id\":\"\",\"text\":\"x\",\"due\":\"2026-11-03\",\"created\":\"yesterday\"}]")]
    [InlineData("[{\"id\":\"nope\",\"text\":\"x\",\"due\":\"2026-11-03\"},null]")]
    public void StoredId_BadWithOtherBadField_Unreadable(string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, content);

        Assert.Throws<TodoStoreException>(() => new TodoStore(root).List());
        Assert.False(new TodoStore(root).Migrate(out int badIds));
        Assert.Equal(0, badIds);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    // 151 AC-3: a todo with a bad id and no text is unreadable, for every bad id value.
    [Theory]
    [MemberData(nameof(BadIds))]
    public void StoredId_BadAndNoText_Unreadable(string bad)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, $"[{{\"id\":{bad},\"due\":\"2026-11-03\"}}]");

        Assert.Throws<TodoStoreException>(() => new TodoStore(root).List());
    }

    // Files that exist but are not a valid todo list.
    public static TheoryData<string> CorruptContents => new()
    {
        "not json",
        "",
        "null",
        "{}",
        "[null]",
        "[{\"due\":\"2026-11-03\"}]",
        "[{\"text\":\"x\"}]",
        "[{\"text\":\"x\",\"due\":\"03.11.2026\"}]",
        "[{\"text\":5,\"due\":\"2026-11-03\"}]",
    };

    // AC-8
    [Theory]
    [MemberData(nameof(CorruptContents))]
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

    // 149: two identical todos and one without created, none with an id.
    private const string Legacy = "[\n  {\"text\": \"twin\", \"due\": \"2026-11-03\", \"created\": \"2026-10-01T09:00:00Z\"},\n"
        + "  {\"text\": \"twin\", \"due\": \"2026-11-03\", \"created\": \"2026-10-01T09:00:00Z\"},\n"
        + "  {\"text\": \"other\", \"due\": \"2026-11-01\"}\n]\n";

    private void AssertNoTempFile() =>
        Assert.Empty(Directory.GetFiles(root, ".todos.json.*.tmp"));

    // The ids stored in todos.json, in file order.
    private List<Guid> StoredIds() =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(FilePath), "\"id\": \"([0-9a-f-]{36})\"")
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .ToList();

    // 149 AC-1, AC-9
    [Fact]
    public void Migrate_LegacyFile_StoresTheIdsListReturned()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, Legacy);
        TodoStore store = new TodoStore(root);
        IReadOnlyList<TodoItem> before = store.List();

        Assert.True(store.Migrate());

        List<Guid> stored = StoredIds();
        Assert.Equal(3, stored.Count);
        Assert.Equal(before.Select(t => t.Id).ToHashSet(), stored.ToHashSet());
        AssertNoTempFile();
    }

    // 149 AC-1: the stored ids are in file order: the twins first, then the todo without created.
    [Fact]
    public void Migrate_LegacyFile_KeepsFileOrder()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, Legacy);
        TodoStore store = new TodoStore(root);
        IReadOnlyList<TodoItem> before = store.List();

        store.Migrate();

        TodoItem other = before.Single(t => t.Text == "other");
        List<TodoItem> twins = before.Where(t => t.Text == "twin").ToList();
        Assert.Equal([twins[0].Id, twins[1].Id, other.Id], StoredIds());
    }

    // 149 AC-2
    [Fact]
    public void Migrate_Twice_SecondWritesNothing_ListUnchanged()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, Legacy);
        IReadOnlyList<TodoItem> before = new TodoStore(root).List();

        Assert.True(new TodoStore(root).Migrate());
        string text = File.ReadAllText(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.False(new TodoStore(root).Migrate());

        Assert.Equal(text, File.ReadAllText(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        IReadOnlyList<TodoItem> after = new TodoStore(root).List();
        Assert.Equal(
            before.Select(t => (t.Id, t.Text, t.Due, t.Created.Ticks)),
            after.Select(t => (t.Id, t.Text, t.Due, t.Created.Ticks)));
        AssertNoTempFile();
    }

    // 149 AC-3
    [Fact]
    public void Migrate_AllIdsStored_WritesNothing()
    {
        Store().Add("a", new DateOnly(2026, 11, 3));
        Store().Add("b", new DateOnly(2026, 11, 3));
        byte[] bytes = File.ReadAllBytes(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.False(Store().Migrate());

        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
    }

    // 149 AC-4
    [Fact]
    public void Migrate_EmptyOrRepeatedId_FirstHolderKeepsIt()
    {
        Directory.CreateDirectory(root);
        Guid kept = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        File.WriteAllText(FilePath,
            $"[{{\"id\":\"{kept}\",\"text\":\"a\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{kept}\",\"text\":\"b\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{Guid.Empty}\",\"text\":\"c\",\"due\":\"2026-11-03\"}}]");
        IReadOnlyList<TodoItem> before = new TodoStore(root).List();

        Assert.True(new TodoStore(root).Migrate());

        Assert.Equal(before.Select(t => t.Id), StoredIds());
        Assert.Equal(kept, StoredIds()[0]);
        Assert.Equal(before, new TodoStore(root).List());
        Assert.False(new TodoStore(root).Migrate());
    }

    // 149 AC-5
    [Fact]
    public void Migrate_AfterAnOlderBuildDroppedTheIds_StoresTheSameIdsAgain()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, Legacy);
        Assert.True(new TodoStore(root).Migrate());
        List<Guid> first = StoredIds();

        // An older build reads and rewrites the list without the id field.
        string dropped = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(FilePath), "\\s*\"id\": \"[0-9a-f-]{36}\",", string.Empty);
        Assert.DoesNotContain("\"id\"", dropped, StringComparison.Ordinal);
        File.WriteAllText(FilePath, dropped);

        Assert.True(new TodoStore(root).Migrate());

        Assert.Equal(first, StoredIds());
    }

    // 149 AC-6
    [Fact]
    public void Migrate_MissingFile_CreatesNothing()
    {
        Assert.False(new TodoStore(root).Migrate());

        Assert.False(Directory.Exists(root));
    }

    // 149 AC-7
    [Theory]
    [MemberData(nameof(CorruptContents))]
    public void Migrate_CorruptFile_ReturnsFalse_LeavesBytes(string content)
    {
        Directory.CreateDirectory(root);
        byte[] original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(FilePath, original);
        TodoStore store = Store();

        Assert.False(store.Migrate());

        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
        Assert.Throws<TodoStoreException>(() => store.List());
    }

    // 149 AC-7: a file that can't be opened is not touched either. 151 AC-5: and reports no bad ids.
    [Theory]
    [InlineData(Legacy)]
    [InlineData(TwoBadOneMissing)]
    public void Migrate_UnreadableFile_ReturnsFalse_LeavesIt(string content)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, content);
        File.SetUnixFileMode(FilePath, UnixFileMode.None);
        try
        {
            TodoStore store = new TodoStore(root);
            if (TryOpen())
            {
                return; // Running as a user that can read anything; there is nothing to check.
            }

            Assert.False(store.Migrate(out int badIds));
            Assert.Equal(0, badIds);

            Assert.Throws<TodoStoreException>(() => store.List());
            Assert.Single(Directory.GetFiles(root));
        }
        finally
        {
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    // 151: two todos with a bad id and one without an id.
    private const string TwoBadOneMissing = "[\n  {\"id\": \"nope\", \"text\": \"a\", \"due\": \"2026-11-03\", \"created\": \"2026-10-01T09:00:00Z\"},\n"
        + "  {\"text\": \"b\", \"due\": \"2026-11-01\"},\n"
        + "  {\"id\": 42, \"text\": \"c\", \"due\": \"2026-11-02\", \"created\": \"2026-10-02T09:00:00Z\"}\n]\n";

    // 151 AC-4: the bad ids are counted and the ids List returned are stored, in file order; a second run writes nothing.
    [Fact]
    public void Migrate_BadIds_CountsThem_StoresTheListedIds_SecondRunWritesNothing()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, TwoBadOneMissing);
        TodoStore store = new TodoStore(root);
        IReadOnlyList<TodoItem> before = store.List();

        Assert.True(store.Migrate(out int badIds));
        Assert.Equal(2, badIds);

        string[] order = ["a", "b", "c"];
        Assert.Equal(order.Select(t => before.Single(b => b.Text == t).Id), StoredIds());
        IReadOnlyList<TodoItem> after = new TodoStore(root).List();
        Assert.Equal(
            before.Select(t => (t.Id, t.Text, t.Due, t.Created.Ticks)),
            after.Select(t => (t.Id, t.Text, t.Due, t.Created.Ticks)));
        Assert.Equal(order, System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(FilePath), "\"text\": \"(.)\"")
            .Select(m => m.Groups[1].Value));
        byte[] bytes = File.ReadAllBytes(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.False(new TodoStore(root).Migrate(out badIds));
        Assert.Equal(0, badIds);
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        AssertNoTempFile();
    }

    // 151 AC-5: missing or repeated ids are written but are not bad ids; a missing file writes nothing.
    [Fact]
    public void Migrate_NoBadIds_CountsZero()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, Legacy);
        Assert.True(new TodoStore(root).Migrate(out int badIds));
        Assert.Equal(0, badIds);

        Guid kept = new Guid("aaaaaaaa-0000-0000-0000-000000000001");
        File.WriteAllText(FilePath,
            $"[{{\"id\":\"{kept}\",\"text\":\"a\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":\"{kept}\",\"text\":\"b\",\"due\":\"2026-11-03\"}},"
            + $"{{\"id\":null,\"text\":\"c\",\"due\":\"2026-11-03\"}}]");
        Assert.True(new TodoStore(root).Migrate(out badIds));
        Assert.Equal(0, badIds);

        File.Delete(FilePath);
        Assert.False(new TodoStore(root).Migrate(out badIds));
        Assert.Equal(0, badIds);
        Assert.False(File.Exists(FilePath));
    }

    // 151 AC-5: the bad ids are read but can't be written: false, 0, the file is unchanged.
    [Fact]
    public void Migrate_BadIds_WriteFails_CountsZero_LeavesBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, TwoBadOneMissing);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanCreateFileInRoot())
            {
                return; // Running as a user that can write anything; there is nothing to check.
            }

            Assert.False(new TodoStore(root).Migrate(out int badIds));
            Assert.Equal(0, badIds);
        }
        finally
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.Equal(TwoBadOneMissing, File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    // 151 AC-8: after the migration, the id List returned for a formerly bad todo completes exactly that todo.
    [Fact]
    public void Migrate_BadIds_CompleteByListedId_RemovesThatTodo()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, TwoBadOneMissing);
        TodoStore store = new TodoStore(root);
        IReadOnlyList<TodoItem> before = store.List();
        Assert.True(store.Migrate(out _));
        TodoItem c = before.Single(t => t.Text == "c");

        AssertDoneOf(c, store.Complete(c.Id));

        Assert.Equal(before.Where(t => t.Text != "c"), store.List());
    }

    // 159: the ids and the clock for the done tests.
    private static readonly Guid IdA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid IdB = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly DateTime DoneAt = new(2026, 11, 14, 8, 30, 0, DateTimeKind.Utc);

    // 159: a todo the way every build writes it: id, text, due, created, then done only when it is set.
    // 168: then repeat, only when it is set.
    private static string JsonTodo(Guid id, string text, string due, string created, string? done = null, string? repeat = null) =>
        "  {\n"
        + $"    \"id\": \"{id}\",\n"
        + $"    \"text\": \"{text}\",\n"
        + $"    \"due\": \"{due}\",\n"
        + $"    \"created\": \"{created}\""
        + (done is null ? string.Empty : $",\n    \"done\": \"{done}\"")
        + (repeat is null ? string.Empty : $",\n    \"repeat\": \"{repeat}\"")
        + "\n  }";

    private static string JsonList(params string[] todos) => "[\n" + string.Join(",\n", todos) + "\n]\n";

    // 159 AC-1: a file from before this job lists the same todos with the same ids, and nothing is done.
    [Fact]
    public void PreJobFile_ListsSameTodosAndIds_ListDoneEmpty()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"id\":\"" + IdA + "\",\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\"},"
            + "{\"text\":\"b\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"}]");
        byte[] before = File.ReadAllBytes(FilePath);
        TodoStore store = new TodoStore(root);
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            [
                new TodoItem(TodoStore.DerivedId("b", new DateOnly(2026, 11, 3), created, 0), "b", new DateOnly(2026, 11, 3), created),
                new TodoItem(IdA, "a", new DateOnly(2026, 11, 4), created),
            ],
            store.List());
        Assert.Empty(store.ListDone());
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    // 159 AC-1: with no done todo, Add writes exactly the bytes a build from before this job writes (no done field).
    [Fact]
    public void Add_NoDoneTodo_SameBytesAsBefore()
    {
        Guid[] ids = [IdA, IdB];
        int next = 0;
        TodoStore store = new TodoStore(root, () => Start, () => ids[next++]);
        store.Add("Buy milk", new DateOnly(2026, 11, 3));
        store.Add("Call", new DateOnly(2026, 11, 1));

        string expected = JsonList(
            JsonTodo(IdA, "Buy milk", "2026-11-03", "2026-10-28T09:00:00Z"),
            JsonTodo(IdB, "Call", "2026-11-01", "2026-10-28T09:00:00Z"));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(FilePath));
    }

    // 159 AC-1: with no done todo, Migrate writes exactly the bytes a build from before this job writes.
    [Fact]
    public void Migrate_NoDoneTodo_SameBytesAsBefore()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\"},"
            + "{\"id\":\"" + IdB + "\",\"text\":\"b\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":null}]");
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid derived = TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 0);

        Assert.True(new TodoStore(root).Migrate());

        string expected = JsonList(
            JsonTodo(derived, "a", "2026-11-04", "2026-10-01T09:00:00Z"),
            JsonTodo(IdB, "b", "2026-11-03", "2026-10-01T09:00:00Z"));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(FilePath));
    }

    // 159 AC-2: Complete sets Done from the clock; the todo stays in the file with its fields, plus done.
    [Fact]
    public void Complete_SetsDoneFromClock_KeepsTodoInFile()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Buy milk", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => Start, () => IdB).Add("Call", new DateOnly(2026, 11, 1));
        TodoStore store = new TodoStore(root, () => DoneAt);

        TodoItem? done = store.Complete(IdA);

        Assert.Equal(new TodoItem(IdA, "Buy milk", new DateOnly(2026, 11, 3), Start, DoneAt), done);
        Assert.Equal(DateTimeKind.Utc, done!.Done!.Value.Kind);
        string expected = JsonList(
            JsonTodo(IdA, "Buy milk", "2026-11-03", "2026-10-28T09:00:00Z", "2026-11-14T08:30:00Z"),
            JsonTodo(IdB, "Call", "2026-11-01", "2026-10-28T09:00:00Z"));
        Assert.Equal(expected, File.ReadAllText(FilePath));
        TodoStore fresh = new TodoStore(root);
        Assert.Equal([IdB], fresh.List().Select(t => t.Id));
        Assert.Equal([done], fresh.ListDone());
        AssertNoTempFile();
    }

    // 159 AC-2: two done todos are both kept; the latest done comes first.
    [Fact]
    public void Complete_Two_BothKept_LatestDoneFirst()
    {
        new TodoStore(root, () => Start, () => IdA).Add("a", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => Start.AddMinutes(1), () => IdB).Add("b", new DateOnly(2026, 11, 4));

        new TodoStore(root, () => DoneAt).Complete(IdB);
        new TodoStore(root, () => DoneAt.AddHours(1)).Complete(IdA);

        TodoStore store = new TodoStore(root);
        Assert.Empty(store.List());
        Assert.Equal(
            [(IdA, DoneAt.AddHours(1)), (IdB, DoneAt)],
            store.ListDone().Select(t => (t.Id, t.Done!.Value)));
    }

    // 159 AC-2: equal done times fall back to created, then file order.
    [Fact]
    public void ListDone_SameDone_ByCreatedThenFileOrder()
    {
        Guid idC = new("cccccccc-0000-0000-0000-000000000003");
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, JsonList(
            JsonTodo(IdA, "a", "2026-11-03", "2026-10-02T09:00:00Z", "2026-11-14T08:30:00Z"),
            JsonTodo(IdB, "b", "2026-11-03", "2026-10-01T09:00:00Z", "2026-11-14T08:30:00Z"),
            JsonTodo(idC, "c", "2026-11-01", "2026-10-02T09:00:00Z", "2026-11-14T08:30:00Z")));

        Assert.Equal([IdB, IdA, idC], new TodoStore(root).ListDone().Select(t => t.Id));
    }

    // 159 AC-2: completing a done todo again returns null and writes nothing.
    [Fact]
    public void Complete_AlreadyDone_ReturnsNull_WritesNothing()
    {
        new TodoStore(root, () => Start, () => IdA).Add("a", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => DoneAt).Complete(IdA);
        byte[] before = File.ReadAllBytes(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath);

        Assert.Null(new TodoStore(root, () => DoneAt.AddDays(1)).Complete(IdA));

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
        Assert.Equal(DoneAt, Assert.Single(new TodoStore(root).ListDone()).Done);
    }

    // 159 AC-3: a done todo survives Add, another Complete and Migrate with its marker.
    [Fact]
    public void DoneTodo_SurvivesAddCompleteAndMigrate()
    {
        new TodoStore(root, () => Start, () => IdA).Add("a", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => DoneAt).Complete(IdA);
        TodoItem doneA = Assert.Single(new TodoStore(root).ListDone());

        TodoItem b = new TodoStore(root, () => Start, () => IdB).Add("b", new DateOnly(2026, 11, 4));
        Assert.Equal([doneA], new TodoStore(root).ListDone());

        TodoItem c = new TodoStore(root, () => Start).Add("c", new DateOnly(2026, 11, 5));
        new TodoStore(root, () => DoneAt.AddHours(2)).Complete(b.Id);
        Assert.Equal([b.Id, IdA], new TodoStore(root).ListDone().Select(t => t.Id));

        // An older build dropped c's id: Migrate writes it again and keeps both done markers.
        string text = File.ReadAllText(FilePath).Replace($"\"id\": \"{c.Id}\",", string.Empty, StringComparison.Ordinal);
        File.WriteAllText(FilePath, text);
        Assert.True(new TodoStore(root).Migrate());

        TodoStore store = new TodoStore(root);
        Assert.Equal(["c"], store.List().Select(t => t.Text));
        Assert.Equal(
            [(b.Id, DoneAt.AddHours(2)), (IdA, DoneAt)],
            store.ListDone().Select(t => (t.Id, t.Done!.Value)));
    }

    // 159 AC-3: a done todo stored without an id gets its derived id (done is not part of the key) and keeps done.
    [Fact]
    public void Migrate_DoneTodoWithoutId_DerivedId_KeepsDone()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":\"2026-11-14T08:30:00Z\"}]");
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid derived = TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 0);
        Assert.Equal(derived, Assert.Single(new TodoStore(root).ListDone()).Id);

        Assert.True(new TodoStore(root).Migrate());

        Assert.Equal(
            JsonList(JsonTodo(derived, "a", "2026-11-04", "2026-10-01T09:00:00Z", "2026-11-14T08:30:00Z")),
            File.ReadAllText(FilePath));
        Assert.False(new TodoStore(root).Migrate());
    }

    // 159 AC-3: a new todo never takes the id of a done one.
    [Fact]
    public void Add_IdOfDoneTodo_NeverReused()
    {
        new TodoStore(root, () => Start, () => IdA).Add("a", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => DoneAt).Complete(IdA);

        TodoItem added = new TodoStore(root, () => Start, () => IdA).Add("b", new DateOnly(2026, 11, 3));

        Assert.NotEqual(IdA, added.Id);
        Assert.NotEqual(Guid.Empty, added.Id);
        TodoStore store = new TodoStore(root);
        Assert.Equal([added.Id], store.List().Select(t => t.Id));
        Assert.Equal([IdA], store.ListDone().Select(t => t.Id));
    }

    // 159 AC-3: a derived id never equals a done todo's stored id either.
    [Fact]
    public void DerivedId_SkipsStoredIdOfDoneTodo()
    {
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid taken = TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 0);
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\"},"
            + "{\"id\":\"" + taken + "\",\"text\":\"x\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":\"2026-11-14T08:30:00Z\"}]");

        TodoStore store = new TodoStore(root);

        Assert.Equal(TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 1), Assert.Single(store.List()).Id);
        Assert.Equal(taken, Assert.Single(store.ListDone()).Id);
    }

    // 159 AC-3: a done value that is not null and not a date-time string makes the file unreadable, never overwritten.
    [Theory]
    [InlineData("\"nope\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"\"")]
    [InlineData("{}")]
    public void BadDone_Unreadable_NeverOverwritten(string done)
    {
        Directory.CreateDirectory(root);
        string content = "[{\"id\":\"" + IdA + "\",\"text\":\"a\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":"
            + done + "}]";
        byte[] original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(FilePath, original);
        TodoStore store = Store();

        TodoStoreException error = Assert.Throws<TodoStoreException>(() => store.List());
        Assert.Equal("Can't read todos.json.", error.Message);
        Assert.Throws<TodoStoreException>(() => store.ListDone());
        Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3)));
        Assert.Throws<TodoStoreException>(() => store.Complete(IdA));
        Assert.False(store.Migrate());
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    // 159 nit: a hand-edited done with an offset or without a zone is read as UTC and written back with Z.
    [Theory]
    [InlineData("2026-11-14T10:30:00+02:00")]
    [InlineData("2026-11-14T08:30:00")]
    [InlineData("2026-11-14T08:30:00Z")]
    public void Done_OffsetOrNoZone_ReadAsUtc(string done)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"id\":\"" + IdA + "\",\"text\":\"a\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":\"" + done + "\"},"
            + "{\"text\":\"b\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\"}]");

        TodoItem read = Assert.Single(new TodoStore(root).ListDone());
        Assert.Equal(DoneAt, read.Done);
        Assert.Equal(DateTimeKind.Utc, read.Done!.Value.Kind);

        Assert.True(new TodoStore(root).Migrate());
        Assert.Contains("\"done\": \"2026-11-14T08:30:00Z\"", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    // 159 AC-3: "done": null is an open todo.
    [Fact]
    public void DoneNull_IsOpen()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"id\":\"" + IdA + "\",\"text\":\"a\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"done\":null}]");
        TodoStore store = new TodoStore(root, () => DoneAt);

        Assert.Null(Assert.Single(store.List()).Done);
        Assert.Empty(store.ListDone());
        Assert.NotNull(store.Complete(IdA));
        Assert.Equal(DoneAt, Assert.Single(store.ListDone()).Done);
    }

    // 168: the next id and the day for the recurring tests.
    private static readonly Guid IdC = new("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateOnly RepeatToday = new(2026, 11, 14);

    // 168 AC-3: completing a recurring todo keeps the done record and appends one open todo, in one write.
    [Fact]
    public void Complete_Recurring_KeepsDone_AppendsNext()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Water plants", new DateOnly(2026, 11, 9), TodoRepeat.Weekly(DayOfWeek.Monday));
        new TodoStore(root, () => Start, () => IdB).Add("Call", new DateOnly(2026, 11, 1));
        TodoStore store = new TodoStore(root, () => DoneAt, () => IdC);

        TodoItem done = store.Complete(IdA, RepeatToday, out TodoItem? next)!;

        Assert.Equal(
            new TodoItem(IdA, "Water plants", new DateOnly(2026, 11, 9), Start, DoneAt, TodoRepeat.Weekly(DayOfWeek.Monday)), done);
        Assert.Equal(
            new TodoItem(IdC, "Water plants", new DateOnly(2026, 11, 16), DoneAt, null, TodoRepeat.Weekly(DayOfWeek.Monday)), next);
        string expected = JsonList(
            JsonTodo(IdA, "Water plants", "2026-11-09", "2026-10-28T09:00:00Z", "2026-11-14T08:30:00Z", "monday"),
            JsonTodo(IdB, "Call", "2026-11-01", "2026-10-28T09:00:00Z"),
            JsonTodo(IdC, "Water plants", "2026-11-16", "2026-11-14T08:30:00Z", repeat: "monday"));
        Assert.Equal(expected, File.ReadAllText(FilePath));
        TodoStore fresh = new TodoStore(root);
        Assert.Equal([IdB, IdC], fresh.List().Select(t => t.Id));
        Assert.Equal([done], fresh.ListDone());
        AssertNoTempFile();
    }

    // 168 AC-3: an overdue daily todo comes back tomorrow, never in the past; Complete(id) still works for it.
    [Fact]
    public void Complete_RecurringOverdue_NextAfterToday()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Stretch", new DateOnly(2026, 11, 1), TodoRepeat.Daily);
        TodoStore store = new TodoStore(root, () => DoneAt, () => IdC);

        store.Complete(IdA, RepeatToday, out TodoItem? next);

        Assert.Equal(new DateOnly(2026, 11, 15), next!.Due);
        Assert.Equal(TodoRepeat.Daily, next.Repeat);
        Assert.Equal(next, Assert.Single(store.List()));
    }

    // 168 AC-3: a todo that doesn't repeat writes exactly the bytes it did before this job, and has no next todo.
    [Fact]
    public void Complete_NonRecurring_SameBytesAsBefore_NoNext()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Buy milk", new DateOnly(2026, 11, 3));
        new TodoStore(root, () => Start, () => IdB).Add("Call", new DateOnly(2026, 11, 1));
        TodoStore store = new TodoStore(root, () => DoneAt, () => IdC);

        TodoItem? done = store.Complete(IdA, RepeatToday, out TodoItem? next);

        Assert.NotNull(done);
        Assert.Null(next);
        string expected = JsonList(
            JsonTodo(IdA, "Buy milk", "2026-11-03", "2026-10-28T09:00:00Z", "2026-11-14T08:30:00Z"),
            JsonTodo(IdB, "Call", "2026-11-01", "2026-10-28T09:00:00Z"));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(FilePath));
    }

    // 168 AC-3: an unknown or already-done id writes nothing and gives no next todo.
    [Fact]
    public void Complete_Recurring_UnknownOrDone_WritesNothing_NoNext()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Stretch", new DateOnly(2026, 11, 14), TodoRepeat.Daily);
        TodoStore store = new TodoStore(root, () => DoneAt, () => IdC);
        byte[] before = File.ReadAllBytes(FilePath);

        Assert.Null(store.Complete(IdB, RepeatToday, out TodoItem? unknown));
        Assert.Null(unknown);
        Assert.Equal(before, File.ReadAllBytes(FilePath));

        Assert.NotNull(store.Complete(IdA, RepeatToday, out _));
        byte[] afterFirst = File.ReadAllBytes(FilePath);
        Assert.Null(store.Complete(IdA, RepeatToday, out TodoItem? again));
        Assert.Null(again);
        Assert.Equal(afterFirst, File.ReadAllBytes(FilePath));
        Assert.Single(store.List());
    }

    // 168 AC-4: a file without repeat reads and migrates to the same bytes a build from before this job writes.
    [Fact]
    public void Migrate_NoRepeat_SameBytesAsBefore()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\"},"
            + "{\"id\":\"" + IdB + "\",\"text\":\"b\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"repeat\":null}]");
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid derived = TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 0);
        Assert.All(new TodoStore(root).List(), t => Assert.Null(t.Repeat));

        Assert.True(new TodoStore(root).Migrate());

        string expected = JsonList(
            JsonTodo(derived, "a", "2026-11-04", "2026-10-01T09:00:00Z"),
            JsonTodo(IdB, "b", "2026-11-03", "2026-10-01T09:00:00Z"));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(FilePath));
    }

    // 168 AC-4: every repeat kind round-trips through Add, read and Migrate; repeat is not part of the derived id.
    [Fact]
    public void Repeat_RoundTrips_NotInDerivedId()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-04\",\"created\":\"2026-10-01T09:00:00Z\",\"repeat\":\"month-31\"},"
            + "{\"id\":\"" + IdB + "\",\"text\":\"b\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"repeat\":\"day\"}]");
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid derived = TodoStore.DerivedId("a", new DateOnly(2026, 11, 4), created, 0);

        Assert.True(new TodoStore(root).Migrate());
        new TodoStore(root, () => Start, () => IdC).Add("c", new DateOnly(2026, 11, 5), TodoRepeat.Weekly(DayOfWeek.Friday));

        string expected = JsonList(
            JsonTodo(derived, "a", "2026-11-04", "2026-10-01T09:00:00Z", repeat: "month-31"),
            JsonTodo(IdB, "b", "2026-11-03", "2026-10-01T09:00:00Z", repeat: "day"),
            JsonTodo(IdC, "c", "2026-11-05", "2026-10-28T09:00:00Z", repeat: "friday"));
        Assert.Equal(expected, File.ReadAllText(FilePath));
        Assert.Equal(
            [TodoRepeat.Daily, TodoRepeat.Monthly(31), TodoRepeat.Weekly(DayOfWeek.Friday)],
            new TodoStore(root).List().Select(t => t.Repeat));
    }

    // 168 AC-4: a repeat that is not null and not a stored string makes the file unreadable, never overwritten.
    [Theory]
    [InlineData("\"week\"")]
    [InlineData("\"Monday\"")]
    [InlineData("\"month-32\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void BadRepeat_Unreadable_NeverOverwritten(string repeat)
    {
        Directory.CreateDirectory(root);
        string content = "[{\"id\":\"" + IdA + "\",\"text\":\"a\",\"due\":\"2026-11-03\",\"created\":\"2026-10-01T09:00:00Z\",\"repeat\":"
            + repeat + "}]";
        byte[] original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(FilePath, original);
        TodoStore store = Store();

        TodoStoreException error = Assert.Throws<TodoStoreException>(() => store.List());
        Assert.Equal("Can't read todos.json.", error.Message);
        Assert.Throws<TodoStoreException>(() => store.ListDone());
        Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3)));
        Assert.Throws<TodoStoreException>(() => store.Complete(IdA, RepeatToday, out _));
        Assert.False(store.Migrate());
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }

    private bool CanCreateFileInRoot()
    {
        string probe = Path.Combine(root, "probe");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool TryOpen()
    {
        try
        {
            using FileStream stream = File.OpenRead(FilePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // 171 AC-5: MoveDue moves open todos due on or before the day, keeps id, created and repeat, in List() order.
    [Fact]
    public void MoveDue_MovesOpenDueTodosOnly()
    {
        TodoStore store = Store();
        TodoItem late = store.Add("Late", new DateOnly(2026, 11, 10));
        TodoItem due = store.Add("Due", new DateOnly(2026, 11, 14), TodoRepeat.Weekly(DayOfWeek.Saturday));
        TodoItem later = store.Add("Later", new DateOnly(2026, 11, 15));
        TodoItem done = store.Add("Done", new DateOnly(2026, 11, 12));
        store.Complete(done.Id);
        string doneLine = File.ReadAllText(FilePath);

        IReadOnlyList<TodoItem> moved = store.MoveDue(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15));

        Assert.Equal(
            [new TodoItem(late.Id, "Late", new DateOnly(2026, 11, 15), late.Created),
             new TodoItem(due.Id, "Due", new DateOnly(2026, 11, 15), due.Created, null, due.Repeat)],
            moved);
        IReadOnlyList<TodoItem> open = store.List();
        Assert.All(open, t => Assert.Equal(new DateOnly(2026, 11, 15), t.Due));
        Assert.Equal([late.Id, due.Id, later.Id], open.Select(t => t.Id));
        TodoItem stillDone = Assert.Single(store.ListDone());
        Assert.Equal(new DateOnly(2026, 11, 12), stillDone.Due);
        Assert.Equal(done.Id, stillDone.Id);
        Assert.NotEqual(doneLine, File.ReadAllText(FilePath));
    }

    // 171 AC-5: nothing due writes nothing; a missing file stays missing; an unreadable file is never written.
    [Fact]
    public void MoveDue_NothingDue_WritesNothing()
    {
        TodoStore store = Store();
        Assert.Empty(store.MoveDue(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15)));
        Assert.False(File.Exists(FilePath));

        store.Add("Later", new DateOnly(2026, 11, 20));
        DateTime written = File.GetLastWriteTimeUtc(FilePath);
        byte[] before = File.ReadAllBytes(FilePath);
        File.SetLastWriteTimeUtc(FilePath, written.AddHours(-1));

        Assert.Empty(store.MoveDue(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 15)));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(FilePath));

        File.WriteAllText(FilePath, "not json");
        Assert.Throws<TodoStoreException>(() => store.MoveDue(new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 1)));
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    // 178: the clock the reminder tests mark todos notified with.
    private static readonly DateTime NotifiedAt = new(2026, 11, 16, 13, 0, 30, DateTimeKind.Utc);

    // 178 AC-1: a timed todo is written with "time":"HH:mm" after the other fields and reads back with its time.
    [Fact]
    public void Add_WithTime_WritesTimeAndReadsItBack()
    {
        TodoStore store = new TodoStore(root, () => Start, () => IdA);

        TodoItem added = store.Add("Call Anna", new DateOnly(2026, 11, 16), null, new TimeOnly(14, 0));

        Assert.Equal(new TodoItem(IdA, "Call Anna", new DateOnly(2026, 11, 16), Start, time: new TimeOnly(14, 0)), added);
        string json = File.ReadAllText(FilePath);
        Assert.Equal(
            "[\n  {\n    \"id\": \"" + IdA + "\",\n    \"text\": \"Call Anna\",\n    \"due\": \"2026-11-16\",\n"
            + "    \"created\": \"2026-10-28T09:00:00Z\",\n    \"time\": \"14:00\"\n  }\n]\n",
            json);
        TodoItem read = Assert.Single(new TodoStore(root).List());
        Assert.Equal(new TimeOnly(14, 0), read.Time);
        Assert.Null(read.Notified);
    }

    // 178 AC-1: a file without time or notified reads, and rewrites through Add, to the same bytes as before.
    [Fact]
    public void FileWithoutTime_RewritesSameBytes()
    {
        Directory.CreateDirectory(root);
        string a = JsonTodo(IdA, "a", "2026-11-03", "2026-10-28T09:00:00Z");
        File.WriteAllText(FilePath, JsonList(a));

        new TodoStore(root, () => Start, () => IdB).Add("b", new DateOnly(2026, 11, 4));

        Assert.Equal(JsonList(a, JsonTodo(IdB, "b", "2026-11-04", "2026-10-28T09:00:00Z")), File.ReadAllText(FilePath));
        Assert.All(new TodoStore(root).List(), t => Assert.Null(t.Time));
    }

    // 178 AC-1: notified reads like done (UTC, an offset converted), and Migrate keeps time and notified.
    [Fact]
    public void Notified_ReadsAsUtc_MigrateKeepsTimeAndNotified()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-16\",\"created\":\"2026-10-01T09:00:00Z\",\"time\":\"09:30\","
            + "\"notified\":\"2026-11-16T09:30:00+01:00\"}]");

        TodoItem read = Assert.Single(new TodoStore(root).List());
        Assert.Equal(new TimeOnly(9, 30), read.Time);
        Assert.Equal(new DateTime(2026, 11, 16, 8, 30, 0, DateTimeKind.Utc), read.Notified);
        Assert.Equal(DateTimeKind.Utc, read.Notified!.Value.Kind);

        Assert.True(new TodoStore(root).Migrate());
        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"time\": \"09:30\"", json, StringComparison.Ordinal);
        Assert.Contains("\"notified\": \"2026-11-16T08:30:00Z\"", json, StringComparison.Ordinal);
        Assert.Equal(read, Assert.Single(new TodoStore(root).List()));
    }

    public static TheoryData<string> BadTimes => new()
    {
        "\"time\":\"14:60\"",
        "\"time\":\"24:00\"",
        "\"time\":\"2pm\"",
        "\"time\":\"9:00\"",
        "\"time\":\"14:00:00\"",
        "\"time\":\"\"",
        "\"time\":42",
        "\"time\":{}",
        "\"notified\":\"soon\"",
        "\"notified\":42",
        "\"notified\":true",
    };

    // 178 AC-1: a bad time or notified value makes the file unreadable; it is never overwritten.
    [Theory]
    [MemberData(nameof(BadTimes))]
    public void BadTimeOrNotified_IsUnreadable(string field)
    {
        Directory.CreateDirectory(root);
        string text = "[{\"text\":\"a\",\"due\":\"2026-11-16\",\"created\":\"2026-10-01T09:00:00Z\"," + field + "}]";
        File.WriteAllText(FilePath, text);
        TodoStore store = new TodoStore(root, () => Start, () => IdB);

        Assert.Throws<TodoStoreException>(() => store.List());
        Assert.Throws<TodoStoreException>(() => store.Add("b", new DateOnly(2026, 11, 16)));
        Assert.Throws<TodoStoreException>(() => store.MarkNotified([IdA]));
        Assert.False(store.Migrate());
        Assert.Equal(text, File.ReadAllText(FilePath));
    }

    // 178 AC-2: within one date the untimed todos come first, then timed ones by time, then by created.
    [Fact]
    public void List_WithinADate_UntimedFirst_ThenByTime_ThenByCreated()
    {
        TodoStore store = Store();
        DateOnly day = new DateOnly(2026, 11, 16);
        store.Add("at 14", day, null, new TimeOnly(14, 0));
        store.Add("untimed late", day);
        store.Add("at 9", day, null, new TimeOnly(9, 0));
        store.Add("next day at 8", day.AddDays(1), null, new TimeOnly(8, 0));
        store.Add("at 14 again", day, null, new TimeOnly(14, 0));
        store.Add("day before", day.AddDays(-1));

        Assert.Equal(
            ["day before", "untimed late", "at 9", "at 14", "at 14 again", "next day at 8"],
            store.List().Select(t => t.Text));
    }

    // 178 AC-2: time and notified are not part of the derived id.
    [Fact]
    public void DerivedId_IgnoresTimeAndNotified()
    {
        Directory.CreateDirectory(root);
        DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(FilePath,
            "[{\"text\":\"a\",\"due\":\"2026-11-16\",\"created\":\"2026-10-01T09:00:00Z\",\"time\":\"14:00\","
            + "\"notified\":\"2026-11-16T13:00:00Z\"}]");

        Assert.Equal(
            TodoStore.DerivedId("a", new DateOnly(2026, 11, 16), created, 0),
            Assert.Single(new TodoStore(root).List()).Id);
    }

    // 178 AC-5: only open, timed, not yet notified todos whose date and time have come, in List order.
    [Fact]
    public void DueReminders_ReturnsOnlyDueTimedOpenUnnotified()
    {
        DateOnly day = new DateOnly(2026, 11, 16);
        Guid[] ids =
        [
            new("00000000-0000-0000-0000-000000000001"), new("00000000-0000-0000-0000-000000000002"),
            new("00000000-0000-0000-0000-000000000003"), new("00000000-0000-0000-0000-000000000004"),
            new("00000000-0000-0000-0000-000000000005"), new("00000000-0000-0000-0000-000000000006"),
            new("00000000-0000-0000-0000-000000000007"),
        ];
        int next = 0;
        TodoStore store = new TodoStore(root, () => now = now.AddMinutes(1), () => ids[next++]);
        store.Add("exactly now", day, null, new TimeOnly(14, 0));
        store.Add("untimed", day.AddDays(-1));
        store.Add("future", day, null, new TimeOnly(14, 1));
        store.Add("yesterday", day.AddDays(-1), null, new TimeOnly(23, 0));
        store.Add("done", day, null, new TimeOnly(8, 0));
        store.Add("notified", day, null, new TimeOnly(9, 0));
        store.Add("tomorrow", day.AddDays(1), null, new TimeOnly(0, 0));
        store.Complete(ids[4]);
        store.MarkNotified([ids[5]]);
        byte[] before = File.ReadAllBytes(FilePath);

        IReadOnlyList<TodoItem> due = store.DueReminders(new DateTime(2026, 11, 16, 14, 0, 0, DateTimeKind.Local));

        Assert.Equal(["yesterday", "exactly now"], due.Select(t => t.Text));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Empty(store.DueReminders(new DateTime(2026, 11, 15, 22, 59, 0, DateTimeKind.Local)));
    }

    // 178 AC-5: a missing file has no reminders and creates nothing; a bad one throws.
    [Fact]
    public void DueReminders_MissingFileEmpty_BadFileThrows()
    {
        Assert.Empty(Store().DueReminders(new DateTime(2026, 11, 16, 14, 0, 0)));
        Assert.False(Directory.Exists(root));

        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "not json");
        Assert.Throws<TodoStoreException>(() => Store().DueReminders(new DateTime(2026, 11, 16, 14, 0, 0)));
    }

    // 178 AC-6: MarkNotified sets notified from the UTC clock for all the ids in one write and returns them.
    [Fact]
    public void MarkNotified_MarksAllInOneWrite_SkipsGoneDoneAndNotified()
    {
        new TodoStore(root, () => Start, () => IdA).Add("a", new DateOnly(2026, 11, 16), null, new TimeOnly(14, 0));
        new TodoStore(root, () => Start, () => IdB).Add("b", new DateOnly(2026, 11, 16), null, new TimeOnly(9, 0));
        new TodoStore(root, () => Start, () => IdC).Add("c", new DateOnly(2026, 11, 16), null, new TimeOnly(10, 0));
        new TodoStore(root, () => DoneAt).Complete(IdC);
        TodoStore store = new TodoStore(root, () => NotifiedAt);

        IReadOnlyList<TodoItem> marked = store.MarkNotified([IdA, IdB, IdC, Guid.NewGuid()]);

        Assert.Equal([IdB, IdA], marked.Select(t => t.Id));
        Assert.All(marked, t => Assert.Equal(NotifiedAt, t.Notified));
        Assert.Contains("\"notified\": \"2026-11-16T13:00:30Z\"", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.Null(Assert.Single(store.ListDone()).Notified);
        Assert.Equal(marked, store.List());

        byte[] before = File.ReadAllBytes(FilePath);
        DateTime written = File.GetLastWriteTimeUtc(FilePath).AddHours(-1);
        File.SetLastWriteTimeUtc(FilePath, written);
        Assert.Empty(store.MarkNotified([IdA, IdB, IdC]));
        Assert.Empty(store.MarkNotified([]));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FilePath));
    }

    // 178 AC-8: a moved timed todo keeps its time and loses notified, so it reminds again on its new date.
    [Fact]
    public void MoveDue_KeepsTime_ClearsNotified()
    {
        new TodoStore(root, () => Start, () => IdA).Add("Call Anna", new DateOnly(2026, 11, 16), null, new TimeOnly(14, 0));
        new TodoStore(root, () => NotifiedAt).MarkNotified([IdA]);
        TodoStore store = new TodoStore(root, () => NotifiedAt);

        TodoItem moved = Assert.Single(store.MoveDue(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17)));

        Assert.Equal(new TodoItem(IdA, "Call Anna", new DateOnly(2026, 11, 17), Start, time: new TimeOnly(14, 0)), moved);
        Assert.Equal(moved, Assert.Single(store.List()));
        Assert.DoesNotContain("notified", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.Empty(store.DueReminders(new DateTime(2026, 11, 17, 13, 59, 0)));
        Assert.Equal([IdA], store.DueReminders(new DateTime(2026, 11, 17, 14, 0, 0)).Select(t => t.Id));
    }

    // 178 AC-8: Complete keeps time and notified on the done todo; the next recurring todo has the time, no notified.
    [Fact]
    public void Complete_RecurringTimed_NextKeepsTime_NotNotified()
    {
        new TodoStore(root, () => Start, () => IdA).Add(
            "Standup", new DateOnly(2026, 11, 16), TodoRepeat.Daily, new TimeOnly(9, 30));
        new TodoStore(root, () => NotifiedAt).MarkNotified([IdA]);
        TodoStore store = new TodoStore(root, () => DoneAt, () => IdB);

        TodoItem done = store.Complete(IdA, new DateOnly(2026, 11, 16), out TodoItem? next)!;

        Assert.Equal(
            new TodoItem(IdA, "Standup", new DateOnly(2026, 11, 16), Start, DoneAt, TodoRepeat.Daily, new TimeOnly(9, 30), NotifiedAt),
            done);
        Assert.Equal(
            new TodoItem(IdB, "Standup", new DateOnly(2026, 11, 17), DoneAt, null, TodoRepeat.Daily, new TimeOnly(9, 30)), next);
        Assert.Equal([done], store.ListDone());
        Assert.Equal([next!], store.List());
        Assert.Equal([IdB], store.DueReminders(new DateTime(2026, 11, 17, 9, 30, 0)).Select(t => t.Id));
    }

    // 275 AC-2: a todos.json over MaxFileBytes can't be read; reads and Add throw and the file stays as it was.
    [Fact]
    public void OverMaxFileBytes_ReadAndAddThrow_FileUnchanged()
    {
        Store().Add("Buy milk", new DateOnly(2026, 11, 3));
        using (FileStream stream = new FileStream(FilePath, FileMode.Append))
        {
            stream.Write(Enumerable.Repeat((byte)' ', (int)(TodoStore.MaxFileBytes + 1 - stream.Length)).ToArray());
        }

        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FilePath, time);
        byte[] before = File.ReadAllBytes(FilePath);
        TodoStore store = Store();

        Assert.Equal("Can't read todos.json.", Assert.Throws<TodoStoreException>(() => store.List()).Message);
        Assert.Equal("Can't read todos.json.", Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3))).Message);

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(time, File.GetLastWriteTimeUtc(FilePath));
    }

    // 275 AC-2: a todos.json of exactly MaxFileBytes (valid JSON padded with spaces) still reads.
    [Fact]
    public void AtMaxFileBytes_Reads()
    {
        Store().Add("Buy milk", new DateOnly(2026, 11, 3));
        using (FileStream stream = new FileStream(FilePath, FileMode.Append))
        {
            stream.Write(Enumerable.Repeat((byte)' ', (int)(TodoStore.MaxFileBytes - stream.Length)).ToArray());
        }

        Assert.Equal(TodoStore.MaxFileBytes, new FileInfo(FilePath).Length);
        Assert.Equal(["Buy milk"], Store().List().Select(t => t.Text));
    }

    // 275 AC-2: a directory named todos.json doesn't read as empty; reads and Add throw and change nothing.
    [Fact]
    public void Directory_ReadAndAddThrow_Unchanged()
    {
        Directory.CreateDirectory(FilePath);
        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(FilePath, time);
        TodoStore store = Store();

        Assert.Equal("Can't read todos.json.", Assert.Throws<TodoStoreException>(() => store.List()).Message);
        Assert.Equal("Can't read todos.json.", Assert.Throws<TodoStoreException>(() => store.Add("x", new DateOnly(2026, 11, 3))).Message);

        Assert.True(Directory.Exists(FilePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(FilePath));
        Assert.Equal(time, Directory.GetLastWriteTimeUtc(FilePath));
        Assert.Equal([FilePath], Directory.GetFileSystemEntries(root));
    }

    // 275 AC-2: a FIFO todos.json with no writer can't be read and doesn't block; reads and Add throw.
    [Fact(Timeout = 30_000)]
    public async Task Fifo_ReadAndAddThrow_WithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(root);
        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", FilePath))
        {
            mkfifo.WaitForExit();
        }

        DateTime time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FilePath, time);
        TodoStore store = Store();

        TodoStoreException list = await Assert.ThrowsAsync<TodoStoreException>(() => Task.Run(store.List));
        TodoStoreException add = await Assert.ThrowsAsync<TodoStoreException>(
            () => Task.Run(() => store.Add("x", new DateOnly(2026, 11, 3))));

        Assert.Equal("Can't read todos.json.", list.Message);
        Assert.Equal("Can't read todos.json.", add.Message);
        Assert.Equal(time, File.GetLastWriteTimeUtc(FilePath));
        Assert.Equal(0, new FileInfo(FilePath).Length);
        Assert.Equal([FilePath], Directory.GetFileSystemEntries(root));
    }
}
