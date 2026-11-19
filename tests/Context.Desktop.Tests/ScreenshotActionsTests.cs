using System.Text;

namespace Context.Tests;

// 260 AC-1 to AC-6, AC-8: DesktopActions.CaptureScreenshot, ImportScreenshot and the folder setting, against real
// files, the fixture's fixed local clock, a fake area picker and a fake notifier.
public sealed class ScreenshotActionsTests : IDisposable
{
    private const string Name = "screenshot-20261029-100500.png";
    private const string Line = "- 2026-10-29 10:05 Screenshot ![" + Name + "](.attachments/" + Name + ")\n";

    // The hotkey runs its capture on Task.Run; the one bounded wait for it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2];
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<svg onload=alert(1)></svg>");

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string InboxPath => Path.Combine(data.Root, "inbox.md");

    private string AttachmentFolder => Path.Combine(data.Root, ".attachments");

    private string[] StoredNames() =>
        Directory.Exists(AttachmentFolder)
            ? Directory.GetFiles(AttachmentFolder).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal).ToArray()
            : [];

    private SortedDictionary<string, string> Files()
    {
        SortedDictionary<string, string> files = new(StringComparer.Ordinal);
        if (!Directory.Exists(data.Root))
        {
            return files;
        }

        foreach (string file in Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(data.Root, file).Replace('\\', '/');
            if (!relative.StartsWith(".context/", StringComparison.Ordinal) && relative != "context.db")
            {
                files[relative] = Convert.ToBase64String(File.ReadAllBytes(file));
            }
        }

        return files;
    }

    private static string NotSaved(string reason) => DesktopActions.ScreenshotNotSavedText(reason);

    private async Task<string> WatchedFolder()
    {
        string folder = data.Outside("shots");
        Directory.CreateDirectory(folder);
        await data.RefreshAsync();
        ActionResult saved = await data.Actions!.SetScreenshotFolder(folder);
        Assert.Equal(ActionKind.Done, saved.Kind);
        return folder;
    }

    // AC-1, AC-2: no inbox: it is created with the line, the PNG is stored byte for byte, one notification.
    [Fact]
    public async Task CaptureScreenshot_Png_StoresItAndAppendsTheLine()
    {
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Screenshot added to inbox.", result.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("# inbox\n" + Line), File.ReadAllBytes(InboxPath));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(AttachmentFolder, Name)));
        Assert.Equal([("Context", "Screenshot added to inbox.")], data.Notifier.Shown);
        Assert.Equal(1, data.ScreenCapture.Calls);
    }

    // AC-1: an existing inbox keeps its bytes; a second capture in the same second gets -2.
    [Fact]
    public async Task CaptureScreenshot_TwiceInOneSecond_SecondGetsDashTwo()
    {
        data.WriteNote("inbox", "# inbox\n- old\n");
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();

        await data.Actions!.CaptureScreenshot();
        ActionResult second = await data.Actions.CaptureScreenshot();

        Assert.Equal(ActionKind.Done, second.Kind);
        Assert.Equal(
            "# inbox\n- old\n" + Line
                + "- 2026-10-29 10:05 Screenshot ![screenshot-20261029-100500-2.png](.attachments/screenshot-20261029-100500-2.png)\n",
            File.ReadAllText(InboxPath));
        Assert.Equal(["screenshot-20261029-100500-2.png", Name], StoredNames());
        Assert.Equal(2, data.Notifier.Shown.Count);
    }

    // AC-3: cancelled: nothing written, nothing shown.
    [Fact]
    public async Task CaptureScreenshot_Cancelled_WritesAndShowsNothing()
    {
        data.WriteNote("inbox", "# inbox\n");
        data.ScreenCapture.Bytes = null;
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal(before, Files());
        Assert.Empty(data.Notifier.Shown);
    }

    // AC-3: bytes that are no image, or over 10 MB, write nothing and show why.
    [Fact]
    public async Task CaptureScreenshot_NoImage_RefusedAndShown()
    {
        data.ScreenCapture.Bytes = Html;
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        string text = "Screenshot not saved: it is not a PNG, JPEG, GIF or WebP image.";
        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(text, result.Text);
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(StoredNames());
        Assert.Equal([("Context", text)], data.Notifier.Shown);
    }

    // AC-3
    [Fact]
    public async Task CaptureScreenshot_OverTenMegabytes_RefusedAndShown()
    {
        byte[] big = new byte[NoteAttachments.MaxBytes + 1];
        Png.CopyTo(big, 0);
        data.ScreenCapture.Bytes = big;
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal("Screenshot not saved: it is over 10 MB.", result.Text);
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(StoredNames());
        Assert.Equal([("Context", "Screenshot not saved: it is over 10 MB.")], data.Notifier.Shown);
    }

    // The area picker throws: shown, nothing written.
    [Fact]
    public async Task CaptureScreenshot_PickerThrows_ShownNothingWritten()
    {
        data.ScreenCapture.Throws = new InvalidOperationException("no screen");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal([("Context", NotSaved(DesktopActions.ScreenshotUnreadableReason))], data.Notifier.Shown);
        Assert.False(File.Exists(InboxPath));
    }

    // AC-4: a sensitive inbox is refused before the area picker opens, without a key-store call.
    [Theory]
    [InlineData("inbox")]
    [InlineData("Inbox")]
    public async Task CaptureScreenshot_SensitiveInbox_RefusedWithoutPickerOrKeyStore(string name)
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive(name, "# inbox\n");
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("inbox is sensitive. Nothing was captured.", result.Text);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(0, data.ScreenCapture.Calls);
        Assert.Equal(before, Files());
        Assert.Equal([("Context", "inbox is sensitive. Nothing was captured.")], data.Notifier.Shown);
    }

    // AC-5: the inbox changes on disk before every save: the attachment is deleted, the last outside write stays.
    [Fact]
    public async Task CaptureScreenshot_InboxKeepsChanging_DeletesTheAttachment()
    {
        data.WriteNote("inbox", "# inbox\n");
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();
        int attempts = 0;
        data.Actions!.BeforeCaptureSave = () => data.WriteNote("inbox", $"# inbox\n- theirs {++attempts}\n");

        ActionResult result = await data.Actions.CaptureScreenshot();

        Assert.Equal(ActionReason.ChangedOnDisk, result.Reason);
        Assert.Equal($"# inbox\n- theirs {attempts}\n", File.ReadAllText(InboxPath));
        Assert.Empty(StoredNames());
        Assert.Equal([("Context", "Screenshot not saved: inbox kept changing on disk.")], data.Notifier.Shown);
    }

    // AC-5: the save throws: the attachment is deleted and a missing inbox stays as "# inbox\n".
    [Fact]
    public async Task CaptureScreenshot_SaveThrows_DeletesTheAttachment()
    {
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();
        data.Actions!.BeforeCaptureSave = () => throw new IOException("disk full");

        ActionResult result = await data.Actions.CaptureScreenshot();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("# inbox\n", File.ReadAllText(InboxPath));
        Assert.Empty(StoredNames());
        Assert.Equal([("Context", "Screenshot not saved: can't write inbox.")], data.Notifier.Shown);
    }

    // AC-5: the inbox would grow too large: the attachment is deleted.
    [Fact]
    public async Task CaptureScreenshot_InboxTooLarge_DeletesTheAttachment()
    {
        data.WriteNote("inbox", "# inbox\n" + new string('a', (int)NoteStore.MaxEditBytes - 20));
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(InboxPath);

        ActionResult result = await data.Actions!.CaptureScreenshot();

        Assert.Equal("Screenshot not saved: inbox is too large.", result.Text);
        Assert.Equal(before, File.ReadAllBytes(InboxPath));
        Assert.Empty(StoredNames());
    }

    // AC-6: a file in the watched folder is imported under its CleanName and stays where it was.
    [Fact]
    public async Task ImportScreenshot_FileInTheFolder_ImportedAndKept()
    {
        string folder = await WatchedFolder();
        string source = Path.Combine(folder, "Screen Shot 1.JPG");
        File.WriteAllBytes(source, Jpeg);

        ActionResult result = await data.Actions!.ImportScreenshot(source);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(
            "# inbox\n- 2026-10-29 10:05 Screenshot ![Screen-Shot-1.jpg](.attachments/Screen-Shot-1.jpg)\n",
            File.ReadAllText(InboxPath));
        Assert.Equal(Jpeg, File.ReadAllBytes(Path.Combine(AttachmentFolder, "Screen-Shot-1.jpg")));
        Assert.Equal(Jpeg, File.ReadAllBytes(source));
        Assert.Contains(("Context", "Screenshot added to inbox."), data.Notifier.Shown);
    }

    // AC-6, AC-7: names starting with a dot, other extensions, subfolders, other folders and missing files are
    // ignored quietly.
    [Theory]
    [InlineData(".hidden.png")]
    [InlineData("notes.txt")]
    [InlineData("shot.svg")]
    [InlineData("sub/shot.png")]
    [InlineData("../shot.png")]
    [InlineData("gone.png")]
    public async Task ImportScreenshot_NotAScreenshot_IgnoredQuietly(string relative)
    {
        string folder = await WatchedFolder();
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (relative != "gone.png")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Png);
        }

        int shownBefore = data.Notifier.Shown.Count;

        ActionResult result = await data.Actions!.ImportScreenshot(path);

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.Equal(DesktopActions.ScreenshotIgnoredText, result.Text);
        Assert.False(File.Exists(InboxPath));
        Assert.Equal(shownBefore, data.Notifier.Shown.Count);
    }

    // AC-7: a link in the watched folder is ignored.
    [Fact]
    public async Task ImportScreenshot_Link_Ignored()
    {
        string folder = await WatchedFolder();
        string target = data.Outside("target.png");
        File.WriteAllBytes(target, Png);
        string link = Path.Combine(folder, "link.png");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return; // No right to make links here (Windows without developer mode): nothing to check.
        }

        ActionResult result = await data.Actions!.ImportScreenshot(link);

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.False(File.Exists(InboxPath));
    }

    // No folder set: every path is ignored.
    [Fact]
    public async Task ImportScreenshot_NoFolderSet_Ignored()
    {
        string path = data.Outside("shot.png");
        File.WriteAllBytes(path, Png);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.ImportScreenshot(path);

        Assert.Equal(ActionReason.Missing, result.Reason);
        Assert.False(File.Exists(InboxPath));
        Assert.Empty(data.Notifier.Shown);
    }

    // AC-7: a file that is no whole image yet is quiet until the last try, which shows why.
    [Fact]
    public async Task ImportScreenshot_NoImage_QuietUntilTheLastTry()
    {
        string folder = await WatchedFolder();
        string path = Path.Combine(folder, "half.png");
        File.WriteAllBytes(path, Html);
        int shownBefore = data.Notifier.Shown.Count;

        ActionResult early = await data.Actions!.ImportScreenshot(path, lastTry: false);
        Assert.Equal(ActionReason.Unreadable, early.Reason);
        Assert.Equal(shownBefore, data.Notifier.Shown.Count);

        ActionResult last = await data.Actions.ImportScreenshot(path, lastTry: true);

        Assert.Equal(ActionReason.Unreadable, last.Reason);
        Assert.Equal(("Context", NotSaved(DesktopActions.ScreenshotUnsupportedReason)), data.Notifier.Shown[^1]);
        Assert.False(File.Exists(InboxPath));
        Assert.True(File.Exists(path));
    }

    // AC-3 for imports: over 10 MB is refused at once and shown.
    [Fact]
    public async Task ImportScreenshot_OverTenMegabytes_RefusedAndShown()
    {
        string folder = await WatchedFolder();
        string path = Path.Combine(folder, "big.png");
        byte[] big = new byte[NoteAttachments.MaxBytes + 1];
        Png.CopyTo(big, 0);
        File.WriteAllBytes(path, big);

        ActionResult result = await data.Actions!.ImportScreenshot(path, lastTry: false);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(("Context", "Screenshot not saved: it is over 10 MB."), data.Notifier.Shown[^1]);
        Assert.False(File.Exists(InboxPath));
    }

    // AC-8: each refusal has its own text and writes nothing.
    [Theory]
    [InlineData("relative/shots", DesktopActions.ScreenshotFolderNotAbsoluteText)]
    [InlineData("", DesktopActions.ScreenshotFolderNotAbsoluteText)]
    [InlineData("missing", DesktopActions.ScreenshotFolderMissingText)]
    [InlineData("long", DesktopActions.ScreenshotFolderTooLongText)]
    [InlineData("root", DesktopActions.ScreenshotFolderInsideText)]
    [InlineData("inside", DesktopActions.ScreenshotFolderInsideText)]
    public async Task SetScreenshotFolder_Refusals_WriteNothing(string kind, string text)
    {
        data.WriteNote("inbox", "# inbox\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "pictures"));
        string folder = kind switch
        {
            "missing" => data.Outside("missing"),
            "long" => data.Outside(new string('a', SettingsStore.MaxScreenshotFolderLength)),
            "root" => data.Root,
            "inside" => Path.Combine(data.Root, "pictures"),
            _ => kind,
        };
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();

        ActionResult result = await data.Actions!.SetScreenshotFolder(folder);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(text, result.Text);
        Assert.Equal(before, Files());
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
    }

    // AC-8: save, then remove, through settings.json; a second remove says none is set.
    [Fact]
    public async Task SetAndClearScreenshotFolder_UpdateTheSetting()
    {
        string folder = data.Outside("shots");
        Directory.CreateDirectory(folder);
        await data.RefreshAsync();

        ActionResult saved = await data.Actions!.SetScreenshotFolder("  " + folder + "  ");
        Assert.Equal("Screenshot folder saved.", saved.Text);
        Assert.Equal(folder, new SettingsStore(data.Root).ReadScreenshotFolder());

        ActionResult removed = await data.Actions.ClearScreenshotFolder();
        Assert.Equal(ActionKind.Done, removed.Kind);
        Assert.Equal("Screenshot folder removed.", removed.Text);
        Assert.Null(new SettingsStore(data.Root).ReadScreenshotFolder());

        ActionResult again = await data.Actions.ClearScreenshotFolder();
        Assert.Equal(ActionReason.Unchanged, again.Reason);
        Assert.Equal("No screenshot folder is set.", again.Text);
    }

    // AC-1, AC-10: the hotkey registers, a press captures once, and dispose releases the hotkey.
    [Fact]
    public async Task Hotkey_Press_CapturesIntoTheInbox()
    {
        data.ScreenCapture.Bytes = Png;
        await data.RefreshAsync();
        FakeGlobalHotkey key = new FakeGlobalHotkey();
        ScreenshotHotkey hotkey = new ScreenshotHotkey(key, () => data.Actions!.CaptureScreenshot());

        Assert.True(hotkey.Register());
        Assert.True(key.Registered);
        key.Press();

        Assert.True(SpinWait.SpinUntil(() => hotkey.Captures == 1, Wait), $"no capture after {Wait}");
        Assert.Equal("# inbox\n" + Line, File.ReadAllText(InboxPath));

        hotkey.Dispose();
        key.Press();

        Assert.True(key.Disposed);
        Assert.Equal(1, data.ScreenCapture.Calls);
    }

    // A throwing capture is swallowed and the next press captures again.
    [Fact]
    public void Hotkey_ThrowingCapture_NextPressStillRuns()
    {
        FakeGlobalHotkey key = new FakeGlobalHotkey();
        int calls = 0;
        using ScreenshotHotkey hotkey = new ScreenshotHotkey(key, () =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("boom");
        });

        key.Press();
        Assert.True(SpinWait.SpinUntil(() => hotkey.Captures == 1, Wait), $"no first capture after {Wait}");
        key.Press();
        Assert.True(SpinWait.SpinUntil(() => hotkey.Captures == 2, Wait), $"no second capture after {Wait}");

        Assert.Equal(2, Volatile.Read(ref calls));
    }
}
