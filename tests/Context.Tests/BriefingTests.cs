namespace Context.Tests;

// 172: the morning briefing in Core (compose AC-3, ensure AC-4, when to write AC-5), Times only read (AC-9).
public sealed class BriefingTests : IDisposable
{
    private const string SealedFixture = "{\"v\":1,\"text\":\"c2VhbGVk\"}";

    private static readonly DateOnly Today = new(2026, 11, 13);

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private SortedDictionary<string, string> timesBefore;
    private DateTime clock = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    public BriefingTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-briefing-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(times);
        timesBefore = FileSnapshot.Take(times);
    }

    // AC-9: no test ever changes Times' folder; the snapshot is retaken only when a test writes a Times file itself.
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(times))
            {
                Assert.Equal(timesBefore, FileSnapshot.Take(times));
            }
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private TodoStore Todos() => new(root, () => clock = clock.AddMinutes(1));

    private NoteStore Notes() => new(root);

    private CalendarStore Calendar() => new(root, IcsParserTests.Zone);

    private string Compose(string? timesFolder = "") =>
        Briefing.Compose(Todos(), Calendar(), null, timesFolder == string.Empty ? times : timesFolder, Today);

    private void WriteTimes(DateOnly day, string json) => WriteTimesFile(DailyNote.NameFor(day) + ".json", json);

    private void WriteTimesFile(string name, string json)
    {
        File.WriteAllText(Path.Combine(times, name), json);
        timesBefore = FileSnapshot.Take(times);
    }

    // A Times day file from "kind HH:mm [name] [workItem]" items; a name's "~" stands for a line break.
    private void WriteEntries(DateOnly day, params string[] entries)
    {
        IEnumerable<string> items = entries.Select(e =>
        {
            string[] parts = e.Split(' ');
            string name = parts.Length > 2 ? ",\"name\":\"" + parts[2].Replace("~", "\\n", StringComparison.Ordinal) + "\"" : string.Empty;
            string item = parts.Length > 3 ? ",\"workItem\":" + parts[3] : string.Empty;
            return $"{{\"kind\":\"{parts[0]}\",\"time\":\"{parts[1]}\"{name}{item}}}";
        });
        WriteTimes(day, "{\"entries\":[" + string.Join(',', items) + "]}");
    }

    private static string Page(string checkpoint, string balance) =>
        "# 2026-11-13\n\n## Appointments\n- No appointments.\n\n## Todos\n- Nothing due.\n\n## Open checkpoint\n"
        + checkpoint + "\n## Flexitime balance\n" + balance;

    // AC-3: the exact page: the daily note's sections byte for byte, the open groups and the balance through today-1.
    [Fact]
    public void Compose_FullPage_ExactText()
    {
        Todos().Add("call Anna", Today);
        WriteEntries(Today.AddDays(-2), "arrival 08:00", "leaving 17:00");
        WriteEntries(
            Today.AddDays(-1), "arrival 08:00", "checkpoint 09:00 A 12", "checkpoint 10:00 B", "checkpoint 11:00 C~line 12");
        WriteEntries(Today, "arrival 06:00", "checkpoint 06:00 Today");

        string text = Compose();

        string daily = DailyNote.Compose(Todos(), Calendar(), null, Today);
        Assert.StartsWith(daily, text, StringComparison.Ordinal);
        Assert.Equal(
            "# 2026-11-13\n\n## Appointments\n- No appointments.\n\n## Todos\n- call Anna\n\n## Open checkpoint\n"
            + "- 2026-11-12: #12 A, C line\n\n## Flexitime balance\n- -4:00 through 2026-11-12.\n",
            text);
    }

    // AC-3: an open group without a work item, and one line per open group.
    [Fact]
    public void Compose_OpenGroups_OneLineEach()
    {
        WriteEntries(Today.AddDays(-1), "arrival 08:00", "checkpoint 08:00 A 7");
        WriteEntries(Today.AddDays(-2), "arrival 08:00", "checkpoint 09:00 Plan");

        Assert.Contains("\n## Open checkpoint\n- 2026-11-12: #7 A\n\n", Compose(), StringComparison.Ordinal);
        File.Delete(Path.Combine(times, "2026-11-12.json"));
        timesBefore = FileSnapshot.Take(times);
        Assert.Contains("\n## Open checkpoint\n- 2026-11-11: Plan\n\n", Compose(), StringComparison.Ordinal);
    }

    // AC-3: a closed day says none open; zero is +0:00.
    [Fact]
    public void Compose_ClosedDay_NoneOpenAndZero()
    {
        WriteEntries(Today.AddDays(-1), "arrival 08:00", "checkpoint 08:00 A", "leaving 16:00");

        Assert.Equal(
            Page("- None open on 2026-11-12.\n", "- +0:00 through 2026-11-12.\n"),
            Compose());
    }

    // AC-3: an empty entries array doesn't count, so the search goes on to an older day.
    [Fact]
    public void Compose_EmptyEntries_LooksFurtherBack()
    {
        WriteTimes(Today.AddDays(-1), "{\"entries\":[]}");
        WriteTimes(Today.AddDays(-2), "{\"absence\":\"sick\"}");
        WriteEntries(Today.AddDays(-7), "arrival 08:00", "checkpoint 08:00 Old");

        Assert.Contains("\n## Open checkpoint\n- 2026-11-06: Old\n\n", Compose(), StringComparison.Ordinal);
    }

    // AC-3: nothing within 7 days, no folder, and an empty folder.
    [Fact]
    public void Compose_NoBookings()
    {
        Assert.Equal(Page("- No Times bookings in the last 7 days.\n", "- No Times bookings.\n"), Compose(null));
        Assert.Equal(
            Page("- No Times bookings in the last 7 days.\n", "- No Times bookings.\n"),
            Compose(Path.Combine(parent, "missing")));
        Assert.Equal(Page("- No Times bookings in the last 7 days.\n", "- No Times bookings.\n"), Compose());

        WriteEntries(Today.AddDays(-8), "arrival 08:00", "checkpoint 08:00 Old", "leaving 18:00");
        Assert.Equal(Page("- No Times bookings in the last 7 days.\n", "- +2:00 through 2026-11-12.\n"), Compose());
    }

    // AC-3: the first unreadable file met stops the search; the balance says how many files it skipped.
    [Fact]
    public void Compose_UnreadableDay_StopsSearchAndIsSkipped()
    {
        WriteTimes(Today.AddDays(-2), "not json");
        WriteEntries(Today.AddDays(-3), "arrival 08:00", "checkpoint 08:00 A", "leaving 17:30");

        Assert.Equal(
            Page(
                "- Can't read Times' day file for 2026-11-11.\n",
                "- +1:30 through 2026-11-12.\n- Skipped 1 Times day file(s) that can't be read.\n"),
            Compose());
    }

    // AC-3: a folder that can't be listed.
    [Fact]
    public void Compose_UnlistableFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(times);
        File.SetUnixFileMode(times, UnixFileMode.None);
        try
        {
            if (TimesBalance.Compute(times, Today).Kind != TimesBalanceKind.Unlistable)
            {
                return;
            }

            Assert.EndsWith("\n## Flexitime balance\n- Can't read Times' folder.\n", Compose(), StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(times, mode);
        }
    }

    [Theory]
    [InlineData(0, "+0:00")]
    [InlineData(59, "+0:59")]
    [InlineData(-61, "-1:01")]
    [InlineData(600, "+10:00")]
    public void FormatSigned_TimesFormat(int minutes, string expected)
    {
        Assert.Equal(expected, Briefing.FormatSigned(TimeSpan.FromMinutes(minutes)));
    }

    // AC-4: creates briefings and the page with the composed text.
    [Fact]
    public void Ensure_CreatesFolderAndPage()
    {
        BriefingResult result = Briefing.Ensure(Notes(), Today, () => Compose());

        Assert.Equal(BriefingKind.Created, result.Kind);
        Assert.Equal("briefings/2026-11-13", result.Name);
        Assert.Equal("# briefings\n", File.ReadAllText(Path.Combine(root, "briefings.md")));
        Assert.Equal(Compose(), File.ReadAllText(Path.Combine(root, "briefings", "2026-11-13.md")));
    }

    // AC-4: an existing briefings note is kept; a second call is Existing without composing; the page is kept.
    [Fact]
    public void Ensure_Existing_ChangesNothing()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "briefings.md"), "# mine\nkeep\n");
        int composed = 0;

        Assert.Equal(BriefingKind.Created, Briefing.Ensure(Notes(), Today, () => { composed++; return "# 2026-11-13\nbody\n"; }).Kind);
        Assert.Equal("# 2026-11-13\nbody\n", File.ReadAllText(Path.Combine(root, "briefings", "2026-11-13.md")));
        File.WriteAllText(Path.Combine(root, "briefings", "2026-11-13.md"), "# 2026-11-13\nedited\n");

        BriefingResult again = Briefing.Ensure(Notes(), Today, () => { composed++; return "other"; });

        Assert.Equal(BriefingKind.Existing, again.Kind);
        Assert.Equal("briefings/2026-11-13", again.Name);
        Assert.Equal(1, composed);
        Assert.Equal("# mine\nkeep\n", File.ReadAllText(Path.Combine(root, "briefings.md")));
        Assert.Equal("# 2026-11-13\nedited\n", File.ReadAllText(Path.Combine(root, "briefings", "2026-11-13.md")));
    }

    // AC-4: a sensitive briefings note gives Sensitive and nothing is read or written.
    [Fact]
    public void Ensure_SensitiveFolder_WritesNothing()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "briefings" + NoteStore.SensitiveExtension), SealedFixture);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        int composed = 0;

        BriefingResult result = Briefing.Ensure(Notes(), Today, () => { composed++; return Compose(); });

        Assert.Equal(BriefingKind.Sensitive, result.Kind);
        Assert.Equal(0, composed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-5: an arrival always counts; a start counts on Monday to Friday unless today is an absence day.
    [Theory]
    [InlineData("2026-11-13", "arrival", false, true)]
    [InlineData("2026-11-14", "arrival", false, true)]
    [InlineData("2026-11-14", "arrival", true, true)]
    [InlineData("2026-11-13", "none", true, true)]
    [InlineData("2026-11-09", "empty", true, true)]
    [InlineData("2026-11-13", "unreadable", true, true)]
    [InlineData("2026-11-13", "checkpoint", true, true)]
    [InlineData("2026-11-14", "none", true, false)]
    [InlineData("2026-11-15", "none", true, false)]
    [InlineData("2026-11-13", "absence", true, false)]
    [InlineData("2026-11-13", "none", false, false)]
    [InlineData("2026-11-13", "empty", false, false)]
    [InlineData("2026-11-13", "absence", false, false)]
    public void ShouldCreate_Rules(string date, string file, bool atStart, bool expected)
    {
        DateOnly day = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        switch (file)
        {
            case "arrival":
                WriteEntries(day, "arrival 08:00");
                break;
            case "checkpoint":
                WriteEntries(day, "checkpoint 08:00 A");
                break;
            case "empty":
                WriteTimes(day, "{\"entries\":[]}");
                break;
            case "absence":
                WriteTimes(day, "{\"absence\":\"vacation\"}");
                break;
            case "unreadable":
                WriteTimes(day, "[]");
                break;
        }

        Assert.Equal(expected, Briefing.ShouldCreate(day, TimesDay.Read(times, day), atStart));
    }
}
