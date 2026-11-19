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

    /// <summary>How long an Outlook read (the appointments of one day) is kept, by the state's UTC clock.</summary>
    public static readonly TimeSpan OutlookCacheAge = TimeSpan.FromMinutes(5);

    /// <summary>The default <see cref="OutlookTimeout"/>.</summary>
    public static readonly TimeSpan DefaultOutlookTimeout = TimeSpan.FromSeconds(10);

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
    private readonly IOutlookSource outlook;
    private readonly IPageTitleSource? pageTitles;
    private readonly string? timesFolder;
    private readonly ICommitLog commitLog;
    private readonly Func<string?>? timesCommand;
    private string? offeredItemPage;

    // Guards the Outlook cache: one call per day (appointments), each with its start time.
    private readonly object outlookGate = new();
    private Dictionary<DateOnly, OutlookCall<Appointment>> outlookDays = new();

    // The mail suggestions accepted or dismissed in this session (job 286); never written to disk.
    private readonly object mailGate = new();
    private readonly Dictionary<MailSuggestionKey, bool> hiddenMail = [];

    private Task<DesktopSnapshot>? running;
    private bool pending;
    private DateOnly? lastDay;
    private DateTime lastRefreshUtc;
    private int builds;
    private volatile DesktopSnapshot? snapshot;
    private string? offeredBriefing;
    private string? offeredStartNote;

    // The recently opened notes (job 240): read once from recent.json at start, then changed only through ChangeRecent.
    private readonly object recentGate = new();
    private IReadOnlyList<string> recent;

    // The pinned notes (job 278): read once from pinned.json at start, then changed only through ChangePinned.
    private readonly object pinnedGate = new();
    private IReadOnlyList<string> pinned;

    // Counts the Changed handlers' reloads that are still running (StateComponent), for tests to wait on.
    private readonly object reloadGate = new();
    private int reloads;
    private TaskCompletionSource? reloadsSettled;

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
        CalendarLink? calendarLink = null,
        IOutlookSource? outlook = null,
        TimeSpan? outlookTimeout = null,
        IPageTitleSource? pageTitles = null,
        bool? isMac = null,
        string? timesFolder = null,
        ICommitLog? commitLog = null,
        Func<string?>? timesCommand = null)
    {
        IsMac = isMac ?? OperatingSystem.IsMacOS();
        this.timesCommand = timesCommand;
        this.timesFolder = timesFolder;
        this.commitLog = commitLog ?? new GitCommitLog();
        this.pageTitles = pageTitles;
        this.outlook = outlook ?? new UnsupportedOutlookSource();
        OutlookTimeout = outlookTimeout ?? DefaultOutlookTimeout;
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
        recent = new RecentNotesStore(notes.Root).Read();
        pinned = new PinnedNotesStore(notes.Root).Read();
    }

    /// <summary>
    /// The full names of the pinned notes in pinned order (<see cref="PinnedNotes"/>), as read once from
    /// <c>pinned.json</c> at start and changed by the actions since. Raw names; they may name notes that are gone.
    /// </summary>
    public IReadOnlyList<string> Pinned
    {
        get
        {
            lock (pinnedGate)
            {
                return pinned;
            }
        }
    }

    /// <summary>Applies <paramref name="step"/> to <see cref="Pinned"/> in memory (nothing is written) and returns the new list.</summary>
    internal IReadOnlyList<string> ChangePinned(Func<IReadOnlyList<string>, IReadOnlyList<string>> step)
    {
        lock (pinnedGate)
        {
            pinned = step(pinned);
            return pinned;
        }
    }

    /// <summary>
    /// The full names of the notes opened most recently, newest first (<see cref="RecentNotes"/>), as read once from
    /// <c>recent.json</c> at start and changed by the actions since. Raw names; they may name notes that are gone.
    /// </summary>
    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (recentGate)
            {
                return recent;
            }
        }
    }

    /// <summary>Applies <paramref name="step"/> to <see cref="Recent"/> in memory (nothing is written) and returns the new list.</summary>
    internal IReadOnlyList<string> ChangeRecent(Func<IReadOnlyList<string>, IReadOnlyList<string>> step)
    {
        lock (recentGate)
        {
            recent = step(recent);
            return recent;
        }
    }

    /// <summary>Whether the app runs on macOS, where the screenshot hotkey works (job 260); tests inject it.</summary>
    public bool IsMac { get; }

    /// <summary>
    /// True when the Times command (<c>tm</c>, job 282) was found for the command bar; checked on each call, never
    /// throws. Without a finder (tests that pass none) it is false.
    /// </summary>
    public bool TimesCommandFound
    {
        get
        {
            try
            {
                return !string.IsNullOrEmpty(timesCommand?.Invoke());
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>How long one Outlook source call may take before it counts as timed out.</summary>
    public TimeSpan OutlookTimeout { get; }

    /// <summary>The kept versions of the notes, with the same clock as this state.</summary>
    public NoteHistory HistoryStore { get; }

    /// <summary>Raised (on a background thread) after every refresh, once <see cref="Snapshot"/> is set.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised (on the calling thread) by every <see cref="Tick"/>, whether or not it refreshes, so the sidebar's Times
    /// line (<see cref="TimesNow()"/>) keeps its time left current between refreshes (job 277).
    /// </summary>
    public event Action? TimesChanged;

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

    /// <summary>
    /// Offers today's daily note <paramref name="name"/>, made sure of at start, for <c>Main</c> to open while nothing
    /// else is shown. An in-memory value only: nothing is written. Called only by the actions' start-up call.
    /// </summary>
    public void OfferStartNote(string name) => Interlocked.Exchange(ref offeredStartNote, name);

    /// <summary>The offered start note's name once, then <c>null</c> until the next offer.</summary>
    public string? TakeStartNote() => Interlocked.Exchange(ref offeredStartNote, null);

    /// <summary>
    /// Offers the work item page asked for from the command line (job 280); <c>Main</c> takes it after the next
    /// <see cref="Changed"/> and opens it, saving or asking about unsaved changes first. A newer offer replaces it.
    /// </summary>
    public void OfferItemPage(string name) => Interlocked.Exchange(ref offeredItemPage, name);

    /// <summary>The offered work item page's name once, then <c>null</c> until the next offer.</summary>
    public string? TakeItemPage() => Interlocked.Exchange(ref offeredItemPage, null);

    /// <summary>Counts one more running reload; called before a <see cref="Changed"/> handler dispatches it.</summary>
    internal void BeginReload() => Interlocked.Increment(ref reloads);

    /// <summary>Ends one reload begun by <see cref="BeginReload"/>; the last one completes <see cref="ReloadsSettled"/>.</summary>
    internal void EndReload()
    {
        if (Interlocked.Decrement(ref reloads) != 0)
        {
            return;
        }

        TaskCompletionSource? done = null;
        lock (reloadGate)
        {
            if (Volatile.Read(ref reloads) == 0)
            {
                done = reloadsSettled;
                reloadsSettled = null;
            }
        }

        done?.TrySetResult();
    }

    /// <summary>
    /// A completed task when no reload is running; otherwise one that completes when the running count drops to 0.
    /// </summary>
    internal Task ReloadsSettled()
    {
        lock (reloadGate)
        {
            if (Volatile.Read(ref reloads) == 0)
            {
                return Task.CompletedTask;
            }

            reloadsSettled ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return reloadsSettled.Task;
        }
    }

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
        try
        {
            return TickOnce();
        }
        finally
        {
            TimesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Today's Times bookings (<see cref="Context.TimesNow.Read"/>) off the calling thread, for today and now on the
    /// state's UTC clock in <see cref="Zone"/>. Only reads the Times folder; never throws (a failure is
    /// <see cref="TimesNowKind.Unreadable"/>).
    /// </summary>
    public Task<TimesNow> TimesNow() => Task.Run(() =>
    {
        try
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc), calendar.Zone);
            return Context.TimesNow.Read(timesFolder, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
        }
        catch (Exception)
        {
            return new TimesNow(TimesNowKind.Unreadable, null, null, TimeSpan.Zero, null);
        }
    });

    private bool TickOnce()
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
    /// How many other plain notes merging <paramref name="source"/> into <paramref name="target"/> would update links in
    /// (<see cref="LinkRenamePlan.ForMerge"/>), off the calling thread. Reads only; 0 on any error.
    /// </summary>
    public Task<int> MergeLinkCount(string target, string source) => Task.Run(() =>
    {
        try
        {
            return LinkRenamePlan.ForMerge(notes, target, source).Notes.Count;
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
    /// The work item page's data (job 280, <see cref="WorkItemView.Build"/>) for <c>#<paramref name="item"/></c>, under
    /// the index lock, off the calling thread. The repository folders come from <c>settings.json</c>; an unreadable
    /// file gives no commits and <see cref="WorkItemPageView.SettingsUnreadable"/>. Reads only (git and the Times folder too)
    /// and never throws: any other failure gives an empty view with <see cref="WorkItemView.IndexUnreadable"/>.
    /// </summary>
    public Task<WorkItemPageView> Item(int item) => Task.Run(() =>
    {
        bool settingsUnreadable = false;
        IReadOnlyList<string> repositories = [];
        try
        {
            repositories = new SettingsStore(notes.Root).ReadRepositories();
        }
        catch (Exception)
        {
            settingsUnreadable = true;
        }

        try
        {
            lock (indexGate)
            {
                return new WorkItemPageView(
                    WorkItemView.Build(index, notes, todos, calendar, feed, timesFolder, repositories, commitLog, item),
                    settingsUnreadable);
            }
        }
        catch (Exception)
        {
            WorkItemView empty = new WorkItemView(
                item, [], [], [], [], [], new WorkItemHours([], TimeSpan.Zero, 0), [], true, false,
                WorkItemTimesKind.NotSetUp, WorkItemCommitsKind.NotSetUp, [], calendar.Zone);
            return new WorkItemPageView(empty, settingsUnreadable);
        }
    });

    /// <summary>
    /// The repository folders saved in <c>settings.json</c> (job 280), in saved order, for the settings page, off the
    /// calling thread; never throws: a broken or unreadable file gives an empty list with the flag set.
    /// </summary>
    public Task<RepositoryFoldersView> RepositoryFolders() => Task.Run(() =>
    {
        try
        {
            return new RepositoryFoldersView(new SettingsStore(notes.Root).ReadRepositories(), false);
        }
        catch (Exception)
        {
            return new RepositoryFoldersView([], true);
        }
    });

    /// <summary>
    /// The appointments of <paramref name="day"/> (<see cref="CalendarStore.Day(DateOnly, IReadOnlyList{Appointment})"/>
    /// over the calendar files and the feed's cached copy) for the sidebar, with the unreadable files and the state's
    /// clock in the calendar's zone, off the calling thread. Reads only, never downloads and never throws: any failure
    /// gives an empty view with <see cref="DayAppointmentsView.Failed"/> set.
    /// </summary>
    public Task<DayAppointmentsView> DayAppointments(DateOnly day) => Task.Run(() =>
    {
        DateTime localNow = default;
        try
        {
            localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc), calendar.Zone);
            (OutlookStatus status, IReadOnlyList<Appointment> fromOutlook) = OutlookDay(day);
            CalendarDay found = ReadDay(day, fromOutlook);
            return new DayAppointmentsView(day, found.Appointments, found.Unreadable, localNow, outlook: status);
        }
        catch (Exception)
        {
            return new DayAppointmentsView(day, [], [], localNow, failed: true);
        }
    });

    /// <summary>
    /// The notes related to the appointments of <paramref name="day"/> whose title is ordinally
    /// <paramref name="title"/> (<see cref="MeetingPrep"/> with no cap, over the title and the union of their
    /// attendees), off the calling thread. Syncs the index under its lock, then reads
    /// <see cref="TagIndex.NoteTags"/> and <see cref="NoteStore.List"/>. Reads only and never throws: an unreadable
    /// index (or note list) gives <see cref="AppointmentPageView.IndexUnreadable"/>.
    /// </summary>
    public Task<AppointmentPageView> AppointmentPage(string title, DateOnly day) => Task.Run(() =>
    {
        Appointment? first = null;
        try
        {
            List<Appointment> same = ReadDay(day, OutlookDay(day).Items).Appointments
                .Where(a => string.Equals(a.Title, title, StringComparison.Ordinal))
                .ToList();
            first = same.FirstOrDefault();
            List<string> attendees = same.SelectMany(a => a.Attendees).Distinct(StringComparer.Ordinal).ToList();
            Appointment merged = first is null
                ? new Appointment(title, null, day.ToDateTime(TimeOnly.MinValue), day.ToDateTime(TimeOnly.MinValue), false, attendees)
                : new Appointment(title, first.Location, first.Start, first.End, first.AllDay, attendees);

            IReadOnlyList<(string Note, string Tag)> noteTags;
            IReadOnlyList<NoteInfo> listed;
            IReadOnlyList<NoteTopic> topics;
            lock (indexGate)
            {
                index.Sync(notes);
                noteTags = index.NoteTags();
                listed = notes.List();
                topics = attendees.Count == 0 ? [] : MeetingTopics.For(index.Topics(), index.Aliases(), attendees);
            }

            return new AppointmentPageView(
                title, day, MeetingPrep.Related(merged, listed, noteTags, null), first, false, topics);
        }
        catch (Exception)
        {
            // TagIndexException for a bad context.db; any other failure reads the same, with no exception text shown.
            return new AppointmentPageView(title, day, [], first, true);
        }
    });

    /// <summary>
    /// The open topics for the next 1:1 with <paramref name="names"/> (<see cref="MeetingTopics.For"/>) after syncing
    /// the index under its lock, on the calling thread. Reads only and never throws: none when the index can't be read.
    /// </summary>
    internal IReadOnlyList<NoteTopic> TopicsFor(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return [];
        }

        try
        {
            lock (indexGate)
            {
                index.Sync(notes);
                return MeetingTopics.For(index.Topics(), index.Aliases(), names);
            }
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Every review date of the plain notes (<see cref="TagIndex.Reviews"/>, unsorted) after syncing the index under its
    /// lock, on the calling thread, for the morning briefing (job 256). None without a notes folder; <c>null</c> when the
    /// index can't be read. Never throws.
    /// </summary>
    internal IReadOnlyList<NoteReview>? ReviewsForBriefing()
    {
        try
        {
            lock (indexGate)
            {
                if (!Directory.Exists(notes.Root))
                {
                    return [];
                }

                index.Sync(notes);
                return index.Reviews();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The day's appointments from the calendar files, the feed's cached copy and Outlook's; never downloads.
    private CalendarDay ReadDay(DateOnly day, IReadOnlyList<Appointment> fromOutlook)
    {
        string? cached = feed?.ReadCache();
        IReadOnlyList<Appointment> fromFeed = cached is null
            ? []
            : CurrentView.FeedEvents(new CalendarFeedResult(CalendarFeedStatus.Stale, cached), calendar.Zone, day);
        return calendar.Day(day, [.. fromFeed, .. fromOutlook]);
    }

    /// <summary>
    /// Forgets every cached Outlook read, so the next read asks the source again. Called when the Outlook setting
    /// changes.
    /// </summary>
    public void ForgetOutlook()
    {
        lock (outlookGate)
        {
            outlookDays = new Dictionary<DateOnly, OutlookCall<Appointment>>();
        }
    }

    /// <summary>
    /// Whether Outlook can be read here (<see cref="IOutlookSource.Availability"/>) and whether the setting is on,
    /// off the calling thread. Never throws: an unreadable <c>settings.json</c> reads as off.
    /// </summary>
    public Task<OutlookSettingView> OutlookSetting() => Task.Run(() =>
    {
        OutlookAvailability availability = OutlookAvailability.Unsupported;
        try
        {
            availability = outlook.Availability();
            SettingsStore settings = new SettingsStore(notes.Root);
            return new OutlookSettingView(availability, settings.ReadOutlook(), false)
            {
                MailDigestEnabled = settings.ReadMailDigest(),
            };
        }
        catch (Exception)
        {
            return new OutlookSettingView(availability, false, true);
        }
    });

    /// <summary>
    /// The watched screenshot folder (job 260) and whether it exists, off the calling thread. Never creates the folder
    /// and never throws: an unreadable <c>settings.json</c> or <c>screenshots</c> value is
    /// <see cref="ScreenshotFolderKind.Unreadable"/>.
    /// </summary>
    public Task<ScreenshotFolderView> ScreenshotFolderSetting() => Task.Run(() =>
    {
        try
        {
            string? folder = new SettingsStore(notes.Root).ReadScreenshotFolder();
            return folder is null
                ? new ScreenshotFolderView(ScreenshotFolderKind.NotSet, null, false)
                : new ScreenshotFolderView(ScreenshotFolderKind.Set, folder, Directory.Exists(folder));
        }
        catch (Exception)
        {
            return new ScreenshotFolderView(ScreenshotFolderKind.Unreadable, null, false);
        }
    });

    /// <summary>
    /// Whether a pasted link's page may be downloaded for its title (<c>"paste":{"fetchTitles":true}</c>), off the
    /// calling thread. Never throws: a missing or unreadable <c>settings.json</c> reads as off.
    /// </summary>
    public Task<bool> FetchTitlesSetting() => Task.Run(FetchTitlesOn);

    /// <summary>
    /// The page title for a link pasted into a plain note, or <c>null</c>. Asks the page-title source (once) only when
    /// the setting is on; with it off (or unreadable), without a source or once <paramref name="token"/> is cancelled,
    /// nothing is requested. Never throws.
    /// </summary>
    public async Task<string?> PageTitle(string url, CancellationToken token)
    {
        try
        {
            if (pageTitles is null || !await FetchTitlesSetting().ConfigureAwait(false) || token.IsCancellationRequested)
            {
                return null;
            }

            return await pageTitles.FetchAsync(url, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool FetchTitlesOn()
    {
        try
        {
            return new SettingsStore(notes.Root).ReadFetchTitles();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether Outlook can be read here; reads no calendar data and never throws.</summary>
    public OutlookAvailability ReadOutlookAvailability()
    {
        try
        {
            return outlook.Availability();
        }
        catch (Exception)
        {
            return OutlookAvailability.Unsupported;
        }
    }

    /// <summary>The days of Inbox mail the Mail digest reads.</summary>
    public const int MailDigestDays = 7;

    /// <summary>The most Inbox mails the Mail digest reads.</summary>
    public const int MailDigestMax = 200;

    /// <summary>
    /// Whether the Mail digest may read Outlook (job 286): its own setting and the Outlook setting are on and Outlook
    /// can be read here. Off the calling thread; never throws (an unreadable <c>settings.json</c> is off).
    /// </summary>
    public Task<bool> MailDigestOn() => Task.Run(MailOn);

    /// <summary>
    /// Reads the Inbox mail of the last <see cref="MailDigestDays"/> days (at most <see cref="MailDigestMax"/>),
    /// bounded by <see cref="OutlookTimeout"/>, and returns its suggestions without those accepted or dismissed in
    /// this session. With the digest off, nothing is asked. Off the calling thread, never cached, never throws.
    /// </summary>
    public Task<MailDigestView> MailDigest() => Task.Run(() =>
    {
        try
        {
            if (!MailOn())
            {
                return new MailDigestView(OutlookStatus.Off, []);
            }

            DateTime nowUtc = DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc);
            DateTime sinceUtc = nowUtc.AddDays(-MailDigestDays);
            (OutlookStatus status, IReadOnlyList<MailMessage> mails) =
                Finish(Start(nowUtc, () => outlook.RecentMail(sinceUtc, MailDigestMax)));
            if (status != OutlookStatus.Ok)
            {
                return new MailDigestView(status, []);
            }

            return new MailDigestView(OutlookStatus.Ok, Context.MailDigest.Suggest(mails, today(), IsMailHidden));
        }
        catch (Exception)
        {
            return new MailDigestView(OutlookStatus.Failed, []);
        }
    });

    /// <summary>Leaves <paramref name="suggestion"/> out of every later <see cref="MailDigest"/> in this session.</summary>
    public void HideMailSuggestion(MailSuggestion suggestion)
    {
        lock (mailGate)
        {
            hiddenMail[MailSuggestionKey.Of(suggestion)] = true;
        }
    }

    private bool IsMailHidden(MailSuggestion suggestion)
    {
        lock (mailGate)
        {
            return hiddenMail.ContainsKey(MailSuggestionKey.Of(suggestion));
        }
    }

    private bool MailOn()
    {
        try
        {
            return new SettingsStore(notes.Root).ReadMailDigest() && OutlookOn();
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Outlook is read only when the setting is on and the source can read it; an unreadable settings.json is off.
    private bool OutlookOn()
    {
        try
        {
            return new SettingsStore(notes.Root).ReadOutlook() && outlook.Availability() == OutlookAvailability.Available;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Outlook's appointments of the day, through the cache; Off and nothing asked when Outlook is off. Never throws.
    private (OutlookStatus Status, IReadOnlyList<Appointment> Items) OutlookDay(DateOnly day)
    {
        try
        {
            if (!OutlookOn())
            {
                return (OutlookStatus.Off, []);
            }

            DateTime nowUtc = DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc);
            TimeZoneInfo zone = calendar.Zone;
            OutlookCall<Appointment> call;
            lock (outlookGate)
            {
                if (!outlookDays.TryGetValue(day, out OutlookCall<Appointment>? cached) || !cached.Usable(nowUtc))
                {
                    cached = Start(nowUtc, () => outlook.Appointments(day, zone));
                    outlookDays[day] = cached;
                }

                call = cached;
            }

            return Finish(call);
        }
        catch (Exception)
        {
            return (OutlookStatus.Failed, []);
        }
    }

    // One source call on its own thread (it may block on Outlook or its security prompt for long, so it neither waits
    // for nor holds a pool thread); a call that throws reads as Failed.
    private static OutlookCall<T> Start<T>(DateTime startedUtc, Func<OutlookRead<T>> read) =>
        new(startedUtc, Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return read();
                }
                catch (Exception)
                {
                    return new OutlookRead<T>(OutlookReadKind.Failed);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default));

    // Waits for the call at most OutlookTimeout; the call itself keeps running and stays cached while it does.
    private (OutlookStatus Status, IReadOnlyList<T> Items) Finish<T>(OutlookCall<T> call)
    {
        if (!call.Read.Wait(OutlookTimeout))
        {
            return (OutlookStatus.TimedOut, []);
        }

        OutlookRead<T> read = call.Read.Result;
        return read.Kind switch
        {
            OutlookReadKind.Ok => (OutlookStatus.Ok, read.Items),
            OutlookReadKind.NotRunning => (OutlookStatus.NotRunning, []),
            OutlookReadKind.Denied => (OutlookStatus.Denied, []),
            _ => (OutlookStatus.Failed, []),
        };
    }

    // One suggestion as the session remembers it: its kind and text and the mail it came from.
    private sealed record MailSuggestionKey(
        MailSuggestionKind Kind, string Text, string Sender, string Subject, DateTime Received)
    {
        public static MailSuggestionKey Of(MailSuggestion s) =>
            new(s.Kind, s.Text, s.Source.Sender, s.Source.Subject, s.Source.Received);
    }

    // One Outlook source call and when it started. A call still running is shared; a finished one is kept for
    // OutlookCacheAge.
    private sealed class OutlookCall<T>
    {
        public OutlookCall(DateTime startedUtc, Task<OutlookRead<T>> read)
        {
            StartedUtc = startedUtc;
            Read = read;
        }

        public DateTime StartedUtc { get; }

        public Task<OutlookRead<T>> Read { get; }

        public bool Usable(DateTime nowUtc)
        {
            if (!Read.IsCompleted)
            {
                return true;
            }

            TimeSpan age = nowUtc - StartedUtc;
            return age >= TimeSpan.Zero && age < OutlookCacheAge;
        }
    }

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
    /// The plain notes that match the tag filter <paramref name="text"/> (<see cref="TagFilterView.Build"/>) after
    /// syncing the index, off the calling thread. Reads only and never throws: any failure gives the
    /// <see cref="TagFilterViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<TagFilterView> TagFilter(string text) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return TagFilterView.Build(index, notes, text);
            }
        }
        catch (Exception)
        {
            return new TagFilterView(TagFilterViewKind.IndexUnreadable, string.Empty, []);
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
    /// The similar plain notes (job 268, <see cref="SimilarNotesView.Build"/> after syncing the index), off the calling
    /// thread under the index lock. Reads only and never throws: any failure is
    /// <see cref="SimilarNotesKind.IndexUnreadable"/>.
    /// </summary>
    public Task<SimilarNotesView> Similar() => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return SimilarNotesView.Build(index, notes);
            }
        }
        catch (Exception)
        {
            return new SimilarNotesView(SimilarNotesKind.IndexUnreadable, []);
        }
    });

    /// <summary>
    /// The board of <paramref name="tag"/> (job 242): its setting from <c>settings.json</c>
    /// (<see cref="SettingsStore.ReadBoards"/>), then <see cref="BoardView.Build"/> after syncing the index, off the
    /// calling thread. Reads only and never throws: a bad tag is <see cref="BoardPageKind.InvalidTag"/> without reading,
    /// a broken <c>settings.json</c> <see cref="BoardPageKind.SettingsUnreadable"/>, any other failure
    /// <see cref="BoardPageKind.Unreadable"/>.
    /// </summary>
    public Task<BoardPage> Board(string tag) => Task.Run(() =>
    {
        if (!BoardView.TryNormalizeTag(tag, out string normalized))
        {
            return new BoardPage(BoardPageKind.InvalidTag, string.Empty, null);
        }

        try
        {
            IReadOnlyList<BoardSetting> boards;
            try
            {
                boards = new SettingsStore(notes.Root).ReadBoards();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new BoardPage(BoardPageKind.SettingsUnreadable, normalized, null);
            }

            BoardSetting? setting = boards.FirstOrDefault(b => string.Equals(b.Tag, normalized, StringComparison.Ordinal));
            if (setting is null)
            {
                return new BoardPage(BoardPageKind.NoBoard, normalized, null);
            }

            lock (indexGate)
            {
                BoardView view = BoardView.Build(notes, index, setting);
                return new BoardPage(view.Unreadable ? BoardPageKind.Unreadable : BoardPageKind.Shown, normalized, view);
            }
        }
        catch (Exception)
        {
            return new BoardPage(BoardPageKind.Unreadable, normalized, null);
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

    /// <summary>
    /// Raises <see cref="Changed"/> once per listener, swallowing a listener's exception. Also used without a refresh
    /// (the snapshot stays as it is) after <see cref="Recent"/> changed.
    /// </summary>
    internal void RaiseChanged()
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

            // Outlook's appointments of the day go into the current view with the feed's, outside the index lock.
            IReadOnlyList<Appointment> fromOutlook = OutlookDay(day).Items;

            CurrentView current;
            IReadOnlyList<NoteTodo> noteTodos;
            bool noteTodosUnreadable = false;
            IReadOnlyList<NoteReview> reviews = [];
            bool reviewsUnreadable = false;
            lock (indexGate)
            {
                current = CurrentView.Build(todos, index, notes, calendar, feed, day, extra: fromOutlook);
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

                // The review dates come from the same synced index (job 256); a failed sync leaves them unread too.
                if (noteTodosUnreadable)
                {
                    reviewsUnreadable = true;
                }
                else if (Directory.Exists(notes.Root))
                {
                    try
                    {
                        reviews = NoteReviews.Due(index.Reviews(), day);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        reviewsUnreadable = true;
                    }
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

            IReadOnlyList<SavedSearch> savedSearches;
            bool savedSearchesUnreadable = false;
            try
            {
                savedSearches = new SettingsStore(notes.Root).ReadSavedSearches();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                savedSearches = [];
                savedSearchesUnreadable = true;
            }

            return new DesktopSnapshot(
                false, day, current, todoList, todosUnreadable, noteList, notesUnreadable, folder, sensitive,
                KeyStore.Supported, RecoveryReady(), noteTodos, noteTodosUnreadable, savedSearches,
                savedSearchesUnreadable, reviews, reviewsUnreadable);
        }
        catch (Exception)
        {
            return DesktopSnapshot.ForFailure(folder, day);
        }
    }
}
