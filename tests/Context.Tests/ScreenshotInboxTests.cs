using System.Text;

namespace Context.Tests;

// Job 260: screenshots into the inbox, the Core step (ScreenshotInbox.Add).
public sealed class ScreenshotInboxTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 19, 10, 15, 0, DateTimeKind.Local);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;
    private readonly NoteAttachments attachments;

    public ScreenshotInboxTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-shot-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
        attachments = new NoteAttachments(store);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string InboxPath => Path.Combine(root, "inbox.md");

    private string Stored(string name) => Path.Combine(root, ".attachments", name);

    private string[] StoredNames() =>
        Directory.Exists(Path.Combine(root, ".attachments"))
            ? Directory.GetFiles(Path.Combine(root, ".attachments")).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal).ToArray()
            : [];

    // AC-1
    [Fact]
    public void HotkeyName_IsTheLocalTimeStamp()
    {
        Assert.Equal("screenshot-20261119-101500.png", ScreenshotInbox.HotkeyName(Now));
        Assert.Equal(
            "screenshot-20260102-030405.png",
            ScreenshotInbox.HotkeyName(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local)));
    }

    // AC-1
    [Fact]
    public void Add_StoresTheImageAndAppendsTheLine()
    {
        File.WriteAllText(InboxPath, "# inbox\n- old\n");

        ScreenshotAdd added = ScreenshotInbox.Add(store, attachments, ScreenshotInbox.HotkeyName(Now), Png, Now);

        Assert.Equal(ScreenshotAddKind.Added, added.Kind);
        Assert.Equal("screenshot-20261119-101500.png", added.Name);
        Assert.Equal(
            "# inbox\n- old\n- 2026-11-19 10:15 Screenshot ![screenshot-20261119-101500.png](.attachments/screenshot-20261119-101500.png)\n",
            File.ReadAllText(InboxPath));
        Assert.Equal(Png, File.ReadAllBytes(Stored("screenshot-20261119-101500.png")));
    }

    // AC-1
    [Fact]
    public void Add_SameNameTwice_GetsDashTwo()
    {
        ScreenshotInbox.Add(store, attachments, "screenshot-20261119-101500.png", Png, Now);
        ScreenshotAdd second = ScreenshotInbox.Add(store, attachments, "screenshot-20261119-101500.png", Png, Now);

        Assert.Equal("screenshot-20261119-101500-2.png", second.Name);
        Assert.EndsWith(
            "- 2026-11-19 10:15 Screenshot ![screenshot-20261119-101500-2.png](.attachments/screenshot-20261119-101500-2.png)\n",
            File.ReadAllText(InboxPath),
            StringComparison.Ordinal);
        Assert.Equal(["screenshot-20261119-101500-2.png", "screenshot-20261119-101500.png"], StoredNames());
    }

    // AC-2
    [Fact]
    public void Add_MissingInbox_CreatesItWithTheLine()
    {
        ScreenshotAdd added = ScreenshotInbox.Add(store, attachments, "shot.png", Png, Now);

        Assert.Equal(ScreenshotAddKind.Added, added.Kind);
        Assert.Equal("# inbox\n- 2026-11-19 10:15 Screenshot ![shot.png](.attachments/shot.png)\n", File.ReadAllText(InboxPath));
        Assert.True(File.Exists(Stored("shot.png")));
    }

    // AC-6: the stored name is the file's CleanName.
    [Fact]
    public void Add_CleansTheFileName()
    {
        ScreenshotAdd added = ScreenshotInbox.Add(store, attachments, "Screen Shot (1).PNG", Png, Now);

        Assert.Equal("Screen-Shot-1.png", added.Name);
        Assert.Contains("Screenshot ![Screen-Shot-1.png](.attachments/Screen-Shot-1.png)", File.ReadAllText(InboxPath), StringComparison.Ordinal);
    }

    // AC-3
    [Theory]
    [InlineData("shot.png")]
    [InlineData("shot.svg")]
    [InlineData("shot.html")]
    public void Add_BytesThatAreNoImage_WriteNothing(string name)
    {
        ScreenshotAdd added = ScreenshotInbox.Add(store, attachments, name, Html, Now);

        Assert.Equal(ScreenshotAddKind.Unsupported, added.Kind);
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(StoredNames());
    }

    // AC-3
    [Fact]
    public void Add_PngNamedAsJpeg_WritesNothing()
    {
        Assert.Equal(ScreenshotAddKind.Unsupported, ScreenshotInbox.Add(store, attachments, "shot.jpg", Png, Now).Kind);
        Assert.False(File.Exists(InboxPath));
    }

    // AC-3
    [Fact]
    public void Add_OverTenMegabytes_WritesNothing()
    {
        byte[] big = new byte[NoteAttachments.MaxBytes + 1];
        Png.CopyTo(big, 0);

        Assert.Equal(ScreenshotAddKind.TooLarge, ScreenshotInbox.Add(store, attachments, "big.png", big, Now).Kind);
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(StoredNames());
    }

    // AC-4
    [Fact]
    public void Add_SensitiveInbox_WritesNothing()
    {
        string sealedInbox = Path.Combine(root, "inbox" + NoteStore.SensitiveExtension);
        File.WriteAllText(sealedInbox, "{}");

        Assert.Equal(ScreenshotAddKind.Sensitive, ScreenshotInbox.Add(store, attachments, "shot.png", Png, Now).Kind);
        Assert.Equal("{}", File.ReadAllText(sealedInbox));
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(StoredNames());
    }

    // AC-5
    [Fact]
    public void Add_InboxKeepsChanging_DeletesTheAttachmentAndKeepsTheLastWrite()
    {
        File.WriteAllText(InboxPath, "# inbox\n");
        int saves = 0;

        ScreenshotAdd added = ScreenshotInbox.Add(
            store, attachments, "shot.png", Png, Now, () => File.WriteAllText(InboxPath, $"# inbox\n- other {++saves}\n"));

        Assert.Equal(ScreenshotAddKind.ChangedOnDisk, added.Kind);
        Assert.Equal($"# inbox\n- other {saves}\n", File.ReadAllText(InboxPath));
        Assert.Empty(StoredNames());
    }

    // AC-5
    [Fact]
    public void Add_InboxTooLarge_DeletesTheAttachment()
    {
        byte[] full = new byte[NoteStore.MaxEditBytes - 10];
        Array.Fill(full, (byte)'a');
        File.WriteAllBytes(InboxPath, full);

        ScreenshotAdd added = ScreenshotInbox.Add(store, attachments, "shot.png", Png, Now);

        Assert.Equal(ScreenshotAddKind.InboxTooLarge, added.Kind);
        Assert.Equal(full.Length, new FileInfo(InboxPath).Length);
        Assert.Empty(StoredNames());
    }

    // AC-5: a missing inbox stays as the "# inbox\n" Add created when the capture throws.
    [Fact]
    public void Add_CaptureThrows_DeletesTheAttachmentAndRethrows()
    {
        Assert.Throws<IOException>(() => ScreenshotInbox.Add(
            store, attachments, "shot.png", Png, Now, () => throw new IOException("disk")));

        Assert.Equal("# inbox\n", File.ReadAllText(InboxPath));
        Assert.Empty(StoredNames());
    }
}
