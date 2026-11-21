using System.Text;

namespace Context.Tests;

// Job 361: todos done more than 30 days ago move from todos.json to todos-archive.json on every write.
public sealed class TodoArchiveTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly Guid OldId = new("00000000-0000-0000-0000-000000000031");
    private static readonly Guid EdgeId = new("00000000-0000-0000-0000-000000000030");
    private static readonly Guid RecentId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OpenId = new("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid NewId = new("00000000-0000-0000-0000-0000000000bb");

    // The old done todo carries every optional field, so the archive must keep them all.
    private const string OldExtra =
        "\"repeat\":\"monday\",\"time\":\"09:30\",\"notified\":\"2026-10-01T09:30:00Z\","
        + "\"meeting\":{\"uid\":\"uid-1\",\"start\":\"2026-10-05T10:00\",\"title\":\"Sync\"}";

    private readonly string parent;
    private readonly string root;

    public TodoArchiveTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-todoarchive-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string FilePath => Path.Combine(root, TodoStore.FileName);

    private string ArchivePath => Path.Combine(root, TodoStore.ArchiveFileName);

    private TodoStore Store(Action<string>? beforeWrite = null) =>
        new(root, () => Now, () => NewId) { BeforeWrite = beforeWrite };

    private static string Old() =>
        TodoArchiveFile.Entry(OldId, "old", new DateOnly(2026, 10, 1), Created, Now.AddDays(-31), OldExtra);

    private static string Edge() =>
        TodoArchiveFile.Entry(EdgeId, "edge", new DateOnly(2026, 10, 2), Created, Now.AddDays(-30));

    private static string Recent() =>
        TodoArchiveFile.Entry(RecentId, "recent", new DateOnly(2026, 11, 19), Created, Now.AddDays(-1));

    // Open, timed and due in the past, so MarkNotified and MoveDue both act on it.
    private static string Open(bool withId = true)
    {
        string json = TodoArchiveFile.Entry(OpenId, "open", new DateOnly(2026, 11, 20), Created, null, "\"time\":\"08:00\"");
        return withId ? json : json.Replace($"\"id\":\"{OpenId}\",", string.Empty, StringComparison.Ordinal);
    }

    private void Seed(bool openWithId = true) =>
        TodoArchiveFile.Write(FilePath, Old(), Edge(), Recent(), Open(openWithId));

    private static IReadOnlyList<TodoItem> ReadFile(string folder, string path)
    {
        // Read a file through a store whose todos.json is that file: copy it into a fresh folder.
        string copy = Path.Combine(folder, "read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        File.Copy(path, Path.Combine(copy, TodoStore.FileName));
        TodoStore store = new TodoStore(copy, () => Now);
        return store.List().Concat(store.ListDone()).ToList();
    }

    private static IReadOnlyList<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();

    private IReadOnlyList<Guid> Ids(string path) => ReadFile(parent, path).Select(t => t.Id).Order().ToList();

    private string[] TempFiles() =>
        Directory.GetFiles(root).Select(Path.GetFileName).Where(n => n!.EndsWith(".tmp", StringComparison.Ordinal)).ToArray()!;

    private static void Act(TodoStore store, string method)
    {
        switch (method)
        {
            case "Add":
                store.Add("new", new DateOnly(2026, 11, 25));
                break;
            case "Complete":
                Assert.NotNull(store.Complete(OpenId));
                break;
            case "MarkNotified":
                Assert.Single(store.MarkNotified([OpenId]));
                break;
            case "MoveDue":
                Assert.Single(store.MoveDue(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22)));
                break;
            case "Tie":
                Assert.NotNull(store.Tie(OpenId, new MeetingLink("uid-2", new DateTime(2026, 11, 23, 10, 0, 0), "Plan")));
                break;
            case "Migrate":
                Assert.True(store.Migrate());
                break;
            default:
                throw new ArgumentException(method);
        }
    }

    // AC-1, AC-2: on every write the todo done 31 days ago moves; the ones done 30 days and 1 day ago and the open one
    // stay. A missing archive is created.
    [Theory]
    [InlineData("Add")]
    [InlineData("Complete")]
    [InlineData("MarkNotified")]
    [InlineData("MoveDue")]
    [InlineData("Tie")]
    [InlineData("Migrate")]
    public void Write_MovesOnlyTodosDoneOver30DaysAgo(string method)
    {
        Seed(openWithId: method != "Migrate");
        TodoStore store = Store();
        Guid openId = method == "Migrate" ? store.List().Single().Id : OpenId;

        Act(store, method);

        List<Guid> expected = [EdgeId, RecentId, openId];
        if (method == "Add")
        {
            expected.Add(NewId);
        }

        Assert.Equal(Sorted([.. expected]), Ids(FilePath));
        Assert.Equal([OldId], Ids(ArchivePath));
        Assert.Empty(TempFiles());
    }

    // AC-1: the archive is UTF-8 without BOM, camelCase, and keeps id, text, due, created, done, repeat, time,
    // notified and meeting.
    [Fact]
    public void Archive_SameFormat_KeepsEveryField()
    {
        Seed();

        Store().Complete(OpenId);

        byte[] bytes = File.ReadAllBytes(ArchivePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        string json = Encoding.UTF8.GetString(bytes);
        foreach (string name in new[] { "id", "text", "due", "created", "done", "repeat", "time", "notified", "meeting", "uid", "start", "title" })
        {
            Assert.Contains($"\"{name}\":", json, StringComparison.Ordinal);
        }

        TodoItem archived = Assert.Single(ReadFile(parent, ArchivePath));
        TodoItem expected = new TodoItem(
            OldId,
            "old",
            new DateOnly(2026, 10, 1),
            Created,
            Now.AddDays(-31),
            TodoRepeat.Weekly(DayOfWeek.Monday),
            new TimeOnly(9, 30),
            new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
            new MeetingLink("uid-1", new DateTime(2026, 10, 5, 10, 0, 0), "Sync"));
        Assert.Equal(expected, archived);
    }

    // AC-2: a write with nothing to move creates no archive, and leaves an existing one byte for byte (and mtime).
    [Fact]
    public void Write_NothingToMove_LeavesArchiveUntouched()
    {
        TodoArchiveFile.Write(FilePath, Edge(), Recent(), Open());

        Store().MarkNotified([OpenId]);
        Assert.False(File.Exists(ArchivePath));

        TodoArchiveFile.Archive(root, Guid.NewGuid(), "archived", Now.AddDays(-40));
        DateTime stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ArchivePath, stamp);
        byte[] before = File.ReadAllBytes(ArchivePath);

        Store().Complete(OpenId);

        Assert.Equal(before, File.ReadAllBytes(ArchivePath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(ArchivePath));
        Assert.Equal(Sorted(EdgeId, RecentId, OpenId), Ids(FilePath));
    }

    // AC-3: a failed archive write keeps the old todos in todos.json and the action succeeds; nothing is left behind.
    [Fact]
    public void ArchiveWriteFails_TodosJsonKeepsOldTodos_ActionSucceeds()
    {
        Seed();
        TodoArchiveFile.Archive(root, Guid.NewGuid(), "archived", Now.AddDays(-40));
        byte[] archive = File.ReadAllBytes(ArchivePath);
        TodoStore store = Store(path =>
        {
            if (path == ArchivePath)
            {
                throw new IOException("disk full");
            }
        });

        TodoItem? done = store.Complete(OpenId);

        Assert.NotNull(done);
        Assert.Equal(archive, File.ReadAllBytes(ArchivePath));
        Assert.Equal(Sorted(OldId, EdgeId, RecentId, OpenId), Ids(FilePath));
        Assert.NotNull(store.ListDone().Single(t => t.Id == OpenId).Done);
        Assert.Empty(TempFiles());
    }

    // AC-3: a failed todos.json write after the archive write fails the action; the todo is in both files.
    [Fact]
    public void TodosWriteFails_ActionThrows_TodoInBothFiles()
    {
        Seed();
        byte[] todos = File.ReadAllBytes(FilePath);
        TodoStore store = Store(path =>
        {
            if (path == FilePath)
            {
                throw new IOException("disk full");
            }
        });

        Assert.Throws<IOException>(() => store.Complete(OpenId));

        Assert.Equal(todos, File.ReadAllBytes(FilePath));
        Assert.Equal([OldId], Ids(ArchivePath));
        Assert.Empty(TempFiles());
        Assert.Single(Store().ListAllDone(out bool unreadable), t => t.Id == OldId);
        Assert.False(unreadable);
    }

    // AC-4: an id already in the archive is replaced there, not added twice; ListAllDone lists a todo in both files
    // once, as its todos.json copy.
    [Fact]
    public void IdInBothFiles_ReplacedInArchive_ListedOnce()
    {
        Seed();
        Guid other = new Guid("00000000-0000-0000-0000-0000000000cc");
        TodoArchiveFile.Write(
            ArchivePath,
            TodoArchiveFile.Entry(other, "other", new DateOnly(2026, 9, 1), Created, Now.AddDays(-60)),
            TodoArchiveFile.Entry(OldId, "stale copy", new DateOnly(2026, 10, 1), Created, Now.AddDays(-31)));

        IReadOnlyList<TodoItem> both = Store().ListAllDone(out bool unreadable);
        Assert.False(unreadable);
        Assert.Equal("old", Assert.Single(both, t => t.Id == OldId).Text);
        Assert.Equal(4, both.Count);

        Store().Complete(OpenId);

        IReadOnlyList<TodoItem> archived = ReadFile(parent, ArchivePath);
        Assert.Equal(Sorted(other, OldId), Ids(ArchivePath));
        Assert.Equal("old", archived.Single(t => t.Id == OldId).Text);

        // Replaced in place: the other todo stays first in file order.
        string json = File.ReadAllText(ArchivePath);
        Assert.True(json.IndexOf(other.ToString(), StringComparison.Ordinal) < json.IndexOf(OldId.ToString(), StringComparison.Ordinal));
    }

    // AC-5: both files in ListDone order: done descending, then created, then todos.json before the archive, then file
    // order. A missing archive is not unreadable. A bad todos.json throws.
    [Fact]
    public void ListAllDone_Order_MissingArchive_BadTodosJson()
    {
        DateTime done = Now.AddDays(-2);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), e = Guid.NewGuid(), f = Guid.NewGuid();
        DateOnly due = new DateOnly(2026, 11, 1);
        TodoArchiveFile.Write(
            FilePath,
            TodoArchiveFile.Entry(a, "json tie 1", due, Created, done),
            TodoArchiveFile.Entry(b, "json later created", due, Created.AddHours(1), done),
            TodoArchiveFile.Entry(c, "json tie 2", due, Created, done),
            Open());

        IReadOnlyList<TodoItem> only = Store().ListAllDone(out bool missing);
        Assert.False(missing);
        Assert.Equal([a, c, b], only.Select(t => t.Id));

        TodoArchiveFile.Write(
            ArchivePath,
            TodoArchiveFile.Entry(d, "archive tie", due, Created, done),
            TodoArchiveFile.Entry(e, "archive newest", due, Created, Now.AddDays(-1)),
            TodoArchiveFile.Entry(f, "archive oldest", due, Created, Now.AddDays(-50)));

        IReadOnlyList<TodoItem> all = Store().ListAllDone(out bool unreadable);
        Assert.False(unreadable);
        Assert.Equal([e, a, c, d, b, f], all.Select(t => t.Id));

        File.WriteAllText(FilePath, "not json");
        Assert.Throws<TodoStoreException>(() => Store().ListAllDone(out _));
    }

    // AC-6: an archive that can't be read, parsed, is too large or has an entry without a valid done is unreadable:
    // only the todos.json done todos are listed, and a write neither touches it nor moves the old todos out.
    [Theory]
    [InlineData("json")]
    [InlineData("large")]
    [InlineData("directory")]
    [InlineData("nodone")]
    [InlineData("baddone")]
    public void UnreadableArchive_ListsTodosJsonOnly_NeverWritten(string kind)
    {
        Seed();
        switch (kind)
        {
            case "json":
                File.WriteAllText(ArchivePath, "[{\"id\":");
                break;
            case "large":
                using (FileStream stream = File.Create(ArchivePath))
                {
                    stream.SetLength(TodoStore.MaxArchiveBytes + 1);
                }

                break;
            case "directory":
                Directory.CreateDirectory(ArchivePath);
                break;
            case "nodone":
                TodoArchiveFile.Write(ArchivePath, TodoArchiveFile.Entry(Guid.NewGuid(), "no done", new DateOnly(2026, 9, 1), Created, null));
                break;
            case "baddone":
                TodoArchiveFile.Write(
                    ArchivePath,
                    TodoArchiveFile.Entry(Guid.NewGuid(), "bad", new DateOnly(2026, 9, 1), Created, null, "\"done\":\"soon\""));
                break;
        }

        byte[]? before = kind == "directory" ? null : File.ReadAllBytes(ArchivePath);

        IReadOnlyList<TodoItem> done = Store().ListAllDone(out bool unreadable);
        Assert.True(unreadable);
        Assert.Equal([RecentId, EdgeId, OldId], done.Select(t => t.Id));

        Assert.NotNull(Store().Complete(OpenId));

        if (before is null)
        {
            Assert.True(Directory.Exists(ArchivePath));
            Assert.Empty(Directory.GetFileSystemEntries(ArchivePath));
        }
        else
        {
            Assert.Equal(before, File.ReadAllBytes(ArchivePath));
        }

        Assert.Equal(Sorted(OldId, EdgeId, RecentId, OpenId), Ids(FilePath));
        Assert.Empty(TempFiles());
    }

    // AC-10: ListDone (the standup's source) reads todos.json only.
    [Fact]
    public void ListDone_IgnoresArchive()
    {
        TodoArchiveFile.Write(FilePath, Recent(), Open());
        TodoArchiveFile.Archive(root, OldId, "archived", Now.AddDays(-40));

        Assert.Equal([RecentId], Store().ListDone().Select(t => t.Id));
    }
}
