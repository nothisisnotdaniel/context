using System.Net;
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
    private DateTime created = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    public DesktopFixture()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-desktop-" + Guid.NewGuid().ToString("N"));
        Root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        Bunit = new BunitContext();
    }

    /// <summary>The data folder; it does not exist until something is written.</summary>
    public string Root { get; }

    public BunitContext Bunit { get; }

    /// <summary>The day the state sees; tests may change it.</summary>
    public DateOnly Day { get; set; } = Today;

    /// <summary>The UTC clock of the state and the feed; tests may change it.</summary>
    public DateTime UtcNow { get; set; } = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The feed's handler; by default every request fails with HTTP 500.</summary>
    public FakeHttpHandler Http { get; set; } = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);

    /// <summary>When set, the feed's clock throws this instead of returning <see cref="UtcNow"/>.</summary>
    public Exception? FeedClockThrows { get; set; }

    public DesktopState? State { get; private set; }

    public DesktopActions? Actions { get; private set; }

    /// <summary>The note store the state and the actions share (set by <see cref="CreateState"/>).</summary>
    public NoteStore? Notes { get; private set; }

    /// <summary>The todo store the state and the actions share (set by <see cref="CreateState"/>).</summary>
    public TodoStore? Todos { get; private set; }

    public void Dispose()
    {
        Bunit.Dispose();
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>Builds the state and the actions (once) and registers both with <see cref="Bunit"/>.</summary>
    public DesktopState CreateState()
    {
        if (State is not null)
        {
            return State;
        }

        Encryption encryption = new Encryption(Root, minIterations: 1000);
        CalendarLink link = new CalendarLink(Root, encryption);
        CalendarFeed feed = new CalendarFeed(Root, link, Http, FeedClock, encryption: encryption);
        Todos = new TodoStore(Root, encryption: encryption);
        Notes = new NoteStore(Root, encryption);
        State = new DesktopState(
            encryption,
            Notes,
            Todos,
            new TagIndex(Root, encryption),
            new CalendarStore(Root, Zone, encryption),
            feed,
            () => Day,
            () => UtcNow);
        Actions = new DesktopActions(encryption, Notes, Todos, State);
        Bunit.Services.AddSingleton(State);
        Bunit.Services.AddSingleton(Actions);
        Bunit.Services.AddTransient<EditorInterop>();
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

    public void AddTodo(string text, DateOnly due)
    {
        TodoStore store = new TodoStore(Root, () => created = created.AddMinutes(1));
        store.Add(text, due);
    }

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
