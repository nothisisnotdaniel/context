using System.Net;

namespace Context.Tests;

public sealed class CurrentViewTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    public CurrentViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-currentview-" + Guid.NewGuid().ToString("N"));
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

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private TodoStore Todos() => new(root, () => clock = clock.AddMinutes(1));

    private CurrentView Build(CalendarFeed? feed = null) =>
        CurrentView.Build(Todos(), new TagIndex(root), new NoteStore(root), new CalendarStore(root, Zone), feed, Today);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string title, string start, string end) =>
        $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART{start}\r\nDTEND{end}\r\nEND:VEVENT\r\n";

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    // AC-4: every appointment comes back; the 15-row cap is the renderer's.
    [Fact]
    public void Build_TwentyAppointments_Untruncated()
    {
        string[] events = Enumerable.Range(0, 20)
            .Select(i => Event(
                $"M{i:00}",
                $":20261029T{8 + (i / 4):00}{i % 4 * 15:00}00",
                $":20261029T{8 + (i / 4):00}{i % 4 * 15 + 10:00}00"))
            .ToArray();
        WriteCalendar("a.ics", Calendar(events));

        CurrentView view = Build();

        Assert.Equal(20, view.Appointments.Count);
        Assert.Equal("M00", view.Appointments[0].Title);
        Assert.Equal("M19", view.Appointments[^1].Title);
        Assert.Equal("08:00-08:10", view.TimeText(view.Appointments[0]));
        Assert.Equal(Path.Combine(root, CalendarStore.FolderName), view.Folder);
        Assert.Empty(view.UnreadableCalendarFiles);
        Assert.Equal(CalendarFeedStatus.NotConfigured, view.Status);
    }

    // AC-4
    [Fact]
    public void Build_UnreadableCalendarFile_Named()
    {
        WriteCalendar("bad [x].ics", "nope");

        CurrentView view = Build();

        Assert.Empty(view.Appointments);
        Assert.Equal(["bad [x].ics"], view.UnreadableCalendarFiles);
    }

    // AC-4: the time text moved here from CurrentCommands.Time, with the same results.
    [Fact]
    public void Time_AllDay_OutsideSides_MidnightEnd()
    {
        DateTime from = At(29, 0);
        DateTime to = At(30, 0);

        Assert.Equal("all day", CurrentView.Time(new Appointment("x", null, from, to, true), from, to));
        Assert.Equal("…-06:00", CurrentView.Time(new Appointment("x", null, At(28, 22), At(29, 6), false), from, to));
        Assert.Equal("23:00-…", CurrentView.Time(new Appointment("x", null, At(29, 23), At(30, 1), false), from, to));
        Assert.Equal(
            "22:00-00:00", CurrentView.Time(new Appointment("x", null, At(29, 22), At(30, 0), false), from, to));
        Assert.Equal("…-…", CurrentView.Time(new Appointment("x", null, At(28, 9), At(30, 9), false), from, to));
    }

    // AC-4: numbers match the sidebar, which numbers TodoStore.List() in order.
    [Fact]
    public void Build_Due_NumbersMatchSidebarOrder()
    {
        TodoStore todos = Todos();
        todos.Add("today", Today);
        todos.Add("two days ago", Today.AddDays(-2));
        todos.Add("tomorrow", Today.AddDays(1));
        todos.Add("yesterday", Today.AddDays(-1));

        CurrentView view = Build();

        Assert.False(view.TodosUnreadable);
        Assert.Equal(
            [(1, "two days ago", true), (2, "yesterday", true), (3, "today", false)],
            view.Due.Select(d => (d.Number, d.Text, d.Overdue)).ToArray());
        Assert.Equal(Today.AddDays(-2), view.Due[0].Due);
    }

    // AC-4
    [Fact]
    public void Build_LinkedNotes_SortedIgnoringCase_DistinctTags()
    {
        Todos().Add("a #work #[Big Plan]", Today);
        Todos().Add("b #WORK #home", Today.AddDays(-1));
        WriteNote("beta", "#work #home");
        WriteNote("Alpha", "#[big plan] #work");
        WriteNote("alpha2", "#home");
        WriteNote("gamma", "#other");

        CurrentView view = Build();

        Assert.False(view.IndexUnreadable);
        Assert.Equal(["Alpha", "alpha2", "beta"], view.LinkedNotes.Select(n => n.Name).ToArray());
        Assert.Equal(["big plan", "work"], view.LinkedNotes[0].Tags);
        Assert.Equal(["home"], view.LinkedNotes[1].Tags);
        Assert.Equal(["home", "work"], view.LinkedNotes[2].Tags);
    }

    // AC-4
    [Fact]
    public void Build_NoLinkedNotes_Empty()
    {
        Todos().Add("call #nobody", Today);
        WriteNote("A", "#work");

        CurrentView view = Build();

        Assert.Single(view.Due);
        Assert.Empty(view.LinkedNotes);
        Assert.False(view.IndexUnreadable);
    }

    // AC-4: a bad todos.json still gives the appointments, and the index is left alone.
    [Fact]
    public void Build_TodosUnreadable_AppointmentsStillPresent()
    {
        WriteCalendar("a.ics", Calendar(Event("Standup", ":20261029T090000", ":20261029T091500")));
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "{ not json");

        CurrentView view = Build();

        Assert.True(view.TodosUnreadable);
        Assert.Equal("Standup", Assert.Single(view.Appointments).Title);
        Assert.Empty(view.Due);
        Assert.Empty(view.LinkedNotes);
        Assert.False(File.Exists(DbPath));
    }

    // AC-4
    [Fact]
    public void Build_IndexUnreadable_DueStillPresent()
    {
        Todos().Add("due #work", Today.AddDays(-1));
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        CurrentView view = Build();

        Assert.True(view.IndexUnreadable);
        Assert.Equal("due #work", Assert.Single(view.Due).Text);
        Assert.Empty(view.LinkedNotes);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // AC-4: nothing due, so the index isn't synced (no context.db is created).
    [Fact]
    public void Build_NothingDue_IndexNotTouched()
    {
        Todos().Add("later #work", Today.AddDays(1));
        WriteNote("A", "#work");

        CurrentView view = Build();

        Assert.Empty(view.Due);
        Assert.Empty(view.LinkedNotes);
        Assert.False(File.Exists(DbPath));
    }

    // 046 AC-4: without a feed the view still carries the calendar store's zone instance.
    [Fact]
    public void Build_NoFeed_KeepsCalendarZone()
    {
        CurrentView view = Build();

        Assert.Equal(CalendarFeedStatus.NotConfigured, view.Status);
        Assert.Same(Zone, view.Zone);
    }

    // AC-4 (also 044 AC-4): a stale feed's events are merged, with its status, a non-null time and a reason.
    [Fact]
    public void Build_StaleFeed_MergesCache()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        string cache = Path.Combine(root, CalendarFeed.CacheFileName);
        File.WriteAllText(cache, Calendar(Event("Cached", ":20261029T140000Z", ":20261029T150000Z")));
        DateTime cachedAt = new(2026, 10, 28, 17, 5, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(cache, cachedAt);
        CalendarFeed feed = new CalendarFeed(
            root, link, FakeHttpHandler.Status(HttpStatusCode.InternalServerError), () => Now);

        CurrentView view = Build(feed);

        Assert.Equal(CalendarFeedStatus.Stale, view.Status);
        Assert.NotNull(view.CachedAtUtc);
        Assert.Equal(cachedAt, view.CachedAtUtc);
        Assert.NotNull(view.Reason);
        Assert.Equal("HTTP 500", view.Reason);
        Assert.Equal("Cached", Assert.Single(view.Appointments).Title);
        Assert.Same(Zone, view.Zone);
    }

    // AC-4 (also 044 AC-4): a failed feed has no time but a reason.
    [Fact]
    public void Build_FailedFeed_ReasonOnly()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        CalendarFeed feed = new CalendarFeed(root, link, FakeHttpHandler.Status(HttpStatusCode.NotFound), () => Now);

        CurrentView view = Build(feed);

        Assert.Equal(CalendarFeedStatus.Failed, view.Status);
        Assert.Null(view.CachedAtUtc);
        Assert.NotNull(view.Reason);
        Assert.Equal("HTTP 404", view.Reason);
        Assert.Empty(view.Appointments);
    }

    // AC-7: the renderer only maps the view; a hand-built view renders to the recorded output.
    [Fact]
    public void Render_HandBuiltStaleView_MatchesGolden()
    {
        CurrentView view = new CurrentView(
            Today,
            Zone,
            [new Appointment("Cached [meeting]", null, At(29, 15), At(29, 16), false)],
            "/nowhere/calendar",
            [],
            CalendarFeedStatus.Stale,
            new DateTime(2026, 10, 28, 17, 5, 0, DateTimeKind.Utc),
            "HTTP 500",
            false,
            [],
            [],
            false);

        Assert.Equal(
            ViewParityTests.Goldens["Current_Stale_NothingDue"],
            ViewParityTests.Render(CurrentCommands.Render(view), "/nowhere"));
    }

    // AC-7
    [Fact]
    public void Render_HandBuiltFullView_MatchesGolden()
    {
        List<Appointment> appointments =
        [
            new Appointment("Holiday", null, At(29, 0), At(30, 0), true),
            new Appointment("Night shift", "Ward [3]", At(28, 22), At(29, 6), false),
            new Appointment(string.Empty, null, At(29, 7, 30), At(29, 8), false),
            new Appointment("Multi\nline [b]x[/b]", "Room 1\nfloor 2", At(29, 8, 15), At(29, 9), false),
        ];
        for (int i = 0; i < 16; i++)
        {
            int hour = 10 + (i / 2);
            int minute = i % 2 * 10;
            appointments.Add(new Appointment(
                $"Meeting {minute + (i / 2):00}", null, At(29, hour, minute), At(29, hour, minute + 5), false));
        }

        appointments.Add(new Appointment("Ends at midnight", null, At(29, 22), At(30, 0), false));
        appointments.Add(new Appointment("Late call", null, At(29, 23), At(30, 1), false));
        CurrentView view = new CurrentView(
            Today,
            Zone,
            appointments,
            "/nowhere/calendar",
            ["broken [x].ics"],
            CalendarFeedStatus.NotConfigured,
            null,
            null,
            false,
            [
                new DueTodo(1, Today.AddDays(-3), "Pay rent #home", true),
                new DueTodo(2, Today, "Plan #Work and #[big plan]", false),
            ],
            [
                new LinkedNote("Alpha [x]", ["big plan", "work"]),
                new LinkedNote("beta", ["home", "work"]),
            ],
            false);

        Assert.Equal(
            ViewParityTests.Goldens["Current_Full"],
            ViewParityTests.Render(CurrentCommands.Render(view), "/nowhere"));
    }

    // AC-7
    [Fact]
    public void Render_HandBuiltBadTodosAndBadDb_MatchGolden()
    {
        CurrentView badTodos = new CurrentView(
            Today, Zone, [new Appointment("Standup", null, At(29, 9), At(29, 9, 15), false)], "/n/calendar", [],
            CalendarFeedStatus.NotConfigured, null, null, true, [], [], false);
        CurrentView badDb = new CurrentView(
            Today, Zone, [], "/n/calendar", [], CalendarFeedStatus.NotConfigured, null, null, false,
            [new DueTodo(1, Today.AddDays(-1), "Due #work", true)], [], true);

        Assert.Equal(
            ViewParityTests.Goldens["Current_BadTodos"], ViewParityTests.Render(CurrentCommands.Render(badTodos), "/n"));
        Assert.Equal(
            ViewParityTests.Goldens["Current_BadDb"], ViewParityTests.Render(CurrentCommands.Render(badDb), "/n"));
    }

    // AC-1 (042)
    [Fact]
    public void ToString_HidesRawText()
    {
        const string Marker = "SECRET-042\u001b[31m";
        DateTime start = Today.ToDateTime(new TimeOnly(9, 0));
        Appointment appointment = new Appointment(Marker, Marker, start, start.AddHours(1), false);
        DueTodo todo = new DueTodo(1, Today, Marker + " #" + Marker, false);
        LinkedNote note = new LinkedNote(Marker, [Marker]);
        CurrentView view = new CurrentView(
            Today,
            Zone,
            [appointment],
            Marker,
            [Marker],
            CalendarFeedStatus.Failed,
            null,
            Marker,
            false,
            [todo],
            [note],
            false);

        string text = view.ToString();

        Assert.Equal("CurrentView", text);
        Assert.Equal(text, $"{view}");
        Assert.DoesNotContain("SECRET-042", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
    }

    // AC-2 (042)
    [Fact]
    public void ToString_DueTodoAndLinkedNote_HideRawText()
    {
        const string Marker = "SECRET-042\u001b[31m";
        DueTodo todo = new DueTodo(3, Today, Marker, true);
        LinkedNote note = new LinkedNote(Marker, [Marker, "work"]);

        string todoText = todo.ToString();
        string noteText = note.ToString();

        Assert.Equal("DueTodo", todoText);
        Assert.Equal("LinkedNote", noteText);
        Assert.Equal(todoText, $"{todo}");
        Assert.Equal(noteText, $"{note}");
        foreach (string text in new[] { todoText, noteText })
        {
            Assert.DoesNotContain("SECRET-042", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        }
    }
}
