namespace Context;

/// <summary>
/// Holds the latest <see cref="DesktopSnapshot"/> and rebuilds it from the Core stores, read-only. A refresh runs
/// off the UI thread, one at a time; requests made while one runs coalesce into one more run. Components read
/// <see cref="Snapshot"/> and re-render on <see cref="Changed"/>.
/// </summary>
internal sealed class DesktopState
{
    /// <summary><see cref="Tick"/> refreshes when the last refresh is at least this old.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly Encryption encryption;
    private readonly NoteStore notes;
    private readonly TodoStore todos;
    private readonly TagIndex index;
    private readonly CalendarStore calendar;
    private readonly CalendarFeed? feed;
    private readonly Func<DateOnly> today;
    private readonly Func<DateTime> utcNow;

    private Task<DesktopSnapshot>? running;
    private bool pending;
    private DateOnly? lastDay;
    private DateTime lastRefreshUtc;
    private int builds;
    private volatile DesktopSnapshot? snapshot;

    public DesktopState(
        Encryption encryption,
        NoteStore notes,
        TodoStore todos,
        TagIndex index,
        CalendarStore calendar,
        CalendarFeed? feed,
        Func<DateOnly> today,
        Func<DateTime> utcNow)
    {
        this.encryption = encryption;
        this.notes = notes;
        this.todos = todos;
        this.index = index;
        this.calendar = calendar;
        this.feed = feed;
        this.today = today;
        this.utcNow = utcNow;
    }

    /// <summary>Raised (on a background thread) after every refresh, once <see cref="Snapshot"/> is set.</summary>
    public event Action? Changed;

    /// <summary>The latest snapshot, or <c>null</c> before the first refresh finished.</summary>
    public DesktopSnapshot? Snapshot => snapshot;

    /// <summary>How many snapshots have been built so far.</summary>
    public int Builds => Volatile.Read(ref builds);

    /// <summary>
    /// Rebuilds the snapshot off the calling thread. While a refresh runs, another call returns the same task and
    /// makes it build once more when done, so a burst of requests costs at most one extra run.
    /// </summary>
    public Task<DesktopSnapshot> Refresh()
    {
        lock (gate)
        {
            if (running is not null)
            {
                pending = true;
                return running;
            }

            running = Task.Run(Loop);
            return running;
        }
    }

    /// <summary>
    /// Refreshes when the date changed or <see cref="MaxAge"/> passed since the last refresh started; otherwise
    /// does nothing. Returns true when it started (or joined) a refresh.
    /// </summary>
    public bool Tick()
    {
        DateOnly day;
        DateTime now;
        try
        {
            day = today();
            now = utcNow();
        }
        catch (Exception)
        {
            return false;
        }

        bool due;
        lock (gate)
        {
            due = lastDay != day || now - lastRefreshUtc >= MaxAge || now < lastRefreshUtc;
        }

        if (due)
        {
            _ = Refresh();
        }

        return due;
    }

    private DesktopSnapshot Loop()
    {
        while (true)
        {
            DesktopSnapshot built = Build();
            snapshot = built;
            Interlocked.Increment(ref builds);
            RaiseChanged();
            lock (gate)
            {
                if (!pending)
                {
                    running = null;
                    return built;
                }

                pending = false;
            }
        }
    }

    private void RaiseChanged()
    {
        Action? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception)
            {
                // A failing listener must not stop the refresh loop or the other listeners.
            }
        }
    }

    // Never throws: any failure becomes the fixed failure snapshot, so no exception text reaches the page.
    private DesktopSnapshot Build()
    {
        string folder = notes.Root;
        DateOnly day = default;
        try
        {
            day = today();
            DateTime now = utcNow();
            lock (gate)
            {
                lastDay = day;
                lastRefreshUtc = now;
            }

            if (encryption.IsConfigured)
            {
                return DesktopSnapshot.ForLocked(folder, day);
            }

            IReadOnlyList<NoteInfo> noteList;
            bool notesUnreadable = false;
            try
            {
                noteList = notes.List();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                noteList = [];
                notesUnreadable = true;
            }

            IReadOnlyList<TodoItem> todoList;
            bool todosUnreadable = false;
            try
            {
                todoList = todos.List();
            }
            catch (TodoStoreException)
            {
                todoList = [];
                todosUnreadable = true;
            }

            CurrentView current = CurrentView.Build(todos, index, notes, calendar, feed, day);
            return new DesktopSnapshot(
                false, false, day, current, todoList, todosUnreadable, noteList, notesUnreadable, folder);
        }
        catch (Exception)
        {
            return DesktopSnapshot.ForFailure(folder, day);
        }
    }
}
