namespace Context;

/// <summary>
/// Watches <c>&lt;root&gt;/.desktop</c> for the open request of <c>context item &lt;n&gt; --open</c> (job 280,
/// <see cref="DesktopRequest"/>) with one non-recursive <see cref="FileSystemWatcher"/>, and handles a request that is
/// already there at <see cref="Start"/>. Each request is read through <see cref="BoundedFile"/> (at most
/// <see cref="DesktopRequest.MaxBytes"/> bytes), deleted, and raises <see cref="ItemRequested"/> only when it is exactly
/// <c>item &lt;n&gt;</c> with a valid number (<see cref="DesktopRequest.ParseItem"/>). Anything else is deleted and
/// ignored; the content never becomes a path. Requests are handled one at a time.
/// </summary>
internal sealed class OpenRequestWatcher : IDisposable
{
    private readonly object gate = new();
    private readonly string root;
    private FileSystemWatcher? watcher;
    private bool disposed;
    private int processed;

    public OpenRequestWatcher(string root)
    {
        this.root = root;
    }

    /// <summary>Raised with the work item number of each valid request, on a pool thread.</summary>
    public event Action<int>? ItemRequested;

    /// <summary>How many request files were read, deleted and handed on so far, valid or not (tests wait on it).</summary>
    public int Processed => Volatile.Read(ref processed);

    /// <summary>
    /// Creates <c>.desktop</c> when missing (<see cref="PrivateFile.CreateFolder"/>), starts watching it and handles a
    /// request that is already there. Never throws: a folder that can't be made or watched is left alone.
    /// </summary>
    public void Start()
    {
        string folder = DesktopRequest.Folder(root);
        lock (gate)
        {
            if (disposed || watcher is not null)
            {
                return;
            }

            try
            {
                PrivateFile.CreateFolder(folder);
                FileSystemWatcher made = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                made.Created += OnEvent;
                made.Changed += OnEvent;
                made.Renamed += OnEvent;
                made.Error += (_, _) => Drain();
                made.EnableRaisingEvents = true;
                watcher = made;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or PlatformNotSupportedException)
            {
                return;
            }
        }

        Drain();
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
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (string.Equals(e.Name, DesktopRequest.RequestFileName, StringComparison.Ordinal))
        {
            Drain();
        }
    }

    // Reads, deletes and parses the request when there is one; raises ItemRequested outside the lock.
    private void Drain()
    {
        int? item;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (!DesktopRequest.TryTake(root, out item))
            {
                return;
            }
        }

        try
        {
            if (item is { } requested)
            {
                ItemRequested?.Invoke(requested);
            }
        }
        finally
        {
            // Counted after the handler ran, so a test that sees the count also sees what the handler did.
            Interlocked.Increment(ref processed);
        }
    }
}
