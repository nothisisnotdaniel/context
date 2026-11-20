namespace Context.Tests;

// 265: DesktopActions.WriteStandup (the Standup item and /standup) against real files: the section is appended or
// replaced (AC-8), a sensitive note is neither read nor written (AC-9), and Times' folder, todos.json and
// settings.json are never changed (AC-11).
public sealed class StandupActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public StandupActionsTests()
    {
        Directory.CreateDirectory(data.TimesFolder);
        File.WriteAllText(
            Path.Combine(data.TimesFolder, "2026-10-28.json"),
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"},"
            + "{\"kind\":\"checkpoint\",\"time\":\"08:00\",\"name\":\"A\",\"workItem\":12},"
            + "{\"kind\":\"leaving\",\"time\":\"09:00\"}]}");
    }

    public void Dispose()
    {
        data.Dispose();
    }

    private string NotePath => Path.Combine(data.Root, "2026-10-29.md");

    private const string Expected =
        "## Standup\n\nYesterday (2026-10-28)\n- #12 A: 1:00\n\nToday\n- call Anna\n\nBlocked\n"
        + "- Ben: slides (follow up 2026-11-03)\n";

    private void AddTodos()
    {
        data.AddTodo("call Anna", DesktopFixture.Today);
        data.AddTodo("waiting: @[Ben] slides", new DateOnly(2026, 11, 3));
    }

    // AC-8: an existing note gets the section appended after a blank line; the rest stays byte for byte; a second
    // write leaves the same bytes and says Unchanged. AC-11: Times, todos.json and settings.json are unchanged.
    [Fact]
    public async Task WriteStandup_Appends_SameBytesTwice()
    {
        AddTodos();
        data.WriteNote("2026-10-29", "# 2026-10-29\n\nmy notes\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);
        string todos = File.ReadAllText(Path.Combine(data.Root, TodoStore.FileName));

        ActionResult first = await data.Actions!.WriteStandup(DesktopFixture.Today);
        byte[] once = File.ReadAllBytes(NotePath);
        ActionResult second = await data.Actions.WriteStandup(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Done, ActionReason.None, "Standup written to 2026-10-29."),
            (first.Kind, first.Reason, first.Text));
        Assert.Equal(DesktopActions.StandupWrittenText(DesktopFixture.Today), first.Text);
        Assert.Equal((ActionKind.Done, ActionReason.Unchanged), (second.Kind, second.Reason));
        Assert.Equal("# 2026-10-29\n\nmy notes\n\n" + Expected, File.ReadAllText(NotePath));
        Assert.Equal(once, File.ReadAllBytes(NotePath));
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
        Assert.Equal(todos, File.ReadAllText(Path.Combine(data.Root, TodoStore.FileName)));
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
    }

    // AC-8: an existing Standup section is replaced up to the next heading.
    [Fact]
    public async Task WriteStandup_ReplacesSection()
    {
        AddTodos();
        data.WriteNote("2026-10-29", "# 2026-10-29\n\n## Standup\n\nold\n\n## Later\nkeep\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.WriteStandup(DesktopFixture.Today);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("# 2026-10-29\n\n" + Expected + "\n## Later\nkeep\n", File.ReadAllText(NotePath));
    }

    // AC-8: a missing note is created like the daily note first.
    [Fact]
    public async Task WriteStandup_MissingNote_Created()
    {
        AddTodos();
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.WriteStandup(DesktopFixture.Today);

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        string text = File.ReadAllText(NotePath);
        Assert.StartsWith("# 2026-10-29\n", text, StringComparison.Ordinal);
        Assert.Contains("\n## Todos\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n\n" + Expected, text, StringComparison.Ordinal);
    }

    // AC-9: a sensitive daily note: Refused with the fixed text, nothing read or written, no key-store call.
    [Fact]
    public async Task WriteStandup_Sensitive_RefusedWithoutAKeyCall()
    {
        data.WriteFile("2026-10-29.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.WriteStandup(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Locked, "2026-10-29 is sensitive, so no standup can be written."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(DesktopActions.StandupSensitiveText("2026-10-29"), result.Text);
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-9: a failed write is Failed with "Can't write <name>.", and the note is unchanged.
    [Fact]
    public async Task WriteStandup_WriteFails_Red()
    {
        string big = "# 2026-10-29\n" + new string('x', (int)NoteStore.MaxEditBytes - 20);
        data.WriteNote("2026-10-29", big);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.WriteStandup(DesktopFixture.Today);

        Assert.Equal(
            (ActionKind.Failed, ActionReason.WriteFailed, "Can't write 2026-10-29."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(big, File.ReadAllText(NotePath));
    }
}
