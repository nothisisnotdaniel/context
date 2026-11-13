using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 137: the tags view, every tag with its note count.
public sealed class TagsPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: heading, Close, one typed button per tag in TagIndex.Tags() order, formatted, with a grey count.
    [Fact]
    public async Task Render_Tags_ButtonsInIndexOrderWithCounts()
    {
        data.WriteNote("Plan", "#work #[Big Project]\n");
        data.WriteNote("Alpha", "#Work #zebra\n");
        data.WriteNote("Beta", "#work\n");
        await data.RefreshAsync();
        IReadOnlyList<TagCount> expected = (await data.State!.Tags()).Tags;

        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();

        cut.WaitForAssertion(() => Assert.Equal(
            expected.Select(t => TagParser.Format(t.Tag)).ToArray(),
            cut.FindAll(".tag-list button").Select(b => b.TextContent).ToArray()));
        Assert.Equal(3, expected.Count);
        Assert.Contains("#[big project]", cut.FindAll(".tag-list button").Select(b => b.TextContent));
        Assert.Equal("Tags", cut.Find(".panel-bar h2").TextContent);
        Assert.Equal("Close", cut.Find(".panel-bar button.close").TextContent);
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        IElement work = cut.FindAll(".tag-row").Single(r => r.QuerySelector("button")!.TextContent == "#work");
        Assert.Equal("3", work.QuerySelector(".count")!.TextContent);
        Assert.True(work.QuerySelector(".count")!.ClassList.Contains("grey"));
        Assert.Equal("#work", work.QuerySelector("button")!.GetAttribute("title"));
        Assert.True(work.QuerySelector("button")!.ClassList.Contains("cut"));
        Assert.All(cut.FindAll(".tag-list button"), b => Assert.Equal("false", b.GetAttribute("aria-pressed")));
    }

    // AC-2: a click reports the tag; the selected tag's button is the only pressed one. Close reports too.
    [Fact]
    public async Task Click_ReportsTheTag_SelectedIsPressed()
    {
        data.WriteNote("Plan", "#work #[big project] #play\n");
        await data.RefreshAsync();
        List<string> clicked = [];
        int closed = 0;

        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>(p => p
            .Add(t => t.Selected, "big project")
            .Add(t => t.OnTag, (string tag) => clicked.Add(tag))
            .Add(t => t.OnClose, () => closed++));
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".tag-list button").Count));

        Assert.Equal(
            ["#[big project]"],
            cut.FindAll(".tag-list button[aria-pressed=true]").Select(b => b.TextContent).ToArray());
        Assert.Equal(2, cut.FindAll(".tag-list button[aria-pressed=false]").Count);
        await cut.FindAll(".tag-list button").Single(b => b.TextContent == "#[big project]").ClickAsync(new MouseEventArgs());
        await cut.FindAll(".tag-list button").Single(b => b.TextContent == "#work").ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["#[big project]", "#work"], clicked);
        Assert.Equal(1, closed);
    }

    // AC-3: grey Loading… until the first result arrives.
    [Fact]
    public async Task Render_BeforeTheFirstResult_Loading()
    {
        data.WriteNote("Plan", "#work\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<TagsPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = data.Bunit.Render<TagsPanel>();
            IElement line = cut.Find("p[role=status]");
            Assert.Equal("Loading…", line.TextContent);
            Assert.True(line.ClassList.Contains("grey"));
            Assert.Empty(cut.FindAll(".tag-list"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["#work"], cut.FindAll(".tag-list button").Select(b => b.TextContent).ToArray()));
        Assert.Empty(cut.FindAll("p[role=status]"));
    }

    // AC-3: no tag (also with no data folder at all) shows the grey hint.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Render_NoTags_GreyHint(bool withNote)
    {
        if (withNote)
        {
            data.WriteNote("Plan", "no tag here\n");
        }

        await data.RefreshAsync();
        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();

        cut.WaitForAssertion(() => Assert.Equal(
            "No tags yet. Add #tag to a note to link it.", cut.Find("p[role=status]").TextContent));
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".tag-list"));
    }

    // AC-3: a context.db that is not a database shows the fixed red line and is left alone.
    [Fact]
    public async Task Render_CorruptIndex_RedLine_FileUnchanged()
    {
        data.WriteNote("Plan", "#work\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();

        cut.WaitForAssertion(() => Assert.Equal("Can't read the tags.", cut.Find("p[role=status]").TextContent));
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".tag-list"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(NotADatabase, File.ReadAllText(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // AC-8: after the state changed the list shows the new tags and counts; the old rows stay while it reloads.
    [Fact]
    public async Task StateChanged_ShowsNewTagsAndCounts()
    {
        data.WriteNote("Plan", "#work\n");
        await data.RefreshAsync();
        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();
        cut.WaitForAssertion(() => Assert.Equal(["#work 1"], Rows(cut)));

        data.WriteNote("Other", "#work #play\n");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["#play 1", "#work 2"], Rows(cut).Order(StringComparer.Ordinal).ToArray()));
        Assert.Empty(cut.FindAll("p[role=status]"));
    }

    // AC-9: showing the tags changes no user data, and the component never names the actions.
    [Fact]
    public async Task Render_ChangesNoUserData()
    {
        data.WriteNote("Plan", "#work\n");
        data.AddTodo("Due #work", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-list button")));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-list button")));

        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-9: neither new panel can write: no call into the actions.
    [Theory]
    [InlineData("TagsPanel.razor")]
    [InlineData("LinksPanel.razor")]
    public void Source_NeverCallsTheActions(string file)
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, file));

        Assert.DoesNotContain("Actions.", source, StringComparison.Ordinal);
        Assert.Contains("@inherits StateComponent", source, StringComparison.Ordinal);
    }

    // AC-10: a long tag is cut by CSS with the full text in the title; the new selectors are styled.
    [Fact]
    public async Task Render_LongTag_FullTextInTitle()
    {
        string tag = new string('a', 50);
        data.WriteNote("Plan", "#" + tag + "\n");
        await data.RefreshAsync();

        IRenderedComponent<TagsPanel> cut = data.Bunit.Render<TagsPanel>();

        cut.WaitForAssertion(() => Assert.Equal("#" + tag, cut.Find(".tag-list button").GetAttribute("title")));
        Assert.True(cut.Find(".tag-list button").ClassList.Contains("cut"));
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        Assert.Contains(".tag-list {", css, StringComparison.Ordinal);
        Assert.Contains("max-height:", css[css.IndexOf(".tag-list {", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains(".link-row {", css, StringComparison.Ordinal);
    }

    private static string[] Rows(IRenderedComponent<TagsPanel> cut) =>
        cut.FindAll(".tag-row")
            .Select(r => r.QuerySelector("button")!.TextContent + " " + r.QuerySelector(".count")!.TextContent)
            .ToArray();
}
