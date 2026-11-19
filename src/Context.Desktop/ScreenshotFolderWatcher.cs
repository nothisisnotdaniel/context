using System.Collections.Concurrent;

namespace Context;

/// <summary>What the settings page knows about the watched screenshot folder (job 260).</summary>
internal enum ScreenshotFolderKind
{
    /// <summary>No folder is set.</summary>
    NotSet,

    /// <summary>A folder is set; <see cref="ScreenshotFolderView.Exists"/> says whether it is there.</summary>
    Set,

    /// <summary><c>settings.json</c> or its <c>screenshots</c> value can't be read.</summary>
    Unreadable,
}

/// <summary>The screenshot folder setting as <see cref="DesktopState.ScreenshotFolderSetting"/> reads it. Raw path.</summary>
internal sealed record ScreenshotFolderView
{
    public ScreenshotFolderView(ScreenshotFolderKind kind, string? folder, bool exists)
    {
        Kind = kind;
        Folder = folder;
        Exists = exists;
    }

    public ScreenshotFolderKind Kind { get; }

    public string? Folder { get; }

    public bool Exists { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(ScreenshotFolderView);
}

/// <summary>
/// Watches the screenshot folder set on the settings page (job 260, <c>"screenshots":{"folder":…}</c>) with one
/// non-recursive <see cref="FileSystemWatcher"/> (Created and Renamed) and hands each new direct child to the import
/// (the desktop's write path, wired in <c>Program.cs</c>) once, after the folder was quiet for <see cref="Debounce"/>.
/// Files are taken one at a time in event order. Names starting with <c>.</c>, other extensions than
/// <see cref="Extensions"/>, folders and links are skipped. A result <see cref="IsRetryable"/> calls is tried again
/// after <see cref="RetryDelay"/>, at most <see cref="MaxRetries"/> times; the last try is told so, so it notifies.
/// <see cref="Sync"/> runs at <see cref="Start"/> and after every state change, so saving or removing the folder
/// restarts or stops the watcher. A missing folder is never created or watched. Files already there are not imported.
/// </summary>
internal sealed class ScreenshotFolderWatcher : IDisposable
{
    /// <summary>How long the folder must be quiet before new files are imported.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How often a file that is still locked or not yet a whole image is tried again.</summary>
    public const int MaxRetries = 5;

    /// <summary>The image extensions that are imported (any case).</summary>
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    private readonly object gate = new();
    private readonly SettingsStore settings;
    private readonly DesktopState state;
    private readonly Func<string, bool, Task<ActionResult>> import;
    private readonly Timer debounce;
    private readonly SemaphoreSlim draining = new(1, 1);
    private ConcurrentQueue<string> pending = new();
    private ConcurrentDictionary<string, bool> seen = new(StringComparer.Ordinal);
    private FileSystemWatcher? watcher;
    private string? folder;
    private bool started;
    private bool disposed;
    private int processed;

    public ScreenshotFolderWatcher(
        SettingsStore settings, DesktopState state, Func<string, bool, Task<ActionResult>> import)
    {
        this.settings = settings;
        this.state = state;
        this.import = import;
        debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The wait before retry <c>n</c> (1-based); <c>n</c> seconds. Tests replace it so no real wait runs.</summary>
    internal Func<int, Task> RetryDelay { get; set; } = n => Task.Delay(TimeSpan.FromSeconds(n));

    /// <summary>The folder being watched, or null.</summary>
    public string? Folder
    {
        get
        {
            lock (gate)
            {
                return watcher is null ? null : folder;
            }
        }
    }

    /// <summary>How many new paths were handled (imported, skipped or given up on); for tests to wait on.</summary>
    public int Processed => Volatile.Read(ref processed);

    /// <summary>True for an extension in <see cref="Extensions"/>, ignoring case.</summary>
    public static bool HasImageExtension(string name) =>
        Extensions.Any(e => name.Length > e.Length && name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True for an import that may work on a later try: another action was running, the file was locked, or its bytes
    /// were not (yet) a whole image.
    /// </summary>
    public static bool IsRetryable(ActionReason reason) =>
        reason is ActionReason.Busy or ActionReason.Locked or ActionReason.Unreadable;

    /// <summary>Starts watching the saved folder and follows the setting on every state change.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
        }

        state.Changed += Sync;
        Sync();
    }

    /// <summary>
    /// Reads the setting and starts, restarts or stops the watcher to match it. An unreadable setting or a missing
    /// folder stops it. Never throws.
    /// </summary>
    public void Sync()
    {
        string? wanted;
        try
        {
            string? saved = settings.ReadScreenshotFolder();
            wanted = saved is not null && Directory.Exists(saved) ? Path.TrimEndingDirectorySeparator(saved) : null;
        }
        catch (Exception)
        {
            wanted = null;
        }

        FileSystemWatcher? old = null;
        lock (gate)
        {
            if (disposed || (watcher is not null && string.Equals(folder, wanted, StringComparison.Ordinal)))
            {
                return;
            }

            old = watcher;
            watcher = null;
            if (!string.Equals(folder, wanted, StringComparison.Ordinal))
            {
                // Another folder: its files are new ones, and queued files of the old one are dropped.
                folder = wanted;
                seen = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
                pending = new ConcurrentQueue<string>();
            }

            if (wanted is not null)
            {
                watcher = TryWatch(wanted);
            }
        }

        old?.Dispose();
    }

    public void Dispose()
    {
        FileSystemWatcher? old;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            old = watcher;
            watcher = null;
        }

        state.Changed -= Sync;
        old?.Dispose();
        debounce.Dispose();
    }

    private FileSystemWatcher? TryWatch(string path)
    {
        try
        {
            FileSystemWatcher created = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            created.Created += OnCreated;
            created.Renamed += OnRenamed;
            created.Error += OnError;
            created.EnableRaisingEvents = true;
            return created;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException
            || e is PlatformNotSupportedException)
        {
            // Not watched; the next state change tries again.
            return null;
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => Queue(sender, e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Queue(sender, e.FullPath);

    // The watcher broke (for example its buffer overflowed): drop it, so the next Sync starts a new one.
    private void OnError(object sender, ErrorEventArgs e)
    {
        FileSystemWatcher? old = null;
        lock (gate)
        {
            if (ReferenceEquals(watcher, sender))
            {
                old = watcher;
                watcher = null;
            }
        }

        old?.Dispose();
    }

    private void Queue(object sender, string path)
    {
        lock (gate)
        {
            if (disposed || !ReferenceEquals(watcher, sender))
            {
                return;
            }

            pending.Enqueue(path);
            debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
        }

        _ = Drain();
    }

    // One file at a time, in event order; a second Fire while this runs waits and then finds the queue empty or new.
    private async Task Drain()
    {
        await draining.WaitAsync().ConfigureAwait(false);
        try
        {
            while (true)
            {
                ConcurrentQueue<string> queue;
                ConcurrentDictionary<string, bool> known;
                lock (gate)
                {
                    if (disposed)
                    {
                        return;
                    }

                    queue = pending;
                    known = seen;
                }

                if (!queue.TryDequeue(out string? path))
                {
                    return;
                }

                if (!known.TryAdd(path, true))
                {
                    continue;
                }

                try
                {
                    if (IsCandidate(path))
                    {
                        await ImportWithRetries(path).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // Given up on this file; the import notifies on its own.
                }
                finally
                {
                    Interlocked.Increment(ref processed);
                }
            }
        }
        finally
        {
            draining.Release();
        }
    }

    private async Task ImportWithRetries(string path)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            bool last = attempt == MaxRetries;
            ActionResult result = await import(path, last).ConfigureAwait(false);
            if (result.Kind == ActionKind.Done || !IsRetryable(result.Reason) || last)
            {
                return;
            }

            await RetryDelay(attempt + 1).ConfigureAwait(false);
        }
    }

    // A regular image file directly in the folder: not hidden, not a link, a known extension.
    private static bool IsCandidate(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Length == 0 || name.StartsWith('.') || !HasImageExtension(name))
        {
            return false;
        }

        try
        {
            FileInfo info = new FileInfo(path);
            return info.Exists
                && info.LinkTarget is null
                && (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        {
            return false;
        }
    }
}
