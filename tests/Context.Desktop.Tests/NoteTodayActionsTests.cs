namespace Context.Tests;

// 282 AC-5: DesktopActions.NoteToday (the bar's note line), against real files and the fixture's fixed local clock.
public sealed class NoteTodayActionsTests : IDisposable
{
    private const string Name = "2026-10-29";

    private readonly DesktopFixture data = new();

    public NoteTodayActionsTests()
    {
        data.LocalNow = new DateTime(2026, 10, 29, 14, 5, 0, DateTimeKind.Local);
    }

    public void Dispose() => data.Dispose();

    private string NotePath => Path.Combine(data.Root, Name + ".md");

    [Fact]
    public async Task NoteToday_Missing_CreatesTheDailyNoteAndAppends()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.NoteToday("  z  ");

        Assert.Equal((ActionKind.Done, "Added to 2026-10-29.", Name), (result.Kind, result.Text, result.Name));
        string text = File.ReadAllText(NotePath);
        Assert.StartsWith("# 2026-10-29\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n- 14:05 z\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Day\nkept", "# Day\nkept\n- 14:05 z\n")]
    [InlineData("# Day\nkept\n", "# Day\nkept\n- 14:05 z\n")]
    [InlineData("", "- 14:05 z\n")]
    public async Task NoteToday_Existing_AppendsAtTheEnd(string before, string after)
    {
        data.WriteNote(Name, before);
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.NoteToday("z");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(after, File.ReadAllText(NotePath));
    }

    [Fact]
    public async Task NoteToday_Sensitive_RefusedNothingWritten()
    {
        data.WriteFile(Name + NoteStore.SensitiveExtension, "kept\r\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.NoteToday("z");

        Assert.Equal((ActionKind.Refused, ActionReason.Locked), (result.Kind, result.Reason));
        Assert.Equal("2026-10-29 is sensitive. Nothing was added.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoteToday_Empty_Refused(string text)
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.NoteToday(text);

        Assert.Equal((ActionKind.Refused, DesktopActions.NoteEmptyText), (result.Kind, result.Text));
        Assert.False(File.Exists(NotePath));
    }

    [Fact]
    public async Task NoteToday_OverTheLimit_Refused()
    {
        await data.RefreshAsync();

        Assert.Equal(ActionKind.Done, (await data.Actions!.NoteToday(new string('x', Inbox.MaxTextLength))).Kind);
        ActionResult result = await data.Actions.NoteToday(new string('x', Inbox.MaxTextLength + 1));

        Assert.Equal((ActionKind.Refused, DesktopActions.NoteTooLongText), (result.Kind, result.Text));
    }

    [Fact]
    public async Task NoteToday_ChangedOnce_ReadsAgain()
    {
        data.WriteNote(Name, "# Day\n");
        await data.RefreshAsync();
        data.Actions!.BeforeNoteTodaySave = attempt =>
        {
            if (attempt == 0)
            {
                File.WriteAllText(NotePath, "# Day\nother\n");
            }
        };

        ActionResult result = await data.Actions.NoteToday("z");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("# Day\nother\n- 14:05 z\n", File.ReadAllText(NotePath));
    }

    [Fact]
    public async Task NoteToday_AlwaysChanging_GivesUp()
    {
        data.WriteNote(Name, "# Day\n");
        await data.RefreshAsync();
        List<int> attempts = [];
        data.Actions!.BeforeNoteTodaySave = attempt =>
        {
            attempts.Add(attempt);
            File.WriteAllText(NotePath, "# Day\n" + attempt + "\n");
        };

        ActionResult result = await data.Actions.NoteToday("z");

        Assert.Equal((ActionKind.Refused, DesktopActions.ChangedText(Name)), (result.Kind, result.Text));
        Assert.Equal([0, 1, 2, 3], attempts);
        Assert.DoesNotContain("14:05 z", File.ReadAllText(NotePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoteToday_ControlCharacters_AreCleaned()
    {
        await data.RefreshAsync();

        await data.Actions!.NoteToday("a\nb");

        Assert.DoesNotContain("a\nb", File.ReadAllText(NotePath), StringComparison.Ordinal);
        Assert.EndsWith("\n", File.ReadAllText(NotePath), StringComparison.Ordinal);
    }
}
