using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 174 AC-12: the kept versions of the open note and their diff to the saved text.
public sealed class HistoryPanelTests : IDisposable
{
    private const string Id = "20261029T090000000Z";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-12: one row per version, newest first, in local yyyy-MM-dd HH:mm:ss (the fixture's zone is UTC+1).
    [Fact]
    public async Task Render_Rows_NewestFirst_LocalTime()
    {
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", "20261028T081500250Z", "older\n");
        WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();

        IRenderedComponent<HistoryPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal(
            ["2026-10-29 10:00:00", "2026-10-28 09:15:00"], cut.FindAll(".history-row button").Select(b => b.TextContent)), Wait);
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal("History", cut.Find("h2").TextContent);
    }

    // AC-12: choosing a version shows its diff: removed red "-", added accent "+", unchanged grey.
    [Fact]
    public async Task Choose_ShowsTheDiffToTheSavedText()
    {
        data.WriteNote("Plan", "# Plan\nnew line\nend\n");
        WriteVersion("Plan", Id, "# Plan\nold line\nend\n");
        await data.RefreshAsync();
        IRenderedComponent<HistoryPanel> cut = Render("Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".history-row button")), Wait);

        await cut.Find(".history-row button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".diff-line")), Wait);
        Assert.Equal(
            [
                ("diff-line same grey", "  # Plan"),
                ("diff-line removed red", "- old line"),
                ("diff-line added accent", "+ new line"),
                ("diff-line same grey", "  end"),
                ("diff-line same grey", "  "),
            ],
            cut.FindAll(".diff-line").Select(l => (l.GetAttribute("class")!, l.TextContent)));
        Assert.All(cut.FindAll(".diff-line"), l => Assert.Equal(l.TextContent, l.GetAttribute("title")));
        Assert.Equal("Restore this version", cut.Find("button.restore-version").TextContent);
        Assert.Equal("true", cut.Find(".history-row button").GetAttribute("aria-pressed"));
    }

    // AC-12: no versions is grey "No earlier versions of <name>."
    [Fact]
    public async Task Render_NoVersions_Grey()
    {
        data.WriteNote("Plan", "now\n");
        await data.RefreshAsync();

        IRenderedComponent<HistoryPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal("No earlier versions of Plan.", cut.Find(".no-history").TextContent), Wait);
        Assert.Contains("grey", cut.Find(".no-history").GetAttribute("class"), StringComparison.Ordinal);
    }

    // AC-12: a history that can't be read is red "Can't read the history of <name>."
    [Fact]
    public async Task Render_BrokenHistory_Red()
    {
        data.WriteNote("Plan", "now\n");
        data.WriteFile(NoteHistory.FolderName, "in the way");
        await data.RefreshAsync();

        IRenderedComponent<HistoryPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the history of Plan.", cut.Find(".history-unreadable").TextContent), Wait);
        Assert.Contains("red", cut.Find(".history-unreadable").GetAttribute("class"), StringComparison.Ordinal);
    }

    // AC-12: a version that can't be read is red "Can't read this version." and offers no restore.
    [Fact]
    public async Task Choose_UnreadableVersion_Red()
    {
        data.WriteNote("Plan", "now\n");
        string version = WriteVersion("Plan", Id, "old\n");
        await data.RefreshAsync();
        IRenderedComponent<HistoryPanel> cut = Render("Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".history-row button")), Wait);
        File.Delete(version);

        await cut.Find(".history-row button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Can't read this version.", cut.Find(".version-unreadable").TextContent), Wait);
        Assert.Contains("red", cut.Find(".version-unreadable").GetAttribute("class"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button.restore-version"));
    }

    // AC-12: more than 1000 changed lines is grey "Too many changes to show."
    [Fact]
    public async Task Choose_TooManyChanges_Grey()
    {
        data.WriteNote("Plan", string.Join('\n', Enumerable.Range(0, 600).Select(i => "new " + i)));
        WriteVersion("Plan", Id, string.Join('\n', Enumerable.Range(0, 600).Select(i => "old " + i)));
        await data.RefreshAsync();
        IRenderedComponent<HistoryPanel> cut = Render("Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".history-row button")), Wait);

        await cut.Find(".history-row button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Too many changes to show.", cut.Find(".too-different").TextContent), Wait);
        Assert.Contains("grey", cut.Find(".too-different").GetAttribute("class"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".diff-line"));
    }

    // AC-12: names and lines are shown on one line; restore hands the chosen version to the parent.
    [Fact]
    public async Task Restore_HandsTheVersionToTheParent()
    {
        data.WriteNote("Plan", "now\n");
        WriteVersion("Plan", Id, "old\u0007\n");
        await data.RefreshAsync();
        (string Id, DateTime SavedUtc)? asked = null;
        IRenderedComponent<HistoryPanel> cut = data.Bunit.Render<HistoryPanel>(p => p
            .Add(h => h.Name, "Plan")
            .Add(h => h.OnRestore, v => asked = v));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".history-row button")), Wait);
        await cut.Find(".history-row button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.restore-version")), Wait);

        Assert.DoesNotContain(cut.FindAll(".diff-line"), l => l.TextContent.Contains('\u0007'));
        await cut.Find("button.restore-version").ClickAsync(new MouseEventArgs());

        Assert.Equal((Id, new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc)), asked);
    }

    // AC-12: the time text is local yyyy-MM-dd HH:mm:ss.
    [Fact]
    public void TimeText_IsLocal()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Plus2", TimeSpan.FromHours(2), "Plus2", "Plus2");

        Assert.Equal("2026-10-29 11:30:05", HistoryPanel.TimeText(new DateTime(2026, 10, 29, 9, 30, 5, DateTimeKind.Utc), zone));
    }

    private IRenderedComponent<HistoryPanel> Render(string name) =>
        data.Bunit.Render<HistoryPanel>(p => p.Add(h => h.Name, name));

    private string WriteVersion(string name, string id, string text)
    {
        string folder = Path.Combine(data.Root, NoteHistory.FolderName, name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, id + ".md");
        File.WriteAllText(path, text);
        return path;
    }
}
