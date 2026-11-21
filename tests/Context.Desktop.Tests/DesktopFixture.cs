using System.Net;
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Context.Tests;

/// <summary>
/// A temp data folder (deleted in <see cref="Dispose"/>) with helpers to fill it, a <see cref="DesktopState"/> over
/// it with an injected day, clocks and HTTP handler, and a bunit context that serves that state.
/// </summary>
internal sealed class DesktopFixture : IDisposable
{
    public static readonly DateOnly Today = new(2026, 10, 29);

    /// <summary>UTC+1 with no clock changes, so tests never depend on the machine's zone.</summary>
    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+01", TimeSpan.FromHours(1), "Test+01", "Test+01");

    private readonly string parent;

    // False for a fixture over another one's root (job 240), which deletes nothing.
    private readonly bool ownsFolder = true;
    private DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public DesktopFixture()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-desktop-" + Guid.NewGuid().ToString("N"));
        Root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        Bunit = new BunitContext();
        Guard = new CloseGuard(Close);
    }

    /// <summary>
    /// A second app over <paramref name="other"/>'s data folder (job 240: the window reopened over the same root),
    /// with its own state, actions and bunit context. It deletes nothing; <paramref name="other"/> still owns the folder.
    /// </summary>
    public DesktopFixture(DesktopFixture other)
    {
        parent = other.parent;
        Root = other.Root;
        ownsFolder = false;
        Bunit = new BunitContext();
        Guard = new CloseGuard(Close);
    }

    /// <summary>The data folder; it does not exist until something is written.</summary>
    public string Root { get; }

    public BunitContext Bunit { get; }

    /// <summary>The day the state sees; tests may change it.</summary>
    public DateOnly Day { get; set; } = Today;

    /// <summary>The UTC clock of the state and the feed; tests may change it.</summary>
    public DateTime UtcNow { get; set; } = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The local clock of the actions (the time stamp of a capture); tests may change it.</summary>
    public DateTime LocalNow { get; set; } = new(2026, 10, 29, 10, 5, 0, DateTimeKind.Local);

    /// <summary>The feed's handler; by default every request fails with HTTP 500.</summary>
    public FakeHttpHandler Http { get; set; } = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);

    /// <summary>When set, the feed's clock throws this instead of returning <see cref="UtcNow"/>.</summary>
    public Exception? FeedClockThrows { get; set; }

    /// <summary>The launcher the actions use for "Open in default editor"; it only records paths.</summary>
    public FakeNoteLauncher Launcher { get; } = new();

    /// <summary>The URL launcher the actions open web links with (job 354).</summary>
    public FakeUrlLauncher UrlLauncher { get; } = new();

    /// <summary>The Outlook source the state reads; in memory, Available, answering nothing. Released in <see cref="Dispose"/>.</summary>
    public FakeOutlookSource Outlook { get; } = new();

    /// <summary>The page-title source the state asks for pasted links (job 244); in memory, never the network.</summary>
    public FakePageTitles PageTitles { get; } = new();

    /// <summary>The state's Outlook timeout; <c>null</c> keeps the default. Set it before <see cref="CreateState"/>.</summary>
    public TimeSpan? OutlookTimeout { get; set; }

    /// <summary>The notifier the actions show reminders through; it only records them.</summary>
    public FakeNotifier Notifier { get; } = new();

    /// <summary>The screen-area picker of the screenshot hotkey (job 260); it returns set bytes and never touches the screen.</summary>
    public FakeScreenCapture ScreenCapture { get; } = new();

    /// <summary>Whether the state reports macOS (job 260); set it before <see cref="CreateState"/>.</summary>
    public bool IsMac { get; set; } = true;

    /// <summary>The key store the state uses: in memory, supported, without keys. Set another before <see cref="CreateState"/>.</summary>
    public FakeKeyStore Keys { get; set; } = new();

    /// <summary>The file dialog the setup dialog uses; it opens no window.</summary>
    public FakeFileDialog Dialog { get; } = new();

    /// <summary>The editor's pause after the last edit; it ends only on <see cref="FakeAutoSavePause.Elapse"/>.</summary>
    public FakeAutoSavePause AutoSave { get; } = new();

    /// <summary>The window close guard; its close action only counts (<see cref="CloseCalls"/>).</summary>
    public CloseGuard Guard { get; }

    /// <summary>How often the close guard closed the window.</summary>
    public int CloseCalls => closeCalls;

    /// <summary>
    /// Completes on the first close (off the renderer, and no render follows it), so a test awaits it with its
    /// <c>Wait</c> instead of a render-driven wait on <see cref="CloseCalls"/>.
    /// </summary>
    public Task Closed => closed.Task;

    /// <summary>
    /// When set, the close action calls it and counts the close (and completes <see cref="Closed"/>) only once the task
    /// it returns completes, on the thread pool: a test can hold the close until after the page's last render.
    /// </summary>
    public Func<Task>? CloseAfter { get; set; }

    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int closeCalls;

    private void Close()
    {
        if (CloseAfter is { } after)
        {
            _ = after().ContinueWith(_ => CountClose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }

        CountClose();
    }

    private void CountClose()
    {
        Interlocked.Increment(ref closeCalls);
        closed.TrySetResult();
    }

    /// <summary>The session of the open sensitive note, on the <see cref="UtcNow"/> clock (set by <see cref="CreateState"/>).</summary>
    public SensitiveSession? Session { get; private set; }

    /// <summary>A place for a recovery file, outside the data folder; nothing is there yet.</summary>
    public string RecoveryPath => Path.Combine(parent, "recovery.pfx");

    /// <summary>A path next to the data folder (outside it) for import sources and export targets; nothing is there yet.</summary>
    public string Outside(string name) => Path.Combine(parent, name);

    /// <summary>Times' data folder for the day's end and the sidebar's Times line, outside the data folder; nothing is there yet.</summary>
    public string TimesFolder => Path.Combine(parent, "Times");

    public DesktopState? State { get; private set; }

    /// <summary>Released once per <c>ContextEditor.setTheme</c> call (job 341), so a test awaits a call off the renderer.</summary>
    public SemaphoreSlim ThemeSet { get; } = new SemaphoreSlim(0);

    /// <summary>The theme names passed to <c>ContextEditor.setTheme</c> so far, in order.</summary>
    public IReadOnlyList<string> Themes() =>
        Bunit.JSInterop.Invocations["ContextEditor.setTheme"].Select(i => (string)i.Arguments[0]!).ToList();

    public DesktopActions? Actions { get; private set; }

    /// <summary>The note store the state and the actions share (set by <see cref="CreateState"/>).</summary>
    public NoteStore? Notes { get; private set; }

    /// <summary>The todo store the state and the actions share (set by <see cref="CreateState"/>).</summary>
    public TodoStore? Todos { get; private set; }

    public void Dispose()
    {
        Outlook.Dispose();
        Bunit.Dispose();
        Keys.Dispose();
        Licences.Dispose();

        // A panel's index read may still run on a pool thread; it writes context.db, so wait for it to finish
        // (and keep later ones out) while the folder is deleted.
        // 240: a recent.json write may still run off the renderer too; it never creates the folder, so once the folder
        // is gone a later write only fails.
        lock (Actions is null ? new object() : RecentWriteGate(Actions))
        lock (State is null ? new object() : IndexGate(State))
        {
            if (ownsFolder && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    /// <summary>The lock every index read of the state takes; a test holds it to keep a load waiting.</summary>
    public static object IndexGate(DesktopState state) =>
        typeof(DesktopState).GetField("indexGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;

    /// <summary>The lock every recent.json write of the actions takes (job 240).</summary>
    public static object RecentWriteGate(DesktopActions actions) =>
        typeof(DesktopActions).GetField("recentWriteGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actions)!;

    /// <summary>
    /// Waits (bounded at 30 s) until every recent.json write <see cref="DesktopActions.RecordOpened"/> started has
    /// finished (job 246), so a folder snapshot taken next cannot catch the write or its temp file half-way. It polls
    /// the count rather than using <c>cut.WaitForAssertion</c>: bUnit re-checks an assertion only when the component
    /// renders, and a finished write renders nothing.
    /// </summary>
    public void WaitForRecentWrites()
    {
        if (!SpinWait.SpinUntil(() => Actions!.PendingRecentWrites == 0, RecentWriteWait))
        {
            Assert.Fail($"{Actions!.PendingRecentWrites} recent.json write(s) still pending after {RecentWriteWait}.");
        }
    }

    /// <summary>
    /// A <see cref="FileSnapshot"/> of the whole data folder that is safe while panels still load (job 246 round 2).
    /// A panel's index load syncs <c>context.db</c> on a pool thread under <see cref="IndexGate"/>, and a read of
    /// the file during that sync's lock fails with "being used by another process" (macOS shares the lock between
    /// SQLite and .NET). So the snapshot is taken under the gate. With <paramref name="syncFirst"/> the index is first
    /// brought up to date under the same gate, so a load still waiting for the gate finds nothing to write and cannot
    /// change <c>context.db</c> after the snapshot. Use it for the "before" snapshot, not the "after" one.
    /// </summary>
    public SortedDictionary<string, string> SnapshotUnderIndexGate(bool syncFirst, params string[] ignorePrefixes)
    {
        lock (IndexGate(State!))
        {
            if (syncFirst)
            {
                new TagIndex(Root).Sync(Notes!);
            }

            return FileSnapshot.Take(Root, ignorePrefixes);
        }
    }

    /// <summary>
    /// Holds <see cref="RecentWriteGate"/> on a thread of its own until <see cref="HeldRecentWrites.Release"/> or
    /// dispose (job 246). A Monitor must be left by the thread that entered it, so the test thread, which may resume
    /// elsewhere after an await, never takes the lock itself. Returns once the gate is held.
    /// </summary>
    public HeldRecentWrites HoldRecentWrites() => new(RecentWriteGate(Actions!), RecentWriteWait);

    private static readonly TimeSpan RecentWriteWait = TimeSpan.FromSeconds(30);

    /// <summary>The commit log the state reads work item commits through (job 280); it never runs git.</summary>
    public FakeCommitLog CommitLog { get; } = new FakeCommitLog();

    /// <summary>The located tm path the state reports (job 282); null means not found. Set it before <see cref="CreateState"/>.</summary>
    public string? TimesCommand { get; set; } = "/fake/tm";

    /// <summary>When true, the state and the actions get no Times folder at all (job 383); set it before <see cref="CreateState"/>.</summary>
    public bool NoTimesFolder { get; set; }

    /// <summary>The Times runner the actions book a meeting through (job 381); it never starts tm.</summary>
    public FakeTimesRunner TimesRunner { get; } = new FakeTimesRunner();

    /// <summary>The licence store the state's Pro reads (job 367); in memory, supported, empty. Set it up before <see cref="CreateState"/>.</summary>
    public FakeLicenceStore LicenceStore { get; } = new FakeLicenceStore();

    /// <summary>The test key pair the state's Pro trusts (job 367). Released in <see cref="Dispose"/>.</summary>
    public TestLicences Licences { get; } = new TestLicences();

    /// <summary>The build date the state's Pro checks a licence against (job 367); set it before <see cref="CreateState"/>.</summary>
    public DateOnly? ProBuildDate { get; set; } = TestLicences.BuildDate;

    /// <summary>The <c>SuitePro</c> folder of the activation file, outside the data folder; nothing is there yet.</summary>
    public string ActivationFolder => Path.Combine(parent, "SuitePro");

    /// <summary>The command bar's hook to Main (job 282), registered with <see cref="Bunit"/>.</summary>
    public CommandBarContext CommandBar { get; } = new CommandBarContext();

    /// <summary>Builds the state and the actions (once) and registers both with <see cref="Bunit"/>.</summary>
    public DesktopState CreateState()
    {
        if (State is not null)
        {
            return State;
        }

        CalendarLink link = new CalendarLink(Root);
        CalendarFeed feed = new CalendarFeed(Root, link, Http, FeedClock);
        Todos = new TodoStore(Root);
        Notes = new NoteStore(Root);
        Session = new SensitiveSession(() => UtcNow);
        State = new DesktopState(
            Notes,
            Todos,
            new TagIndex(Root),
            new CalendarStore(Root, Zone),
            feed,
            () => Day,
            () => UtcNow,
            Keys,
            Session,
            link,
            Outlook,
            OutlookTimeout,
            PageTitles,
            IsMac,
            NoTimesFolder ? null : TimesFolder,
            CommitLog,
            () => TimesCommand,
            pro: new Pro(LicenceStore, Licences.Keys, LicenceRevocations.None, ProBuildDate),
            activationFile: new LicenceActivationFile(ActivationFolder));
        Actions = new DesktopActions(
            Notes, Todos, State, Launcher, link, feed, () => LocalNow, new CalendarStore(Root, Zone), NoTimesFolder ? null : TimesFolder, Notifier,
            ScreenCapture, UrlLauncher, TimesRunner);
        Bunit.Services.AddSingleton(State);
        Bunit.Services.AddSingleton(Actions);
        Bunit.Services.AddTransient<EditorInterop>();
        // Job 341: Main applies the saved theme on its first render and after a save in Settings; the calls are
        // recorded in Bunit.JSInterop, and each one releases ThemeSet once.
        Bunit.JSInterop.SetupVoid("ContextEditor.setTheme", _ =>
        {
            ThemeSet.Release();
            return true;
        }).SetVoidResult();
        Bunit.Services.AddSingleton<IFileDialog>(Dialog);
        Bunit.Services.AddSingleton<IAutoSavePause>(AutoSave);
        Bunit.Services.AddSingleton(Guard);
        Bunit.Services.AddSingleton(CommandBar);
        return State;
    }

    /// <summary>Builds the state, refreshes it once and returns the snapshot.</summary>
    public async Task<DesktopSnapshot> RefreshAsync() => await CreateState().Refresh();

    /// <summary>Refreshes, then renders <typeparamref name="T"/>.</summary>
    public async Task<IRenderedComponent<T>> RenderAsync<T>()
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        await RefreshAsync();
        return Bunit.Render<T>();
    }

    public void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, name + ".md"), text);
    }

    /// <summary>Writes <c>&lt;name&gt;.ctxs</c> sealed for the machine and recovery keys of <see cref="Keys"/> (both must exist).</summary>
    public void WriteSensitive(string name, string text)
    {
        Directory.CreateDirectory(Root);
        SealedRecipient[] recipients =
        [
            new SealedRecipient(SealedRecipient.Machine, Keys.MachinePublicKey()!),
            new SealedRecipient(SealedRecipient.Recovery, Keys.RecoveryPublicKey()!),
        ];
        File.WriteAllBytes(Path.Combine(Root, name + NoteStore.SensitiveExtension), SealedNote.Seal(name, text, recipients));
    }

    public void AddTodo(string text, DateOnly due, TodoRepeat? repeat = null, TimeOnly? time = null)
    {
        TodoStore store = new TodoStore(Root, () => created = created.AddMinutes(1));
        store.Add(text, due, repeat, time);
    }

    /// <summary>
    /// Writes recent.json with <paramref name="names"/>, newest first (job 240); the state reads it once when it is
    /// built, so call this before <see cref="CreateState"/>.
    /// </summary>
    public void WriteRecent(params string[] names) => new RecentNotesStore(Root).Write(names);

    /// <summary>
    /// Writes pinned.json with <paramref name="names"/> in pinned order (job 278); the state reads it once when it is
    /// built, so call this before <see cref="CreateState"/>.
    /// </summary>
    public void WritePinned(params string[] names) => new PinnedNotesStore(Root).Write(names);

    /// <summary>The GUID <see cref="WriteDecisionFolder"/> writes (job 349).</summary>
    public static readonly Guid DecisionFolderGuid = new("11111111-2222-3333-4444-555555555555");

    /// <summary>The <c>decision-folder::</c> line of a note numbered with <see cref="WriteDecisionFolder"/>'s folder.</summary>
    public static readonly string DecisionFolderLine = "decision-folder:: " + DecisionFolderGuid.ToString("D");

    /// <summary>
    /// Writes <c>decision-folder.json</c> with <paramref name="prefix"/>, <see cref="DecisionFolderGuid"/> and
    /// <paramref name="migrated"/> (job 349), so no test draws a random prefix.
    /// </summary>
    public void WriteDecisionFolder(string prefix = "ab12", bool migrated = true) =>
        WriteFile(
            DecisionFolder.FileName,
            "{\"prefix\":\"" + prefix + "\",\"guid\":\"" + DecisionFolderGuid.ToString("D") + "\",\"migrated\":"
                + (migrated ? "true" : "false") + "}");

    public void WriteFile(string relative, string text)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void WriteCalendar(string name, string text) => WriteFile(Path.Combine(CalendarStore.FolderName, name), text);

    public void SaveLink(string link) => new CalendarLink(Root).Set(link);

    /// <summary>Writes the feed cache and stamps it with <paramref name="atUtc"/>.</summary>
    public void WriteCache(string text, DateTime atUtc)
    {
        string path = Path.Combine(Root, CalendarFeed.CacheFileName);
        WriteFile(CalendarFeed.CacheFileName, text);
        File.SetLastWriteTimeUtc(path, atUtc);
    }

    public static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    public static string Event(string title, string start, string end, string? location = null) =>
        "BEGIN:VEVENT\r\n"
        + (title.Length == 0 ? string.Empty : $"SUMMARY:{title}\r\n")
        + (location is null ? string.Empty : $"LOCATION:{location}\r\n")
        + $"DTSTART:{start}\r\nDTEND:{end}\r\nEND:VEVENT\r\n";

    private DateTime FeedClock()
    {
        if (FeedClockThrows is not null)
        {
            throw FeedClockThrows;
        }

        return UtcNow;
    }
}

/// <summary>The recent.json write gate held on its own thread (see <see cref="DesktopFixture.HoldRecentWrites"/>).</summary>
internal sealed class HeldRecentWrites : IDisposable
{
    private readonly ManualResetEventSlim held = new();
    private readonly ManualResetEventSlim release = new();
    private readonly Thread thread;
    private readonly TimeSpan wait;

    public HeldRecentWrites(object gate, TimeSpan wait)
    {
        this.wait = wait;
        thread = new Thread(() =>
        {
            lock (gate)
            {
                held.Set();
                release.Wait(wait);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        if (!held.Wait(wait))
        {
            throw new TimeoutException("The recent.json write gate was not taken in time.");
        }
    }

    /// <summary>Lets the holding thread leave the gate and waits until it has.</summary>
    public void Release()
    {
        release.Set();
        thread.Join(wait);
    }

    public void Dispose()
    {
        Release();
        held.Dispose();
        release.Dispose();
    }
}
