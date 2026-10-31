using System.Net;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace Context.Tests;

/// <summary>
/// Golden output of <c>/current</c>, <c>/notes</c>, <c>/tags</c>, <c>/links</c> and <c>/calendar-link</c>,
/// recorded from the code before the views moved to <c>Context.Core</c>. Each case calls the registered handler
/// and renders its message and hint at a fixed width with ANSI codes on, so colours count too. The temp root is
/// shown as <c>&lt;root&gt;</c> and ESC as <c>␛</c>.
/// </summary>
public sealed class ViewParityTests : IDisposable
{
    internal const int Width = 250;

    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private HttpMessageHandler handler = FakeHttpHandler.Throw();

    public ViewParityTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-parity-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(root))
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>The message, then the hint, of <paramref name="result"/> as a golden string.</summary>
    internal static string Render(CommandResult result, string root) =>
        "message:\n" + Render(result.Message, root) + "\nhint:\n" + Render(result.Hint, root);

    private static string Render(IRenderable? renderable, string root)
    {
        if (renderable is null)
        {
            return "(none)";
        }

        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Profile.Width = Width;
        console.Write(renderable);
        string[] lines = console.Output
            .Replace(root, "<root>", StringComparison.Ordinal)
            .Replace("\u001b", "␛", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        return string.Join("\n", lines.Select(l => l.TrimEnd(' ')));
    }

    private CommandResult Run(string command, string argument = "", bool feed = false)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteStore notes = new NoteStore(root);
        TagIndex index = new TagIndex(root);
        TodoStore todos = new TodoStore(root, () => clock = clock.AddMinutes(1));
        CalendarLink link = new CalendarLink(root);
        CalendarFeed calendarFeed = new CalendarFeed(root, link, handler, () => Now);
        NoteCommands.Register(registry, notes);
        TagCommands.Register(registry, index, notes);
        CalendarLinkCommands.Register(registry, link, calendarFeed, Zone);
        CurrentCommands.Register(
            registry, todos, index, notes, new CalendarStore(root, Zone), () => Today, feed ? calendarFeed : null);
        Assert.True(registry.TryGet(command, out Command found));
        return found.Handler(argument);
    }

    private void AssertGolden(string name, CommandResult result)
    {
        Assert.Equal(Goldens[name], Render(result, root));
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name + ".md");
        File.WriteAllText(path, text);
        File.SetLastWriteTime(path, new DateTime(2026, 10, 1, 8, 15, 0, DateTimeKind.Local));
    }

    private void AddTodo(string text, DateOnly due) =>
        new TodoStore(root, () => clock = clock.AddMinutes(1)).Add(text, due);

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string? title, string start, string? end = null, string? location = null)
    {
        string titleLine = title is null ? string.Empty : $"SUMMARY:{title}\r\n";
        string endLine = end is null ? string.Empty : $"DTEND{end}\r\n";
        string whereLine = location is null ? string.Empty : $"LOCATION:{location}\r\n";
        return $"BEGIN:VEVENT\r\n{titleLine}DTSTART{start}\r\n{endLine}{whereLine}END:VEVENT\r\n";
    }

    private void WriteBadDb()
    {
        Directory.CreateDirectory(root);
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(Path.Combine(root, TagIndex.FileName), garbage);
    }

    private void SetLinkWithCache(DateTime? cachedAtUtc)
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        if (cachedAtUtc is not null)
        {
            string cache = Path.Combine(root, CalendarFeed.CacheFileName);
            File.WriteAllText(cache, Calendar(Event("Cached [meeting]", ":20261029T140000Z", ":20261029T150000Z")));
            File.SetLastWriteTimeUtc(cache, cachedAtUtc.Value);
        }
    }

    // ---- /current -------------------------------------------------------------------------

    [Fact]
    public void Current_Full()
    {
        List<string> events =
        [
            Event("Holiday", ";VALUE=DATE:20261029", ";VALUE=DATE:20261030"),
            Event("Night shift", ":20261028T220000", ":20261029T060000", "Ward [3]"),
            Event("Late call", ":20261029T230000", ":20261030T010000"),
            Event(null, ":20261029T073000", ":20261029T080000"),
            Event("Ends at midnight", ":20261029T220000", ":20261030T000000"),
            Event("Multi\\nline [b]x[/b]", ":20261029T081500", ":20261029T090000", "Room 1\\nfloor 2"),
        ];
        for (int i = 0; i < 16; i++)
        {
            events.Add(Event($"Meeting {i:00}", $":20261029T1{i % 10}{i / 10}000", $":20261029T1{i % 10}{i / 10}500"));
        }

        WriteCalendar("work.ics", Calendar([.. events]));
        WriteCalendar("broken [x].ics", "not a calendar");
        AddTodo("Pay rent #home", Today.AddDays(-3));
        AddTodo("Plan #Work and #[big plan]", Today);
        AddTodo("Later #home", Today.AddDays(2));
        WriteNote("beta", "#work #home");
        WriteNote("Alpha [x]", "#[big plan] #work");
        WriteNote("gamma", "#other");

        AssertGolden(nameof(Current_Full), Run("/current"));
    }

    [Fact]
    public void Current_Stale_NothingDue()
    {
        SetLinkWithCache(new DateTime(2026, 10, 28, 17, 5, 0, DateTimeKind.Utc));
        handler = FakeHttpHandler.Status(HttpStatusCode.InternalServerError);
        AddTodo("Later", Today.AddDays(1));

        AssertGolden(nameof(Current_Stale_NothingDue), Run("/current", feed: true));
    }

    [Fact]
    public void Current_Failed_NoLinkedNotes()
    {
        SetLinkWithCache(null);
        handler = FakeHttpHandler.Status(HttpStatusCode.NotFound);
        AddTodo("Call #nobody", Today);

        AssertGolden(nameof(Current_Failed_NoLinkedNotes), Run("/current", feed: true));
    }

    [Fact]
    public void Current_Empty()
    {
        AssertGolden(nameof(Current_Empty), Run("/current"));
    }

    [Fact]
    public void Current_BadTodos()
    {
        WriteCalendar("a.ics", Calendar(Event("Standup", ":20261029T090000", ":20261029T091500")));
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ not json");

        AssertGolden(nameof(Current_BadTodos), Run("/current"));
    }

    [Fact]
    public void Current_BadDb()
    {
        AddTodo("Due #work", Today.AddDays(-1));
        WriteBadDb();

        AssertGolden(nameof(Current_BadDb), Run("/current"));
    }

    // ---- /notes ---------------------------------------------------------------------------

    [Fact]
    public void Notes_Empty()
    {
        AssertGolden(nameof(Notes_Empty), Run("/notes"));
    }

    [Fact]
    public void Notes_List()
    {
        WriteNote("Shopping", "# Shopping\n");
        WriteNote("Odd [red]name", "x");

        AssertGolden(nameof(Notes_List), Run("/notes"));
    }

    // ---- /tags ----------------------------------------------------------------------------

    [Fact]
    public void Tags_None()
    {
        WriteNote("A", "no tags");

        AssertGolden(nameof(Tags_None), Run("/tags"));
    }

    [Fact]
    public void Tags_List()
    {
        WriteNote("A", "#work #[big plan]");
        WriteNote("B", "#work #[x]y");

        AssertGolden(nameof(Tags_List), Run("/tags"));
    }

    [Theory]
    [InlineData("Tags_Hit_Plain", "work")]
    [InlineData("Tags_Hit_Hash", "#work")]
    [InlineData("Tags_Hit_Spaces", "[Big   Plan]")]
    [InlineData("Tags_Miss", "#[no such]")]
    [InlineData("Tags_Miss_Invalid", "#")]
    public void Tags_Argument(string name, string argument)
    {
        WriteNote("A [1]", "#work #[big plan]");
        WriteNote("B", "#work");

        AssertGolden(name, Run("/tags", argument));
    }

    [Theory]
    [InlineData("Tags_BadDb_All", "")]
    [InlineData("Tags_BadDb_Tag", "work")]
    public void Tags_BadDb(string name, string argument)
    {
        WriteNote("A", "#work");
        WriteBadDb();

        AssertGolden(name, Run("/tags", argument));
    }

    // ---- /links ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Links_NoArgument", "")]
    [InlineData("Links_Unknown", "Nope [x]")]
    [InlineData("Links_MdSuffix", "a.md")]
    [InlineData("Links_NoLinks", "Lonely")]
    [InlineData("Links_Links", "A")]
    public void Links(string name, string argument)
    {
        WriteNote("A", "#work #[big plan]");
        WriteNote("B [b]", "#work #[big plan]");
        WriteNote("c", "#work");
        WriteNote("Lonely", "#solo");

        AssertGolden(name, Run("/links", argument));
    }

    // 048 AC-8: To, From, Both (with a shared tag), a tag-only row and a missing note.
    [Fact]
    public void Links_NoteLinks()
    {
        WriteNote("A", "#work [[Beta]] [[Gamma]] [[delta.md]]");
        WriteNote("Beta", "#work [[A]]");
        WriteNote("c", "#work");
        WriteNote("Delta", "plain");
        WriteNote("Epsilon", "see [[a]]");

        AssertGolden(nameof(Links_NoteLinks), Run("/links", "A"));
    }

    [Fact]
    public void Links_BadDb()
    {
        WriteNote("A", "#work");
        WriteBadDb();

        AssertGolden(nameof(Links_BadDb), Run("/links", "A"));
    }

    // ---- /calendar-link -------------------------------------------------------------------

    [Fact]
    public void Link_NotSet()
    {
        AssertGolden(nameof(Link_NotSet), Run("/calendar-link"));
    }

    [Fact]
    public void Link_Shown_NotDownloaded()
    {
        SetLinkWithCache(null);

        AssertGolden(nameof(Link_Shown_NotDownloaded), Run("/calendar-link"));
    }

    [Fact]
    public void Link_Shown_Downloaded()
    {
        SetLinkWithCache(new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));

        AssertGolden(nameof(Link_Shown_Downloaded), Run("/calendar-link"));
    }

    [Fact]
    public void Link_Saved()
    {
        AssertGolden(nameof(Link_Saved), Run("/calendar-link", FakeHttpHandler.Link));
    }

    [Fact]
    public void Link_Invalid()
    {
        AssertGolden(nameof(Link_Invalid), Run("/calendar-link", "http://example.com/secret"));
    }

    [Fact]
    public void Link_Removed()
    {
        SetLinkWithCache(Now);

        AssertGolden(nameof(Link_Removed), Run("/calendar-link", "off"));
    }

    [Fact]
    public void Link_Off_NotSet()
    {
        AssertGolden(nameof(Link_Off_NotSet), Run("/calendar-link", "OFF"));
    }

    [Fact]
    public void Link_Unreadable()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, CalendarLink.FileName), "http://example.com/x");

        AssertGolden(nameof(Link_Unreadable), Run("/calendar-link"));
    }

    [Fact]
    public void Link_NotSaved()
    {
        File.WriteAllText(root, "a file where the folder should be");

        AssertGolden(nameof(Link_NotSaved), Run("/calendar-link", FakeHttpHandler.Link));
    }

    [Fact]
    public void Link_NotRemoved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        SetLinkWithCache(null);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        AssertGolden(nameof(Link_NotRemoved), Run("/calendar-link", "off"));
    }

    // ---- Golden output, recorded from the old code ------------------------------------------

    internal static readonly Dictionary<string, string> Goldens = new(StringComparer.Ordinal)
    {
        ["Current_BadDb"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
        ␛[38;5;8mNo appointments today. Put .ics files in <root>/calendar to see them here.␛[0m
                     ␛[38;5;8mDue␛[0m
        ␛[38;5;8m╭───┬────────────┬───────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8m#␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8mDate␛[0m       ␛[38;5;8m│␛[0m ␛[38;5;8mTodo␛[0m      ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───┼────────────┼───────────┤␛[0m
        ␛[38;5;8m│␛[0m 1 ␛[38;5;8m│␛[0m ␛[38;5;9m2026-10-28␛[0m ␛[38;5;8m│␛[0m Due #work ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───┴────────────┴───────────╯␛[0m
        ␛[38;5;9mCan't read context.db. Move it out of the Context folder and it is rebuilt from your notes.␛[0m

        hint:
        (none)
        """,

        ["Current_BadTodos"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
                    ␛[38;5;8mAppointments␛[0m
        ␛[38;5;8m╭─────────────┬─────────────┬───────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mTime␛[0m        ␛[38;5;8m│␛[0m ␛[38;5;8mAppointment␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8mWhere␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├─────────────┼─────────────┼───────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m09:00-09:15␛[0m ␛[38;5;8m│␛[0m Standup     ␛[38;5;8m│␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰─────────────┴─────────────┴───────╯␛[0m

        hint:
        ␛[38;5;9mCan't read todos.json. Fix or move the file and try again.␛[0m
        """,

        ["Current_Empty"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
        ␛[38;5;8mNo appointments today. Put .ics files in <root>/calendar to see them here.␛[0m
        ␛[38;5;8mNothing due today. Add one with /todo today <text>.␛[0m

        hint:
        (none)
        """,

        ["Current_Failed_NoLinkedNotes"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
        ␛[38;5;8mNo appointments today. Put .ics files in <root>/calendar to see them here.␛[0m
        ␛[38;5;9mCan't download the Exchange calendar (HTTP 404).␛[0m
                       ␛[38;5;8mDue␛[0m
        ␛[38;5;8m╭───┬────────────┬──────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8m#␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8mDate␛[0m       ␛[38;5;8m│␛[0m ␛[38;5;8mTodo␛[0m         ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───┼────────────┼──────────────┤␛[0m
        ␛[38;5;8m│␛[0m 1 ␛[38;5;8m│␛[0m ␛[38;5;75m2026-10-29␛[0m ␛[38;5;8m│␛[0m Call #nobody ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───┴────────────┴──────────────╯␛[0m
        ␛[38;5;8mNo linked notes. Add the same #tag to a todo and a note to link them.␛[0m

        hint:
        (none)
        """,

        ["Current_Full"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
                             ␛[38;5;8mAppointments␛[0m
        ␛[38;5;8m╭─────────────┬─────────────────────┬────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mTime␛[0m        ␛[38;5;8m│␛[0m ␛[38;5;8mAppointment␛[0m         ␛[38;5;8m│␛[0m ␛[38;5;8mWhere␛[0m          ␛[38;5;8m│␛[0m
        ␛[38;5;8m├─────────────┼─────────────────────┼────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mall day␛[0m     ␛[38;5;8m│␛[0m Holiday             ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m…-06:00␛[0m     ␛[38;5;8m│␛[0m Night shift         ␛[38;5;8m│␛[0m ␛[38;5;8mWard [3]␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m07:30-08:00␛[0m ␛[38;5;8m│␛[0m (no title)          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m08:15-09:00␛[0m ␛[38;5;8m│␛[0m Multi line [b]x[/b] ␛[38;5;8m│␛[0m ␛[38;5;8mRoom 1 floor 2␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m10:00-10:05␛[0m ␛[38;5;8m│␛[0m Meeting 00          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m10:10-10:15␛[0m ␛[38;5;8m│␛[0m Meeting 10          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m11:00-11:05␛[0m ␛[38;5;8m│␛[0m Meeting 01          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m11:10-11:15␛[0m ␛[38;5;8m│␛[0m Meeting 11          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m12:00-12:05␛[0m ␛[38;5;8m│␛[0m Meeting 02          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m12:10-12:15␛[0m ␛[38;5;8m│␛[0m Meeting 12          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m13:00-13:05␛[0m ␛[38;5;8m│␛[0m Meeting 03          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m13:10-13:15␛[0m ␛[38;5;8m│␛[0m Meeting 13          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m14:00-14:05␛[0m ␛[38;5;8m│␛[0m Meeting 04          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m14:10-14:15␛[0m ␛[38;5;8m│␛[0m Meeting 14          ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m             ␛[38;5;8m│␛[0m ␛[38;5;8m… 8 more␛[0m            ␛[38;5;8m│␛[0m                ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰─────────────┴─────────────────────┴────────────────╯␛[0m
        ␛[38;5;9mCan't read calendar/broken [x].ics␛[0m
                              ␛[38;5;8mDue␛[0m
        ␛[38;5;8m╭───┬────────────┬────────────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8m#␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8mDate␛[0m       ␛[38;5;8m│␛[0m ␛[38;5;8mTodo␛[0m                       ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───┼────────────┼────────────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m 1 ␛[38;5;8m│␛[0m ␛[38;5;9m2026-10-26␛[0m ␛[38;5;8m│␛[0m Pay rent #home             ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m 2 ␛[38;5;8m│␛[0m ␛[38;5;75m2026-10-29␛[0m ␛[38;5;8m│␛[0m Plan #Work and #[big plan] ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───┴────────────┴────────────────────────────╯␛[0m
                   ␛[38;5;8mLinked notes␛[0m
        ␛[38;5;8m╭───────────┬────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNote␛[0m      ␛[38;5;8m│␛[0m ␛[38;5;8mTags␛[0m               ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───────────┼────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mAlpha [x]␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8m#[big plan], #work␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mbeta␛[0m      ␛[38;5;8m│␛[0m ␛[38;5;8m#home, #work␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───────────┴────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Current_Stale_NothingDue"] =
        """
        message:
        ␛[38;5;75mToday, 2026-10-29␛[0m
                       ␛[38;5;8mAppointments␛[0m
        ␛[38;5;8m╭─────────────┬──────────────────┬───────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mTime␛[0m        ␛[38;5;8m│␛[0m ␛[38;5;8mAppointment␛[0m      ␛[38;5;8m│␛[0m ␛[38;5;8mWhere␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├─────────────┼──────────────────┼───────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m15:00-16:00␛[0m ␛[38;5;8m│␛[0m Cached [meeting] ␛[38;5;8m│␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰─────────────┴──────────────────┴───────╯␛[0m
        ␛[38;5;8mExchange calendar offline, showing the copy from 2026-10-28 18:05.␛[0m
        ␛[38;5;8mNothing due today. Add one with /todo today <text>.␛[0m

        hint:
        (none)
        """,

        ["Link_Invalid"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mThe calendar link must be an https:// or webcal:// address.␛[0m
        """,

        ["Link_NotRemoved"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't remove the calendar link. Check that the Context folder is writable.␛[0m
        """,

        ["Link_NotSaved"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't save the calendar link. Check that the Context folder is writable.␛[0m
        """,

        ["Link_NotSet"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;8mNo calendar link set.␛[0m
        """,

        ["Link_Off_NotSet"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;8mNo calendar link set.␛[0m
        """,

        ["Link_Removed"] =
        """
        message:
        (none)
        hint:
        Calendar link removed.
        """,

        ["Link_Saved"] =
        """
        message:
        (none)
        hint:
        Calendar link saved (␛[38;5;75mhttps://outlook.example.com/…␛[0m).
        """,

        ["Link_Shown_Downloaded"] =
        """
        message:
        (none)
        hint:
        Calendar link: ␛[38;5;75mhttps://outlook.example.com/…␛[0m ␛[38;5;8m(last download 2026-10-29 08:30)␛[0m
        """,

        ["Link_Shown_NotDownloaded"] =
        """
        message:
        (none)
        hint:
        Calendar link: ␛[38;5;75mhttps://outlook.example.com/…␛[0m ␛[38;5;8m(not downloaded yet)␛[0m
        """,

        ["Link_Unreadable"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't read calendar-link.txt. Set the link again or type /calendar-link off.␛[0m
        """,

        ["Links_BadDb"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't read context.db. Move it out of the Context folder and it is rebuilt from your notes.␛[0m
        """,

        ["Links_Links"] =
        """
        message:
                 ␛[38;5;75mLinked to A␛[0m
        ␛[38;5;8m╭───────┬────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNote␛[0m  ␛[38;5;8m│␛[0m ␛[38;5;8mShared tags␛[0m        ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───────┼────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mB [b]␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8m#[big plan], #work␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mc␛[0m     ␛[38;5;8m│␛[0m ␛[38;5;8m#work␛[0m              ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───────┴────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Links_MdSuffix"] =
        """
        message:
                 ␛[38;5;75mLinked to A␛[0m
        ␛[38;5;8m╭───────┬────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNote␛[0m  ␛[38;5;8m│␛[0m ␛[38;5;8mShared tags␛[0m        ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───────┼────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mB [b]␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8m#[big plan], #work␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mc␛[0m     ␛[38;5;8m│␛[0m ␛[38;5;8m#work␛[0m              ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───────┴────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Links_NoteLinks"] =
        """
        message:
                         ␛[38;5;75mLinked to A␛[0m
        ␛[38;5;8m╭─────────┬────────────────────┬─────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNote␛[0m    ␛[38;5;8m│␛[0m ␛[38;5;8mLink␛[0m               ␛[38;5;8m│␛[0m ␛[38;5;8mShared tags␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├─────────┼────────────────────┼─────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mBeta␛[0m    ␛[38;5;8m│␛[0m ␛[38;5;8mboth ways␛[0m          ␛[38;5;8m│␛[0m ␛[38;5;8m#work␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mc␛[0m       ␛[38;5;8m│␛[0m                    ␛[38;5;8m│␛[0m ␛[38;5;8m#work␛[0m       ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mDelta␛[0m   ␛[38;5;8m│␛[0m ␛[38;5;8mlinks to␛[0m           ␛[38;5;8m│␛[0m             ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mEpsilon␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8mlinked from␛[0m        ␛[38;5;8m│␛[0m             ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mGamma␛[0m   ␛[38;5;8m│␛[0m ␛[38;5;8mlinks to (missing)␛[0m ␛[38;5;8m│␛[0m             ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰─────────┴────────────────────┴─────────────╯␛[0m

        hint:
        (none)
        """,

        ["Links_NoArgument"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mType ␛[0m␛[1;38;5;9m/links <note>␛[0m␛[38;5;9m to see the notes linked to it by tags or [[links]]. Type ␛[0m␛[1;38;5;9m/notes␛[0m␛[38;5;9m to see all notes.␛[0m
        """,

        ["Links_NoLinks"] =
        """
        message:
        ␛[38;5;8mLonely has no linked notes.␛[0m
        hint:
        (none)
        """,

        ["Links_Unknown"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mNo note named Nope [x]. Type ␛[0m␛[1;38;5;9m/notes␛[0m␛[38;5;9m to see all notes.␛[0m
        """,

        ["Notes_Empty"] =
        """
        message:
        ␛[38;5;8mNo notes yet. Type /new <title> to create one.␛[0m
        hint:
        (none)
        """,

        ["Notes_List"] =
        """
        message:
        ␛[38;5;8m╭───────────────┬──────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNote␛[0m          ␛[38;5;8m│␛[0m ␛[38;5;8mModified␛[0m         ␛[38;5;8m│␛[0m
        ␛[38;5;8m├───────────────┼──────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mOdd [red]name␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8m2026-10-01 08:15␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mShopping␛[0m      ␛[38;5;8m│␛[0m ␛[38;5;8m2026-10-01 08:15␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰───────────────┴──────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Tags_BadDb_All"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't read context.db. Move it out of the Context folder and it is rebuilt from your notes.␛[0m
        """,

        ["Tags_BadDb_Tag"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mCan't read context.db. Move it out of the Context folder and it is rebuilt from your notes.␛[0m
        """,

        ["Tags_Hit_Hash"] =
        """
        message:
        ␛[38;5;8m╭────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNotes tagged #work␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mA [1]␛[0m              ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mB␛[0m                  ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Tags_Hit_Plain"] =
        """
        message:
        ␛[38;5;8m╭────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNotes tagged #work␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mA [1]␛[0m              ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mB␛[0m                  ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Tags_Hit_Spaces"] =
        """
        message:
        ␛[38;5;8m╭──────────────────────────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mNotes tagged #[big plan]␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├──────────────────────────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75mA [1]␛[0m                    ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰──────────────────────────╯␛[0m

        hint:
        (none)
        """,

        ["Tags_List"] =
        """
        message:
        ␛[38;5;8m╭─────────────┬───────╮␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;8mTag␛[0m         ␛[38;5;8m│␛[0m ␛[38;5;8mNotes␛[0m ␛[38;5;8m│␛[0m
        ␛[38;5;8m├─────────────┼───────┤␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m#[big plan]␛[0m ␛[38;5;8m│␛[0m ␛[38;5;8m1␛[0m     ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m#work␛[0m       ␛[38;5;8m│␛[0m ␛[38;5;8m2␛[0m     ␛[38;5;8m│␛[0m
        ␛[38;5;8m│␛[0m ␛[38;5;75m#x␛[0m          ␛[38;5;8m│␛[0m ␛[38;5;8m1␛[0m     ␛[38;5;8m│␛[0m
        ␛[38;5;8m╰─────────────┴───────╯␛[0m

        hint:
        (none)
        """,

        ["Tags_Miss"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mNo notes tagged #[no such]. Type ␛[0m␛[1;38;5;9m/tags␛[0m␛[38;5;9m to see all tags.␛[0m
        """,

        ["Tags_Miss_Invalid"] =
        """
        message:
        (none)
        hint:
        ␛[38;5;9mNo notes tagged #. Type ␛[0m␛[1;38;5;9m/tags␛[0m␛[38;5;9m to see all tags.␛[0m
        """,

        ["Tags_None"] =
        """
        message:
        ␛[38;5;8mNo tags yet. Add #tag to a note to link it.␛[0m
        hint:
        (none)
        """,
    };
}
