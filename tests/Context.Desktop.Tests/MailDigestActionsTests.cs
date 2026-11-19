namespace Context.Tests;

// 286 AC-5, AC-6, AC-8, AC-9: DesktopState.MailDigest and DesktopActions.AcceptSuggestion, with FakeOutlookSource.
public sealed class MailDigestActionsTests : IDisposable
{
    private const string MeetingClass = "IPM.Schedule.Meeting.Request";

    // 23:30 UTC is 00:30 the next day in the fixture's UTC+1 zone.
    private static readonly DateTime Received = new(2026, 10, 27, 23, 30, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private static MailMessage Mail(string body, string subject = "Status", string sender = "Anna", string messageClass = "IPM.Note") =>
        new(subject, sender, Received, messageClass, body);

    private static MailSuggestion Suggestion(MailMessage mail) => Assert.Single(MailDigest.Suggest([mail], DesktopFixture.Today));

    private void TurnOn()
    {
        SettingsStore store = new SettingsStore(data.Root);
        store.SaveOutlook(true);
        store.SaveMailDigest(true);
    }

    // Every file under the data folder with its bytes.
    private SortedDictionary<string, byte[]> Files()
    {
        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(data.Root, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(data.Root, path).Replace('\\', '/')] = File.ReadAllBytes(path);
        }

        return files;
    }

    private void AssertUnchanged(SortedDictionary<string, byte[]> before)
    {
        SortedDictionary<string, byte[]> after = Files();
        Assert.Equal(before.Keys, after.Keys);
        Assert.All(before, f => Assert.Equal(f.Value, after[f.Key]));
    }

    // AC-5: off (no setting, or Outlook off) answers Off and never asks the source.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task MailDigest_Off_NoCall(bool digest, bool outlook)
    {
        SettingsStore store = new SettingsStore(data.Root);
        store.SaveOutlook(outlook);
        store.SaveMailDigest(digest);
        DesktopState state = data.CreateState();

        MailDigestView view = await state.MailDigest();

        Assert.Equal(OutlookStatus.Off, view.Status);
        Assert.Empty(view.Suggestions);
        Assert.False(await state.MailDigestOn());
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // AC-5: a broken settings.json is off too.
    [Fact]
    public async Task MailDigest_BrokenSettings_Off()
    {
        data.WriteFile(SettingsStore.FileName, "not json");
        DesktopState state = data.CreateState();

        Assert.Equal(OutlookStatus.Off, (await state.MailDigest()).Status);
        Assert.Equal(0, data.Outlook.MailCalls);
    }

    // AC-6: each call reads anew (no cache), 7 days back from the UTC clock, at most 200.
    [Fact]
    public async Task MailDigest_On_ReadsEachTime()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it."));
        DesktopState state = data.CreateState();

        Assert.True(await state.MailDigestOn());
        MailDigestView first = await state.MailDigest();
        MailDigestView second = await state.MailDigest();

        Assert.Equal(OutlookStatus.Ok, first.Status);
        Assert.Equal("Please send it.", Assert.Single(first.Suggestions).Text);
        Assert.Single(second.Suggestions);
        Assert.Equal(
            [(data.UtcNow.AddDays(-7), 200), (data.UtcNow.AddDays(-7), 200)],
            data.Outlook.MailArguments.ToArray());
        Assert.Equal("MailDigestView", first.ToString());
    }

    // AC-8: a Todo is added due today, and a Done suggestion is left out of later reads.
    [Fact]
    public async Task Accept_Todo_DueToday_HiddenAfterwards()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it."));
        DesktopState state = data.CreateState();
        MailSuggestion todo = Assert.Single((await state.MailDigest()).Suggestions);

        ActionResult result = await data.Actions!.AcceptSuggestion(todo);

        Assert.Equal((ActionKind.Done, "Please send it."), (result.Kind, result.Text));
        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(("Please send it.", DesktopFixture.Today), (added.Text, added.Due));
        Assert.Empty((await state.MailDigest()).Suggestions);
    }

    // AC-8: a FollowUp is added on its due date; a 300-character sender and subject still give a Done todo.
    [Fact]
    public async Task Accept_FollowUp_LongSubject_DueInTwoDays()
    {
        data.CreateState();
        MailSuggestion follow = Suggestion(Mail("Ok?", new string('t', 300), new string('s', 300)));

        ActionResult result = await data.Actions!.AcceptSuggestion(follow);

        Assert.Equal(ActionKind.Done, result.Kind);
        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal(DesktopFixture.Today.AddDays(2), added.Due);
        Assert.Equal(200, added.Text.Length);
        Assert.StartsWith("Reply to sss", added.Text, StringComparison.Ordinal);
    }

    // AC-8: a MeetingNote creates the top-level note <Title> with the heading, the sender and the date in the zone.
    [Fact]
    public async Task Accept_MeetingNote_CreatesTopLevelNote()
    {
        data.CreateState();
        MailSuggestion note = Suggestion(Mail(string.Empty, "Q4/Q1: plan", "Eve\nAdams", MeetingClass));

        ActionResult result = await data.Actions!.AcceptSuggestion(note);

        Assert.Equal((ActionKind.Done, "Q4 Q1 plan"), (result.Kind, result.Text));
        string path = Path.Combine(data.Root, "Q4 Q1 plan.md");
        Assert.Equal("# Q4 Q1 plan\n\nFrom mail by Eve Adams, 2026-10-28.\n", File.ReadAllText(path));
        Assert.Equal(["Q4 Q1 plan.md"], Files().Keys.Where(k => k.EndsWith(".md", StringComparison.Ordinal)).ToArray());
        Assert.Empty(data.Todos!.List());
    }

    // AC-8: a Windows-reserved stem is Refused/Invalid with the ValidateTitle text; nothing is written.
    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    public async Task Accept_MeetingNote_ReservedName_Invalid(string subject)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.CreateState();
        SortedDictionary<string, byte[]> before = Files();
        MailSuggestion note = Suggestion(Mail(string.Empty, subject, messageClass: MeetingClass));

        ActionResult result = await data.Actions!.AcceptSuggestion(note);

        Assert.Equal((ActionKind.Refused, ActionReason.Invalid), (result.Kind, result.Reason));
        Assert.Equal(NoteStore.ValidateTitle(subject), result.Text);
        Assert.False(string.IsNullOrEmpty(result.Text));
        AssertUnchanged(before);
    }

    // AC-8: a taken name (ignoring case) is Refused/Exists with ExistsText(Title); the note stays as it was.
    [Theory]
    [InlineData("Planning")]
    [InlineData("planning")]
    public async Task Accept_MeetingNote_Exists_Refused(string existing)
    {
        data.WriteNote(existing, "# mine\n");
        data.CreateState();
        SortedDictionary<string, byte[]> before = Files();
        MailSuggestion note = Suggestion(Mail(string.Empty, "Planning", messageClass: MeetingClass));

        ActionResult result = await data.Actions!.AcceptSuggestion(note);

        Assert.Equal((ActionKind.Refused, ActionReason.Exists), (result.Kind, result.Reason));
        Assert.Equal(DesktopActions.ExistsText("Planning"), result.Text);
        AssertUnchanged(before);
    }

    // AC-8: a refused accept is not hidden; the next read still shows it.
    [Fact]
    public async Task Accept_Refused_NotHidden()
    {
        TurnOn();
        data.WriteNote("Planning", "# mine\n");
        data.Outlook.Mails.Add(Mail(string.Empty, "Planning", messageClass: MeetingClass));
        DesktopState state = data.CreateState();
        MailSuggestion note = Assert.Single((await state.MailDigest()).Suggestions);

        Assert.Equal(ActionKind.Refused, (await data.Actions!.AcceptSuggestion(note)).Kind);

        Assert.Single((await state.MailDigest()).Suggestions);
    }

    // AC-9: a hidden suggestion stays hidden in this state only; another app over the same folder shows it again.
    [Fact]
    public async Task Hide_SessionOnly()
    {
        TurnOn();
        data.Outlook.Mails.Add(Mail("Please send it."));
        DesktopState state = data.CreateState();
        MailSuggestion todo = Assert.Single((await state.MailDigest()).Suggestions);
        SortedDictionary<string, byte[]> before = Files();

        state.HideMailSuggestion(todo);

        Assert.Empty((await state.MailDigest()).Suggestions);
        AssertUnchanged(before);
        using DesktopFixture again = new DesktopFixture(data);
        again.Outlook.Mails.Add(Mail("Please send it."));
        Assert.Single((await again.CreateState().MailDigest()).Suggestions);
    }
}
