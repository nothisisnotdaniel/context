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
        store.Complete(1);

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

    // AC-6
    [Fact]
    public void Complete_RemovesThatPosition_ReturnsIt()
    {
        TodoStore store = Store();
        store.Add("c", new DateOnly(2026, 11, 5));
        store.Add("a", new DateOnly(2026, 11, 3));
        store.Add("b", new DateOnly(2026, 11, 4));

        TodoItem done = store.Complete(2);

        Assert.Equal("b", done.Text);
        Assert.Equal(["a", "c"], new TodoStore(root).List().Select(t => t.Text));
    }

    // AC-6
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Complete_OutOfRange_Throws_ChangesNothing(int number)
    {
        TodoStore store = Store();
        store.Add("a", new DateOnly(2026, 11, 3));
        store.Add("b", new DateOnly(2026, 11, 4));
        byte[] before = File.ReadAllBytes(FilePath);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Complete(number));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    // AC-6
    [Fact]
    public void Complete_NoFile_Throws_CreatesNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Store().Complete(1));
        Assert.False(Directory.Exists(root));
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
        Assert.Throws<TodoStoreException>(() => store.Complete(1));
        Assert.Equal(original, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(root));
    }
}
