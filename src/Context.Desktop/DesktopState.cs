using System.Globalization;
using System.Text;

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

    /// <summary>How many days, from today, <see cref="UpcomingAppointments"/> covers (job 287).</summary>
    public const int UpcomingDays = 14;

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
    private ActionResult? offeredConversion;

    // Guards the Outlook cache: one call per day (appointments), each with its start time.
    private readonly object outlookGate = new();
    private readonly object journalDayGate = new();
    private OutlookCall<Appointment>? lastJournalDay;
    private int outlookOptOuts;
    private (DateOnly Day, OutlookCall<DateOnly> Call)? outlookHistory;
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

    // 1 while the last numbering found no decision numbers left (job 343).
    private int decisionNumbersRanOut;

    // Job 349: the folder's decision id, loaded once; 1 while it can't be read.
    private readonly object decisionFolderGate = new();
    private DecisionFolder? decisionFolder;
    private int decisionFolderUnreadable;
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
        Func<string?>? timesCommand = null,
        Pro? pro = null,
        LicenceActivationFile? activationFile = null)
    {
        Pro = pro ?? new Pro(new UnsupportedLicenceStore(), LicencePublicKeys.Embedded(), LicenceRevocations.Embedded(), null);
        ActivationFile = activationFile;
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
        // 338: stored person pages are dropped in memory only; the file changes only with the next normal write.
        recent = RecentNotes.WithoutPeople(new RecentNotesStore(notes.Root).Read());
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

    internal TaskScheduler OutlookScheduler { get; set; } = TaskScheduler.Default;

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

    /// <summary>
    /// Offers the result of the start-up todo marker conversion (job 329) for <c>Main</c> to show in its status line.
    /// An in-memory value only. Called only by the todo marker conversion of the actions (job 329).
    /// </summary>
    public void OfferConversion(ActionResult result) => Interlocked.Exchange(ref offeredConversion, result);

    /// <summary>The offered conversion result once, then <c>null</c> until the next offer.</summary>
    public ActionResult? TakeConversion() => Interlocked.Exchange(ref offeredConversion, null);

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
    /// Whether the meeting note of <paramref name="appointment"/> already exists (job 395, the appointment page's create
    /// link: <see cref="MeetingNote.Exists"/>), off the calling thread. Reads only and never throws: false on a failure,
    /// so the link then runs the <c>/meeting</c> steps, which report it.
    /// </summary>
    public Task<bool> MeetingNoteExists(Appointment appointment) => Task.Run(() =>
    {
        try
        {
            return MeetingNote.Exists(notes, appointment);
        }
        catch (Exception)
        {
            return false;
        }
    });

    /// <summary>
    /// Whether Times is there (job 383): the resolved Times folder is set and is a directory. Checked off the calling
    /// thread; never throws, never writes.
    /// </summary>
    public Task<bool> HasTimes() => Task.Run(() =>
    {
        try
        {
            return timesFolder is not null && Directory.Exists(timesFolder);
        }
        catch (Exception)
        {
            return false;
        }
    });

    /// <summary>Times' project quicklist (<see cref="Context.TimesProjects.Read"/>, job 383) off the calling thread; read only.</summary>
    public Task<IReadOnlyList<(int WorkItem, string Title)>> TimesProjects() =>
        Task.Run(() => Context.TimesProjects.Read(timesFolder));

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
    /// calling thread. A stored name must still match the plain-note listing exactly.
    /// <c>null</c> when there is no such note, or it can't be read.
    /// </summary>
    public Task<EditableNote?> ReadForEdit(string name, bool storedName = false) => Task.Run(() =>
    {
        try
        {
            NoteReference target = storedName ? NoteReference.Stored(name) : (NoteReference)name;
            return notes.TryFind(target, out NoteInfo? found) && found is not null ? notes.ReadForEdit(found) : null;
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
    public Task<int> LinkUpdateCount(NoteReference name, string newTitle) => Task.Run(() =>
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
    /// accepts as an image, with its MIME type and base64 bytes, then (job 384) each source of
    /// <see cref="NoteAttachments.DrawingSources"/> that resolves to an Excalidraw drawing and renders through
    /// <see cref="ExcalidrawSvg.Render"/>, as <c>image/svg+xml</c> with the base64 of the SVG. Empty on any error. Reads only.
    /// </summary>
    public Task<IReadOnlyList<InlineImage>> InlineImages(NoteReference name, string text) => Task.Run(() =>
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

            foreach (string src in NoteAttachments.DrawingSources(text))
            {
                ResolvedAttachment? resolved = attachments.TryResolve(name, src);
                string? svg = resolved is { Type: AttachmentType.Excalidraw } ? ExcalidrawSvg.Render(resolved.Bytes) : null;
                if (svg is not null)
                {
                    images.Insert(images.Count, new InlineImage(src, ExcalidrawSvg.Mime, Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))));
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
    public Task<EditableNote?> ReadSensitive(NoteReference name) => Task.Run(() =>
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
    public Task<HistoryView> History(NoteReference name) => Task.Run(() =>
    {
        try
        {
            if (notes.TryFind(name, out NoteInfo? plain) && plain is not null)
            {
                string text = NoteStore.ReadSelectedForEdit(plain, name.Resolved(plain)).Text;
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
    public Task<string?> ReadVersion(NoteReference name, string id) => Task.Run(() => ReadVersionText(name, id));

    /// <summary>
    /// The text of version <paramref name="id"/> of the note <paramref name="name"/> as the editor would hold it
    /// (<c>\n</c> line endings, no byte order mark). A plain version is decoded like a note; a sealed one is opened
    /// with <see cref="SealedNote.Open"/> under the title in its file name and one <see cref="IKeyStore.Agree"/>, only
    /// while the session holds the note open. <c>null</c> when there is no such version or it can't be read.
    /// </summary>
    public string? ReadVersionText(NoteReference name, string id)
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
    /// The <c>affiliation::</c> and <c>role::</c> values of the person pages (<see cref="Context.PeopleAttributes.Build"/>)
    /// after syncing the index, off the calling thread. Never throws: any failure gives the
    /// <see cref="PeopleAttributesKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<PeopleAttributes> PeopleAttributes() => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return Context.PeopleAttributes.Build(index, notes);
            }
        }
        catch (Exception)
        {
            return Context.PeopleAttributes.Unreadable;
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
    /// The appointments a <c>MeetingTodosPanel</c> works on (job 287), off the calling thread: those of
    /// <paramref name="day"/> (files, the feed's cached copy and Outlook when on, as <see cref="AppointmentPage"/>) whose
    /// title is ordinally <paramref name="title"/>, or, with <paramref name="title"/> <c>null</c>, those whose meeting
    /// note is <paramref name="noteName"/> (<see cref="MeetingNote.IsNoteOf"/>). In the day's order, so the first is the
    /// page's <see cref="AppointmentPageView.First"/>. Reads only, never downloads and never throws: none on a failure.
    /// </summary>
    public Task<IReadOnlyList<Appointment>> MeetingAppointments(DateOnly day, string? title, string? noteName) =>
        Task.Run<IReadOnlyList<Appointment>>(() =>
        {
            try
            {
                return ReadDay(day, OutlookDay(day).Items).Appointments
                    .Where(a => title is not null
                        ? string.Equals(a.Title, title, StringComparison.Ordinal)
                        : noteName is not null && MeetingNote.IsNoteOf(a, noteName))
                    .ToList();
            }
            catch (Exception)
            {
                return [];
            }
        });

    /// <summary>
    /// The day of the meeting note <paramref name="name"/> (<c>Meetings/&lt;part&gt;/yyyy-MM-dd</c>, invariant), or
    /// <c>null</c> when it isn't named like one.
    /// </summary>
    internal static DateOnly? MeetingNoteDay(string? name)
    {
        if (name is null || !name.StartsWith(MeetingNote.FolderName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] parts = name.Split('/');
        return parts.Length == 3
            && DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;
    }

    /// <summary>
    /// The open todos of <see cref="Snapshot"/> tied to one of <paramref name="appointments"/>
    /// (<see cref="MeetingLink.Matches"/>), in <see cref="TodoStore.List"/> order (job 287).
    /// </summary>
    internal IReadOnlyList<TodoItem> MeetingTodos(IReadOnlyList<Appointment> appointments) =>
        (snapshot?.Todos ?? [])
            .Where(t => t.Done is null && t.Meeting is { } link && appointments.Any(link.Matches))
            .ToList();

    /// <summary>
    /// The next occurrence of the meeting <paramref name="todo"/> is tied to (<see cref="MeetingLink.NextOccurrence"/>
    /// over the calendar files, the feed's cached copy and Outlook when on, with the state's clock in the calendar's
    /// zone), off the calling thread. Never downloads and never throws: <c>null</c> when there is none, the todo is
    /// untied, or reading fails.
    /// </summary>
    public Task<Appointment?> NextMeeting(TodoItem todo) => Task.Run(() =>
    {
        try
        {
            if (todo.Meeting is not { } link)
            {
                return null;
            }

            DateTime localNow = LocalNow();
            string? cached = feed?.ReadCache();
            return MeetingLink.NextOccurrence(link, d => ReadDay(d, OutlookDay(d).Items, cached).Appointments, localNow);
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>
    /// The appointments with a Uid of <paramref name="today"/> through 13 days on (the same sources as
    /// <see cref="NextMeeting"/>), in day order, off the calling thread (job 287). Never throws: none on a failure.
    /// </summary>
    public Task<IReadOnlyList<Appointment>> UpcomingAppointments(DateOnly today) => Task.Run<IReadOnlyList<Appointment>>(() =>
    {
        try
        {
            List<Appointment> found = [];
            string? cached = feed?.ReadCache();
            for (int offset = 0; offset < UpcomingDays; offset++)
            {
                DateOnly day = today.AddDays(offset);
                found.AddRange(ReadDay(day, OutlookDay(day).Items, cached).Appointments
                    .Where(a => MeetingLink.For(a) is not null && DateOnly.FromDateTime(a.Start) == day));
            }

            return found;
        }
        catch (Exception)
        {
            return [];
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

    // The state's clock as the calendar zone's wall clock.
    private DateTime LocalNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc), calendar.Zone);

    internal Action<DateOnly>? BeforeJournalDay { get; set; }

    public Task<(Journal Journal, JournalOutlook Outlook)> OpenJournal() => Task.Run(() => OpenJournal(null));

    internal JournalFiles BindJournalFiles() => new(notes, calendar, feed);

    internal (Journal Journal, JournalOutlook Outlook) OpenJournal(JournalFiles? boundFiles)
    {
        int optOutsAtOpen = Volatile.Read(ref outlookOptOuts);
        JournalFiles files = boundFiles ?? BindJournalFiles();
        // An explicit opt-out also applies to an open journal whose original store has since been moved.
        bool Consent() => files.ReadOutlook() && optOutsAtOpen == Volatile.Read(ref outlookOptOuts);
        try
        {
            DateOnly day = today();
            DateOnly? localStart = calendar.Oldest(day, files.ReadCache(), files.ReadCalendars());
            (OutlookStatus status, IReadOnlyList<DateOnly> dates) = OutlookOn(Consent) ? JournalHistory(day, Consent) : (OutlookStatus.Off, []);
            JournalOutlook journalOutlook = new(status, dates.Count > 0 ? dates[0] : null, date => JournalOutlookDay(date, Consent));
            return (new Journal(files.ListNotes, files.OpenNote, day, localStart, date =>
            {
                BeforeJournalDay?.Invoke(date);
                return ReadDay(date, journalOutlook.Day(date), files.ReadCache(), files.ReadCalendars()).Appointments;
            }, () => journalOutlook.Oldest, files), journalOutlook);
        }
        catch
        {
            files.Dispose();
            throw;
        }
    }

    private (OutlookStatus Status, IReadOnlyList<DateOnly> Dates) JournalHistory(DateOnly day, Func<bool> consent)
    {
        OutlookCall<DateOnly> call;
        lock (outlookGate)
        {
            if (outlookHistory is { } pending && !pending.Call.Read.IsCompleted)
            {
                // An earlier day's unfinished search cannot answer a new date or start another Outlook thread.
                if (pending.Day != day)
                {
                    return (OutlookStatus.TimedOut, []);
                }
                call = pending.Call;
            }
            else
            {
                call = Start(utcNow(), () => outlook.Oldest(day, calendar.Zone), consent);
                outlookHistory = (day, call);
            }
        }
        return Finish(call, consent);
    }

    // A later journal, even on a different date, shares the outstanding-call limit until Outlook returns.
    private (OutlookStatus Status, IReadOnlyList<Appointment> Items) JournalOutlookDay(DateOnly day, Func<bool> consent)
    {
        lock (journalDayGate)
        {
            return OutlookDay(day, consent);
        }
    }

    // The day's appointments from the calendar files, the feed's cached copy and Outlook's; never downloads.
    private CalendarDay ReadDay(DateOnly day, IReadOnlyList<Appointment> fromOutlook) =>
        ReadDay(day, fromOutlook, feed?.ReadCache());

    // ReadDay with the feed's cached text already read (null: no cache).
    private CalendarDay ReadDay(DateOnly day, IReadOnlyList<Appointment> fromOutlook, string? cached,
        IEnumerable<(string Name, string? Text)>? sources = null)
    {
        IReadOnlyList<Appointment> fromFeed = cached is null
            ? []
            : CurrentView.FeedEvents(new CalendarFeedResult(CalendarFeedStatus.Stale, cached), calendar.Zone, day);
        return calendar.Day(day, [.. fromFeed, .. fromOutlook], sources);
    }

    /// <summary>
    /// Forgets every cached Outlook read, so the next read asks the source again. Called when the Outlook setting
    /// changes.
    /// </summary>
    public void ForgetOutlook(bool disabled)
    {
        lock (outlookGate)
        {
            if (disabled)
            {
                Interlocked.Increment(ref outlookOptOuts);
            }
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
    /// The saved theme (job 341, <c>"appearance":{"theme":…}</c>), off the calling thread. Never throws: a missing,
    /// broken or unreadable <c>settings.json</c> gives <see cref="AppTheme.System"/>.
    /// </summary>
    public Task<AppTheme> ThemeSetting() => Task.Run(ThemeNow);

    private AppTheme ThemeNow()
    {
        try
        {
            return new SettingsStore(notes.Root).ReadTheme();
        }
        catch (Exception)
        {
            return AppTheme.System;
        }
    }

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
    private bool OutlookOn(Func<bool>? journalConsent = null)
    {
        try
        {
            bool enabled = journalConsent?.Invoke() ?? new SettingsStore(notes.Root).ReadOutlook();
            return enabled && outlook.Availability() == OutlookAvailability.Available;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Outlook's appointments of the day, through the cache; Off and nothing asked when Outlook is off. Never throws.
    private (OutlookStatus Status, IReadOnlyList<Appointment> Items) OutlookDay(DateOnly day, Func<bool>? journalConsent = null)
    {
        try
        {
            if (!OutlookOn(journalConsent))
            {
                return (OutlookStatus.Off, []);
            }
            if (journalConsent is not null && lastJournalDay?.Read.IsCompleted == false)
            {
                return (OutlookStatus.TimedOut, []);
            }

            DateTime nowUtc = DateTime.SpecifyKind(utcNow(), DateTimeKind.Utc);
            TimeZoneInfo zone = calendar.Zone;
            OutlookCall<Appointment> call;
            lock (outlookGate)
            {
                if (!outlookDays.TryGetValue(day, out OutlookCall<Appointment>? cached) || !cached.Usable(nowUtc))
                {
                    cached = Start(nowUtc, () => outlook.Appointments(day, zone), journalConsent);
                    outlookDays[day] = cached;
                }

                call = cached;
            }

            if (journalConsent is not null)
            {
                lastJournalDay = call;
            }
            return Finish(call, journalConsent);
        }
        catch (Exception)
        {
            return (OutlookStatus.Failed, []);
        }
    }

    // One source call on its own thread (it may block on Outlook or its security prompt for long, so it neither waits
    // for nor holds a pool thread); a call that throws reads as Failed.
    private OutlookCall<T> Start<T>(DateTime startedUtc, Func<OutlookRead<T>> read, Func<bool>? journalConsent = null) =>
        new(startedUtc, Task.Factory.StartNew(
            () =>
            {
                try
                {
                    // Consent can be withdrawn after this read is queued.
                    return journalConsent is null || journalConsent() ? read() : new OutlookRead<T>(OutlookReadKind.Off);
                }
                catch (Exception)
                {
                    return new OutlookRead<T>(OutlookReadKind.Failed);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            OutlookScheduler));

    // Waits for the call at most OutlookTimeout; the call itself keeps running and stays cached while it does.
    private (OutlookStatus Status, IReadOnlyList<T> Items) Finish<T>(OutlookCall<T> call, Func<bool>? journalConsent = null)
    {
        bool completed = call.Read.Wait(OutlookTimeout);
        // A source call may have been running when the user turned Outlook off.
        if (journalConsent is not null && !journalConsent())
        {
            return (OutlookStatus.Off, []);
        }
        if (!completed)
        {
            return (OutlookStatus.TimedOut, []);
        }

        OutlookRead<T> read = call.Read.Result;
        return read.Kind switch
        {
            OutlookReadKind.Off => (OutlookStatus.Off, []),
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
            return Read.Result.Kind != OutlookReadKind.Off && age >= TimeSpan.Zero && age < OutlookCacheAge;
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
    /// and <see cref="ReviewView.DoneLogUnreadable"/> set.
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
                true,
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
    /// The done log grouped by week (<see cref="DoneLogView.Build"/>), off the calling thread. Reads only, never opens
    /// <c>context.db</c> and never throws: any failure gives an empty view with <see cref="DoneLogView.Unreadable"/>.
    /// </summary>
    public Task<DoneLogView> DoneWeeks() => Task.Run(() =>
    {
        try
        {
            return DoneLogView.Build(notes);
        }
        catch (Exception)
        {
            return new DoneLogView([], true);
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
    /// The notes whose text contains <paramref name="text"/> and the matching Times bookings (job 379), both limited to
    /// the inclusive range <paramref name="from"/>..<paramref name="to"/> (either may be <c>null</c>), through
    /// <see cref="SearchView.Build(TagIndex, NoteStore, string?, string, DateOnly?, DateOnly?, TimeZoneInfo)"/> with
    /// Times' folder and <see cref="Zone"/>, after syncing the index, off the calling thread. Reads only and never
    /// throws: any failure gives the <see cref="SearchViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<SearchView> Search(string text, DateOnly? from = null, DateOnly? to = null) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return SearchView.Build(index, notes, timesFolder, text, from, to, calendar.Zone);
            }
        }
        catch (Exception)
        {
            return new SearchView(SearchViewKind.IndexUnreadable, string.Empty, []);
        }
    });

    /// <summary>
    /// Whether the last numbering found no decision numbers left (job 343): its markers stayed as typed. Main shows it
    /// until a later numbering gets numbers again.
    /// </summary>
    public bool DecisionNumbersRanOut => Volatile.Read(ref decisionNumbersRanOut) != 0;

    /// <summary>
    /// Whether the last try to read or create <c>decision-folder.json</c> failed (job 349): nothing is numbered, and Main
    /// shows the red <see cref="DecisionFolder.UnreadableText"/> line.
    /// </summary>
    public bool DecisionFolderUnreadable => Volatile.Read(ref decisionFolderUnreadable) != 0;

    /// <summary>The random source of a new folder prefix (job 349); tests set it. Cryptographic by default.</summary>
    internal Func<int, int>? DecisionRandom { get; init; }

    /// <summary>The GUID source of a new folder id (job 349); tests set it. <see cref="Guid.NewGuid"/> by default.</summary>
    internal Func<Guid>? DecisionGuid { get; init; }

    /// <summary>
    /// The data folder's decision id (job 349, <see cref="DecisionFolder.Load"/>): read, or created on the first
    /// call. A loaded id is kept for the session; a failure is retried on the next call. <c>null</c> (and
    /// <see cref="DecisionFolderUnreadable"/>) when the file can't be read or made.
    /// </summary>
    internal DecisionFolder? LoadDecisionFolder()
    {
        lock (decisionFolderGate)
        {
            if (decisionFolder is { } loaded)
            {
                return loaded;
            }

            DecisionFolder? folder = null;
            try
            {
                folder = DecisionFolder.Load(notes.Root, DecisionRandom, DecisionGuid);
            }
            catch (Exception)
            {
                folder = null;
            }

            Volatile.Write(ref decisionFolderUnreadable, folder is null ? 1 : 0);
            decisionFolder = folder;
            return folder;
        }
    }

    /// <summary>
    /// The data folder's decision id for a read: the loaded one, else <see cref="DecisionFolder.Read"/> without creating
    /// the file (opening a note writes nothing). <c>null</c> when there is none or it can't be read.
    /// </summary>
    private DecisionFolder? PeekDecisionFolder()
    {
        lock (decisionFolderGate)
        {
            if (decisionFolder is { } loaded)
            {
                return loaded;
            }
        }

        try
        {
            return DecisionFolder.Read(notes.Root);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Forgets the loaded folder id, so the next numbering reads the file again (after the migration marked it).</summary>
    internal void ReloadDecisionFolder()
    {
        lock (decisionFolderGate)
        {
            decisionFolder = null;
        }
    }

    /// <summary>
    /// Job 349: numbers the <c>!d</c> markers of the plain note <paramref name="note"/>'s new <paramref name="text"/>
    /// with the folder's prefix through <see cref="DecisionNumbering.Number"/>, under the index lock. With no numbers left
    /// the markers stay as typed (<see cref="DecisionEdits.Unnumbered"/>, <see cref="DecisionNumbersRanOut"/>).
    /// <c>null</c> when there is nothing to tell, the folder id can't be read, or anything fails: the note is then saved
    /// as typed.
    /// </summary>
    internal DecisionEdits? NumberDecisions(string note, string text)
    {
        if (!text.Contains(DecisionParser.Marker, StringComparison.Ordinal) || LoadDecisionFolder() is not { } folder)
        {
            return null;
        }

        try
        {
            DecisionEdits? edits;
            lock (indexGate)
            {
                edits = DecisionNumbering.Number(index, notes, folder, note, text);
            }

            NoteRanOut(edits);
            return edits;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Job 349: after a write that was not a save, numbers the plain note <paramref name="name"/> in a second write
    /// (<see cref="DecisionNumbering.NumberWritten(TagIndex, NoteStore, NoteHistory, Func{DecisionFolder}, string, out DecisionEdits?)"/>;
    /// the folder id is loaded only for a note with a marker; job 351: its previous bytes kept in <see cref="HistoryStore"/>)
    /// under the index lock, keeping <see cref="DecisionNumbersRanOut"/> like a save. Never throws.
    /// </summary>
    internal void NumberWritten(string name)
    {
        try
        {
            DecisionEdits? edits;
            lock (indexGate)
            {
                DecisionNumbering.NumberWritten(index, notes, HistoryStore, LoadDecisionFolder, name, out edits);
            }

            NoteRanOut(edits);
        }
        catch (Exception)
        {
            // The write is done; the markers stay until the next save.
        }
    }

    /// <summary>
    /// Job 349: the one-time move of job 343's markers (<see cref="DecisionMigration.Run"/>) under the index lock, with
    /// the history; <c>null</c> when the folder id can't be read. <paramref name="beforeSave"/> is the test hook.
    /// </summary>
    internal DecisionMigrationReport? MigrateDecisions(Action<string>? beforeSave = null)
    {
        if (LoadDecisionFolder() is not { } folder)
        {
            return null;
        }

        try
        {
            DecisionMigrationReport result;
            lock (indexGate)
            {
                result = DecisionMigration.Run(index, notes, HistoryStore, folder, beforeSave);
            }

            ReloadDecisionFolder();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Set while a numbering found no numbers left, cleared once one gets numbers again.
    private void NoteRanOut(DecisionEdits? edits)
    {
        if (edits is { Unnumbered: > 0 })
        {
            Volatile.Write(ref decisionNumbersRanOut, 1);
        }
        else if (edits is { Set.Count: > 0 })
        {
            Volatile.Write(ref decisionNumbersRanOut, 0);
        }
    }

    /// <summary>
    /// Job 343: after the app renamed or moved <paramref name="oldName"/> to <paramref name="newName"/>, its decision
    /// numbers (and its subpages') stay with it. Never throws; on a failure the next sync gives the numbers to the
    /// renamed note anyway, as no other note holds them.
    /// </summary>
    internal void MoveDecisionOwners(string oldName, string newName)
    {
        try
        {
            lock (indexGate)
            {
                index.MoveDecisionOwners(oldName, newName);
            }
        }
        catch (Exception)
        {
            // Left to the next sync.
        }
    }

    /// <summary>
    /// Job 349: the headline (raw) of each of <paramref name="references"/> (at most <see cref="MaxDecisionReferences"/>)
    /// that a readable owner holds, keyed by the reference as given (<see cref="DecisionRef.Text"/>: <c>1234</c> for the
    /// own prefix, <c>ab12-1234</c>); unknown ones are not in the result. Off the calling thread under the index lock.
    /// With <paramref name="sync"/> the index is synced first (a plain note); a sensitive note's lookups only read the
    /// index. <c>null</c> on any failure.
    /// </summary>
    public Task<IReadOnlyDictionary<DecisionRef, string>?> DecisionTitles(IReadOnlyList<DecisionRef> references, bool sync) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                if (sync)
                {
                    index.Sync(notes);
                }

                string? own = index.OwnPrefix;
                List<DecisionRef> wanted = [.. references.Distinct().Take(MaxDecisionReferences)];
                IReadOnlyDictionary<(string Prefix, int Number), string> headlines = index.DecisionHeadlines(
                    wanted.Where(r => (r.Prefix ?? own) is not null).Select(r => (r.Prefix ?? own!, r.Number)));
                Dictionary<DecisionRef, string> titles = [];
                foreach (DecisionRef reference in wanted)
                {
                    if ((reference.Prefix ?? own) is string prefix && headlines.TryGetValue((prefix, reference.Number), out string? headline))
                    {
                        titles[reference] = headline;
                    }
                }

                return (IReadOnlyDictionary<DecisionRef, string>?)titles;
            }
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>
    /// Job 349: the note that owns the decision <paramref name="reference"/> names, and the token its marker is written
    /// with there (<c>ab12-1234</c>, the folder's prefix for a clash alias), read from the index only, off the calling
    /// thread under the index lock; <c>null</c> when none does or the index can't be read.
    /// </summary>
    public Task<(string Note, string Token)?> DecisionNote(DecisionRef reference) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                if ((reference.Prefix ?? index.OwnPrefix) is not string prefix
                    || index.DecisionByKey(prefix, reference.Number) is not { } decision)
                {
                    return ((string, string)?)null;
                }

                string written = index.DecisionFolderAliases().FirstOrDefault(a => a.Alias == prefix)?.Prefix ?? prefix;
                return (decision.Note, DecisionParser.Label(written, reference.Number));
            }
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>
    /// Job 349: the prefixes the editor draws a note's Copy reference buttons with: the folder's own prefix and, for a
    /// note whose <c>decision-folder::</c> GUID is a clashing foreign folder's, the alias its own-prefix markers show as.
    /// Off the calling thread; <c>null</c> when the folder id is missing or can't be read. Reads only: never creates it.
    /// </summary>
    public Task<(string Own, string? Alias)?> DecisionPrefixes(string text) => Task.Run(() =>
    {
        if (PeekDecisionFolder() is not { } folder)
        {
            return ((string, string?)?)null;
        }

        string? alias = null;
        if (DecisionFolder.NoteGuid(text ?? string.Empty) is Guid guid && guid != folder.Guid)
        {
            try
            {
                lock (indexGate)
                {
                    string key = guid.ToString("D");
                    alias = index.DecisionFolderAliases().FirstOrDefault(a => string.Equals(a.Guid, key, StringComparison.OrdinalIgnoreCase))?.Alias;
                }
            }
            catch (Exception)
            {
                alias = null;
            }
        }

        return (folder.Prefix, alias);
    });

    /// <summary>The most distinct references a note's lookup asks for (job 343).</summary>
    public const int MaxDecisionReferences = 500;

    /// <summary>The most rows the picker gets (job 349).</summary>
    public const int MaxDecisionPicks = 20;

    /// <summary>
    /// Job 349: the picker's rows for <paramref name="query"/> (trimmed; blank: every decision): the log's decisions
    /// (<see cref="DecisionsView.Build"/>, so only plain notes, never a sensitive one) whose headline contains it
    /// (ignoring case) or whose label starts with it, newest first, at most <see cref="MaxDecisionPicks"/>, as
    /// (label, reference, headline); raw headlines. Read only, off the calling thread; empty on any failure.
    /// </summary>
    public Task<IReadOnlyList<(string Label, string Reference, string Headline)>> DecisionPicks(string query) => Task.Run(() =>
    {
        try
        {
            string wanted = (query ?? string.Empty).Trim();
            if (wanted.StartsWith(DecisionParser.Token, StringComparison.OrdinalIgnoreCase))
            {
                wanted = wanted[DecisionParser.Token.Length..];
            }

            DecisionsView view;
            lock (indexGate)
            {
                view = DecisionsView.Build(index, notes, string.Empty, calendar.Zone);
            }

            return (IReadOnlyList<(string, string, string)>)[.. view.Decisions
                .Where(d => d.Label is not null && (wanted.Length == 0
                    || d.Headline.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || d.Label.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxDecisionPicks)
                .Select(d => (d.Label!, d.Label!, d.Headline))];
        }
        catch (Exception)
        {
            return [];
        }
    });

    /// <summary>
    /// Job 349: whether <see cref="TakeDecisionClashes"/> may have a clash to show, as the index saw at its last sync or
    /// clash read (<see cref="CheckDecisionClashes"/> at start). Reads no file and takes no lock, so <c>Main</c> asks it
    /// at start and on every state change and starts the background read only when it is true.
    /// </summary>
    public bool DecisionClashesDue => index.HasUnwarnedDecisionFolders;

    /// <summary>
    /// Job 349: reads the stored clashes once at start (<c>Program</c>, right after the migration), so
    /// <see cref="DecisionClashesDue"/> also tells a clash another process stored. Never throws.
    /// </summary>
    internal void CheckDecisionClashes()
    {
        try
        {
            lock (indexGate)
            {
                _ = index.DecisionFolderAliases();
            }
        }
        catch (Exception)
        {
            // An unreadable index shows no clash line; the next sync tries again.
        }
    }

    /// <summary>
    /// Job 349: the clash lines not shown yet: each foreign folder that uses the own prefix and has not been
    /// <c>warned</c>, as (guid, prefix, alias), each then marked warned so it shows once. Empty on any failure.
    /// </summary>
    public Task<IReadOnlyList<DecisionFolderAlias>> TakeDecisionClashes() => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                List<DecisionFolderAlias> fresh = [.. index.DecisionFolderAliases().Where(a => !a.Warned)];
                foreach (DecisionFolderAlias alias in fresh)
                {
                    index.MarkDecisionFolderWarned(alias.Guid);
                }

                return (IReadOnlyList<DecisionFolderAlias>)fresh;
            }
        }
        catch (Exception)
        {
            return [];
        }
    });

    /// <summary>
    /// The <c>#!d</c> decisions of every plain note, or of the notes with <paramref name="tag"/>
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
    /// The glossary (job 380, <see cref="GlossaryView.Build"/> after syncing the index), kept to the terms that match
    /// <paramref name="filter"/>, off the calling thread under the index lock. Reads only and never throws: any failure
    /// gives the <see cref="GlossaryViewKind.IndexUnreadable"/> view.
    /// </summary>
    public Task<GlossaryView> Glossary(string filter) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                return GlossaryView.Build(index, notes, filter);
            }
        }
        catch (Exception)
        {
            return new GlossaryView(GlossaryViewKind.IndexUnreadable, []);
        }
    });

    /// <summary>The most glossary terms the editor gets to decorate (job 380), longest first.</summary>
    public const int MaxGlossaryTerms = 500;

    /// <summary>The most meanings one term's hover lists; the rest is one <c>… n more</c> line.</summary>
    public const int MaxGlossaryMeanings = 20;

    // One line per meaning, "<meaning> (<note title>)", at most MaxGlossaryMeanings, then "… n more".
    private static string GlossaryTitle(GlossaryTerm term)
    {
        IEnumerable<string> lines = term.Meanings
            .Take(MaxGlossaryMeanings)
            .Select(m => $"{DesktopText.OneLine(m.Meaning)} ({DesktopText.NoteName(m.Title)})");
        if (term.Meanings.Count > MaxGlossaryMeanings)
        {
            string more = (term.Meanings.Count - MaxGlossaryMeanings).ToString(CultureInfo.InvariantCulture);
            lines = lines.Append($"{CurrentView.Outside} {more} more");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Job 380: every glossary term for the editor's hover, each through <see cref="DesktopText.OneLine"/>, with its
    /// title: one <c>&lt;meaning&gt; (&lt;note title&gt;)</c> line per meaning (at most <see cref="MaxGlossaryMeanings"/>),
    /// joined with <c>\n</c>. Longest term first (then ordinal), at most <see cref="MaxGlossaryTerms"/>. Off the calling
    /// thread under the index lock; with <paramref name="sync"/> the index is synced first (a plain note), a sensitive
    /// note's lookups only read it. <c>null</c> on any failure.
    /// </summary>
    public Task<IReadOnlyList<(string Term, string Title)>?> GlossaryTitles(bool sync) => Task.Run(() =>
    {
        try
        {
            lock (indexGate)
            {
                if (sync)
                {
                    index.Sync(notes);
                }

                GlossaryView view = GlossaryView.FromRows(index.Glossary());
                IEnumerable<(string Term, string Title)> terms = view.Terms
                    .Select(term => (Term: DesktopText.OneLine(term.Term), Title: GlossaryTitle(term)))
                    .Where(t => t.Term.Length > 0);
                return (IReadOnlyList<(string Term, string Title)>?)terms
                    .OrderByDescending(t => t.Term.Length)
                    .ThenBy(t => t.Term, StringComparer.Ordinal)
                    .Take(MaxGlossaryTerms)
                    .ToList();
            }
        }
        catch (Exception)
        {
            return null;
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

    /// <summary>The one Suite Pro gate (job 367); the actions write its store and clears its cache.</summary>
    public Pro Pro { get; }

    /// <summary><c>SuitePro/activation.json</c>, shown only (never trusted); <c>null</c> when there is none to read.</summary>
    public LicenceActivationFile? ActivationFile { get; }

    /// <summary>
    /// The Suite Pro state for Settings (job 367), off the calling thread, since reading a Keychain item Times wrote
    /// may show the macOS prompt. The gate reads only the store; the activation file is read only for the
    /// <see cref="ProViewKind.FileOnly"/> hint. Never throws.
    /// </summary>
    public Task<ProView> ProStatus() => Task.Run(() =>
    {
        try
        {
            ProStatus status = Pro.Status();
            switch (status.Kind)
            {
                case ProStatusKind.Unsupported:
                    return new ProView(ProViewKind.Unsupported);
                case ProStatusKind.Unreadable:
                    return new ProView(ProViewKind.Unreadable);
                case ProStatusKind.Invalid:
                    return new ProView(ProViewKind.Invalid, problem: status.Problem, keyStored: true);
                case ProStatusKind.Valid when status.Licence is { } licence:
                    if (Pro.BuildDate is not { } buildDate)
                    {
                        return new ProView(ProViewKind.NoBuildDate, licence, keyStored: true);
                    }

                    return status.Covered
                        ? new ProView(ProViewKind.Active, licence, keyStored: true)
                        : new ProView(ProViewKind.NotCovered, licence, buildDate: buildDate, keyStored: true);
                default:
                    return new ProView(ProViewKind.None, licensee: FileLicensee());
            }
        }
        catch (Exception)
        {
            return new ProView(ProViewKind.Unreadable);
        }
    });

    // The licensee activation.json names, or null when there is no readable file.
    private string? FileLicensee()
    {
        try
        {
            return ActivationFile?.Read()?.Licensee;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

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

    // The open tied todos whose day is today through MaxSearchDays on and whose occurrence is Missing from a source that
    // was read (job 287): an outlook: Uid from an Ok Outlook read of the day; any other from the calendar files plus the
    // feed's cached copy, when no file is unreadable and, without a cache, only for a Uid the files have. Never
    // downloads and never throws: none on a failure.
    private HashSet<Guid> MeetingHints(IReadOnlyList<TodoItem> todoList, DateOnly today)
    {
        try
        {
            string? cached = null;
            bool cacheRead = false;
            HashSet<string>? fileUids = null;
            Dictionary<DateOnly, CalendarDay> days = [];

            bool Hinted(MeetingLink link)
            {
                if (link.Uid.StartsWith(MeetingLink.OutlookUidPrefix, StringComparison.Ordinal))
                {
                    (OutlookStatus status, IReadOnlyList<Appointment> items) = OutlookDay(link.Day);
                    return status == OutlookStatus.Ok && MeetingLink.Missing(link, items);
                }

                if (!cacheRead)
                {
                    cached = feed?.ReadCache();
                    cacheRead = true;
                }

                if (!days.TryGetValue(link.Day, out CalendarDay? read))
                {
                    read = ReadDay(link.Day, [], cached);
                    days[link.Day] = read;
                }

                if (read.Unreadable.Count > 0 || !MeetingLink.Missing(link, read.Appointments))
                {
                    return false;
                }

                // Without a cached feed, a Uid the files don't have may be the feed's: it can't be checked.
                fileUids ??= cached is null
                    ? calendar.All([]).Appointments.Select(a => a.Uid).OfType<string>().ToHashSet(StringComparer.Ordinal)
                    : null;
                return cached is not null || fileUids!.Contains(link.Uid);
            }

            return todoList
                .Where(t => t.Done is null
                    && t.Meeting is { } link
                    && link.Day >= today
                    && link.Day.DayNumber <= (long)today.DayNumber + MeetingLink.MaxSearchDays
                    && Hinted(link))
                .Select(t => t.Id)
                .ToHashSet();
        }
        catch (Exception)
        {
            return [];
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
                savedSearchesUnreadable, reviews, reviewsUnreadable, MeetingHints(todoList, day));
        }
        catch (Exception)
        {
            return DesktopSnapshot.ForFailure(folder, day);
        }
    }
}
