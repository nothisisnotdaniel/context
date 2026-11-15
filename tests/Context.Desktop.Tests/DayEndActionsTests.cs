namespace Context.Tests;

// 171: DesktopActions.EndDay (the button) and AutoDayEnd (the day-end timer), against real files and the
// fixture's fixed local clock. Times' folder is never changed (AC-8).
public sealed class DayEndActionsTests : IDisposable
{
    private const string Times =
        """
        {"entries":[
          {"kind":"arrival","time":"08:00"},
          {"kind":"checkpoint","time":"08:00","name":"A","workItem":12},
          {"kind":"breakStart","time":"12:00"},
          {"kind":"breakEnd","time":"12:30"},
          {"kind":"checkpoint","time":"12:30","name":"B"},
          {"kind":"leaving","time":"17:00"}
        ]}
        """;

    private const string Worked = "## Worked\n- Arrived 08:00, left 17:00, worked 8:30\n- #12 A: 4:00\n- B: 4:30\n";

    private readonly DesktopFixture data = new();

    public DayEndActionsTests()
    {
        Directory.CreateDirectory(data.TimesFolder);
    }

    public void Dispose()
    {
        data.Dispose();
    }

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private void WriteTimes(DateOnly day, string json) =>
        File.WriteAllText(Path.Combine(data.TimesFolder, day.ToString("yyyy-MM-dd") + ".json"), json);

    private string ReadNote(string name) => File.ReadAllText(Path.Combine(data.Root, name + ".md"));

    // AC-5/AC-6/AC-13: the button closes the day, answers with the accent text, refreshes, writes no settings.json.
    [Fact]
    public async Task EndDay_ClosesTheDay_AndRefreshes()
    {
        WriteTimes(DesktopFixture.Today, Times);
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();
        int builds = data.State!.Builds;
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);

        ActionResult result = await data.Actions!.EndDay(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Done, ActionReason.None, "Day closed. Moved 1 todo(s) to 2026-10-30."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(DesktopActions.DayClosedText(1, new DateOnly(2026, 10, 30)), result.Text);
        Assert.Contains(Worked + "\n## Moved to tomorrow\n- call Anna\n", ReadNote("2026-10-29"), StringComparison.Ordinal);
        Assert.EndsWith("## Moved from 2026-10-29\n- call Anna\n", ReadNote("2026-10-30"), StringComparison.Ordinal);
        Assert.True(data.State.Builds > builds);
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
    }

    // AC-7: a sensitive daily note: Refused with the fixed text, nothing written, no key-store call.
    [Fact]
    public async Task EndDay_Sensitive_RefusedWithoutAKeyCall()
    {
        data.WriteFile("2026-10-29.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.EndDay(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Locked, DesktopActions.DayEndSensitiveText("2026-10-29")),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-7: an unreadable todos.json: the worked hours are written, so it is Done with the red text; the file is kept.
    [Fact]
    public async Task EndDay_TodosUnreadable_WritesWorkedOnly()
    {
        WriteTimes(DesktopFixture.Today, Times);
        data.WriteFile(TodoStore.FileName, "not json");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.EndDay(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Done, ActionReason.Unreadable, DesktopActions.DayEndTodosUnreadableText),
            (result.Kind, result.Reason, result.Text));
        Assert.Contains(Worked, ReadNote("2026-10-29"), StringComparison.Ordinal);
        Assert.Equal("not json", File.ReadAllText(Path.Combine(data.Root, TodoStore.FileName)));
    }

    // AC-10/AC-11: after the leaving at 17:00 the timer refreshes Worked and moves the todos once, recorded in
    // settings.json; the next call changes nothing and does not refresh.
    [Fact]
    public async Task AutoDayEnd_AfterLeaving_ClosesOnce()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Local);
        WriteTimes(DesktopFixture.Today, Times);
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);

        ActionResult result = await data.Actions!.AutoDayEnd();

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Contains(Worked, ReadNote("2026-10-29"), StringComparison.Ordinal);
        Assert.EndsWith("## Moved from 2026-10-29\n- call Anna\n", ReadNote("2026-10-30"), StringComparison.Ordinal);
        Assert.Equal(
            new DayEndSettings("2026-10-29T17:00", DesktopFixture.Today),
            new SettingsStore(data.Root).ReadDayEnd());
        int builds = data.State!.Builds;
        SortedDictionary<string, string> after = FileSnapshot.Take(data.Root);

        ActionResult again = await data.Actions.AutoDayEnd();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (again.Kind, again.Reason));
        Assert.Equal(builds, data.State.Builds);
        Assert.Equal(after, FileSnapshot.Take(data.Root));
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
    }

    // AC-10: a leaving booked later than now, no Times file, or no leaving yet: nothing is written at all.
    [Theory]
    [InlineData(Times)]
    [InlineData("{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"}]}")]
    [InlineData(null)]
    public async Task AutoDayEnd_NotLeftYet_WritesNothing(string? json)
    {
        if (json is not null)
        {
            WriteTimes(DesktopFixture.Today, json);
        }

        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoDayEnd();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-12: a broken settings.json: Refused with the fixed text, nothing written, the file kept.
    [Fact]
    public async Task AutoDayEnd_BrokenSettings_Refused()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Local);
        WriteTimes(DesktopFixture.Today, Times);
        data.WriteFile(SettingsStore.FileName, "broken");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoDayEnd();

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Unreadable, DesktopActions.SettingsUnreadableText),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10: a timer call while another action runs is refused as busy and writes nothing; it never throws.
    [Fact]
    public async Task AutoDayEnd_WhileRunning_RefusedBusy()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Local);
        WriteTimes(DesktopFixture.Today, Times);
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
        ActionResult second = await data.Actions.AutoDayEnd();
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (second.Kind, second.Reason));
        Assert.False(File.Exists(SettingsPath));
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29.md")));
    }

    // AC-10: an exception inside the day's end (here from the test hook) is a Failed answer, never a throw.
    [Fact]
    public async Task AutoDayEnd_NeverThrows()
    {
        await data.RefreshAsync();
        data.Actions!.BeforeWrite = () => throw new IOException("disk");

        ActionResult result = await data.Actions.AutoDayEnd();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.DoesNotContain("disk", result.Text, StringComparison.Ordinal);
    }
}
