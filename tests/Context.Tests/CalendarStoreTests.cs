namespace Context.Tests;

public sealed class CalendarStoreTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    private readonly string root;
    private readonly string folder;

    public CalendarStoreTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-calendar-" + Guid.NewGuid().ToString("N"));
        folder = Path.Combine(root, CalendarStore.FolderName);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private CalendarDay Day() => new CalendarStore(root, IcsParserTests.Zone).Day(Today);

    private void Write(string name, params string[] events)
    {
        Directory.CreateDirectory(folder);
        string text = "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Event(string title, string start, string? end = null, string extra = "")
    {
        string endLine = end is null ? string.Empty : $"DTEND{end}\r\n";
        return $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART{start}\r\n{endLine}{extra}END:VEVENT\r\n";
    }

    private static string[] Titles(CalendarDay day) => day.Appointments.Select(a => a.Title).ToArray();

    // AC-4
    [Fact]
    public void Day_NoFolder_IsEmpty()
    {
        CalendarDay day = Day();

        Assert.Empty(day.Appointments);
        Assert.Empty(day.Unreadable);
        Assert.False(Directory.Exists(folder));
    }

    // AC-2
    [Fact]
    public void Day_KeepsOnlyEventsOverlappingToday()
    {
        Write(
            "a.ics",
            Event("yesterday", ":20261028T100000", ":20261028T110000"),
            Event("ends at midnight", ":20261028T230000", ":20261029T000000"),
            Event("over midnight", ":20261028T230000", ":20261029T010000"),
            Event("late", ":20261029T220000", ":20261030T020000"),
            Event("tomorrow", ":20261030T000000", ":20261030T010000"),
            Event("zero today", ":20261029T000000"),
            Event("zero tomorrow", ":20261030T000000"),
            Event("multi-day", ";VALUE=DATE:20261027", ";VALUE=DATE:20261031"),
            Event("all day yesterday", ";VALUE=DATE:20261028"),
            Event("all day today", ";VALUE=DATE:20261029"),
            Event("cancelled", ":20261029T100000", null, "STATUS:CANCELLED\r\n"),
            Event("utc late", ":20261029T233000Z"));

        CalendarDay day = Day();

        Assert.Equal(
            ["multi-day", "all day today", "over midnight", "zero today", "late"],
            Titles(day));
    }

    // AC-2: all-day first, then start, then title ignoring case; events from every file.
    [Fact]
    public void Day_SortsAcrossFiles()
    {
        Write("b.ics", Event("beta", ":20261029T100000"), Event("Alpha", ":20261029T100000"));
        Write("a.ICS", Event("early", ":20261029T080000"), Event("zz all", ";VALUE=DATE:20261029"));
        Write("c.ics", Event("aa all", ";VALUE=DATE:20261029"));

        CalendarDay day = Day();

        Assert.Equal(["aa all", "zz all", "early", "Alpha", "beta"], Titles(day));
        Assert.Empty(day.Unreadable);
    }

    // Scope: only top-level *.ics files.
    [Fact]
    public void Day_IgnoresSubfoldersAndOtherFiles()
    {
        Write("x.txt", Event("txt", ":20261029T100000"));
        Write("x.icsx", Event("icsx", ":20261029T100000"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(
            Path.Combine(folder, "sub", "y.ics"),
            "BEGIN:VCALENDAR\r\n" + Event("sub", ":20261029T100000") + "END:VCALENDAR\r\n");
        Directory.CreateDirectory(Path.Combine(folder, "dir.ics"));

        CalendarDay day = Day();

        Assert.Empty(day.Appointments);
        Assert.Empty(day.Unreadable);
    }

    // AC-5
    [Fact]
    public void Day_BadAndLargeFiles_AreUnreadable_OthersStillRead()
    {
        Write("good.ics", Event("good", ":20261029T100000"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.ics"), "just some text");
        string big = Path.Combine(folder, "big.ics");
        using (FileStream stream = new FileStream(big, FileMode.CreateNew))
        {
            stream.SetLength(CalendarStore.MaxFileBytes + 1);
        }

        CalendarDay day = Day();

        Assert.Equal(["good"], Titles(day));
        Assert.Equal(["big.ics", "notes.ics"], day.Unreadable);
        Assert.Equal(CalendarStore.MaxFileBytes + 1, new FileInfo(big).Length);
    }

    // AC-5: exactly at the cap is still read.
    [Fact]
    public void Day_FileAtCap_IsRead()
    {
        Directory.CreateDirectory(folder);
        string text = "BEGIN:VCALENDAR\r\n" + Event("cap", ":20261029T100000") + "END:VCALENDAR\r\n";
        string padded = text + new string(' ', (int)CalendarStore.MaxFileBytes - text.Length);
        File.WriteAllText(Path.Combine(folder, "cap.ics"), padded);

        Assert.Equal(["cap"], Titles(Day()));
    }

    // A UTF-8 BOM doesn't hide BEGIN:VCALENDAR.
    [Fact]
    public void Day_Utf8Bom_IsRead()
    {
        Directory.CreateDirectory(folder);
        string text = "BEGIN:VCALENDAR\r\n" + Event("Café", ":20261029T100000") + "END:VCALENDAR\r\n";
        File.WriteAllText(Path.Combine(folder, "bom.ics"), text, new System.Text.UTF8Encoding(true));

        Assert.Equal(["Café"], Titles(Day()));
    }
}
