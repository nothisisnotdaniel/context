using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 280 AC-12: the repository folders in settings.json, through DesktopActions and the Settings page.
public sealed class RepositorySettingsActionsTests : IDisposable
{
    // The settings page loads behind Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string Others =
        "{\"dayEnd\":{\"moved\":\"2026-10-28\"},\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]";

    private readonly DesktopFixture data = new();
    private readonly string repos;

    public RepositorySettingsActionsTests()
    {
        repos = Path.GetFullPath(Path.Combine(data.Root, "..", "repos"));
        Directory.CreateDirectory(repos);
    }

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private string Repo(string name)
    {
        string folder = Path.Combine(repos, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private IReadOnlyList<string> Stored() => new SettingsStore(data.Root).ReadRepositories();

    private void AssertOthersKept()
    {
        SettingsStore store = new SettingsStore(data.Root);
        Assert.Equal(new DateOnly(2026, 10, 28), store.ReadDayEnd().Moved);
        Assert.Equal([("Work", "#work")], store.ReadSavedSearches().Select(s => (s.Name, s.Query)).ToArray());
    }

    // AC-12: adding keeps the saved order and the other properties; removing drops just that folder.
    [Fact]
    public async Task AddThenRemove_KeepsOrderAndOthers()
    {
        data.WriteFile(SettingsStore.FileName, Others + "}");
        await data.RefreshAsync();
        string b = Repo("b");
        string a = Repo("a");
        string c = Repo("c");

        ActionResult first = await data.Actions!.AddRepository("  " + b + " ");
        await data.Actions.AddRepository(a);
        await data.Actions.AddRepository(c);

        Assert.Equal((ActionKind.Done, "Added " + b + "."), (first.Kind, first.Text));
        Assert.Equal([b, a, c], Stored());
        AssertOthersKept();

        ActionResult removed = await data.Actions.RemoveRepository(a);

        Assert.Equal((ActionKind.Done, "Removed " + a + "."), (removed.Kind, removed.Text));
        Assert.Equal([b, c], Stored());
        AssertOthersKept();

        ActionResult gone = await data.Actions.RemoveRepository(a);
        Assert.Equal((ActionKind.Refused, ActionReason.Missing, DesktopActions.RepositoryGoneText), (gone.Kind, gone.Reason, gone.Text));
    }

    // AC-12: a blank, relative, missing or duplicate (any case) folder is refused with its fixed text; nothing written.
    [Fact]
    public async Task Refusals_WriteNothing()
    {
        string existing = Repo("app");
        data.WriteFile(SettingsStore.FileName, Others + ",\"repositories\":[" + System.Text.Json.JsonSerializer.Serialize(existing) + "]}");
        await data.RefreshAsync();
        byte[] before = File.ReadAllBytes(SettingsPath);
        string missing = Path.Combine(repos, "missing");
        // 281: a folder that exists and differs from existing only in case.
        string upper = Repo("APP");

        (string Typed, ActionReason Reason, string Text)[] cases =
        [
            ("   ", ActionReason.Invalid, DesktopActions.RepositoryBlankText),
            (Path.Combine("relative", "app"), ActionReason.Invalid, DesktopActions.RepositoryRelativeText),
            (missing, ActionReason.Missing, DesktopActions.RepositoryMissingText(missing)),
            (existing, ActionReason.Exists, DesktopActions.RepositoryDuplicateText(existing)),
            (upper, ActionReason.Exists, DesktopActions.RepositoryDuplicateText(upper)),
            (new string('a', 401), ActionReason.Invalid, DesktopActions.RepositoryTooLongText),
        ];
        foreach ((string typed, ActionReason reason, string text) in cases)
        {
            ActionResult result = await data.Actions!.AddRepository(typed);

            Assert.Equal((ActionKind.Refused, reason, text), (result.Kind, result.Reason, result.Text));
        }

        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // AC-12: a 21st folder is refused and nothing is written.
    [Fact]
    public async Task TwentyFirst_Refused()
    {
        await data.RefreshAsync();
        for (int i = 0; i < Repositories.MaxCount; i++)
        {
            Assert.Equal(ActionKind.Done, (await data.Actions!.AddRepository(Repo("r" + i))).Kind);
        }

        byte[] before = File.ReadAllBytes(SettingsPath);

        ActionResult result = await data.Actions!.AddRepository(Repo("one-more"));

        Assert.Equal((ActionKind.Refused, DesktopActions.RepositoryFullText), (result.Kind, result.Text));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(20, Stored().Count);
    }

    // AC-12: a broken settings.json is never overwritten.
    [Fact]
    public async Task BrokenSettings_FailedNotOverwritten()
    {
        data.WriteFile(SettingsStore.FileName, "{broken");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.AddRepository(Repo("app"));

        Assert.Equal((ActionKind.Failed, DesktopActions.RepositoryUnreadableText), (result.Kind, result.Text));
        Assert.Equal("{broken", File.ReadAllText(SettingsPath));
    }

    // AC-12: the Settings page lists the folders in saved order, adds through the field (accent line, field emptied),
    // shows a refusal in red and removes through the row's button.
    [Fact]
    public async Task SettingsPage_ListsAddsRefusesRemoves()
    {
        string z = Repo("z");
        string a = Repo("a");
        new SettingsStore(data.Root).SaveRepositories([z, a]);
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);

        Assert.Equal([z, a], cut.FindAll(".repository-folder").Select(s => s.TextContent).ToArray());
        Assert.Equal("Repository folders", cut.Find(".repository-list").GetAttribute("aria-label"));

        // A Load() or DesktopState reload renders the page again by itself and gives the handlers new IDs, so each
        // event finds its element and dispatches in one dispatcher turn (382).
        string m = Repo("m");
        await cut.InvokeAsync(() => cut.Find("input.repository-field").InputAsync(new ChangeEventArgs { Value = m }));
        await cut.InvokeAsync(() => cut.Find("input.repository-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));

        cut.WaitForAssertion(() => Assert.Equal([z, a, m], cut.FindAll(".repository-folder").Select(s => s.TextContent).ToArray()), Wait);
        Assert.Equal("Added " + m + ".", data.StatusBar.Text);
        Assert.Equal("accent", data.StatusBar.Css);
        Assert.Null(cut.Find("input.repository-field").GetAttribute("value"));

        await cut.InvokeAsync(() => cut.Find("input.repository-field").InputAsync(new ChangeEventArgs { Value = "relative" }));
        await cut.InvokeAsync(() => cut.Find("button.repository-add").ClickAsync(new MouseEventArgs()));

        Bar.WaitForAssertion(() => Assert.Equal(DesktopActions.RepositoryRelativeText, data.StatusBar.Text), Wait);
        Assert.Equal("red", data.StatusBar.Css);

        await cut.InvokeAsync(() => cut.FindAll("button.repository-remove")[0].ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() => Assert.Equal([a, m], cut.FindAll(".repository-folder").Select(s => s.TextContent).ToArray()), Wait);
        Assert.Equal("Removed " + z + ".", data.StatusBar.Text);
        Assert.Equal([a, m], Stored());
    }

    // AC-12: no folders shows the grey hint.
    [Fact]
    public void SettingsPage_None_GreyHint()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);

        Assert.Equal(SettingsPanel.RepositoriesNotSetText, cut.Find(".repository-state").TextContent);
        Assert.True(cut.Find(".repository-state").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".repository-list"));
    }
}
