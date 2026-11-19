using System.Collections.Concurrent;
using System.Text;

namespace Context.Tests;

// 260 AC-7, AC-9: ScreenshotFolderWatcher over a temp watched folder next to the fixture's data folder. Every test
// waits for an observable state (the Processed count, the watched Folder) with the one bounded Wait; retries use the
// RetryDelay hook, so no real backoff runs.
public sealed class ScreenshotFolderWatcherTests : IDisposable
{
    // File-system events and the debounce run on the thread pool; the one bounded wait for them.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a....");
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<html></html>");

    private readonly DesktopFixture data = new();
    private readonly ConcurrentQueue<(string Path, bool Last)> calls = new();
    private ScreenshotFolderWatcher? watcher;

    public void Dispose()
    {
        watcher?.Dispose();
        data.Dispose();
    }

    private string InboxPath => Path.Combine(data.Root, "inbox.md");

    private string InboxText => File.Exists(InboxPath) ? File.ReadAllText(InboxPath) : string.Empty;

    // A watched folder saved through the actions, and a started watcher whose import goes through the actions.
    private async Task<string> Watch(Func<string, bool, Task<ActionResult>>? import = null)
    {
        string folder = data.Outside("shots");
        Directory.CreateDirectory(folder);
        await data.RefreshAsync();
        Assert.Equal(ActionKind.Done, (await data.Actions!.SetScreenshotFolder(folder)).Kind);
        Start(import);
        WaitUntil(() => watcher!.Folder is not null, "the watcher to start");
        return folder;
    }

    private void Start(Func<string, bool, Task<ActionResult>>? import = null)
    {
        watcher = new ScreenshotFolderWatcher(
            new SettingsStore(data.Root),
            data.State!,
            (path, last) =>
            {
                calls.Enqueue((path, last));
                return import is null ? data.Actions!.ImportScreenshot(path, last) : import(path, last);
            })
        {
            RetryDelay = _ => Task.CompletedTask,
        };
        watcher.Start();
    }

    private static void WaitUntil(Func<bool> condition, string what) =>
        Assert.True(SpinWait.SpinUntil(condition, Wait), $"waited {Wait} for {what}");

    private void WaitProcessed(int count) =>
        WaitUntil(() => watcher!.Processed >= count, $"{count} processed file(s), got {watcher!.Processed}");

    private int Added() => data.Notifier.Shown.Count(s => s.Body == DesktopActions.ScreenshotAddedText);

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // AC-7: a new image is imported exactly once; a later file shows the first gave no second import.
    [Fact]
    public async Task NewFile_ImportedExactlyOnce()
    {
        string folder = await Watch();

        File.WriteAllBytes(Path.Combine(folder, "one.png"), Png);
        WaitProcessed(1);
        File.WriteAllBytes(Path.Combine(folder, "two.gif"), Gif);
        WaitProcessed(2);

        string text = InboxText;
        Assert.Equal(1, Count(text, "Screenshot ![one.png](.attachments/one.png)"));
        Assert.Equal(1, Count(text, "Screenshot ![two.gif](.attachments/two.gif)"));
        Assert.Equal(2, Count(text, "Screenshot !["));
        Assert.True(File.Exists(Path.Combine(folder, "one.png")));
    }

    // AC-7: a file renamed into the folder from outside, or within it from a temp name, is imported.
    [Fact]
    public async Task RenamedIn_Imported()
    {
        string folder = await Watch();
        string outside = data.Outside("moved.png");
        File.WriteAllBytes(outside, Png);
        File.WriteAllBytes(Path.Combine(folder, ".partial"), Png);

        File.Move(outside, Path.Combine(folder, "moved.png"));
        File.Move(Path.Combine(folder, ".partial"), Path.Combine(folder, "renamed.png"));

        // The notification comes after the import's write and refresh: nothing of it runs on after this.
        WaitUntil(() => Added() == 2, "two added notifications");
        Assert.Equal(1, Count(InboxText, "![moved.png]"));
        Assert.Equal(1, Count(InboxText, "![renamed.png]"));
    }

    // AC-7: dot-names, other extensions, subfolders (and files in them) and links are skipped without an import call.
    [Fact]
    public async Task Skipped_NoImportCalls()
    {
        string folder = await Watch();
        File.WriteAllBytes(Path.Combine(folder, ".hidden.png"), Png);
        File.WriteAllBytes(Path.Combine(folder, "notes.txt"), Png);
        Directory.CreateDirectory(Path.Combine(folder, "sub.png"));
        string target = data.Outside("target.png");
        File.WriteAllBytes(target, Png);
        try
        {
            File.CreateSymbolicLink(Path.Combine(folder, "link.png"), target);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // No right to make links here (Windows without developer mode): the other kinds are still checked.
        }

        // A last real file: once it is in the inbox, every earlier event was handled (one at a time, in order).
        WaitProcessed(1);
        File.WriteAllBytes(Path.Combine(folder, "real.png"), Png);
        WaitUntil(() => Added() == 1, "the added notification of real.png");

        Assert.Equal(["real.png"], calls.Select(c => Path.GetFileName(c.Path)).Distinct().ToArray());
        Assert.Equal(1, Count(InboxText, "Screenshot !["));
    }

    // AC-7: a file that stays locked is tried 1 + 5 times; only the last try is told it is the last.
    [Fact]
    public async Task Locked_RetriedFiveTimesThenGivenUp()
    {
        string folder = await Watch((_, _) => Task.FromResult(
            new ActionResult(ActionKind.Failed, ActionReason.Locked, "locked")));

        File.WriteAllBytes(Path.Combine(folder, "busy.png"), Png);
        WaitProcessed(1);

        (string Path, bool Last)[] made = calls.ToArray();
        Assert.Equal(1 + ScreenshotFolderWatcher.MaxRetries, made.Length);
        Assert.All(made, c => Assert.Equal("busy.png", Path.GetFileName(c.Path)));
        Assert.Equal([false, false, false, false, false, true], made.Select(c => c.Last).ToArray());
    }

    // AC-7: a file that is never a whole image is skipped after the last try with one notification.
    [Fact]
    public async Task NoImage_GivenUpWithOneNotification()
    {
        string folder = await Watch();
        int shownBefore = data.Notifier.Shown.Count;

        File.WriteAllBytes(Path.Combine(folder, "half.png"), Html);
        WaitProcessed(1);

        Assert.Equal(1 + ScreenshotFolderWatcher.MaxRetries, calls.Count);
        Assert.Equal(
            [("Context", "Screenshot not saved: it is not a PNG, JPEG, GIF or WebP image.")],
            data.Notifier.Shown.Skip(shownBefore).ToArray());
        Assert.False(File.Exists(InboxPath));
    }

    // AC-7: a file that becomes a whole image on a retry is imported then.
    [Fact]
    public async Task NoImageYet_ImportedOnARetry()
    {
        string folder = await Watch();
        string path = Path.Combine(folder, "slow.png");
        watcher!.RetryDelay = _ =>
        {
            File.WriteAllBytes(path, Png);
            return Task.CompletedTask;
        };

        File.WriteAllBytes(path, Html);
        WaitProcessed(1);

        Assert.Equal(2, calls.Count);
        Assert.Equal(1, Count(InboxText, "Screenshot ![slow.png](.attachments/slow.png)"));
    }

    // AC-9: no folder at start: nothing watched; saving one starts the watcher, removing it stops it; no restart.
    [Fact]
    public async Task SaveAndRemove_StartAndStopTheWatcher()
    {
        string folder = data.Outside("shots");
        Directory.CreateDirectory(folder);
        await data.RefreshAsync();
        Start();
        Assert.Null(watcher!.Folder);

        Assert.Equal(ActionKind.Done, (await data.Actions!.SetScreenshotFolder(folder)).Kind);
        WaitUntil(() => watcher.Folder is not null, "the watcher to start");
        Assert.Equal(Path.TrimEndingDirectorySeparator(folder), watcher.Folder);

        File.WriteAllBytes(Path.Combine(folder, "a.png"), Png);
        WaitProcessed(1);
        Assert.Contains("![a.png]", InboxText, StringComparison.Ordinal);

        Assert.Equal(ActionKind.Done, (await data.Actions.ClearScreenshotFolder()).Kind);
        WaitUntil(() => watcher.Folder is null, "the watcher to stop");
    }

    // AC-9: a saved folder that is gone at start is not created and not watched.
    [Fact]
    public async Task MissingFolder_NotCreatedNotWatched()
    {
        string folder = data.Outside("gone");
        Directory.CreateDirectory(data.Root);
        new SettingsStore(data.Root).SaveScreenshotFolder(folder);
        await data.RefreshAsync();

        Start();
        watcher!.Sync();

        Assert.Null(watcher.Folder);
        Assert.False(Directory.Exists(folder));
    }

    // AC-8: a broken setting watches nothing and does not throw.
    [Fact]
    public async Task BrokenSetting_WatchesNothing()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllText(Path.Combine(data.Root, SettingsStore.FileName), "{\"screenshots\":{\"folder\":42}}");
        await data.RefreshAsync();

        Start();

        Assert.Null(watcher!.Folder);
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.webp", true)]
    [InlineData(".png", false)]
    [InlineData("a.svg", false)]
    [InlineData("a.png.txt", false)]
    public void HasImageExtension_Matches(string name, bool expected) =>
        Assert.Equal(expected, ScreenshotFolderWatcher.HasImageExtension(name));
}
