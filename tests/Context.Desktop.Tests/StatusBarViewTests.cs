using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 455 AC-1, AC-2, AC-4, AC-7: the status bar in Main, its view, and its CSS.
public sealed class StatusBarViewTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private static void AssertOneEmptyBar(IRenderedComponent<Main> cut)
    {
        Assert.Single(cut.FindAll("footer.status-bar"));
        IElement status = Assert.Single(cut.FindAll("p.main-status"));
        // The parent by selector, not by instance: a render the page starts by itself between two FindAll calls
        // gives new element instances (job 491).
        Assert.True(status.ParentElement!.Matches("footer.status-bar"));
        Assert.True(status.ClassList.Contains("cut"));
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal(string.Empty, status.TextContent);
        Assert.Null(status.ParentElement!.Closest(".layout"));
    }

    // AC-1: while the first snapshot loads, Main shows Loading… and the one empty bar.
    [Fact]
    public void Main_WhileLoading_HasOneEmptyBar()
    {
        data.CreateState();

        IRenderedComponent<Main> cut = data.Bunit.Render<Main>();

        Assert.NotEmpty(cut.FindAll(".loading"));
        AssertOneEmptyBar(cut);
    }

    // AC-1: a failed load keeps the bar.
    [Fact]
    public async Task Main_FailedLoad_HasOneEmptyBar()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("boom");

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("Can't read the data folder.", cut.Find(".failed").TextContent);
        AssertOneEmptyBar(cut);
    }

    // AC-1: with the layout shown, the bar sits outside .layout, exactly once.
    [Fact]
    public async Task Main_WithLayout_HasOneEmptyBarOutsideTheLayout()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".layout")), Wait);
        cut.WaitForAssertion(() => AssertOneEmptyBar(cut), Wait);
    }

    // AC-1, AC-4: with a note open, there is still exactly one bar, and a save's text shows in it.
    [Fact]
    public async Task Main_WithNoteOpen_OneBarShowsTheSave()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await NoteOpener.OpenAsync(cut, "Plan");
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        AssertOneEmptyBar(cut);
        js.Edit("# Plan\n\nMore\n");

        await StaleClick.KeyDownAsync(cut, ".note-editor", new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find("footer.status-bar p.main-status").TextContent), Wait);
        Assert.Single(cut.FindAll("p.main-status"));
        Assert.True(cut.Find("p.main-status").ClassList.Contains("accent"));
        Assert.Equal("Saved Plan.", cut.Find("p.main-status").GetAttribute("title"));
        Assert.Empty(cut.FindAll(".note-editor p.status"));
    }

    // AC-2: the view shows the bar's text, class and title, and follows each change.
    [Fact]
    public void View_FollowsTheBar()
    {
        data.CreateState();
        IRenderedComponent<StatusBarView> bar = data.Bunit.Render<StatusBarView>();

        long id = data.StatusBar.Show("Can't save.", "red");

        bar.WaitForAssertion(() => Assert.Equal("Can't save.", bar.Find("p.main-status").TextContent), Wait);
        Assert.Equal(["main-status", "cut", "red"], bar.Find("p.main-status").ClassList.ToArray());
        Assert.Equal("Can't save.", bar.Find("p.main-status").GetAttribute("title"));

        data.StatusBar.Clear(id);

        bar.WaitForAssertion(() => Assert.Equal(string.Empty, bar.Find("p.main-status").TextContent), Wait);
    }

    // AC-4: a multi-line message is one span.line.cut per line, each with its class and title.
    [Fact]
    public void View_MultiLine_OneSpanPerLine()
    {
        data.CreateState();
        IRenderedComponent<StatusBarView> bar = data.Bunit.Render<StatusBarView>();

        data.StatusBar.Show([("Attached a.png", "accent"), ("Can't attach b.exe", "red")]);

        bar.WaitForAssertion(() => Assert.Equal(2, bar.FindAll("p.main-status span.line").Count), Wait);
        IReadOnlyList<IElement> lines = bar.FindAll("p.main-status span.line.cut");
        Assert.Equal(["Attached a.png", "Can't attach b.exe"], lines.Select(l => l.TextContent).ToArray());
        Assert.True(lines[0].ClassList.Contains("accent"));
        Assert.True(lines[1].ClassList.Contains("red"));
        Assert.Equal("Can't attach b.exe", lines[1].GetAttribute("title"));
    }

    // AC-7: the bar is fixed at the bottom, and .layout leaves its height free through the same variable.
    [Fact]
    public void AppCss_BarFixedAtTheBottom_LayoutReservesItsHeight()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));

        string bar = Block(css, "\n.status-bar {");
        Assert.Contains("position: fixed;", bar, StringComparison.Ordinal);
        Assert.Contains("bottom: 0;", bar, StringComparison.Ordinal);
        Assert.Contains("height: var(--status-bar-height);", bar, StringComparison.Ordinal);
        Assert.Contains(
            "height: calc(100% - var(--status-bar-height));",
            Block(css, "\n.layout {"),
            StringComparison.Ordinal);
        Assert.Contains("--status-bar-height:", css, StringComparison.Ordinal);
    }

    // AC-3: no component keeps a result line of its own; only StatusBarView draws p.main-status.
    [Fact]
    public void Razor_MovedLinesAreGone_OnlyTheBarDrawsMainStatus()
    {
        string folder = Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder);
        string[] moved =
        [
            "triage-status", "saved-search-result", "pinned-result", "sensitive-result", "note-entry-result",
            "board-result", "topic-result", "create-note-result", "add-result", "meeting-todo-result", "decision-copied",
            "similar-result", "review-result", "digest-result", "search-save-result", "times-result", "theme-result",
            "settings-result", "outlook-result", "fetch-titles-result", "screenshot-result", "repository-result",
            "pro-result", "class=\"result", "class=\"status ", "class=\"status\"",
        ];
        foreach (string file in Directory.GetFiles(folder, "*.razor"))
        {
            string razor = File.ReadAllText(file);
            foreach (string name in moved)
            {
                Assert.False(razor.Contains(name, StringComparison.Ordinal), Path.GetFileName(file) + ": " + name);
            }

            Assert.Equal(
                Path.GetFileName(file) == "StatusBarView.razor",
                razor.Contains("main-status", StringComparison.Ordinal));
        }
    }

    // AC-6: the lines the spec keeps stay in their panels.
    [Theory]
    [InlineData("NotesPanel.razor", "import-result")]
    [InlineData("SettingsPanel.razor", "update-mode")]
    [InlineData("SettingsPanel.razor", "update-last")]
    [InlineData("SettingsPanel.razor", "pro-state")]
    [InlineData("Main.razor", "decision-clash")]
    [InlineData("Main.razor", "<CrashHintBar")]
    [InlineData("Main.razor", "Loading…")]
    [InlineData("BoardPanel.razor", "Loading…")]
    public void Razor_KeptLinesStayInTheirPanels(string file, string text)
    {
        string razor = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, file));

        Assert.Contains(text, razor, StringComparison.Ordinal);
    }

    // AC-6: Loading… renders in its place, not in the bar.
    [Fact]
    public void Main_WhileLoading_LoadingLineStaysOutOfTheBar()
    {
        data.CreateState();

        IRenderedComponent<Main> cut = data.Bunit.Render<Main>();

        Assert.Equal("Loading…", cut.Find("p.loading").TextContent);
        Assert.Null(cut.Find("p.loading").Closest("footer"));
        Assert.Equal(string.Empty, data.StatusBar.Text);
    }

    private static string Block(string css, string start)
    {
        int at = css.IndexOf(start, StringComparison.Ordinal);
        Assert.True(at >= 0, start);
        return css[at..css.IndexOf('}', at)];
    }
}
