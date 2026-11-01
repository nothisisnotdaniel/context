using Bunit;

namespace Context.Tests;

public sealed class DataWatcherTests : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "watch-root", "Context");

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-9: the files the window shows.
    [Theory]
    [InlineData("Note.md")]
    [InlineData("NOTE.MD")]
    [InlineData("with space.Md")]
    [InlineData("todos.json")]
    [InlineData("calendar-link.ics")]
    [InlineData("encryption.json")]
    [InlineData("calendar/work.ics")]
    [InlineData("calendar/WORK.ICS")]
    public void IsRelevant_DataFiles_True(string relative)
    {
        Assert.True(DataWatcher.IsRelevant(Root, Path.Combine(Root, relative)));
    }

    // AC-9: the index, temp files, the link itself, subfolder notes and anything outside the root.
    [Theory]
    [InlineData("context.db")]
    [InlineData("context.db-journal")]
    [InlineData("context.db-wal")]
    [InlineData(".todos.json.0123456789abcdef.tmp")]
    [InlineData(".Note.md.0123456789abcdef.tmp")]
    [InlineData(".calendar-link.ics.0123456789abcdef.tmp")]
    [InlineData("calendar-link.txt")]
    [InlineData("sub/Note.md")]
    [InlineData("calendar/sub/work.ics")]
    [InlineData("calendar/notes.md")]
    [InlineData("calendar")]
    [InlineData("other.json")]
    [InlineData(".md")]
    [InlineData("")]
    [InlineData("../Note.md")]
    [InlineData("../todos.json")]
    [InlineData("../Context2/todos.json")]
    public void IsRelevant_Others_False(string relative)
    {
        Assert.False(DataWatcher.IsRelevant(Root, Path.Combine(Root, relative)));
    }

    // AC-9: absolute paths elsewhere are never relevant.
    [Fact]
    public void IsRelevant_OutsideRoot_False()
    {
        Assert.False(DataWatcher.IsRelevant(Root, Path.Combine(Path.GetTempPath(), "todos.json")));
        Assert.False(DataWatcher.IsRelevant(Root, Path.Combine(Path.GetTempPath(), "x", "Context", "todos.json")));
    }

    // AC-9: a burst of writes to todos.json triggers exactly one refresh, and the panel then shows the new todo.
    [Fact]
    public async Task Watcher_BurstOfWrites_OneRefresh()
    {
        data.AddTodo("First", DesktopFixture.Today);
        IRenderedComponent<TodoPanel> cut = await data.RenderAsync<TodoPanel>();
        DesktopState state = data.State!;
        Assert.Equal(1, state.Builds);
        using DataWatcher watcher = new DataWatcher(data.Root, state);
        watcher.Start();
        await Task.Delay(500);

        for (int i = 0; i < 5; i++)
        {
            data.AddTodo($"New {i}", DesktopFixture.Today);
        }

        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (state.Builds < 2 && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        await Task.Delay(1000);
        Assert.Equal(2, state.Builds);
        cut.WaitForAssertion(() => Assert.Contains("New 4", cut.Markup), TimeSpan.FromSeconds(5));
        Assert.Equal(6, cut.FindAll("li.todo").Count);
    }

    // AC-9: a write to a file the window doesn't show triggers no refresh.
    [Fact]
    public async Task Watcher_IrrelevantWrite_NoRefresh()
    {
        DesktopState state = data.CreateState();
        Directory.CreateDirectory(data.Root);
        await state.Refresh();
        using DataWatcher watcher = new DataWatcher(data.Root, state);
        watcher.Start();
        await Task.Delay(500);

        data.WriteFile("calendar-link.txt", "x");
        data.WriteFile("context.db-journal", "x");
        await Task.Delay(1500);

        Assert.Equal(1, state.Builds);
    }

    // A missing folder is not created by the watcher.
    [Fact]
    public void Start_MissingFolder_NotCreated()
    {
        DesktopState state = data.CreateState();
        using DataWatcher watcher = new DataWatcher(data.Root, state);

        watcher.Start();

        Assert.False(Directory.Exists(data.Root));
    }
}
