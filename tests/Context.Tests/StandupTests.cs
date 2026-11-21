using System.Net;

namespace Context.Tests;

// 265: the standup text in Core (AC-1 to AC-6), Times and todos.json only read (AC-11), and the section write (AC-8, AC-9).
public sealed class StandupTests : IDisposable
{
    // A Monday; the Friday before is 2026-11-13.
    private static readonly DateOnly Today = new(2026, 11, 16);

    // A fixed non-UTC zone (UTC+1, no DST), so local midnight is 23:00 UTC the day before.
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private static readonly DateTime Now = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private SortedDictionary<string, string> timesBefore;
    private DateTime clock = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    public StandupTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-standup-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(times);
        timesBefore = FileSnapshot.Take(times);
    }

    // AC-11: no test changes Times' folder; the snapshot is retaken only when a test writes a Times file itself.
    public void Dispose()
    {
        try
        {
            Assert.Equal(timesBefore, FileSnapshot.Take(times));
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private TodoStore Todos() => new(root, () => clock);

    private CalendarStore Calendar() => new(root, Zone);

    private string Compose(CalendarFeed? feed = null) =>
        Standup.Compose(Todos(), Calendar(), feed, times, Today, Zone);

    private void WriteTimes(DateOnly day, string json)
    {
        File.WriteAllText(Path.Combine(times, DailyNote.NameFor(day) + ".json"), json);
        timesBefore = FileSnapshot.Take(times);
    }

    // A Times day file from "kind HH:mm [name] [workItem]" items; a name's "~" stands for a line break.
    private void WriteEntries(DateOnly day, params string[] entries)
    {
        IEnumerable<string> items = entries.Select(e =>
        {
            string[] parts = e.Split(' ');
            string name = parts.Length > 2
                ? ",\"name\":\"" + parts[2].Replace("~", "\\n", StringComparison.Ordinal) + "\""
                : string.Empty;
            string item = parts.Length > 3 ? ",\"workItem\":" + parts[3] : string.Empty;
            return $"{{\"kind\":\"{parts[0]}\",\"time\":\"{parts[1]}\"{name}{item}}}";
        });
        WriteTimes(day, "{\"entries\":[" + string.Join(',', items) + "]}");
    }

    private void WriteFile(string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // Adds a todo and marks it done at the given UTC instant.
    private void Done(string text, DateTime utc)
    {
        TodoStore todos = Todos();
        TodoItem item = todos.Add(text, Today.AddDays(-10));
        clock = utc;
        todos.Complete(item.Id);
        clock = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    }

    // AC-1: three blocks in order, \n line ends, ending in \n; nothing anywhere gives the empty lines.
    [Fact]
    public void Compose_Empty_ThreeBlocks()
    {
        string text = Compose();

        Assert.Equal(
            "Yesterday (2026-11-13)\n- No Times bookings in the last 7 days.\n\nToday\n- Nothing planned.\n\nBlocked\n- Nothing blocked.\n",
            text);
        Assert.DoesNotContain('\r', text);
        Assert.False(Directory.Exists(root));
    }

    // AC-2, AC-3: Monday looks back over the weekend to Friday; groups in order, work item, open group, clean names.
    [Fact]
    public void Compose_FridayToMonday_GroupsInOrder()
    {
        WriteEntries(
            Today.AddDays(-3),
            "arrival 08:00",
            "checkpoint 08:00 Review 12",
            "checkpoint 09:30 Mail",
            "checkpoint 10:00 Plan~##_Heading\\u0007x");

        Assert.EndsWith("\n\nToday\n- Nothing planned.\n\nBlocked\n- Nothing blocked.\n", Compose(), StringComparison.Ordinal);
        string yesterday = Compose().Split("\n\n")[0];
        string[] lines = yesterday.Split('\n');
        Assert.Equal("Yesterday (2026-11-13)", lines[0]);
        Assert.Equal("- #12 Review: 1:30", lines[1]);
        Assert.Equal("- Mail: 0:30", lines[2]);
        Assert.StartsWith("- Plan ##_Heading", lines[3], StringComparison.Ordinal);
        Assert.EndsWith(" (open)", lines[3], StringComparison.Ordinal);
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain('\u0007', yesterday);

        // The same group lines as the day's end writes under ## Worked.
        TimesDay day = TimesDay.Read(times, Today.AddDays(-3));
        Assert.Equal(string.Concat(day.Groups.Select(DayEnd.FormatGroup)), string.Join('\n', lines[1..]) + "\n");
    }

    // AC-2: an absence day and a day without entries are skipped.
    [Fact]
    public void Compose_AbsenceSkipped()
    {
        WriteTimes(Today.AddDays(-1), "{\"absence\":\"sick\"}");
        WriteTimes(Today.AddDays(-2), "{\"entries\":[]}");
        WriteEntries(Today.AddDays(-4), "arrival 08:00", "checkpoint 08:00 A", "leaving 09:00");

        Assert.StartsWith("Yesterday (2026-11-12)\n- A: 1:00\n\nToday\n", Compose(), StringComparison.Ordinal);
    }

    // AC-2: an unreadable file met first is the last workday and says so; an older good day is not used.
    [Fact]
    public void Compose_UnreadableFirst()
    {
        WriteEntries(Today.AddDays(-4), "arrival 08:00", "checkpoint 08:00 A");
        WriteTimes(Today.AddDays(-3), "not json");

        Assert.StartsWith(
            "Yesterday (2026-11-13)\n- Can't read Times' day file for 2026-11-13.\n\nToday\n",
            Compose(),
            StringComparison.Ordinal);
    }

    // AC-2: nothing in 7 days: the latest weekday before today (a Monday falls back to Friday, a Sunday to Friday).
    [Fact]
    public void LastWorkday_NoBookings_WeekendFallback()
    {
        WriteEntries(Today.AddDays(-8), "arrival 08:00");

        Assert.Equal((new DateOnly(2026, 11, 13), (TimesDay?)null), Standup.LastWorkday(times, Today));
        Assert.Equal(new DateOnly(2026, 11, 13), Standup.LastWorkday(null, new DateOnly(2026, 11, 15)).Day);
        Assert.Equal(new DateOnly(2026, 11, 16), Standup.LastWorkday(null, new DateOnly(2026, 11, 17)).Day);
        Assert.Equal(new DateOnly(2026, 11, 13), Standup.LastWorkday(null, Today).Day);
    }

    // AC-3: done todos from local midnight of the last workday up to just before local midnight of today, oldest
    // first, in a non-UTC zone.
    [Fact]
    public void Compose_DoneTodos_LocalMidnightBoundaries()
    {
        // Friday 00:00 local is Thursday 23:00 UTC.
        Done("before friday", new DateTime(2026, 11, 12, 22, 59, 59, DateTimeKind.Utc));
        Done("friday start", new DateTime(2026, 11, 12, 23, 0, 0, DateTimeKind.Utc));
        Done("sunday late", new DateTime(2026, 11, 15, 22, 59, 59, DateTimeKind.Utc));
        Done("monday start", new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc));
        Done("saturday", new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc));
        WriteEntries(Today.AddDays(-3), "arrival 08:00", "checkpoint 08:00 A", "leaving 09:00");

        Assert.StartsWith(
            "Yesterday (2026-11-13)\n- A: 1:00\n- Done: friday start\n- Done: saturday\n- Done: sunday late\n\nToday\n",
            Compose(),
            StringComparison.Ordinal);
    }

    // AC-3: done todos alone, without Times data, and a done text cleaned to one line.
    [Fact]
    public void Compose_DoneOnly_OneLine()
    {
        Done("a\nb", new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith(
            "Yesterday (2026-11-13)\n- No Times bookings in the last 7 days.\n- Done: a b\n\nToday\n",
            Compose(),
            StringComparison.Ordinal);
    }

    // AC-3: a readable day whose entries have no groups, and no done todos: nothing logged.
    [Fact]
    public void Compose_NoGroups_NothingLogged()
    {
        WriteEntries(Today.AddDays(-1), "arrival 08:00", "leaving 09:00");

        Assert.StartsWith("Yesterday (2026-11-15)\n- Nothing logged.\n\nToday\n", Compose(), StringComparison.Ordinal);
    }

    // AC-4: appointment lines exactly as the daily note writes them (unreadable file and failed feed too), then the
    // open due todos that aren't waiting-for todos; AC-5: a future waiting-for todo is in Blocked only.
    [Fact]
    public void Compose_TodayAndBlocked()
    {
        TodoStore todos = Todos();
        todos.Add("overdue", new DateOnly(2026, 11, 1));
        todos.Add("due today", Today);
        todos.Add("tomorrow", Today.AddDays(1));
        todos.Add("waiting: @[Anna] the report", Today.AddDays(5));
        todos.Add("waiting: @[Ben]", Today.AddDays(-1));
        WriteFile(Path.Combine(CalendarStore.FolderName, "work.ics"),
            "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Standup\r\nLOCATION:Room\r\nDTSTART:20261116T093000\r\n"
            + "DTEND:20261116T094500\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        WriteFile(Path.Combine(CalendarStore.FolderName, "bad.ics"), "not a calendar");
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        CalendarFeed feed = new CalendarFeed(root, link, FakeHttpHandler.Status(HttpStatusCode.NotFound), () => Now);

        string text = Compose(feed);

        string daily = DailyNote.Compose(Todos(), Calendar(), feed, Today);
        string appointments = daily.Split("## Appointments\n")[1].Split("\n## Todos")[0];
        Assert.Equal(
            "- 09:30-09:45 Standup (Room)\n- Can't read calendar/bad.ics.\n- Can't download the Exchange calendar (HTTP 404).\n",
            appointments);
        Assert.Equal(
            "Yesterday (2026-11-13)\n- No Times bookings in the last 7 days.\n\nToday\n"
            + appointments
            + "- overdue (due 2026-11-01)\n- due today\n\nBlocked\n"
            + "- Ben (follow up 2026-11-15)\n- Anna: the report (follow up 2026-11-21)\n",
            text);
    }

    // AC-4: the ok feed's events are listed, and with appointments only there is no "Nothing planned.".
    [Fact]
    public void Compose_FeedOk_Listed()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        CalendarFeed feed = new CalendarFeed(
            root,
            link,
            FakeHttpHandler.Ok("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Sync\r\nDTSTART:20261116T100000\r\n"
                + "DTEND:20261116T103000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n"),
            () => Now);

        Assert.Contains("\n\nToday\n- 10:00-10:30 Sync\n\nBlocked\n", Compose(feed), StringComparison.Ordinal);
    }

    // AC-6: a bad todos.json: its line in all three blocks; the Times and appointment lines still show.
    [Fact]
    public void Compose_TodosUnreadable()
    {
        WriteFile(TodoStore.FileName, "{ broken");
        WriteFile(Path.Combine(CalendarStore.FolderName, "bad.ics"), "not a calendar");
        WriteEntries(Today.AddDays(-3), "arrival 08:00", "checkpoint 08:00 A", "leaving 09:00");

        string text = Standup.Compose(Todos(), Calendar(), null, times, Today, Zone, out bool unreadable);

        Assert.True(unreadable);
        Assert.Equal(
            "Yesterday (2026-11-13)\n- A: 1:00\n- Can't read todos.json.\n\nToday\n- Can't read calendar/bad.ics.\n"
            + "- Can't read todos.json.\n\nBlocked\n- Can't read todos.json.\n",
            text);
        Assert.Equal("{ broken", File.ReadAllText(Path.Combine(root, TodoStore.FileName)));
    }

    // AC-11: composing writes nothing: todos.json, the Times folder and the data folder stay as they were, and
    // neither context.db nor settings.json appears.
    [Fact]
    public void Compose_WritesNothing()
    {
        Todos().Add("waiting: @[Anna] x", Today);
        Done("done", new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));
        WriteEntries(Today.AddDays(-3), "arrival 08:00", "checkpoint 08:00 A");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Compose();

        Assert.Equal(before, FileSnapshot.Take(root));
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
        Assert.False(File.Exists(Path.Combine(root, SettingsStore.FileName)));
    }

    // AC-8: no note: it is created first, then the section appended after a blank line; twice gives the same bytes.
    [Fact]
    public void RefreshStandup_MissingNote_CreatesAndAppends_SameBytesTwice()
    {
        NoteStore notes = new NoteStore(root);
        DayEnd dayEnd = new DayEnd(notes, Todos(), Calendar(), null, times, null);

        DayEndResult first = dayEnd.RefreshStandup(Today, Zone);
        string path = Path.Combine(root, "2026-11-16.md");
        byte[] once = File.ReadAllBytes(path);
        DayEndResult second = dayEnd.RefreshStandup(Today, Zone);

        Assert.Equal(DayEndKind.Done, first.Kind);
        Assert.True(first.Changed);
        Assert.Equal(DayEndKind.Done, second.Kind);
        Assert.False(second.Changed);
        Assert.Equal(once, File.ReadAllBytes(path));
        Assert.Equal(
            DailyNote.Compose(Todos(), Calendar(), null, Today) + "\n## Standup\n\n" + Compose(),
            File.ReadAllText(path));
    }

    // AC-8: an existing section is replaced up to the next heading; the rest stays byte for byte, CRLF line ends kept.
    [Fact]
    public void RefreshStandup_ReplacesSection()
    {
        WriteFile("2026-11-16.md", "# 2026-11-16\r\n\r\nnotes\r\n\r\n## Standup\r\n\r\nold\r\nlines\r\n\r\n## Later\r\nkeep\r\n");
        DayEnd dayEnd = new DayEnd(new NoteStore(root), Todos(), Calendar(), null, times, null);

        Assert.Equal(DayEndKind.Done, dayEnd.RefreshStandup(Today, Zone).Kind);

        string text = File.ReadAllText(Path.Combine(root, "2026-11-16.md"));
        Assert.Equal(
            ("# 2026-11-16\n\nnotes\n\n## Standup\n\n" + Compose() + "\n## Later\nkeep\n").Replace("\n", "\r\n", StringComparison.Ordinal),
            text);
    }

    // AC-9: a sensitive daily note is neither read nor written, and nothing is created.
    [Fact]
    public void RefreshStandup_Sensitive_Untouched()
    {
        WriteFile("2026-11-16.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        DayEnd dayEnd = new DayEnd(new NoteStore(root), Todos(), Calendar(), null, times, null);

        DayEndResult result = dayEnd.RefreshStandup(Today, Zone);

        Assert.Equal(DayEndKind.Sensitive, result.Kind);
        Assert.Equal("2026-11-16", result.Name);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 361 AC-10: the standup reads todos.json only; a todo found only in the archive is not shown.
    [Fact]
    public void Compose_ArchivedTodo_NotShown()
    {
        TodoArchiveFile.Archive(root, Guid.NewGuid(), "archived friday", new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith(
            "Yesterday (2026-11-13)\n- No Times bookings in the last 7 days.\n\nToday\n",
            Compose(),
            StringComparison.Ordinal);
    }
}
