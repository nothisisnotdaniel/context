namespace Context.Tests;

public sealed class ReviewViewTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    // UTC+1: a UTC time late in the evening falls on the next day in this zone.
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;

    public ReviewViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-reviewview-" + Guid.NewGuid().ToString("N"));
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

    private ReviewView Build(CalendarFeed? feed = null) =>
        ReviewView.Build(new NoteStore(root), new TodoStore(root), new CalendarStore(root, Zone), feed, Today);

    private string WriteNote(string name, DateTime modifiedUtc, string extension = ".md")
    {
        string path = Path.Combine([root, .. name.Split('/')]) + extension;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# " + name + "\n");
        File.SetLastWriteTimeUtc(path, modifiedUtc);
        return path;
    }

    private void WriteTodos(string json)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), json);
    }

    private static string Todo(string text, string due, string? done = null, string created = "2026-10-01T08:00:00Z", string? repeat = null)
    {
        string doneField = done is null ? string.Empty : $",\"done\":\"{done}\"";
        string repeatField = repeat is null ? string.Empty : $",\"repeat\":\"{repeat}\"";
        return $"{{\"text\":\"{text}\",\"due\":\"{due}\",\"created\":\"{created}\"{doneField}{repeatField}}}";
    }

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string title, string start, string end, string extra = "") =>
        $"BEGIN:VEVENT\r\nUID:{title}\r\nSUMMARY:{title}\r\nDTSTART:{start}\r\nDTEND:{end}\r\n{extra}END:VEVENT\r\n";

    private static DateTime Utc(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    // AC-1: the 7-day window counted in the calendar's zone; boundaries today-7, today-6, today and after today.
    [Fact]
    public void ChangedNotes_WindowInZone_Boundaries()
    {
        // 22:30 UTC on 10-22 is 23:30 on 10-22 (today-7) in the zone: out.
        WriteNote("Old", Utc(10, 22, 22, 30));
        // 23:30 UTC on 10-22 is 00:30 on 10-23 (today-6): in.
        WriteNote("Edge", Utc(10, 22, 23, 30));
        // 22:59 UTC on 10-29 is 23:59 today: in.
        WriteNote("Late", Utc(10, 29, 22, 59));
        // 23:30 UTC on 10-29 is 00:30 on 10-30 (after today): out.
        WriteNote("Future", Utc(10, 29, 23, 30));

        ReviewView view = Build();

        Assert.Equal(["Late", "Edge"], view.ChangedNotes.Select(n => n.Name));
        Assert.Equal(new DateTime(2026, 10, 29, 23, 59, 0), view.ChangedNotes[0].Modified);
        Assert.Equal(new DateTime(2026, 10, 23, 0, 30, 0), view.ChangedNotes[1].Modified);
        Assert.Equal(new DateOnly(2026, 10, 23), view.From);
        Assert.Equal(Today, view.To);
        Assert.Same(Zone, view.Zone);
    }

    // AC-1: subpages by full name, newest first then name ignoring case; sealed notes never.
    [Fact]
    public void ChangedNotes_SubpagesSortedSealedLeftOut()
    {
        DateTime same = Utc(10, 27, 10);
        WriteNote("beta", same);
        WriteNote("Alpha", same);
        WriteNote("Alpha/Child", Utc(10, 28, 10));
        WriteNote("Secret", Utc(10, 29, 8), NoteStore.SensitiveExtension);

        ReviewView view = Build();

        Assert.Equal(["Alpha/Child", "Alpha", "beta"], view.ChangedNotes.Select(n => n.Name));
        Assert.DoesNotContain(view.ChangedNotes, n => n.Name == "Secret");
    }

    // AC-2: done todos in the window (in the zone) in ListDone order; overdue is due < today in List order.
    [Fact]
    public void Todos_DoneInWindowAndOverdue()
    {
        WriteTodos("["
            + Todo("done today", "2026-10-20", done: "2026-10-29T07:00:00Z") + ","
            + Todo("done edge", "2026-10-20", done: "2026-10-22T23:30:00Z") + ","
            + Todo("done too old", "2026-10-20", done: "2026-10-22T22:30:00Z") + ","
            + Todo("done tomorrow", "2026-10-20", done: "2026-10-29T23:30:00Z") + ","
            + Todo("late b", "2026-10-28") + ","
            + Todo("late a", "2026-10-01", repeat: "day") + ","
            + Todo("due today", "2026-10-29") + ","
            + Todo("later", "2026-11-02")
            + "]");

        ReviewView view = Build();

        Assert.False(view.TodosUnreadable);
        Assert.Equal(["done today", "done edge"], view.DoneTodos.Select(t => t.Text));
        Assert.Equal(new DateOnly(2026, 10, 23), view.DoneDate(view.DoneTodos[1]));
        Assert.Equal(["late a", "late b"], view.Overdue.Select(t => t.Text));
    }

    // AC-2: an unreadable todos.json sets the flag, empties both lists and still fills the rest.
    [Fact]
    public void Todos_Unreadable_RestStillFilled()
    {
        WriteTodos("not json");
        WriteNote("A", Utc(10, 28, 10));
        WriteCalendar("a.ics", Calendar(Event("Plan", "20261030T090000", "20261030T100000")));

        ReviewView view = Build();

        Assert.True(view.TodosUnreadable);
        Assert.Empty(view.DoneTodos);
        Assert.Empty(view.Overdue);
        Assert.Equal("A", Assert.Single(view.ChangedNotes).Name);
        Assert.Equal("Plan", Assert.Single(view.Days[0].Appointments).Title);
    }

    // AC-3: seven days today+1 to today+7; boundaries today, today+1, today+7, today+8; recurring and multi-day events.
    [Fact]
    public void Days_SevenAhead_RecurringAndMultiDay()
    {
        WriteCalendar("a.ics", Calendar(
            Event("Today", "20261029T090000", "20261029T100000"),
            Event("Tomorrow", "20261030T090000", "20261030T100000"),
            Event("Last", "20261105T090000", "20261105T100000"),
            Event("Beyond", "20261106T090000", "20261106T100000"),
            Event("Daily", "20261020T070000", "20261020T071500", "RRULE:FREQ=DAILY;COUNT=30\r\n"),
            Event("Trip", "20261031T200000", "20261102T100000")));

        ReviewView view = Build();

        Assert.Equal(7, view.Days.Count);
        Assert.Equal(Enumerable.Range(1, 7).Select(i => Today.AddDays(i)), view.Days.Select(d => d.Date));
        Assert.All(view.Days, d => Assert.Contains(d.Appointments, a => a.Title == "Daily"));
        Assert.DoesNotContain(view.Days.SelectMany(d => d.Appointments), a => a.Title is "Today" or "Beyond");
        Assert.Contains(view.Days[0].Appointments, a => a.Title == "Tomorrow");
        Assert.Contains(view.Days[6].Appointments, a => a.Title == "Last");
        int[] tripDays = [.. view.Days.Select((d, i) => (d, i)).Where(x => x.d.Appointments.Any(a => a.Title == "Trip")).Select(x => x.i)];
        Assert.Equal([1, 2, 3], tripDays);
        Appointment trip = view.Days[1].Appointments.Single(a => a.Title == "Trip");
        Assert.Equal("20:00-…", view.Days[1].TimeText(trip));
        Assert.Equal("…-…", view.Days[2].TimeText(trip));
        Assert.Equal("…-10:00", view.Days[3].TimeText(trip));
        Assert.Empty(view.UnreadableCalendarFiles);
        Assert.Equal(CalendarFeedStatus.NotConfigured, view.Status);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    // AC-3: the feed is fetched once per Build and its events merged on every day; bad files are listed once each.
    [Fact]
    public void Feed_FetchedOnce_UnreadableListedOnce()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        FakeHttpHandler http = FakeHttpHandler.Ok(Calendar(
            Event("Feed daily", "20261020T120000", "20261020T130000", "RRULE:FREQ=DAILY\r\n")));
        CalendarFeed feed = new CalendarFeed(root, link, http, () => Now);
        WriteCalendar("bad.ics", "not a calendar");
        WriteCalendar("good.ics", Calendar(Event("File", "20261031T090000", "20261031T100000")));

        ReviewView view = Build(feed);

        Assert.Equal(1, http.Requests);
        Assert.Equal(CalendarFeedStatus.Fresh, view.Status);
        Assert.NotNull(view.CachedAtUtc);
        Assert.All(view.Days, d => Assert.Contains(d.Appointments, a => a.Title == "Feed daily"));
        Assert.Contains(view.Days[1].Appointments, a => a.Title == "File");
        Assert.Equal(["bad.ics"], view.UnreadableCalendarFiles);
        Assert.Equal(Path.Combine(root, CalendarStore.FolderName), view.Folder);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    // The view never prints its raw text.
    [Fact]
    public void ToString_TypeNamesOnly()
    {
        WriteNote("Private", Utc(10, 28, 10));

        ReviewView view = Build();

        Assert.Equal(nameof(ReviewView), view.ToString());
        Assert.Equal(nameof(ChangedNote), view.ChangedNotes[0].ToString());
        Assert.Equal(nameof(ReviewDay), view.Days[0].ToString());
    }
}
