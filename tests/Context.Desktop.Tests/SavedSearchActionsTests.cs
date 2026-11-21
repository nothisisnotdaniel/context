namespace Context.Tests;

// 207 AC-6, AC-7, AC-9: saving and removing searches in settings.json through DesktopActions.
public sealed class SavedSearchActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private IReadOnlyList<SavedSearch> Stored() => new SettingsStore(data.Root).ReadSavedSearches();

    private void WriteSettings(string json) => data.WriteFile(SettingsStore.FileName, json);

    // AC-6, AC-7: the name is trimmed and collapsed, the query trimmed; the entry goes last, other properties stay,
    // and the state is refreshed so the sidebar shows it.
    [Fact]
    public async Task Save_AddsLast_NormalizesName_KeepsOthers_Refreshes()
    {
        WriteSettings("{\"theme\":\"dark\",\"dayEnd\":{\"moved\":\"2026-10-28\"},\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.SaveSearch("  Open \t  job1234 ", "  #job1234 and not #done  ");

        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.Equal("Saved search Open job1234.", result.Text);
        Assert.Equal(
            [("Work", "#work"), ("Open job1234", "#job1234 and not #done")],
            Stored().Select(s => (s.Name, s.Query)).ToArray());
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 28), new SettingsStore(data.Root).ReadDayEnd().Moved);
        Assert.True(data.State.Builds > builds);
        Assert.Equal(["Work", "Open job1234"], data.State.Snapshot!.SavedSearches.Select(s => s.Name).ToArray());
    }

    // AC-6: a missing file is created with just the entry.
    [Fact]
    public async Task Save_NoFile_CreatesIt()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.SaveSearch("Budget", "budget 2027");

        Assert.Equal(ActionKind.Done, result.Kind);
        SavedSearch saved = Assert.Single(Stored());
        Assert.Equal(("Budget", "budget 2027"), (saved.Name, saved.Query));
    }

    // AC-6: every refusal has its text and writes nothing.
    [Theory]
    [InlineData("", "#work", "Type a name for the search.")]
    [InlineData("   ", "#work", "Type a name for the search.")]
    [InlineData("long", "#work", "A search name can be at most 50 characters, without control characters.")]
    [InlineData("a\u0001b", "#work", "A search name can be at most 50 characters, without control characters.")]
    [InlineData("Mine", "", "Can't save that search.")]
    [InlineData("Mine", "   ", "Can't save that search.")]
    [InlineData("Mine", " ab ", "Can't save that search.")]
    [InlineData("Mine", "long", "Can't save that search.")]
    [InlineData("Mine", "#a or #b", "Can't save that search.")]
    [InlineData("Mine", "not #done", "Can't save that search.")]
    [InlineData("Mine", "#01234", "Can't save that search.")] // 379: #1234 is a work item search now; #0… is not
    [InlineData("work", "#home", "A saved search named work already exists.")]
    [InlineData("  WORK ", "#home", "A saved search named WORK already exists.")]
    public async Task Save_Refused_WritesNothing(string name, string query, string expected)
    {
        WriteSettings("{\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);
        string shownName = name == "long" ? new string('n', 51) : name;
        string shownQuery = query == "long" ? new string('q', 201) : query;

        ActionResult result = await data.Actions!.SaveSearch(shownName, shownQuery);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(expected, result.Text);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // AC-6: a 50-character name is fine; the 51st entry is refused.
    [Fact]
    public async Task Save_FiftyStored_51stRefused()
    {
        await data.RefreshAsync();
        for (int i = 0; i < SavedSearches.MaxCount - 1; i++)
        {
            Assert.Equal(ActionKind.Done, (await data.Actions!.SaveSearch($"n{i}", "#a")).Kind);
        }

        Assert.Equal(ActionKind.Done, (await data.Actions!.SaveSearch(new string('n', 50), "#a")).Kind);
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions.SaveSearch("one more", "#a");

        Assert.Equal((ActionKind.Refused, "Can't save more than 50 searches."), (result.Kind, result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(50, Stored().Count);
    }

    // AC-6: a broken settings.json fails and stays byte for byte as it was.
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"savedSearches\":{}}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\"}]}")]
    [InlineData("{\"dayEnd\":{\"moved\":5}}")]
    public async Task Save_BrokenFile_Failed_Unchanged(string json)
    {
        WriteSettings(json);
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.SaveSearch("Mine", "#work");

        Assert.Equal(
            (ActionKind.Failed, ActionReason.Unreadable, "Can't read settings.json. Nothing was saved."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 207 security: a save that would make settings.json over 64 KiB is refused and writes nothing.
    [Fact]
    public async Task Save_OverTheLimit_Refused_Unchanged()
    {
        WriteSettings("{\"padding\":\"" + new string('x', 65_500) + "\"}");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.SaveSearch("Mine", "#work");

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Invalid, "settings.json would be over 64 KiB. Nothing was saved."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // AC-9: remove takes the entry out (ignoring case), keeps the others in order and refreshes.
    [Fact]
    public async Task Remove_Done_KeepsOthers_Refreshes()
    {
        WriteSettings(
            "{\"x\":1,\"savedSearches\":[{\"name\":\"A\",\"query\":\"#a\"},{\"name\":\"Work\",\"query\":\"#work\"},{\"name\":\"C\",\"query\":\"#c\"}]}");
        await data.RefreshAsync();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.RemoveSavedSearch("work");

        Assert.Equal((ActionKind.Done, "Removed saved search Work."), (result.Kind, result.Text));
        Assert.Equal(["A", "C"], Stored().Select(s => s.Name).ToArray());
        Assert.Contains("\"x\": 1", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.True(data.State.Builds > builds);
        Assert.Equal(["A", "C"], data.State.Snapshot!.SavedSearches.Select(s => s.Name).ToArray());
    }

    // AC-9: a name no longer stored is refused, writing nothing (also with no file at all).
    [Theory]
    [InlineData("{\"savedSearches\":[{\"name\":\"A\",\"query\":\"#a\"}]}")]
    [InlineData(null)]
    public async Task Remove_Gone_Refused(string? json)
    {
        await AssertRemoveGone(json, "Work");
    }

    // 207 security: a name that can't be normalized can't be stored, so it is gone without matching it raw.
    [Theory]
    [InlineData("Work\u200B")]
    [InlineData("Wo\u0001rk")]
    [InlineData("")]
    public async Task Remove_NameThatCantBeStored_RefusedGone(string name)
    {
        await AssertRemoveGone("{\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}", name);
    }

    private async Task AssertRemoveGone(string? json, string name)
    {
        if (json is not null)
        {
            WriteSettings(json);
        }

        await data.RefreshAsync();
        byte[]? before = json is null ? null : File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.RemoveSavedSearch(name);

        Assert.Equal(
            (ActionKind.Refused, ActionReason.Missing, "That saved search is gone. Nothing was done."),
            (result.Kind, result.Reason, result.Text));
        Assert.Equal(before, File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null);
    }

    // AC-9: a broken settings.json fails the removal and stays as it was.
    [Fact]
    public async Task Remove_BrokenFile_Failed_Unchanged()
    {
        WriteSettings("{\"savedSearches\":[1]}");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.RemoveSavedSearch("Work");

        Assert.Equal(
            (ActionKind.Failed, "Can't read settings.json. Nothing was removed."), (result.Kind, result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // Busy: while another action runs, saving and removing are refused and write nothing.
    [Fact]
    public async Task SaveAndRemove_WhileRunning_RefusedBusy()
    {
        WriteSettings("{\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        Task<ActionResult> first = data.Actions.CaptureText("first");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        ActionResult save = await data.Actions.SaveSearch("Mine", "#work");
        ActionResult remove = await data.Actions.RemoveSavedSearch("Work");
        release.Set();
        await first;

        Assert.Equal((ActionKind.Refused, ActionReason.Busy, DesktopActions.BusyText), (save.Kind, save.Reason, save.Text));
        Assert.Equal((ActionKind.Refused, ActionReason.Busy), (remove.Kind, remove.Reason));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }
}
