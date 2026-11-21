namespace Context.Tests;

public sealed class WorkItemViewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private readonly FakeHttpHandler http = FakeHttpHandler.Ok("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");
    private readonly FakeCommitLog log = new FakeCommitLog();

    public WorkItemViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-workitemview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        http.Dispose();
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private WorkItemView Build(int item = 1234, string? timesFolder = null, IReadOnlyList<string>? repositories = null)
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        CalendarFeed feed = new CalendarFeed(root, link, http, () => Now);
        return WorkItemView.Build(
            new TagIndex(root), new NoteStore(root), new TodoStore(root), new CalendarStore(root, Zone), feed,
            timesFolder, repositories ?? [], log, item);
    }

    private void WriteNote(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private TodoItem AddTodo(string text, int dueDay, int? doneHours = null)
    {
        TodoItem todo = new TodoStore(root, () => Now).Add(text, new DateOnly(2026, 11, dueDay));
        return doneHours is { } hours ? new TodoStore(root, () => Now.AddHours(hours)).Complete(todo.Id)! : todo;
    }

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string title, string start, string? location = null) =>
        $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART{start}\r\n"
        + (location is null ? string.Empty : "LOCATION:" + location + "\r\n") + "END:VEVENT\r\n";

    private void WriteDay(DateOnly day, string json)
    {
        Directory.CreateDirectory(times);
        File.WriteAllText(Path.Combine(times, day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".json"), json);
    }

    // A day with one booking of `minutes` on `item` from 08:00, and 30 minutes on #99 after it.
    private void WriteBooking(DateOnly day, int item, int minutes)
    {
        string end = new TimeOnly(8, 0).AddMinutes(minutes).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        string after = new TimeOnly(8, 0).AddMinutes(minutes + 30).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        WriteDay(day, "{\"entries\":["
            + "{\"kind\":\"arrival\",\"time\":\"07:55\"},"
            + $"{{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"Work\",\"workItem\":{item}}},"
            + $"{{\"kind\":\"checkpoint\",\"time\":\"{end}\",\"name\":\"Other\",\"workItem\":99}},"
            + $"{{\"kind\":\"leaving\",\"time\":\"{after}\"}}]}}");
    }

    // 280 AC-3: every list, in its order.
    [Fact]
    public void Build_NotesTodosPeopleMeetings_InOrder()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\naliases:: AS");
        WriteNote("beta", "#1234 with @[AS]");
        WriteNote("Alpha", "#1234 with @[Anna Schmidt] and @[Bob]");
        WriteNote("gamma", "#12345 @[Carl]");
        WriteNote("items", "# items");
        WriteNote("items/1234", "# 1234\n@[Dora] #1234");
        AddTodo("later #1234 @[Bob]", 20);
        AddTodo("first #1234", 12);
        AddTodo("other #99", 11);
        AddTodo("done #1234 @[as]", 5, doneHours: 1);
        AddTodo("done later #1234", 6, doneHours: 5);
        WriteCalendar("a.ics", Calendar(
            Event("Plan #1234", ":20261105T090000"),
            Event("Review", ":20261108T100000", "Room #1234"),
            Event("Other #12345", ":20261109T100000")));
        File.WriteAllText(
            Path.Combine(root, CalendarFeed.CacheFileName), Calendar(Event("Feed #1234", ":20261101T080000")));

        WorkItemView view = Build();

        Assert.Equal(1234, view.Item);
        Assert.Equal(["Alpha", "beta"], view.Notes);
        Assert.Equal(["first #1234", "later #1234 @[Bob]"], view.OpenTodos.Select(t => t.Text));
        Assert.Equal(["done later #1234", "done #1234 @[as]"], view.DoneTodos.Select(t => t.Text));
        Assert.Equal(new DateOnly(2026, 11, 10), view.DoneDate(view.DoneTodos[0]));
        // Anna: Alpha, beta (alias) and the done todo (alias); Bob: Alpha and a todo.
        Assert.Equal(["Anna Schmidt|3", "Bob|2"], view.People.Select(p => p.Name + "|" + p.Count));
        Assert.Equal(["Review", "Plan #1234", "Feed #1234"], view.Meetings.Select(a => a.Title));
        Assert.Equal(0, http.Requests);
        Assert.False(view.IndexUnreadable);
        Assert.False(view.TodosUnreadable);
        Assert.Equal(nameof(WorkItemView), view.ToString());
    }

    // 280 AC-3: a bad context.db sets the flag and still fills todos and meetings.
    [Fact]
    public void Build_BadIndex_StillTodosAndMeetings()
    {
        WriteNote("A", "#1234");
        AddTodo("call @[Bob] about #1234", 12);
        WriteCalendar("a.ics", Calendar(Event("Plan #1234", ":20261105T090000")));
        File.WriteAllText(Path.Combine(root, TagIndex.FileName), "not a database at all, not at all, really not");

        WorkItemView view = Build();

        Assert.True(view.IndexUnreadable);
        Assert.Empty(view.Notes);
        Assert.Equal(["call @[Bob] about #1234"], view.OpenTodos.Select(t => t.Text));
        Assert.Equal(["Bob|1"], view.People.Select(p => p.Name + "|" + p.Count));
        Assert.Equal(["Plan #1234"], view.Meetings.Select(a => a.Title));
    }

    // 280 AC-3: a bad todos.json sets the flag and still fills the rest.
    [Fact]
    public void Build_BadTodos_StillTheRest()
    {
        WriteNote("A", "#1234 @[Anna]");
        File.WriteAllText(Path.Combine(root, "todos.json"), "{ not json");
        WriteCalendar("a.ics", Calendar(Event("Plan #1234", ":20261105T090000")));

        WorkItemView view = Build();

        Assert.True(view.TodosUnreadable);
        Assert.False(view.IndexUnreadable);
        Assert.Empty(view.OpenTodos);
        Assert.Empty(view.DoneTodos);
        Assert.Equal(["A"], view.Notes);
        Assert.Equal(["Anna|1"], view.People.Select(p => p.Name + "|" + p.Count));
        Assert.Equal(["Plan #1234"], view.Meetings.Select(a => a.Title));
    }

    // 280 AC-4: only the item's groups, per ISO week (two days in one week give one row; a week across the year
    // boundary is named by ISOWeek), newest first; an unreadable day file is skipped; nothing in Times changes.
    [Fact]
    public void Hours_PerIsoWeek_SkipsUnreadable_ChangesNothing()
    {
        WriteBooking(new DateOnly(2026, 11, 9), 1234, 60);
        WriteBooking(new DateOnly(2026, 11, 10), 1234, 45);
        WriteBooking(new DateOnly(2026, 11, 2), 99, 120);
        // 2026-12-31 (Thursday) and 2027-01-01 (Friday) are both in ISO week 2026-W53.
        WriteBooking(new DateOnly(2026, 12, 31), 1234, 30);
        WriteBooking(new DateOnly(2027, 1, 1), 1234, 15);
        WriteDay(new DateOnly(2026, 11, 11), "{ broken");
        File.WriteAllText(Path.Combine(times, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(times, "2026-11-12.txt"), "x");
        SortedDictionary<string, string> before = FileSnapshot.Take(times);

        WorkItemView view = Build(timesFolder: times);

        Assert.Equal(WorkItemTimesKind.Read, view.TimesKind);
        Assert.Equal(
            ["2026-W53|45", "2026-W46|105"],
            view.Hours.Weeks.Select(w => w.Week + "|" + (int)w.Total.TotalMinutes));
        Assert.Equal(TimeSpan.FromMinutes(150), view.Hours.Total);
        Assert.Equal(1, view.Hours.SkippedFiles);
        Assert.Equal(before, FileSnapshot.Take(times));
        Assert.Equal("2027-W01", WorkItemView.WeekOf(new DateOnly(2027, 1, 4)));
    }

    // 280 AC-4: a null or missing folder is NotSetUp.
    [Fact]
    public void Hours_NullOrMissingFolder_NotSetUp()
    {
        Assert.Equal(WorkItemTimesKind.NotSetUp, Build(timesFolder: null).TimesKind);
        WorkItemView missing = Build(timesFolder: Path.Combine(parent, "nope"));
        Assert.Equal(WorkItemTimesKind.NotSetUp, missing.TimesKind);
        Assert.Empty(missing.Hours.Weeks);
        Assert.Equal(TimeSpan.Zero, missing.Hours.Total);
    }

    // 280 AC-5: no repositories: NotSetUp, and the log is never called.
    [Fact]
    public void Commits_NoRepositories_NotSetUp_NoCall()
    {
        WorkItemView view = Build();

        Assert.Equal(WorkItemCommitsKind.NotSetUp, view.CommitsKind);
        Assert.Empty(view.Commits);
        Assert.Empty(log.Calls);
    }

    // 280 AC-5: newest first across repositories; a failing repo is listed while the others show.
    [Fact]
    public void Commits_NewestFirst_FailedRepoListed()
    {
        string one = Path.Combine(parent, "one");
        string two = Path.Combine(parent, "two") + Path.DirectorySeparatorChar;
        string bad = Path.Combine(parent, "bad");
        log.Set(one, new LoggedCommit("a1", new DateOnly(2026, 11, 1), "Fix #1234"), new LoggedCommit("a0", new DateOnly(2026, 10, 1), "Start #1234"));
        log.Set(two, new LoggedCommit("b1", new DateOnly(2026, 11, 5), "More #1234"));
        log.Fail(bad);

        WorkItemView view = Build(repositories: [one, bad, two]);

        Assert.Equal(WorkItemCommitsKind.Read, view.CommitsKind);
        Assert.Equal(
            ["two|b1|2026-11-05|More #1234", "one|a1|2026-11-01|Fix #1234", "one|a0|2026-10-01|Start #1234"],
            view.Commits.Select(c => $"{c.Repo}|{c.Hash}|{c.Date:yyyy-MM-dd}|{c.Subject}"));
        Assert.Equal(["bad"], view.FailedRepos);
        Assert.Equal([(one, 1234), (bad, 1234), (two, 1234)], log.Calls);
    }

    // 280 AC-5: a missing git gives GitMissing and no commits.
    [Fact]
    public void Commits_GitMissing()
    {
        log.GitMissing = true;

        WorkItemView view = Build(repositories: [Path.Combine(parent, "one")]);

        Assert.Equal(WorkItemCommitsKind.GitMissing, view.CommitsKind);
        Assert.Empty(view.Commits);
        Assert.Empty(view.FailedRepos);
    }

    // 361 AC-7: archived done todos that mention the item are in DoneTodos after the todos.json ones done later; a bad
    // archive sets ArchiveUnreadable and DoneTodos still lists the todos.json matches.
    [Fact]
    public void Build_ArchivedTodos_InDoneTodos_BadArchiveFlagged()
    {
        TodoItem open = AddTodo("open #1234", 12);
        TodoItem done = AddTodo("done #1234", 5, doneHours: 1);
        Guid archivedId = Guid.NewGuid();
        string archive = Path.Combine(root, TodoStore.ArchiveFileName);
        TodoArchiveFile.Write(
            archive,
            TodoArchiveFile.Entry(archivedId, "old #1234 with @[Anna]", new DateOnly(2026, 8, 1), Now.AddDays(-90), Now.AddDays(-60)),
            TodoArchiveFile.Entry(Guid.NewGuid(), "old #99", new DateOnly(2026, 8, 1), Now.AddDays(-90), Now.AddDays(-60)));

        WorkItemView view = Build();

        Assert.Equal([open.Id], view.OpenTodos.Select(t => t.Id));
        Assert.Equal([done.Id, archivedId], view.DoneTodos.Select(t => t.Id));
        Assert.Equal(["Anna"], view.People.Select(p => p.Name));
        Assert.False(view.ArchiveUnreadable);
        Assert.False(view.TodosUnreadable);

        File.WriteAllText(archive, "[{");

        WorkItemView bad = Build();

        Assert.True(bad.ArchiveUnreadable);
        Assert.False(bad.TodosUnreadable);
        Assert.Equal([open.Id], bad.OpenTodos.Select(t => t.Id));
        Assert.Equal([done.Id], bad.DoneTodos.Select(t => t.Id));
    }
}
