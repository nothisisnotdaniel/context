using System.Text;

namespace Context.Tests;

// 164 AC-7: DesktopActions.OpenDailyNote, against real files and the fixture's fixed local clock.
public sealed class DailyNoteActionsTests : IDisposable
{
    private const string Name = "2026-10-29";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string NotePath => Path.Combine(data.Root, Name + ".md");

    // No daily note: it is created with the day's appointment (in the calendar's zone) and due todo, then refreshed.
    [Fact]
    public async Task OpenDailyNote_Missing_CreatesItAndRefreshes()
    {
        data.AddTodo("call Anna", DesktopFixture.Today);
        data.AddTodo("later", DesktopFixture.Today.AddDays(1));
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000Z", "20261029T093000Z", "Room 1")));
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenDailyNote();

        Assert.Equal((ActionKind.Done, ActionReason.None, Name), (result.Kind, result.Reason, result.Text));
        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "# 2026-10-29\n\n## Appointments\n- 10:00-10:30 Standup (Room 1)\n\n## Todos\n- call Anna\n"),
            File.ReadAllBytes(NotePath));
        Assert.Contains(data.State!.Snapshot!.Notes, n => n.Name == Name);
    }

    // The day comes from the actions' local clock, not the state's day.
    [Fact]
    public async Task OpenDailyNote_UsesTheLocalClock()
    {
        data.LocalNow = new DateTime(2026, 11, 5, 0, 30, 0, DateTimeKind.Local);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenDailyNote();

        Assert.Equal("2026-11-05", result.Text);
        Assert.Equal("2026-11-05", data.Actions.DailyNoteName());
        Assert.True(File.Exists(Path.Combine(data.Root, "2026-11-05.md")));
        Assert.False(File.Exists(NotePath));
    }

    // An existing plain or sensitive note: Done/Unchanged with its name, nothing written, no key call, no refresh.
    [Theory]
    [InlineData(".md")]
    [InlineData(".ctxs")]
    public async Task OpenDailyNote_Existing_Unchanged(string extension)
    {
        data.WriteFile(Name + extension, "kept\r\n");
        data.AddTodo("call Anna", DesktopFixture.Today);
        DesktopSnapshot snapshot = await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.OpenDailyNote();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, Name), (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Same(snapshot, data.State!.Snapshot);
    }

    // An IO error: Failed with the fixed text, no exception text, nothing created.
    [Fact]
    public async Task OpenDailyNote_WriteFails_FixedText()
    {
        await data.RefreshAsync();
        data.Actions!.BeforeWrite = () => Directory.CreateDirectory(NotePath);

        ActionResult result = await data.Actions.OpenDailyNote();

        Assert.Equal(
            (ActionKind.Failed, ActionReason.WriteFailed, "Can't write 2026-10-29. Nothing was done."),
            (result.Kind, result.Reason, result.Text));
        Assert.False(File.Exists(NotePath));
    }

    // Busy-guarded: a call while another action runs is refused and creates nothing.
    [Fact]
    public async Task OpenDailyNote_WhileRunning_RefusedBusy()
    {
        await data.RefreshAsync();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("a thought");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult second = await data.Actions.OpenDailyNote();
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (second.Kind, second.Reason));
        Assert.False(File.Exists(NotePath));
    }
}
