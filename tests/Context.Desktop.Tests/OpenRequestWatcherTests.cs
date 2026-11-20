using System.Collections.Concurrent;
using System.Text;

namespace Context.Tests;

// 280 AC-10: the desktop's watcher of <root>/.desktop/open-request.
public sealed class OpenRequestWatcherTests : IDisposable
{
    // The watcher handles requests on pool threads; every wait is bounded by this timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-watch-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<int> requested = new ConcurrentQueue<int>();
    private int shown;

    public OpenRequestWatcherTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private OpenRequestWatcher Start()
    {
        OpenRequestWatcher watcher = new OpenRequestWatcher(root);
        watcher.ItemRequested += requested.Enqueue;
        watcher.ShowRequested += () => Interlocked.Increment(ref shown);
        watcher.Start();
        return watcher;
    }

    private static void WaitProcessed(OpenRequestWatcher watcher, int count) =>
        Assert.True(SpinWait.SpinUntil(() => watcher.Processed >= count, Wait), $"Processed stayed at {watcher.Processed}.");

    private void WriteRaw(byte[] bytes)
    {
        Directory.CreateDirectory(DesktopRequest.Folder(root));
        string temp = Path.Combine(DesktopRequest.Folder(root), "raw." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, DesktopRequest.RequestPath(root), overwrite: true);
    }

    // AC-10: a request that is there at start is handled and deleted.
    [Fact]
    public void Start_ExistingRequest_Handled()
    {
        DesktopRequest.WriteItem(root, 1234);

        using OpenRequestWatcher watcher = Start();

        WaitProcessed(watcher, 1);
        Assert.Equal([1234], requested.ToArray());
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // AC-10: a request written after start is handled; then a second one.
    [Fact]
    public void LaterRequests_Handled()
    {
        using OpenRequestWatcher watcher = Start();
        Assert.True(Directory.Exists(DesktopRequest.Folder(root)));
        Assert.Equal(0, watcher.Processed);

        DesktopRequest.WriteItem(root, 42);
        WaitProcessed(watcher, 1);
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(DesktopRequest.RequestPath(root)), Wait));
        DesktopRequest.WriteItem(root, 7);
        WaitProcessed(watcher, 2);

        Assert.Equal([42, 7], requested.ToArray());
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // AC-10: any other content is deleted and ignored; the next valid one still works.
    [Theory]
    [InlineData("item #1234\n")]
    [InlineData("item 0123\n")]
    [InlineData("item 1234 5\n")]
    [InlineData("open ../../etc/passwd\n")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void InvalidContent_DeletedAndIgnored(string content)
    {
        using OpenRequestWatcher watcher = Start();

        WriteRaw(Encoding.UTF8.GetBytes(content));
        WaitProcessed(watcher, 1);
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(DesktopRequest.RequestPath(root)), Wait));
        DesktopRequest.WriteItem(root, 5);
        WaitProcessed(watcher, 2);

        Assert.Equal([5], requested.ToArray());
    }

    // AC-10: a request over 64 bytes is deleted and ignored, even when it starts like a valid one.
    [Fact]
    public void Oversize_DeletedAndIgnored()
    {
        WriteRaw(Encoding.ASCII.GetBytes("item 1234\n" + new string(' ', DesktopRequest.MaxBytes)));

        using OpenRequestWatcher watcher = Start();

        WaitProcessed(watcher, 1);
        Assert.Empty(requested);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // AC-10: after Dispose nothing is handled.
    [Fact]
    public void Disposed_HandlesNothing()
    {
        OpenRequestWatcher watcher = Start();
        watcher.Dispose();

        DesktopRequest.WriteItem(root, 9);
        watcher.Start();

        Assert.Equal(0, watcher.Processed);
        Assert.True(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // 315 AC-7: a show request (with or without \r\n) raises ShowRequested once, no item, and is deleted and counted.
    [Theory]
    [InlineData("show")]
    [InlineData("show\n")]
    [InlineData("show\r\n")]
    public void ShowRequest_RaisesShowOnce(string content)
    {
        using OpenRequestWatcher watcher = Start();

        WriteRaw(Encoding.ASCII.GetBytes(content));
        WaitProcessed(watcher, 1);

        Assert.Equal(1, Volatile.Read(ref shown));
        Assert.Empty(requested);
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(DesktopRequest.RequestPath(root)), Wait));
    }

    // 315 AC-7: the request DesktopRequest.WriteShow leaves at start is handled like any other.
    [Fact]
    public void Start_ExistingShowRequest_Handled()
    {
        DesktopRequest.WriteShow(root);

        using OpenRequestWatcher watcher = Start();

        WaitProcessed(watcher, 1);
        Assert.Equal(1, Volatile.Read(ref shown));
        Assert.Empty(requested);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // 315 AC-7: anything close to show raises nothing and is deleted; an item request still works after it.
    [Theory]
    [InlineData("show me")]
    [InlineData("SHOW")]
    [InlineData("show\n\n")]
    [InlineData("show                                                            \n")]
    public void NearShow_DeletedAndIgnored(string content)
    {
        using OpenRequestWatcher watcher = Start();

        WriteRaw(Encoding.ASCII.GetBytes(content));
        WaitProcessed(watcher, 1);
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(DesktopRequest.RequestPath(root)), Wait));
        DesktopRequest.WriteItem(root, 5);
        WaitProcessed(watcher, 2);

        Assert.Equal(0, Volatile.Read(ref shown));
        Assert.Equal([5], requested.ToArray());
    }

    // 315 AC-7: a 65-byte request that starts with show is deleted and raises nothing.
    [Fact]
    public void OversizeShow_DeletedAndIgnored()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("show\n" + new string(' ', DesktopRequest.MaxBytes - 4));
        Assert.Equal(DesktopRequest.MaxBytes + 1, bytes.Length);
        WriteRaw(bytes);

        using OpenRequestWatcher watcher = Start();

        WaitProcessed(watcher, 1);
        Assert.Equal(0, Volatile.Read(ref shown));
        Assert.Empty(requested);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // 315 AC-7: an item request raises no show.
    [Fact]
    public void ItemRequest_RaisesNoShow()
    {
        using OpenRequestWatcher watcher = Start();

        DesktopRequest.WriteItem(root, 3);
        WaitProcessed(watcher, 1);

        Assert.Equal([3], requested.ToArray());
        Assert.Equal(0, Volatile.Read(ref shown));
    }
}
