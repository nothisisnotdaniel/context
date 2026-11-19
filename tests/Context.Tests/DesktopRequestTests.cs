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
}
