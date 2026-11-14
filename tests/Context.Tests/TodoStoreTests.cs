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

        Assert.Equal(c, store.Complete(c.Id));

        Assert.Equal(before.Where(t => t.Text != "c"), store.List());
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
}
