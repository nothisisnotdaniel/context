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
    private readonly CalendarLink? calendarLink;
    private readonly Func<DateOnly> today;
    private readonly Func<DateTime> utcNow;

    private Task<DesktopSnapshot>? running;
    private bool pending;
    private DateOnly? lastDay;
    private DateTime lastRefreshUtc;
    private int builds;
    private volatile DesktopSnapshot? snapshot;
    private string? offeredBriefing;

    public DesktopState(
        NoteStore notes,
        TodoStore todos,
        TagIndex index,
        CalendarStore calendar,
        CalendarFeed? feed,
        Func<DateOnly> today,
        Func<DateTime> utcNow,
        IKeyStore? keyStore = null,
        SensitiveSession? session = null,
        CalendarLink? calendarLink = null)
    {
        this.calendarLink = calendarLink;
        this.notes = notes;
        this.todos = todos;
        this.index = index;
        this.calendar = calendar;
        this.feed = feed;
        this.today = today;
        this.utcNow = utcNow;
        KeyStore = keyStore ?? new UnsupportedKeyStore();
        Session = session ?? new SensitiveSession(utcNow);
        HistoryStore = new NoteHistory(notes, utcNow);
    }

    /// <summary>The kept versions of the notes, with the same clock as this state.</summary>
    public NoteHistory HistoryStore { get; }

    /// <summary>Raised (on a background thread) after every refresh, once <see cref="Snapshot"/> is set.</summary>
    public event Action? Changed;

    /// <summary>The OS key store of sensitive notes (one that supports nothing unless one was given).</summary>
    public IKeyStore KeyStore { get; }

    /// <summary>The open sensitive note and its idle lock.</summary>
    public SensitiveSession Session { get; }

    /// <summary>The calendar's zone; times of the calendar (the last download) are shown in it.</summary>
    public TimeZoneInfo Zone => calendar.Zone;

    /// <summary>The latest snapshot, or <c>null</c> before the first refresh finished.</summary>
    public DesktopSnapshot? Snapshot => snapshot;

    /// <summary>
    /// Offers the briefing <paramref name="name"/> that was just created, for <c>Main</c> to open or announce. An
    /// in-memory value only: nothing is written. Called only by the automatic briefing of the actions.
    /// </summary>
    public void OfferBriefing(string name) => Interlocked.Exchange(ref offeredBriefing, name);

    /// <summary>The offered briefing's name once, then <c>null</c> until the next offer.</summary>
    public string? TakeBriefing() => Interlocked.Exchange(ref offeredBriefing, null);

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
    /// How many plain notes would get their <c>[[links]]</c> rewritten when <paramref name="name"/> is renamed to
    /// <paramref name="newTitle"/> (<see cref="LinkRenamePlan"/>; the renamed note itself counts for a self-link), off
    /// the calling thread. Reads only, writes nothing; 0 for the same name, a case-only rename, a title the rename
    /// refuses, and on any error.
    /// </summary>
    public Task<int> LinkUpdateCount(string name, string newTitle) => Task.Run(() =>
    {
        try
        {
            return LinkRenamePlan.For(notes, name, newTitle).Notes.Count;
        }
        catch (Exception)
        {
            return 0;
        }
    });

    /// <summary>
    /// The images the plain note <paramref name="name"/> shows inline, off the calling thread: each source of
    /// <see cref="NoteAttachments.ImageSources"/> in <paramref name="text"/> that <see cref="NoteAttachments.TryResolve"/>
    /// accepts as an image, with its MIME type and base64 bytes. Empty on any error. Reads only.
    /// </summary>
    public Task<IReadOnlyList<InlineImage>> InlineImages(string name, string text) => Task.Run(() =>
    {
        List<InlineImage> images = [];
        try
        {
            NoteAttachments attachments = new NoteAttachments(notes);
            foreach (string src in NoteAttachments.ImageSources(text))
            {
                ResolvedAttachment? resolved = attachments.TryResolve(name, src);
                if (resolved is not null && resolved.IsImage)
                {
                    images.Insert(images.Count, new InlineImage(src, resolved.Mime, Convert.ToBase64String(resolved.Bytes)));
                }
            }
        }
        catch (Exception)
        {
            return (IReadOnlyList<InlineImage>)[];
        }

        return images;
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
    /// The kept versions of the note called <paramref name="name"/> (plain, or the sensitive note the
    /// <see cref="Session"/> holds open), newest first, and its saved text, off the calling thread. Never asks the key
    /// store. <see cref="HistoryKind.Unreadable"/> when the note is gone, locked, or it or its history can't be read.
    /// </summary>
    public Task<HistoryView> History(string name) => Task.Run(() =>
    {
        try
        {
            if (notes.TryFind(name, out NoteInfo? plain) && plain is not null)
            {
                string text = notes.ReadForEdit(plain).Text;
                return new HistoryView(HistoryKind.Shown, HistoryStore.Versions(plain), text);
            }

            if (notes.TryFindSensitive(name, out NoteInfo? found) && found is not null
                && OpenNote(found) is { } open)
            {
                return new HistoryView(HistoryKind.Shown, HistoryStore.Versions(found), open.Text);
            }

            return HistoryView.Unreadable;
        }
        catch (Exception)
        {
            return HistoryView.Unreadable;
        }
    });

    /// <summary>
    /// The text of version <paramref name="id"/> of the note <paramref name="name"/>, as the editor would hold it, off
    /// the calling thread (see <see cref="ReadVersionText"/>).
    /// </summary>
    public Task<string?> ReadVersion(string name, string id) => Task.Run(() => ReadVersionText(name, id));

    /// <summary>
    /// The text of version <paramref name="id"/> of the note <paramref name="name"/> as the editor would hold it
    /// (<c>\n</c> line endings, no byte order mark). A plain version is decoded like a note; a sealed one is opened
    /// with <see cref="SealedNote.Open"/> under the title in its file name and one <see cref="IKeyStore.Agree"/>, only
    /// while the session holds the note open. <c>null</c> when there is no such version or it can't be read.
    /// </summary>
    public string? ReadVersionText(string name, string id)
    {
        try
        {
            if (notes.TryFind(name, out NoteInfo? plain) && plain is not null)
            {
                return HistoryStore.TryRead(plain, id, out byte[]? bytes) && bytes is not null
                    ? NoteStore.DecodeText(bytes).Replace("\r\n", "\n", StringComparison.Ordinal)
                    : null;
            }

            if (!notes.TryFindSensitive(name, out NoteInfo? found) || found is null || OpenNote(found) is null)
            {
                return null;
            }

            NoteVersion? version = HistoryStore.Versions(found)
                .FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.Ordinal));
            byte[]? machine = KeyStore.MachinePublicKey();
            if (version?.SealedTitle is not { } title || machine is null
                || !HistoryStore.TryRead(found, id, out byte[]? file) || file is null)
            {
                return null;
            }

            return SensitiveSession.ToEditable(found, SealedNote.Open(title, file, machine, KeyStore.Agree), string.Empty).Text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The session's note when it is the sensitive note found on disk, else null.
    private EditableNote? OpenNote(NoteInfo found)
    {
        EditableNote? open = Session.Note;
        return open is not null
            && string.Equals(open.Name, found.Name, StringComparison.Ordinal)
            && string.Equals(open.Path, found.Path, StringComparison.Ordinal)
            ? open
            : null;
    }

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
    /// The person page of <paramref name="name"/> (<see cref="PersonView.Build"/>): the notes that mention the person,
    /// their appointments from the calendar files and the cached published calendar, and their open and done todos, after syncing the index, off
    /// the calling thread. Reads only (no download) and never throws: any failure gives a <c>NoMentions</c> view with
    /// <see cref="PersonView.IndexUnreadable"/> set.
    /// </summary>
    public Task<PersonView> Person(string name) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return PersonView.Build(index, notes, calendar, feed, name, todos);
            }
        }
        catch (Exception)
        {
            return new PersonView(PersonViewKind.NoMentions, name, [], [], [], true, calendar.Zone);
        }
    });

    /// <summary>
    /// The inbox lines to triage for today (<see cref="InboxTriage.Items"/> over the plain <c>inbox</c> note read through
    /// <see cref="NoteStore.ReadForEdit"/>), off the calling thread. A missing inbox is an empty queue; a sensitive inbox
    /// is <see cref="TriageKind.Sensitive"/> without any key-store call. Reads only and never throws.
    /// </summary>
    public Task<TriageView> Triage() => Task.Run(() =>
    {
        DateOnly day = today();
        try
        {
            if (notes.TryFindSensitive(Inbox.NoteName, out _))
            {
                return new TriageView(TriageKind.Sensitive, InboxQueue.Empty, day);
            }

            if (!notes.TryFind(Inbox.NoteName, out NoteInfo? found) || found is null)
            {
                return new TriageView(TriageKind.Shown, InboxQueue.Empty, day);
            }

            return new TriageView(TriageKind.Shown, InboxTriage.Items(notes.ReadForEdit(found).Text, day), day);
        }
        catch (Exception)
        {
            return new TriageView(TriageKind.Unreadable, InboxQueue.Empty, day);
        }
    });

    /// <summary>
    /// The weekly review (<see cref="ReviewView.Build"/>) for today: the notes changed and the todos done in the last 7
    /// days, the overdue todos and the next 7 days' appointments, off the calling thread. It never opens
    /// <c>context.db</c> and never throws: any failure gives an empty view with <see cref="ReviewView.TodosUnreadable"/>
    /// set.
    /// </summary>
    public Task<ReviewView> Review() => Task.Run(() =>
    {
        DateOnly day = today();
        try
        {
            return ReviewView.Build(notes, todos, calendar, feed, day);
        }
        catch (Exception)
        {
            return new ReviewView(
                day.AddDays(-(ReviewView.WindowDays - 1)),
                day,
                calendar.Zone,
                [],
                true,
                [],
                [],
                [],
                calendar.Folder,
                [],
                CalendarFeedStatus.NotConfigured,
                null,
                null);
        }
    });

    /// <summary>
    /// The notes with the attribute <paramref name="key"/><c>::</c> <paramref name="value"/> (<see cref="FindView.Build"/>)
    /// after syncing the index, off the calling thread. Reads only and never throws: any failure gives the
    /// <see cref="FindViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<FindView> Find(string key, string value) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return FindView.Build(index, notes, key, value);
            }
        }
        catch (Exception)
        {
            return new FindView(FindViewKind.IndexUnreadable, string.Empty, string.Empty, []);
        }
    });

    /// <summary>
    /// The notes whose text contains <paramref name="text"/> (<see cref="SearchView.Build"/>) after syncing the index,
    /// off the calling thread. Reads only and never throws: any failure gives the
    /// <see cref="SearchViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<SearchView> Search(string text) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return SearchView.Build(index, notes, text);
            }
        }
        catch (Exception)
        {
            return new SearchView(SearchViewKind.IndexUnreadable, string.Empty, []);
        }
    });

    /// <summary>
    /// The <c>Decision:</c> lines of every plain note, or of the notes with <paramref name="tag"/>
    /// (<see cref="DecisionsView.Build"/>, dated in the calendar's zone) after syncing the index, off the calling
    /// thread. Reads only and never throws: any failure gives the <see cref="DecisionsViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<DecisionsView> Decisions(string tag) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return DecisionsView.Build(index, notes, tag, calendar.Zone);
            }
        }
        catch (Exception)
        {
            return new DecisionsView(DecisionsViewKind.IndexUnreadable, string.Empty, []);
        }
    });

    /// <summary>
    /// The calendar link as the settings page shows it (<see cref="CalendarLinkView.Show"/>): the masked link and
    /// the time of the cached copy, <c>NotSet</c> or <c>Unreadable</c>, off the calling thread. The link itself never
    /// leaves Core. Reads only (no download) and never throws: any failure gives the <c>Unreadable</c> view.
    /// </summary>
    public Task<CalendarLinkView> CalendarLinkSetting() => Task.Run(() =>
    {
        try
        {
            if (calendarLink is null || feed is null)
            {
                return new CalendarLinkView(CalendarLinkViewKind.NotSet);
            }

            return CalendarLinkView.Show(calendarLink, feed);
        }
        catch (Exception)
        {
            return new CalendarLinkView(CalendarLinkViewKind.Unreadable);
        }
    });

    /// <summary>
    /// The key that opens sensitive notes on this machine (<see cref="IKeyStore.MachineKeyDetails"/>), off the
    /// calling thread. Public data only: no private-key call, and no key is created or stored. Never throws: a
    /// failing key store gives <see cref="SensitiveKeyKind.Unreadable"/>.
    /// </summary>
    public Task<SensitiveKeyView> SensitiveKey() => Task.Run(() =>
    {
        try
        {
            if (!KeyStore.Supported)
            {
                return new SensitiveKeyView(SensitiveKeyKind.Unsupported);
            }

            KeyDetails? details = KeyStore.MachineKeyDetails();
            return details is null
                ? new SensitiveKeyView(SensitiveKeyKind.NotSetUp)
                : new SensitiveKeyView(SensitiveKeyKind.Shown, details);
        }
        catch (Exception)
        {
            return new SensitiveKeyView(SensitiveKeyKind.Unreadable);
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

    /// <summary>
    /// The note and heading a <c>[[link]]</c> target names (<see cref="NameLookup.ResolveLink"/>, trimmed: the whole
    /// target first, then the part before the first <c>#</c>) against the notes in the latest snapshot, sensitive ones
    /// included (by name only). <c>null</c> if no note matches.
    /// </summary>
    public NoteLinkTarget? ResolveLink(string target)
    {
        DesktopSnapshot? current = snapshot;
        if (current is null)
        {
            return null;
        }

        return new NameLookup(current.Notes.Concat(current.Sensitive).Select(n => n.Name)).ResolveLink(target.Trim());
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

    /// <summary>
    /// The view of <paramref name="day"/> (appointments touching it, todos due exactly that day, their linked notes),
    /// built like the snapshot's off the calling thread; nothing is written. Never throws: a failure gives a view
    /// with no appointments and unreadable todos.
    /// </summary>
    public Task<CurrentView> DayView(DateOnly day) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return CurrentView.Build(todos, index, notes, calendar, feed, day, dueOnDayOnly: true);
            }
        }
        catch (Exception)
        {
            return new CurrentView(
                day, calendar.Zone, [], calendar.Folder, [], CalendarFeedStatus.NotConfigured, null, null, true, [], [], false);
        }
    });

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
            IReadOnlyList<NoteTodo> noteTodos;
            bool noteTodosUnreadable = false;
            lock (indexGate)
            {
                current = CurrentView.Build(todos, index, notes, calendar, feed, day);
                try
                {
                    // Syncs first: the current view skips the sync when nothing needs the index.
                    noteTodos = NoteTodos.Load(notes, index);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    noteTodos = [];
                    noteTodosUnreadable = true;
                }
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
                KeyStore.Supported, RecoveryReady(), noteTodos, noteTodosUnreadable);
        }
        catch (Exception)
        {
            return DesktopSnapshot.ForFailure(folder, day);
        }
    }
}
