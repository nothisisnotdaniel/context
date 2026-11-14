namespace Context.Tests;

public sealed class PersonViewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private readonly FakeHttpHandler http = FakeHttpHandler.Ok("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");

    public PersonViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-personview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
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

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    // A feed with a link set, so a fetch would make a request: the view must never make one.
    private CalendarFeed Feed()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);
        return new CalendarFeed(root, link, http, () => Now);
    }

    private PersonView Build(string argument, CalendarFeed? feed = null) =>
        PersonView.Build(new TagIndex(root), new NoteStore(root), new CalendarStore(root, Zone), feed, argument);

    private void WriteNote(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void WriteCalendar(string name, string text)
    {
        string folder = Path.Combine(root, CalendarStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private void PlantCache(string text)
    {
        string cache = Path.Combine(root, CalendarFeed.CacheFileName);
        File.WriteAllText(cache, text);
        // Older than the cache lifetime: a fetch would download again.
        File.SetLastWriteTimeUtc(cache, Now.AddDays(-3));
    }

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    private static string Event(string title, string start, string end, params string[] more) =>
        $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART{start}\r\nDTEND{end}\r\n"
        + string.Concat(more.Select(m => m + "\r\n")) + "END:VEVENT\r\n";

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    // 156 AC-6: every field, with notes, file and cached appointments, latest first, and no request made.
    [Fact]
    public void Build_NotesAndAppointments_EveryField_NoRequest()
    {
        WriteNote("people", "# people");
        WriteNote("people/Anna Schmidt", "# Anna Schmidt\n@[Anna Schmidt]");
        WriteNote("b notes", "met @[anna  SCHMIDT]");
        WriteNote("A plan", "ask @[Anna Schmidt] #work");
        WriteNote("Other", "@[Bob]");
        WriteCalendar("work.ics", Calendar(
            Event("Sync with @[Anna Schmidt]", ":20261027T090000Z", ":20261027T100000Z"),
            Event("Lunch", ":20261028T110000Z", ":20261028T120000Z", "LOCATION:Cafe with @[anna schmidt]"),
            Event("Review", ":20261026T080000Z", ":20261026T090000Z", "ATTENDEE;CN=\"Anna  Schmidt\":mailto:anna@example.com"),
            Event("Not her", ":20261029T080000Z", ":20261029T090000Z", "ATTENDEE;CN=Bob:mailto:anna.schmidt@example.com"),
            Event("Mail only", ":20261029T100000Z", ":20261029T110000Z", "ATTENDEE:mailto:anna@example.com"),
            Event("Anna Schmidt plain", ":20261029T120000Z", ":20261029T130000Z")));
        PlantCache(Calendar(
            Event("Cached @[Anna Schmidt]", ":20261030T080000Z", ":20261030T090000Z"),
            "BEGIN:VEVENT\r\nSUMMARY:Day off\r\nDTSTART;VALUE=DATE:20261025\r\nATTENDEE;CN=anna schmidt:mailto:a@b\r\nEND:VEVENT\r\n"));

        PersonView view = Build("  anna   schmidt ", Feed());

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna Schmidt", view.Person);
        Assert.Equal(["A plan", "b notes"], view.Notes);
        Assert.Equal(
            ["Cached @[Anna Schmidt]", "Lunch", "Sync with @[Anna Schmidt]", "Review", "Day off"],
            view.Appointments.Select(a => a.Title));
        Assert.Equal(At(30, 9), view.Appointments[0].Start);
        Assert.Equal("09:00-10:00", PersonView.TimeText(view.Appointments[0]));
        Assert.Equal("all day", PersonView.TimeText(view.Appointments[4]));
        Assert.Empty(view.UnreadableCalendarFiles);
        Assert.False(view.IndexUnreadable);
        Assert.Same(Zone, view.Zone);
        Assert.Equal(0, http.Requests);
        Assert.Equal(nameof(PersonView), view.ToString());
    }

    // 156 AC-6 / AC-7: a typed mention is the same person; with no page, the ordinal-first spelling is shown.
    [Fact]
    public void Build_TypedMention_NoPage_FirstSpelling()
    {
        WriteNote("A", "@[bob]");
        WriteNote("B", "@[Bob]");

        PersonView view = Build("@[ BOB ]");

        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Bob", view.Person);
        Assert.Equal(["A", "B"], view.Notes);
        Assert.Empty(view.Appointments);
    }

    // 156 AC-6: nobody mentions the person: NoMentions with the typed name, collapsed.
    [Fact]
    public void Build_NoMentions_TypedName()
    {
        WriteNote("A", "@[Bob]");

        PersonView view = Build(" Carl   Lee ");

        Assert.Equal(PersonViewKind.NoMentions, view.Kind);
        Assert.Equal("Carl Lee", view.Person);
        Assert.Empty(view.Notes);
        Assert.Empty(view.Appointments);
        Assert.False(view.IndexUnreadable);
    }

    // 156 AC-6: no name is MissingArgument, with nothing read.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@[ ]")]
    public void Build_NoName_MissingArgument(string argument)
    {
        PersonView view = Build(argument);

        Assert.Equal(PersonViewKind.MissingArgument, view.Kind);
        Assert.Equal(string.Empty, view.Person);
        Assert.False(File.Exists(DbPath));
    }

    // 156 AC-6: a bad context.db sets IndexUnreadable and still returns the appointments.
    [Fact]
    public void Build_BadDb_IndexUnreadable_StillAppointments()
    {
        WriteNote("A", "@[Anna]");
        WriteCalendar("a.ics", Calendar(Event("With @[Anna]", ":20261027T090000Z", ":20261027T100000Z")));
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        PersonView view = Build("Anna");

        Assert.True(view.IndexUnreadable);
        Assert.Equal(PersonViewKind.Mentions, view.Kind);
        Assert.Equal("Anna", view.Person);
        Assert.Empty(view.Notes);
        Assert.Equal("With @[Anna]", Assert.Single(view.Appointments).Title);
    }

    // 156 AC-6: unreadable calendar files are named, as in the current view; the rest still count.
    [Fact]
    public void Build_UnreadableCalendarFile_Named()
    {
        WriteCalendar("bad.ics", "not a calendar");
        WriteCalendar("good.ics", Calendar(Event("With @[Anna]", ":20261027T090000Z", ":20261027T100000Z")));

        PersonView view = Build("anna");

        Assert.Equal(["bad.ics"], view.UnreadableCalendarFiles);
        Assert.Equal("With @[Anna]", Assert.Single(view.Appointments).Title);
    }

    // 156 AC-6: without a cached copy nothing is downloaded either, and the files are only read.
    [Fact]
    public void Build_NoCache_NoRequest_OnlyContextDbWritten()
    {
        WriteNote("A", "@[Anna]");
        CalendarFeed feed = Feed();
        string[] before = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

        PersonView view = Build("Anna", feed);

        Assert.Equal(0, http.Requests);
        Assert.Equal(["A"], view.Notes);
        Assert.Equal(
            before.Append(DbPath).Order(StringComparer.Ordinal),
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith("-journal", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }
}
