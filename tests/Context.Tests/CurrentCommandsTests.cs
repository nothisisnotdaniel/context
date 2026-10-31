using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Context.Tests;

public sealed class CurrentCommandsTests : IDisposable
{
    private const int Width = 140;
    private const int Height = 40;
    private const int MainWidth = Width - 32;
    private const string NothingDue = "Nothing due today. Add one with /todo today <text>.";
    private const string NoLinks = "No linked notes. Add the same #tag to a todo and a note to link them.";
    private const string DbUnreadable =
        "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.";

    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;
    private static readonly Regex Ansi = new(@"\u001b\[[0-9;?]*[A-Za-z]");

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private CalendarFeed? feed;

    public CurrentCommandsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-current-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private TodoStore Store() => new(root, () => clock = clock.AddMinutes(1));

    private void AddTodo(string text, DateOnly due) => Store().Add(text, due);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private string CalendarFolder => Path.Combine(root, CalendarStore.FolderName);

    private string WriteCalendar(string name, params string[] events)
    {
        Directory.CreateDirectory(CalendarFolder);
        string path = Path.Combine(CalendarFolder, name);
        File.WriteAllText(path, "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n");
        return path;
    }

    private static string Event(string? title, string start, string? end = null, string extra = "")
    {
        string titleLine = title is null ? string.Empty : $"SUMMARY:{title}\r\n";
        string endLine = end is null ? string.Empty : $"DTEND{end}\r\n";
        return $"BEGIN:VEVENT\r\n{titleLine}DTSTART{start}\r\n{endLine}{extra}END:VEVENT\r\n";
    }

    private string EmptyCalendar =>
        $"No appointments today. Put .ics files in {Path.Combine(root, "calendar")} to see them here.";

    private static string NoSpace(string text) => Regex.Replace(text, "\\s+", string.Empty);

    private static int LineIndex(string[] main, string text) =>
        Array.FindIndex(main, l => l.Contains(text, StringComparison.Ordinal));

    private (int ExitCode, string Output) Run(bool ansi, params string[] commands)
    {
        CommandRegistry registry = CommandRegistry.CreateDefault();
        NoteStore notes = new NoteStore(root);
        TodoStore todos = Store();
        TodoCommands.Register(registry, todos, () => Today);
        CurrentCommands.Register(
            registry, todos, new TagIndex(root), notes, new CalendarStore(root, Zone), () => Today, feed);
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.EmitAnsiSequences = ansi;
        foreach (string command in commands)
        {
            console.Input.PushTextWithEnter(command);
        }

        console.Input.PushTextWithEnter("/quit");
        int exitCode = new App(console, registry, new TodoSidebar(todos, () => Today)).Run();
        return (exitCode, console.Output);
    }

    /// <summary>The main view of the frame drawn after the last command, without ANSI codes.</summary>
    private string[] Main(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        List<string[]> frames = Frames(output);
        Assert.Equal(commands.Length + 1, frames.Count);
        return frames[^1][..^4].Select(l => l.Length > MainWidth ? l[..MainWidth] : l).ToArray();
    }

    private string[] Frame(params string[] commands)
    {
        (int exitCode, string output) = Run(false, commands);
        Assert.Equal(0, exitCode);
        return Frames(output)[^1];
    }

    private static List<string[]> Frames(string output)
    {
        List<string[]> frames = [];
        foreach (string chunk in output.Split("\u001b[2J").Skip(1))
        {
            string[] lines = Ansi.Replace(chunk, string.Empty).Split('\n');
            if (lines.Length < 2)
            {
                continue;
            }

            string last = lines[^1];
            int end = last.IndexOf('╯');
            lines[^1] = end < 0 ? last : last[..(end + 1)];
            frames.Add(lines);
        }

        return frames;
    }

    private static string Style(string markup)
    {
        TestConsole console = new TestConsole { EmitAnsiSequences = true };
        console.Markup(markup);
        return console.Output[..console.Output.IndexOf('X')];
    }

    private static int RowIndex(string[] main, params string[] cells)
    {
        Regex row = new("│\\s*" + string.Join("\\s*│\\s*", cells.Select(Regex.Escape)) + "\\s*│");
        return Array.FindIndex(main, l => row.IsMatch(l));
    }

    private static bool HasLine(string[] main, string text) =>
        main.Any(l => l.Contains(text, StringComparison.Ordinal));

    // AC-1
    [Fact]
    public void Current_ShowsTitle_AndDueTable_WithSidebarNumbers()
    {
        AddTodo("later", new DateOnly(2026, 11, 5));
        AddTodo("today one", Today);
        AddTodo("old one", new DateOnly(2026, 10, 1));

        string[] main = Main("/current");

        Assert.True(HasLine(main, "Today, 2026-10-29"));
        Assert.True(HasLine(main, "Due"));
        Assert.True(RowIndex(main, "1", "2026-10-01", "old one") >= 0);
        Assert.True(RowIndex(main, "2", "2026-10-29", "today one") >= 0);
        Assert.False(HasLine(main, "later"));
    }

    // AC-1
    [Fact]
    public void Current_Colours_TitleSteelBlue_OverdueRed_TodaySteelBlue()
    {
        AddTodo("old", new DateOnly(2026, 10, 28));
        AddTodo("now", Today);

        (int exitCode, string output) = Run(true, "/current");

        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[steelblue1]X[/]") + "Today, 2026-10-29", output);
        Assert.Contains(Style("[red]X[/]") + "2026-10-28", output);
        Assert.Contains(Style("[steelblue1]X[/]") + "2026-10-29", output);
    }

    // AC-1, AC-9
    [Fact]
    public void Help_ListsCurrent()
    {
        string[] main = Main("/help");

        Assert.Contains(main, l => l.Contains("/current", StringComparison.Ordinal)
            && l.Contains("Today's appointments, due todos and linked notes", StringComparison.Ordinal));
    }

    // AC-2
    [Fact]
    public void Current_ShowsOverdueAndToday_NotLater()
    {
        AddTodo("a", new DateOnly(2026, 10, 28));
        AddTodo("b", new DateOnly(2026, 10, 29));
        AddTodo("c", new DateOnly(2026, 10, 30));

        string[] main = Main("/current");

        int a = RowIndex(main, "1", "2026-10-28", "a");
        int b = RowIndex(main, "2", "2026-10-29", "b");
        Assert.True(a >= 0 && b > a);
        Assert.Equal(-1, RowIndex(main, "3", "2026-10-30", "c"));
        Assert.DoesNotContain(main, l => l.Contains("2026-10-30", StringComparison.Ordinal));
        Assert.DoesNotContain(main, l => Regex.IsMatch(l, "│\\s*c\\s*│"));
    }

    // AC-3
    [Fact]
    public void Current_NothingDue_ShowsGreyLine_NoTable()
    {
        AddTodo("later", new DateOnly(2026, 10, 30));

        string[] main = Main("/current");

        Assert.True(HasLine(main, NothingDue));
        Assert.DoesNotContain(main, l => l.Contains('│'));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + NothingDue, output);
    }

    // AC-3: no todos file at all
    [Fact]
    public void Current_NoTodos_ShowsNothingDue()
    {
        string[] main = Main("/current");

        Assert.True(HasLine(main, NothingDue));
    }

    // AC-4
    [Fact]
    public void Current_LinkedNotes_SortedByName_WithMatchingTags()
    {
        AddTodo("call Bob #work #[project alpha]", Today);
        WriteNote("Plan", "# Plan\n#work");
        WriteNote("Alpha", "# Alpha\n#[Project Alpha]");
        WriteNote("Home", "# Home\n#home");

        string[] main = Main("/current");

        Assert.True(HasLine(main, "Linked notes"));
        int alpha = RowIndex(main, "Alpha", "#[project alpha]");
        int plan = RowIndex(main, "Plan", "#work");
        Assert.True(alpha >= 0 && plan > alpha);
        Assert.False(HasLine(main, "Home"));

        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[steelblue1]X[/]") + "Alpha", output);
        Assert.Contains(Style("[grey]X[/]") + "#[project alpha]", output);
    }

    // AC-4: Sync runs first, so a note written after the index exists still links.
    [Fact]
    public void Current_SyncsIndexFirst()
    {
        AddTodo("x #work", Today);
        Main("/current");
        WriteNote("Fresh", "#work");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "Fresh", "#work") >= 0);
    }

    // AC-5
    [Fact]
    public void Current_OnlyDueTodosLink_AndNotesAreListedOnce()
    {
        AddTodo("one #work #home2", Today);
        AddTodo("two #work #side", new DateOnly(2026, 10, 20));
        AddTodo("later #home", new DateOnly(2026, 11, 1));
        WriteNote("Plan", "#work #side");
        WriteNote("Home", "#home");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "Plan", "#side, #work") >= 0);
        Assert.Single(main, l => l.Contains("Plan", StringComparison.Ordinal));
        Assert.False(HasLine(main, "Home"));
    }

    // AC-6
    [Fact]
    public void Current_NoMatchingTags_ShowsGreyLine()
    {
        AddTodo("plain todo", Today);
        AddTodo("tagged #nomatch", Today);
        WriteNote("Plan", "#work");

        string[] main = Main("/current");

        Assert.True(HasLine(main, NoLinks));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + NoLinks, output);
    }

    // AC-7
    [Fact]
    public void Current_BadTodos_ShowsRedHint_LoopContinues()
    {
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, TodoStore.FileName), [0x7b, 0x00, 0xff, 0x13]);
        const string Text = "Can't read todos.json. Fix or move the file and try again.";

        string[] frame = Frame("/current");

        Assert.Contains(Text, frame[^4]);
        Assert.DoesNotContain(frame[..^4], l => l.Contains(Text, StringComparison.Ordinal));
        (int exitCode, string output) = Run(true, "/current", "/help");
        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[red]X[/]") + Text, output);
        Assert.Contains("Today's appointments, due todos and linked notes", output);
    }

    // AC-7
    [Fact]
    public void Current_BadDatabase_ShowsDueTable_AndRedLine_BytesUnchanged()
    {
        AddTodo("a #work", Today);
        WriteNote("Plan", "#work");
        string db = Path.Combine(root, TagIndex.FileName);
        byte[] garbage = [.. Enumerable.Range(0, 200).Select(i => (byte)(i * 7))];
        File.WriteAllBytes(db, garbage);

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "1", "2026-10-29", "a #work") >= 0);
        Assert.True(HasLine(main, DbUnreadable));
        Assert.False(HasLine(main, "Linked notes"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[red]X[/]") + DbUnreadable, output);
        Assert.Equal(garbage, File.ReadAllBytes(db));
    }

    // AC-8
    [Fact]
    public void Current_MarkupAndControlsInText_ShownLiterally()
    {
        AddTodo("[red]x[/] #a\nsecond\u001b", Today);
        WriteNote("[b]", "#a");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "1", "2026-10-29", "[red]x[/] #a second�") >= 0);
        Assert.True(RowIndex(main, "[b]", "#a") >= 0);
    }

    // AC-9
    [Theory]
    [InlineData("/current foo")]
    [InlineData("/CURRENT")]
    public void Current_IgnoresArgument_AndCase(string command)
    {
        AddTodo("a", Today);

        string[] main = Main(command);

        Assert.True(HasLine(main, "Today, 2026-10-29"));
        Assert.True(RowIndex(main, "1", "2026-10-29", "a") >= 0);
    }

    // 028 AC-3: title, then appointments, then Due, then linked notes.
    [Fact]
    public void Current_Appointments_TableBeforeDueAndLinks()
    {
        AddTodo("call #work", Today);
        WriteNote("Plan", "#work");
        WriteCalendar(
            "work.ics",
            Event("Standup", ":20261029T080000Z", ":20261029T083000Z", "LOCATION:Room 1\r\n"),
            Event("Holiday", ";VALUE=DATE:20261029"),
            Event("Late", ":20261029T220000", ":20261030T010000"),
            Event("Early", ":20261028T230000", ":20261029T010000"),
            Event(null, ":20261029T120000", ":20261029T130000"),
            Event("Midnight", ":20261029T230000", ":20261030T000000"));

        string[] main = Main("/current");

        int title = LineIndex(main, "Today, 2026-10-29");
        int appointments = LineIndex(main, "Appointments");
        int holiday = RowIndex(main, "all day", "Holiday", "");
        int early = RowIndex(main, "…-01:00", "Early", "");
        int standup = RowIndex(main, "09:00-09:30", "Standup", "Room 1");
        int noTitle = RowIndex(main, "12:00-13:00", "(no title)", "");
        int late = RowIndex(main, "22:00-…", "Late", "");
        int midnight = RowIndex(main, "23:00-00:00", "Midnight", "");
        int due = RowIndex(main, "1", "2026-10-29", "call #work");
        int linked = RowIndex(main, "Plan", "#work");
        Assert.True(title >= 0 && appointments > title && holiday > appointments);
        Assert.True(early > holiday && standup > early && noTitle > standup && late > noTitle && midnight > late);
        Assert.True(due > midnight && linked > due);
        Assert.Contains(main, l => l.Contains('╭') && l.Contains('╮'));
    }

    // 028 AC-3: colours.
    [Fact]
    public void Current_Appointments_Colours()
    {
        WriteCalendar(
            "a.ics",
            Event("Standup", ":20261029T090000", ":20261029T093000", "LOCATION:Room 1\r\n"),
            Event("Holiday", ";VALUE=DATE:20261029"));

        (int exitCode, string output) = Run(true, "/current");

        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[steelblue1]X[/]") + "09:00-09:30", output);
        Assert.Contains(Style("[steelblue1]X[/]") + "all day", output);
        Assert.Contains(Style("[grey]X[/]") + "Room 1", output);
        Assert.Contains(Style("[grey]X[/]") + "Appointments", output);
    }

    // 028 AC-3: appointments show even when nothing is due; Due behaviour unchanged.
    [Fact]
    public void Current_Appointments_ShownWhenNothingDue()
    {
        WriteCalendar("a.ics", Event("Standup", ":20261029T090000"));

        string[] main = Main("/current");

        int standup = RowIndex(main, "09:00-09:00", "Standup", "");
        Assert.True(standup >= 0);
        Assert.True(LineIndex(main, NothingDue) > standup);
        Assert.False(HasLine(main, "Linked notes"));
    }

    // 028 AC-3: a bad todos.json still shows the red hint, and the appointments in the main view.
    [Fact]
    public void Current_BadTodos_StillShowsAppointments()
    {
        WriteCalendar("a.ics", Event("Standup", ":20261029T090000"));
        File.WriteAllBytes(Path.Combine(root, TodoStore.FileName), [0x7b, 0x00]);

        string[] frame = Frame("/current");

        Assert.Contains("Can't read todos.json", frame[^4]);
        string[] main = frame[..^4].Select(l => l[..Math.Min(l.Length, MainWidth)]).ToArray();
        Assert.True(RowIndex(main, "09:00-09:00", "Standup", "") >= 0);
    }

    // 028 AC-4
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Current_NoAppointments_ShowsGreyLineWithFolder(bool folder, bool otherDay)
    {
        if (folder)
        {
            Directory.CreateDirectory(CalendarFolder);
        }

        if (otherDay)
        {
            WriteCalendar("a.ics", Event("Tomorrow", ":20261030T090000"));
        }

        string[] main = Main("/current");

        Assert.Contains(NoSpace(EmptyCalendar), NoSpace(string.Concat(main)));
        Assert.False(HasLine(main, "Appointments"));
        Assert.False(HasLine(main, "Can't read"));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + "No appointments today.", output);
    }

    // 028 AC-4: the folder path is escaped.
    [Fact]
    public void Current_NoAppointments_PathIsEscaped()
    {
        string odd = Path.Combine(parent, "[red]odd");
        CommandRegistry registry = CommandRegistry.CreateDefault();
        TodoStore todos = new TodoStore(odd);
        CurrentCommands.Register(
            registry, todos, new TagIndex(odd), new NoteStore(odd), new CalendarStore(odd, Zone), () => Today);
        TestConsole console = new TestConsole().Interactive();
        console.Profile.Width = Width;
        console.Profile.Height = Height;
        console.Input.PushTextWithEnter("/current");
        console.Input.PushTextWithEnter("/quit");

        int exitCode = new App(console, registry).Run();

        Assert.Equal(0, exitCode);
        Assert.Contains(NoSpace("[red]odd"), NoSpace(Ansi.Replace(console.Output, string.Empty)));
    }

    // 028 AC-5
    [Fact]
    public void Current_BadFiles_RedLines_OthersShown_FilesUnchanged()
    {
        WriteCalendar("good.ics", Event("Standup", ":20261029T090000"));
        string bad = Path.Combine(CalendarFolder, "notes.ics");
        File.WriteAllText(bad, "not a calendar [red]");
        string big = Path.Combine(CalendarFolder, "big.ics");
        using (FileStream stream = new FileStream(big, FileMode.CreateNew))
        {
            stream.SetLength(6L * 1024 * 1024);
        }

        byte[] badBytes = File.ReadAllBytes(bad);

        string[] main = Main("/current", "/current");

        int table = RowIndex(main, "09:00-09:00", "Standup", "");
        int bigLine = LineIndex(main, "Can't read calendar/big.ics");
        int notesLine = LineIndex(main, "Can't read calendar/notes.ics");
        Assert.True(table >= 0 && bigLine > table && notesLine > bigLine);
        (int exitCode, string output) = Run(true, "/current", "/help");
        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[red]X[/]") + "Can't read calendar/notes.ics", output);
        Assert.Contains("Today's appointments, due todos and linked notes", output);
        Assert.Equal(badBytes, File.ReadAllBytes(bad));
        Assert.Equal(6L * 1024 * 1024, new FileInfo(big).Length);
        string[] entries = Directory.GetFileSystemEntries(CalendarFolder)
            .Select(p => Path.GetRelativePath(CalendarFolder, p))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["big.ics", "good.ics", "notes.ics"], entries);
    }

    // 028 AC-5: only bad files, so the red lines go under the empty line.
    [Fact]
    public void Current_OnlyBadFile_RedLineUnderEmptyLine()
    {
        Directory.CreateDirectory(CalendarFolder);
        File.WriteAllText(Path.Combine(CalendarFolder, "x.ics"), "nope");

        string[] main = Main("/current");

        int empty = LineIndex(main, "No appointments today.");
        int red = LineIndex(main, "Can't read calendar/x.ics");
        Assert.True(empty >= 0 && red > empty);
    }

    // 028 AC-6
    [Fact]
    public void Current_Appointments_MarkupAndControls_ShownLiterally()
    {
        WriteCalendar(
            "a.ics",
            Event("[red]x[/]\u001b\\nsecond", ":20261029T090000", null, "LOCATION:[b]here\\n\u0007\r\n"));
        File.WriteAllText(Path.Combine(CalendarFolder, "[b].ics"), "bad");

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "09:00-09:00", "[red]x[/]� second", "[b]here �") >= 0);
        Assert.True(HasLine(main, "Can't read calendar/[b].ics"));
    }

    // 028 AC-7
    [Fact]
    public void Current_Appointments_AtMost15Rows()
    {
        string[] events = Enumerable.Range(0, 20)
            .Select(i => Event($"E{i:00}", $":20261029T{i:00}0000"))
            .ToArray();
        WriteCalendar("a.ics", events);

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "13:00-13:00", "E13", "") >= 0);
        Assert.Equal(-1, RowIndex(main, "14:00-14:00", "E14", ""));
        Assert.True(RowIndex(main, "", "… 6 more", "") > RowIndex(main, "13:00-13:00", "E13", ""));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + "… 6 more", output);
    }

    // 028 AC-7: exactly 15 fit without a "more" row.
    [Fact]
    public void Current_Appointments_Exactly15_AllShown()
    {
        string[] events = Enumerable.Range(0, 15)
            .Select(i => Event($"E{i:00}", $":20261029T{i:00}0000"))
            .ToArray();
        WriteCalendar("a.ics", events);

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "14:00-14:00", "E14", "") >= 0);
        Assert.False(HasLine(main, "more"));
    }

    // ---- 031: Exchange calendar via published link ----

    private static readonly DateTime FeedNow = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private string CachePath => Path.Combine(root, CalendarFeed.CacheFileName);

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private FakeHttpHandler UseFeed(FakeHttpHandler handler)
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        feed = new CalendarFeed(root, link, handler, () => FeedNow, TimeSpan.FromMilliseconds(100));
        return handler;
    }

    private void WriteCache(string text, DateTime atUtc)
    {
        File.WriteAllText(CachePath, text);
        File.SetLastWriteTimeUtc(CachePath, atUtc);
    }

    private static void AssertNoSecret(string text)
    {
        Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, text, StringComparison.Ordinal);
    }

    private void AssertNoSecretInFiles()
    {
        foreach (string path in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) != CalendarLink.FileName)
            {
                AssertNoSecret(File.ReadAllText(path));
            }
        }
    }

    // AC-4: feed events and file events in one table, sorted together.
    [Fact]
    public void Current_Feed_MergedWithFiles_Sorted()
    {
        WriteCalendar("a.ics", Event("File lunch", ":20261029T120000", ":20261029T130000"));
        UseFeed(FakeHttpHandler.Ok(Calendar(
            Event("Feed standup", ":20261029T080000Z", ":20261029T081500Z", "LOCATION:Room 1\r\n"),
            Event("Feed holiday", ";VALUE=DATE:20261029"),
            Event("Feed tomorrow", ":20261030T080000Z"))));

        string[] main = Main("/current");

        int holiday = RowIndex(main, "all day", "Feed holiday", "");
        int standup = RowIndex(main, "09:00-09:15", "Feed standup", "Room 1");
        int lunch = RowIndex(main, "12:00-13:00", "File lunch", "");
        Assert.True(holiday >= 0 && standup > holiday && lunch > standup);
        Assert.Single(main, l => l.Contains("Appointments", StringComparison.Ordinal));
        Assert.False(HasLine(main, "Feed tomorrow"));
        Assert.False(HasLine(main, "Exchange calendar"));
        Assert.False(HasLine(main, "No appointments today"));
    }

    // AC-4: feed text is cleaned and escaped like file text.
    [Fact]
    public void Current_Feed_TextCleanedAndEscaped()
    {
        UseFeed(FakeHttpHandler.Ok(Calendar(
            Event("[red]x[/]\u0007 feed", ":20261029T080000Z", extra: "LOCATION:[b]there\u001b\r\n"))));

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "09:00-09:00", "[red]x[/]\uFFFD feed", "[b]there\uFFFD") >= 0);
    }

    // AC-4: the 15-row limit counts feed and file events together.
    [Fact]
    public void Current_Feed_RowLimit_CountsBoth()
    {
        WriteCalendar("a.ics", Enumerable.Range(0, 10)
            .Select(i => Event($"F{i:00}", $":20261029T{i:00}0000")).ToArray());
        UseFeed(FakeHttpHandler.Ok(Calendar(Enumerable.Range(10, 10)
            .Select(i => Event($"X{i:00}", $":20261029T{i:00}3000")).ToArray())));

        string[] main = Main("/current");

        Assert.True(RowIndex(main, "13:30-13:30", "X13", "") >= 0);
        Assert.Equal(-1, RowIndex(main, "14:30-14:30", "X14", ""));
        Assert.True(RowIndex(main, "", "… 6 more", "") >= 0);
    }

    // AC-4: with a link set and no events today, the empty line still shows.
    [Fact]
    public void Current_Feed_NoEventsToday_EmptyLine()
    {
        UseFeed(FakeHttpHandler.Ok(Calendar(Event("Tomorrow", ":20261030T080000Z"))));

        string[] main = Main("/current");

        Assert.Contains(NoSpace(EmptyCalendar), NoSpace(string.Concat(main)));
        Assert.False(HasLine(main, "Exchange calendar"));
    }

    // AC-5: offline with a cache: its events, then a grey line with the cache time in local time.
    [Fact]
    public void Current_Feed_Stale_GreyLine_AndCachedEvents()
    {
        UseFeed(FakeHttpHandler.Status(System.Net.HttpStatusCode.ServiceUnavailable));
        WriteCache(Calendar(Event("Cached meeting", ":20261029T140000Z")), new DateTime(2026, 10, 28, 17, 5, 0, DateTimeKind.Utc));
        const string Line = "Exchange calendar offline, showing the copy from 2026-10-28 18:05.";

        string[] main = Main("/current");

        int row = RowIndex(main, "15:00-15:00", "Cached meeting", "");
        Assert.True(row >= 0);
        Assert.True(LineIndex(main, Line) > row);
        Assert.True(LineIndex(main, Line) < LineIndex(main, NothingDue));
        (_, string output) = Run(true, "/current");
        Assert.Contains(Style("[grey]X[/]") + Line, output);
    }

    // AC-5, AC-6: every failure reason gives a red line and no secret anywhere.
    [Theory]
    [MemberData(nameof(CalendarFeedTests.Failures), MemberType = typeof(CalendarFeedTests))]
    public void Current_Feed_Failed_RedLine_NoSecret(string failure)
    {
        UseFeed(CalendarFeedTests.FailingHandler(failure));
        string line = $"Can't download the Exchange calendar ({CalendarFeedTests.Reason(failure)}).";

        (int exitCode, string output) = Run(true, "/current");

        Assert.Equal(0, exitCode);
        Assert.Contains(Style("[red]X[/]") + line, output);
        Assert.Contains("No appointments today", output);
        Assert.Contains(NothingDue, output);
        AssertNoSecret(output);
        AssertNoSecretInFiles();
    }

    // AC-5, AC-6: a fresh download adds no status line and shows no secret.
    [Fact]
    public void Current_Feed_Fresh_NoStatusLine_NoSecret()
    {
        UseFeed(FakeHttpHandler.Ok(Calendar(Event("Standup", ":20261029T080000Z"))));

        (int exitCode, string output) = Run(false, "/current", "/current");

        Assert.Equal(0, exitCode);
        Assert.Contains("Standup", output);
        Assert.DoesNotContain("Exchange calendar", output);
        AssertNoSecret(output);
        AssertNoSecretInFiles();
    }

    // AC-2, AC-5: no link means no request and no line.
    [Fact]
    public void Current_Feed_NotConfigured_NoRequest_NoLine()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Calendar(Event("Standup", ":20261029T080000Z")));
        feed = new CalendarFeed(root, new CalendarLink(root), handler, () => FeedNow);

        string[] main = Main("/current");

        Assert.Equal(0, handler.Requests);
        Assert.False(HasLine(main, "Standup"));
        Assert.False(HasLine(main, "Exchange calendar"));
    }

    // AC-5: /current keeps working and the loop goes on after a failure.
    [Fact]
    public void Current_Feed_Failure_LoopGoesOn()
    {
        UseFeed(FakeHttpHandler.Throw());

        string[] main = Main("/current", "/current");

        Assert.True(HasLine(main, "Can't download the Exchange calendar (network error)."));
    }

    // 044 AC-5: a hand-built Stale view without a cache time draws no offline line and doesn't throw.
    [Fact]
    public void Render_StaleWithoutCacheTime_NoOfflineLine()
    {
        CurrentView view = new CurrentView(
            Today,
            Zone,
            [],
            "/nowhere/calendar",
            [],
            CalendarFeedStatus.Stale,
            null,
            "timed out",
            false,
            [],
            [],
            false);

        CommandResult result = CurrentCommands.Render(view);
        TestConsole console = new TestConsole();
        console.Profile.Width = Width;
        console.Write(Assert.IsAssignableFrom<Spectre.Console.Rendering.IRenderable>(result.Message));

        Assert.DoesNotContain("offline", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Exchange calendar", console.Output, StringComparison.Ordinal);
        Assert.Contains("No appointments today", console.Output, StringComparison.Ordinal);
    }

    // 046 AC-3: the cache time is shown in view.Zone, not the machine's zone.
    [Fact]
    public void Render_StaleCacheTime_UsesViewZone()
    {
        DateTime cachedAt = new(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        TimeZoneInfo plusOne = TimeZoneInfo.CreateCustomTimeZone(
            "Test+01:00", TimeSpan.FromHours(1), "Test+01:00", "Test+01:00");
        TimeZoneInfo plusFiveFortyFive = TimeZoneInfo.CreateCustomTimeZone(
            "Test+05:45", new TimeSpan(5, 45, 0), "Test+05:45", "Test+05:45");

        string one = RenderStale(plusOne, cachedAt);
        string other = RenderStale(plusFiveFortyFive, cachedAt);

        Assert.Contains("showing the copy from 2026-10-29 11:00.", one, StringComparison.Ordinal);
        Assert.Contains("showing the copy from 2026-10-29 15:45.", other, StringComparison.Ordinal);
    }

    private static string RenderStale(TimeZoneInfo zone, DateTime cachedAtUtc)
    {
        CurrentView view = new CurrentView(
            Today,
            zone,
            [],
            "/nowhere/calendar",
            [],
            CalendarFeedStatus.Stale,
            cachedAtUtc,
            "timed out",
            false,
            [],
            [],
            false);

        CommandResult result = CurrentCommands.Render(view);
        TestConsole console = new TestConsole();
        console.Profile.Width = Width;
        console.Write(Assert.IsAssignableFrom<Spectre.Console.Rendering.IRenderable>(result.Message));
        return console.Output;
    }
}
