namespace Context.Tests;

// 267 AC-1 to AC-5: FileSnapshot.Take leaves out the temp files of an ignored file, and only those.
public sealed class FileSnapshotTests : IDisposable
{
    private const string Hex = "0123456789abcdef0123456789abcdef";
    private const string RecentTemp = ".recent.json." + Hex + ".tmp";

    private readonly string root;

    public FileSnapshotTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string Write(string relative, string text = "x")
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    // AC-1
    [Fact]
    public void Take_IgnoredFile_LeavesOutItsTempFile_KeepsOthers()
    {
        Write("recent.json");
        Write(RecentTemp);
        Write("Plan.md");

        SortedDictionary<string, string> files = FileSnapshot.Take(root, "recent.json");

        Assert.Equal(["Plan.md"], files.Keys);
    }

    // AC-2
    [Fact]
    public void Take_NestedPrefix_LeavesOutTempFileInThatFolder()
    {
        Write("Context/recent.json");
        Write("Context/" + RecentTemp);
        Write("Context/Plan.md");

        SortedDictionary<string, string> files = FileSnapshot.Take(root, "Context/recent.json");

        Assert.Equal(["Context/Plan.md"], files.Keys);
    }

    // AC-3
    [Fact]
    public void Take_WithoutRecentPrefix_ListsTempFile()
    {
        Write(RecentTemp);
        Write("context.db");

        Assert.Contains(RecentTemp, FileSnapshot.Take(root).Keys);
        Assert.Equal([RecentTemp], FileSnapshot.Take(root, "context.db").Keys);
    }

    // AC-4
    [Fact]
    public void Take_RecentPrefix_StillListsLookalikes()
    {
        string[] kept = [".Plan.md." + Hex + ".tmp", ".recent.json.bak", "recent.tmp", "sub/" + RecentTemp];
        foreach (string file in kept)
        {
            Write(file);
        }

        SortedDictionary<string, string> files = FileSnapshot.Take(root, "recent.json");

        Assert.Equal(kept.Order(StringComparer.Ordinal), files.Keys);
    }

    // AC-5: the held temp file can't be read, yet Take returns without reading it.
    [Fact]
    public void Take_TempFileHeldOpen_IsNotRead()
    {
        string temp = Write(RecentTemp);
        Write("Plan.md");

        using (new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(temp));

            SortedDictionary<string, string> files = FileSnapshot.Take(root, "recent.json");

            Assert.Equal(["Plan.md"], files.Keys);
        }
    }

    // AC-5: the rule looks only at the path, so a temp file that is gone before the read never reaches File.ReadAllBytes.
    [Fact]
    public void IsTempFileOf_UsesOnlyThePath()
    {
        Assert.False(File.Exists(Path.Combine(root, RecentTemp)));
        Assert.True(FileSnapshot.IsTempFileOf(RecentTemp, "recent.json"));
        Assert.True(FileSnapshot.IsTempFileOf("Context/" + RecentTemp, "Context/recent.json"));
        Assert.False(FileSnapshot.IsTempFileOf("sub/" + RecentTemp, "recent.json"));
        Assert.False(FileSnapshot.IsTempFileOf(RecentTemp, "Context/recent.json"));
        Assert.False(FileSnapshot.IsTempFileOf(".recent.json..tmp", "recent.json"));
        Assert.False(FileSnapshot.IsTempFileOf(".x..tmp", "attachments/"));
    }
}
