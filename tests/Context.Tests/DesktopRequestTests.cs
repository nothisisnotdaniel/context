using System.Text;

namespace Context.Tests;

// 280: the open request between `context item <n> --open` and the desktop app.
public sealed class DesktopRequestTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public DesktopRequestTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-request-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("item 1234\n", 1234)]
    [InlineData("item 1234", 1234)]
    [InlineData("item 1234\r\n", 1234)]
    [InlineData("item 7\n", 7)]
    [InlineData("item #1234\n", null)]
    [InlineData("item 0\n", null)]
    [InlineData("item 0123\n", null)]
    [InlineData("item 1234567890\n", null)]
    [InlineData("item  1234\n", null)]
    [InlineData("Item 1234\n", null)]
    [InlineData("item 1234\n\n", null)]
    [InlineData("item 12 34\n", null)]
    [InlineData("item ../../etc/passwd\n", null)]
    [InlineData("/etc/passwd\n", null)]
    [InlineData("item \n", null)]
    [InlineData("", null)]
    public void ParseItem_OnlyExactlyItemN(string text, int? expected)
    {
        Assert.Equal(expected, DesktopRequest.ParseItem(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void ParseItem_NonAsciiOrTooLong_IsNull()
    {
        Assert.Null(DesktopRequest.ParseItem(Encoding.UTF8.GetBytes("item ١٢\n")));
        Assert.Null(DesktopRequest.ParseItem(Encoding.ASCII.GetBytes("item 1" + new string(' ', 70))));
    }

    [Fact]
    public void WriteItem_ThenTryTake_DeletesAndParses()
    {
        DesktopRequest.WriteItem(root, 1234);
        Assert.Equal(DesktopRequest.ItemRequest(1234), File.ReadAllBytes(DesktopRequest.RequestPath(root)));
        Assert.Equal([DesktopRequest.RequestFileName], Directory.GetFiles(DesktopRequest.Folder(root)).Select(Path.GetFileName));

        Assert.True(DesktopRequest.TryTake(root, out int? item));

        Assert.Equal(1234, item);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
        Assert.False(DesktopRequest.TryTake(root, out int? none));
        Assert.Null(none);
    }

    // An oversize request is deleted and gives no item.
    [Fact]
    public void TryTake_Oversize_DeletedNoItem()
    {
        Directory.CreateDirectory(DesktopRequest.Folder(root));
        File.WriteAllText(DesktopRequest.RequestPath(root), "item 1234\n" + new string('x', 200));

        Assert.True(DesktopRequest.TryTake(root, out int? item));

        Assert.Null(item);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // The lock: free until someone holds it.
    [Fact]
    public void IsLockFree_FalseWhileHeld()
    {
        Assert.True(DesktopRequest.IsLockFree(root));
        using (FileStream held = PrivateFile.OpenLock(DesktopRequest.LockPath(root)))
        {
            Assert.False(DesktopRequest.IsLockFree(root));
        }

        Assert.True(DesktopRequest.IsLockFree(root));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(DesktopRequest.LockPath(root)));
        }
    }

    // 315 AC-8: show is no item, and only exactly show (optional final \n or \r\n) is a show request.
    [Theory]
    [InlineData("show", true)]
    [InlineData("show\n", true)]
    [InlineData("show\r\n", true)]
    [InlineData("show\n\n", false)]
    [InlineData("SHOW\n", false)]
    [InlineData("show me\n", false)]
    [InlineData(" show\n", false)]
    [InlineData("item 3\n", false)]
    [InlineData("item 3", false)]
    [InlineData("", false)]
    public void IsShow_OnlyExactlyShow(string text, bool expected)
    {
        Assert.Equal(expected, DesktopRequest.IsShow(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void ShowRequest_IsNoItem_AndItemIsNoShow()
    {
        Assert.Equal("show\n"u8.ToArray(), DesktopRequest.ShowRequest());
        Assert.Null(DesktopRequest.ParseItem(DesktopRequest.ShowRequest()));
        Assert.False(DesktopRequest.IsShow(DesktopRequest.ItemRequest(3)));
        Assert.False(DesktopRequest.IsShow(Encoding.ASCII.GetBytes("show" + new string(' ', DesktopRequest.MaxBytes))));
    }

    // 315: WriteShow uses the same temp + move as WriteItem, and TryTake tells it apart.
    [Fact]
    public void WriteShow_ThenTryTake_IsShow()
    {
        DesktopRequest.WriteShow(root);
        Assert.Equal(DesktopRequest.ShowRequest(), File.ReadAllBytes(DesktopRequest.RequestPath(root)));
        Assert.Equal([DesktopRequest.RequestFileName], Directory.GetFiles(DesktopRequest.Folder(root)).Select(Path.GetFileName));

        Assert.True(DesktopRequest.TryTake(root, out int? item, out bool show));

        Assert.Null(item);
        Assert.True(show);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(DesktopRequest.Folder(root)));
        }
    }

    [Fact]
    public void TryTake_Item_IsNoShow()
    {
        DesktopRequest.WriteItem(root, 3);

        Assert.True(DesktopRequest.TryTake(root, out int? item, out bool show));

        Assert.Equal(3, item);
        Assert.False(show);
    }
}
