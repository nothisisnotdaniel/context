namespace Context;

/// <summary>
/// Watches the data folder and refreshes <see cref="DesktopState"/> when a file the window shows changes: events
/// are debounced (<see cref="Debounce"/>) into one <see cref="DesktopState.Refresh"/>. A timer calls
/// <see cref="DesktopState.Tick"/> every <see cref="TickInterval"/>, so the date change and the Exchange feed are
/// picked up too. A missing data folder is never created: the watcher starts once the folder appears.
/// </summary>
internal sealed class DataWatcher : IDisposable
{
    /// <summary>How long the folder must be quiet before a refresh starts.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How often <see cref="DesktopState.Tick"/> runs.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly string root;
    private readonly DesktopState state;
    private readonly Timer debounce;
    private readonly Timer ticker;
    private FileSystemWatcher? watcher;
    private bool disposed;

    public DataWatcher(string root, DesktopState state)
    {
        this.root = Path.GetFullPath(root);
        this.state = state;
        debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        ticker = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts watching (if the folder exists) and the tick timer.</summary>
    public void Start()
    {
        _ = TryWatch();
        ticker.Change(TickInterval, TickInterval);
    }

    /// <summary>
    /// True for a path whose change the window shows: a top-level <c>*.md</c> note (any case),
    /// <c>todos.json</c>, <c>calendar-link.ics</c> or a top-level <c>calendar/*.ics</c>
    /// file. False for the index (<c>context.db*</c>), temp files, <c>calendar-link.txt</c>, notes in subfolders
    /// and anything outside <paramref name="root"/>. A successful <see cref="CalendarFeed.Fetch"/> rewrites
    /// <c>calendar-link.ics</c>, so it causes one extra refresh; that one is served from the fresh cache (no request).
    /// </summary>
    public static bool IsRelevant(string root, string path)
    {
        string full;
        string rootFull;
        try
        {
            full = Path.GetFullPath(path);
            rootFull = Path.GetFullPath(root);
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        {
            return false;
        }

        string relative = Path.GetRelativePath(rootFull, full);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.None);
        if (parts.Length == 1)
        {
            string name = parts[0];
            return HasExtension(name, ".md")
                || HasExtension(name, NoteStore.SensitiveExtension)
                || Same(name, TodoStore.FileName)
                || Same(name, CalendarFeed.CacheFileName);
        }

        return parts.Length == 2 && Same(parts[0], CalendarStore.FolderName) && HasExtension(parts[1], ".ics");
    }

    public void Dispose()
    {
        FileSystemWatcher? old;
        lock (gate)
        {
            disposed = true;
            old = watcher;
            watcher = null;
        }

        old?.Dispose();
        debounce.Dispose();
        ticker.Dispose();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool HasExtension(string name, string extension) =>
        name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    // Returns true when a new watcher was started.
    private bool TryWatch()
    {
        lock (gate)
        {
            if (disposed || watcher is not null || !Directory.Exists(root))
            {
                return false;
            }

            try
            {
                FileSystemWatcher created = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                        | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                created.Changed += OnEvent;
                created.Created += OnEvent;
                created.Deleted += OnEvent;
                created.Renamed += OnRenamed;
                created.Error += OnError;
                created.EnableRaisingEvents = true;
                watcher = created;
                return true;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException
                || e is PlatformNotSupportedException)
            {
                // Watching is best effort: the tick timer still refreshes every few minutes.
                return false;
            }
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (IsRelevant(root, e.FullPath))
        {
            Schedule();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevant(root, e.FullPath) || IsRelevant(root, e.OldFullPath))
        {
            Schedule();
        }
    }

    // The watcher's buffer overflowed or it broke: refresh once, and start a new one on the next tick.
    private void OnError(object sender, ErrorEventArgs e)
    {
        FileSystemWatcher? old;
        lock (gate)
        {
            old = watcher;
            watcher = null;
        }

        old?.Dispose();
        Schedule();
    }

    private void Schedule()
    {
        lock (gate)
        {
            if (!disposed)
            {
                debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
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

        _ = state.Refresh();
    }

    // A watcher started late (the folder appeared, or the old one broke) may have missed changes: refresh once.
    private void OnTick()
    {
        if (TryWatch())
        {
            Schedule();
        }

        state.Tick();
    }
}
