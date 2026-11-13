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

    // One user of the tag index at a time: the snapshot build, NotesWithTag, Tags and Links.
    private readonly object indexGate = new();
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
        NoteStore notes,
        TodoStore todos,
        TagIndex index,
        CalendarStore calendar,
        CalendarFeed? feed,
        Func<DateOnly> today,
        Func<DateTime> utcNow,
        IKeyStore? keyStore = null,
        SensitiveSession? session = null)
    {
        this.notes = notes;
        this.todos = todos;
        this.index = index;
        this.calendar = calendar;
        this.feed = feed;
        this.today = today;
        this.utcNow = utcNow;
        KeyStore = keyStore ?? new UnsupportedKeyStore();
        Session = session ?? new SensitiveSession(utcNow);
    }

    /// <summary>Raised (on a background thread) after every refresh, once <see cref="Snapshot"/> is set.</summary>
    public event Action? Changed;

    /// <summary>The OS key store of sensitive notes (one that supports nothing unless one was given).</summary>
    public IKeyStore KeyStore { get; }

    /// <summary>The open sensitive note and its idle lock.</summary>
    public SensitiveSession Session { get; }

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

    /// <summary>
    /// Reads the note called <paramref name="name"/> for editing (<see cref="NoteStore.ReadForEdit"/>) off the
    /// calling thread. <c>null</c> when there is no such note, or it can't be read.
    /// </summary>
    public Task<EditableNote?> ReadForEdit(string name) => Task.Run(() =>
    {
        try
        {
            if (!notes.TryFind(name, out NoteInfo? found) || found is null)
            {
                return null;
            }

            return notes.ReadForEdit(found);
        }
        catch (Exception)
        {
            return (EditableNote?)null;
        }
    });

    /// <summary>
    /// Opens the sensitive note called <paramref name="name"/> for editing, off the calling thread: reads its sealed
    /// file (found in <see cref="NoteStore.ListSensitive"/>, at most <see cref="SealedNote.MaxFileBytes"/>) and
    /// unwraps it with one <see cref="IKeyStore.Agree"/> call, which may make the OS ask the user. The opened note
    /// becomes the <see cref="Session"/>'s note; a note the session already holds under that name is returned
    /// without asking again. <c>null</c> when there is no such note or it can't be unlocked, whatever the reason.
    /// Nothing is written.
    /// </summary>
    public Task<EditableNote?> ReadSensitive(string name) => Task.Run(() =>
    {
        try
        {
            if (!notes.TryFindSensitive(name, out NoteInfo? found) || found is null)
            {
                return null;
            }

            EditableNote? open = Session.Note;
            if (open is not null
                && string.Equals(open.Name, found.Name, StringComparison.Ordinal)
                && string.Equals(open.Path, found.Path, StringComparison.Ordinal))
            {
                return open;
            }

            byte[]? machine = KeyStore.MachinePublicKey();
            if (machine is null)
            {
                return null;
            }

            byte[] file = BoundedFile.Read(found.Path, SealedNote.MaxFileBytes);
            string text = SealedNote.Open(found.Name, file, machine, KeyStore.Agree);
            EditableNote note = SensitiveSession.ToEditable(found, text, EditableNote.Hash(file));
            Session.Open(note);
            return note;
        }
        catch (Exception)
        {
            return (EditableNote?)null;
        }
    });

    /// <summary>
    /// The notes tagged <paramref name="tag"/> (normalized like <see cref="TagIndex.NormalizeTag"/>) after syncing the
    /// index, sorted by name ignoring case, off the calling thread. <c>null</c> when the index can't be read.
    /// </summary>
    public Task<IReadOnlyList<string>?> NotesWithTag(string tag) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                index.Sync(notes);
                return (IReadOnlyList<string>?)index.NotesWithTag(tag)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList();
            }
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>
    /// Every tag with its note count (<see cref="TagsView.Build"/> without an argument) after syncing the index, off
    /// the calling thread. Never throws: any failure gives the <see cref="TagsViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<TagsView> Tags() => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return TagsView.Build(index, notes, string.Empty);
            }
        }
        catch (Exception)
        {
            return new TagsView(TagsViewKind.IndexUnreadable, string.Empty, [], []);
        }
    });

    /// <summary>
    /// The notes linked to the note called <paramref name="name"/> by shared tags or <c>[[note links]]</c>
    /// (<see cref="LinksView.Build"/>) after syncing the index, off the calling thread. Never throws: any failure
    /// gives the <see cref="LinksViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<LinksView> Links(string name) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return LinksView.Build(index, notes, name);
            }
        }
        catch (Exception)
        {
            return new LinksView(LinksViewKind.IndexUnreadable, string.Empty, null, []);
        }
    });

    /// <summary>
    /// The note a <c>[[link]]</c> target or a typed name names, resolved like <c>/open</c>
    /// (<see cref="NameLookup.Resolve"/>, trimmed) against the notes in the latest snapshot, sensitive ones included
    /// (by name only). <c>null</c> if none.
    /// </summary>
    public string? ResolveNote(string target)
    {
        DesktopSnapshot? current = snapshot;
        if (current is null)
        {
            return null;
        }

        return new NameLookup(current.Notes.Concat(current.Sensitive).Select(n => n.Name)).Resolve(target.Trim());
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

    // True when notes can be marked sensitive: the key store has this machine's key and the recovery public key.
    private bool RecoveryReady()
    {
        try
        {
            return KeyStore.Supported && KeyStore.MachinePublicKey() is not null && KeyStore.RecoveryPublicKey() is not null;
        }
        catch (KeyStoreException)
        {
            return false;
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

            CurrentView current;
            lock (indexGate)
            {
                current = CurrentView.Build(todos, index, notes, calendar, feed, day);
            }

            IReadOnlyList<NoteInfo> sensitive = [];
            if (!notesUnreadable)
            {
                try
                {
                    sensitive = notes.ListSensitive();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    notesUnreadable = true;
                }
            }

            return new DesktopSnapshot(
                false, day, current, todoList, todosUnreadable, noteList, notesUnreadable, folder, sensitive,
                KeyStore.Supported, RecoveryReady());
        }
        catch (Exception)
        {
            return DesktopSnapshot.ForFailure(folder, day);
        }
    }
}
