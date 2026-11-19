namespace Context.Tests;

// 172: DesktopActions.AutoBriefing (start-up and day-end timer, AC-6) and OpenBriefing (/briefing, AC-8), against real
// files and the fixture's fixed local clock (Thursday 2026-10-29). Times' folder is never changed (AC-9).
public sealed class BriefingActionsTests : IDisposable
{
    private const string Sealed = "{\"v\":1,\"text\":\"c2VhbGVk\"}";
    private const string Arrival = "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"}]}";

    private static readonly DateTime Saturday = new(2026, 10, 31, 9, 0, 0, DateTimeKind.Local);

    private readonly DesktopFixture data = new();
    private SortedDictionary<string, string> timesBefore;

    public BriefingActionsTests()
    {
        Directory.CreateDirectory(data.TimesFolder);
        timesBefore = FileSnapshot.Take(data.TimesFolder);
    }

    public void Dispose()
    {
        try
        {
            Assert.Equal(timesBefore, FileSnapshot.Take(data.TimesFolder));
        }
        finally
        {
            data.Dispose();
        }
    }

    private string PagePath(string day) => Path.Combine(data.Root, "briefings", day + ".md");

    private void WriteTimes(DateOnly day, string json)
    {
        File.WriteAllText(Path.Combine(data.TimesFolder, day.ToString("yyyy-MM-dd") + ".json"), json);
        timesBefore = FileSnapshot.Take(data.TimesFolder);
    }

    // AC-6: a tick after the day's first arrival creates the page and offers it once; later ticks do nothing.
    [Fact]
    public async Task AutoBriefing_TickAfterArrival_CreatesAndOffersOnce()
    {
        data.LocalNow = Saturday;
        await data.RefreshAsync();

        ActionResult start = await data.Actions!.AutoBriefing();
        ActionResult quiet = await data.Actions.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (start.Kind, start.Reason));
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (quiet.Kind, quiet.Reason));
        Assert.False(Directory.Exists(Path.Combine(data.Root, "briefings")));
        Assert.Null(data.State!.TakeBriefing());

        WriteTimes(new DateOnly(2026, 10, 31), Arrival);
        int builds = data.State.Builds;
        ActionResult tick = await data.Actions.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.None, "briefings/2026-10-31"), (tick.Kind, tick.Reason, tick.Text));
        Assert.True(File.Exists(PagePath("2026-10-31")));
        Assert.True(data.State.Builds > builds);
        Assert.Equal("briefings/2026-10-31", data.State.TakeBriefing());
        Assert.Null(data.State.TakeBriefing());

        File.WriteAllText(PagePath("2026-10-31"), "# 2026-10-31\nedited\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        ActionResult later = await data.Actions.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (later.Kind, later.Reason));
        Assert.Null(data.State.TakeBriefing());
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: on a weekday the start-up call creates and offers the page even with no Times file.
    [Fact]
    public async Task AutoBriefing_WeekdayStart_CreatesWithoutTimes()
    {
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.None, "briefings/2026-10-29"), (result.Kind, result.Reason, result.Text));
        Assert.Equal("# briefings\n", File.ReadAllText(Path.Combine(data.Root, "briefings.md")));
        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- No appointments.\n\n## Todos\n- call Anna\n\n## Follow-ups\n- No follow-ups due.\n\n## To review\n- Nothing to review.\n\n## Open checkpoint\n"
            + "- No Times bookings in the last 7 days.\n\n## Flexitime balance\n- No Times bookings.\n",
            File.ReadAllText(PagePath("2026-10-29")));
        Assert.Equal("briefings/2026-10-29", data.State!.TakeBriefing());
        Assert.Contains(data.State.Snapshot!.Notes, n => n.Name == "briefings/2026-10-29");
    }

    // AC-5/AC-6: an absence day at start, and a later tick without an arrival, write nothing.
    [Fact]
    public async Task AutoBriefing_AbsenceStartAndTicksWithoutArrival_WriteNothing()
    {
        WriteTimes(DesktopFixture.Today, "{\"absence\":\"vacation\"}");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult start = await data.Actions!.AutoBriefing();
        File.Delete(Path.Combine(data.TimesFolder, "2026-10-29.json"));
        timesBefore = FileSnapshot.Take(data.TimesFolder);
        ActionResult tick = await data.Actions.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (start.Kind, start.Reason));
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (tick.Kind, tick.Reason));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Null(data.State!.TakeBriefing());
    }

    // AC-6: a restart on a day whose page exists writes nothing and offers nothing.
    [Fact]
    public async Task AutoBriefing_PageExists_WritesAndOffersNothing()
    {
        data.WriteNote("briefings", "# briefings\n");
        data.WriteFile(Path.Combine("briefings", "2026-10-29.md"), "# 2026-10-29\nmine\n");
        WriteTimes(DesktopFixture.Today, Arrival);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, "briefings/2026-10-29"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Null(data.State!.TakeBriefing());
    }

    // AC-6: the next day's first arrival writes that day's page.
    [Fact]
    public async Task AutoBriefing_NextDay_CreatesAgain()
    {
        await data.RefreshAsync();
        await data.Actions!.AutoBriefing();
        Assert.Equal("briefings/2026-10-29", data.State!.TakeBriefing());

        data.LocalNow = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Local);
        Assert.Equal(ActionReason.Unchanged, (await data.Actions.AutoBriefing()).Reason);
        WriteTimes(new DateOnly(2026, 10, 30), Arrival);
        ActionResult result = await data.Actions.AutoBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.True(File.Exists(PagePath("2026-10-30")));
        Assert.Equal("briefings/2026-10-30", data.State.TakeBriefing());
    }

    // AC-6: a sensitive briefings note: refused, nothing read or written or offered, no key-store call.
    [Fact]
    public async Task AutoBriefing_SensitiveFolder_WritesAndShowsNothing()
    {
        data.WriteFile("briefings.ctxs", Sealed);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoBriefing();

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Locked, DesktopActions.BriefingSensitiveText),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Null(data.State!.TakeBriefing());
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-6: a call while another action runs is refused as busy and writes nothing.
    [Fact]
    public async Task AutoBriefing_WhileRunning_RefusedBusy()
    {
        await data.RefreshAsync();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.AutoBriefing();
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (second.Kind, second.Reason));
        Assert.False(File.Exists(Path.Combine(data.Root, "briefings.md")));
        Assert.Null(data.State!.TakeBriefing());
    }

    // AC-6: an exception (here from the test hook or an unwritable folder) is a Failed answer, never a throw.
    [Fact]
    public async Task AutoBriefing_NeverThrows()
    {
        await data.RefreshAsync();
        data.Actions!.BeforeWrite = () => throw new IOException("disk");

        ActionResult result = await data.Actions.AutoBriefing();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.DoesNotContain("disk", result.Text, StringComparison.Ordinal);
        Assert.Null(data.State!.TakeBriefing());
    }

    // AC-8: OpenBriefing creates the page (None), then finds it (Unchanged); it never offers.
    [Fact]
    public async Task OpenBriefing_CreatesThenUnchanged()
    {
        await data.RefreshAsync();

        ActionResult created = await data.Actions!.OpenBriefing();
        ActionResult again = await data.Actions.OpenBriefing();

        Assert.Equal((ActionKind.Done, ActionReason.None, "briefings/2026-10-29"), (created.Kind, created.Reason, created.Text));
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, "briefings/2026-10-29"), (again.Kind, again.Reason, again.Text));
        Assert.Equal("briefings/2026-10-29", data.Actions.BriefingName());
        Assert.Null(data.State!.TakeBriefing());
    }

    // AC-8: a sensitive briefings note is refused with the fixed text; a failed write gives Can't write <name>.
    [Fact]
    public async Task OpenBriefing_SensitiveAndFailure_FixedTexts()
    {
        data.WriteFile("briefings.ctxs", Sealed);
        await data.RefreshAsync();

        ActionResult sensitive = await data.Actions!.OpenBriefing();

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Locked, "briefings is sensitive, so no briefing can be written."),
            (sensitive.Kind, sensitive.Reason, sensitive.Text));

        File.Delete(Path.Combine(data.Root, "briefings.ctxs"));
        data.WriteNote("briefings", "# briefings\n");
        data.WriteFile("briefings", "a file where the folder goes");
        ActionResult failed = await data.Actions.OpenBriefing();

        Assert.Equal(
            (ActionKind.Failed, "Can't write briefings/2026-10-29."),
            (failed.Kind, failed.Text));
        Assert.Equal(DesktopActions.BriefingFailedText("briefings/2026-10-29"), failed.Text);
    }

    // 172 AC-10: a refresh or a tick with a Times arrival leaves the data folder unchanged (only the actions write).
    [Fact]
    public async Task RefreshAndTick_WithArrival_WriteNothing()
    {
        WriteTimes(DesktopFixture.Today, Arrival);
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        await data.State!.Refresh();
        data.State.Tick();
        data.UtcNow = data.UtcNow.AddDays(1);
        data.Day = data.Day.AddDays(1);
        data.State.Tick();
        await data.State.Refresh();

        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Null(data.State.TakeBriefing());
    }
}
