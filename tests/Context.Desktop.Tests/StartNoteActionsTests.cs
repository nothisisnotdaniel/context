namespace Context.Tests;

// 216 AC-4: DesktopActions.AutoOpenToday (the start-up call) against real files and the fixture's fixed local clock
// (2026-10-29 10:05): it makes sure today's note exists and offers it once through DesktopState.
public sealed class StartNoteActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose()
    {
        data.Dispose();
    }

    // AC-4: a missing note is created like OpenDailyNote() (DailyNote.Compose) and offered once.
    [Fact]
    public async Task AutoOpenToday_Missing_CreatesAndOffersOnce()
    {
        data.AddTodo("call Anna", DesktopFixture.Today);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AutoOpenToday();

        Assert.Equal((ActionKind.Done, ActionReason.None, "2026-10-29"), (result.Kind, result.Reason, result.Text));
        string text = File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md"));
        Assert.StartsWith("# 2026-10-29\n", text, StringComparison.Ordinal);
        Assert.Contains("call Anna", text, StringComparison.Ordinal);
        Assert.Equal("2026-10-29", data.State!.TakeStartNote());
        Assert.Null(data.State.TakeStartNote());
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
    }

    // AC-4: the day comes from the injected local clock, not from the snapshot's day.
    [Fact]
    public async Task AutoOpenToday_UsesTheLocalClock()
    {
        await data.RefreshAsync();
        data.LocalNow = new DateTime(2026, 10, 30, 0, 1, 0, DateTimeKind.Local);

        ActionResult result = await data.Actions!.AutoOpenToday();

        Assert.Equal("2026-10-30", result.Text);
        Assert.True(File.Exists(Path.Combine(data.Root, "2026-10-30.md")));
        Assert.Equal("2026-10-30", data.State!.TakeStartNote());
    }

    // AC-4: an existing note is left as it is and still offered; the state is refreshed so Main sees the offer.
    [Fact]
    public async Task AutoOpenToday_Existing_UnchangedOfferedAndRefreshed()
    {
        data.WriteNote("2026-10-29", "# mine\n");
        await data.RefreshAsync();
        int builds = data.State!.Builds;
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        ActionResult result = await data.Actions!.AutoOpenToday();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged, "2026-10-29"), (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.True(data.State.Builds > builds);
        Assert.Equal("2026-10-29", data.State.TakeStartNote());
    }

    // AC-4: a sensitive today's note is offered without any key-store call and stays unchanged.
    [Fact]
    public async Task AutoOpenToday_Sensitive_NoKeyCall()
    {
        data.WriteFile("2026-10-29.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.AutoOpenToday();

        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (result.Kind, result.Reason));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal("2026-10-29", data.State!.TakeStartNote());
    }

    // AC-4: a failed write never throws, gives the existing red text and offers nothing.
    [Fact]
    public async Task AutoOpenToday_WriteFails_OffersNothing()
    {
        await data.RefreshAsync();
        string path = Path.Combine(data.Root, "2026-10-29.md");
        data.Actions!.BeforeWrite = () => Directory.CreateDirectory(path);

        ActionResult result = await data.Actions.AutoOpenToday();

        Assert.Equal(
            (ActionKind.Failed, DesktopActions.DailyNoteFailedText("2026-10-29")), (result.Kind, result.Text));
        Assert.Null(data.State!.TakeStartNote());
        Assert.False(File.Exists(path));
    }

    // AC-4: an exception from inside the call (here the test hook) is a Failed answer, never a throw, and no offer.
    [Fact]
    public async Task AutoOpenToday_NeverThrows()
    {
        await data.RefreshAsync();
        data.Actions!.BeforeWrite = () => throw new IOException("disk");

        ActionResult result = await data.Actions.AutoOpenToday();

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.DoesNotContain("disk", result.Text, StringComparison.Ordinal);
        Assert.Null(data.State!.TakeStartNote());
    }
}
