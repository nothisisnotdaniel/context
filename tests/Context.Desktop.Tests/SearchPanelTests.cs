using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 170 AC-10: the panel of /search <text>: the notes whose text contains it, with the first matching line.
public sealed class SearchPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Search on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-10: heading, Close, one note button (full name in title) and grey line (full text in title) per hit; nothing
    // is written but context.db.
    [Fact]
    public async Task Render_Hits_HeadingButtonsLinesClose()
    {
        data.WriteNote("projects", "# projects\n");
        data.WriteFile(Path.Combine("projects", "Plan.md"), "# Plan\nthe Budget 2027 line\nbudget again\n");
        data.WriteNote("bob", "budget\n");
        data.WriteNote("Carl", "nothing\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, " budget ")
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));

        cut.WaitForAssertion(() => Assert.Equal(["bob", "projects/Plan"], NoteNames(cut)), Wait);
        Assert.Equal("Search: budget", cut.Find("h2").TextContent);
        Assert.Equal("Notes containing this text", cut.Find("section.search-panel").GetAttribute("aria-label"));
        Assert.Equal("projects/Plan", cut.FindAll(".search-hits button")[1].GetAttribute("title"));
        AngleSharp.Dom.IElement line = cut.FindAll(".search-hits .line")[1];
        Assert.Equal("the Budget 2027 line", line.TextContent);
        Assert.Equal("the Budget 2027 line", line.GetAttribute("title"));
        Assert.True(line.ClassList.Contains("grey"));
        Assert.True(line.ClassList.Contains("cut"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".search-hits button")[1].ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["projects/Plan"], opened);
        Assert.Equal(1, closed);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-10: at most 100 rows, then the grey "… n more" line.
    [Fact]
    public async Task Render_ManyHits_CappedWithMoreLine()
    {
        for (int i = 0; i < SearchPanel.MaxRows + 3; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Equal("n099", NoteNames(cut)[^1]);
        Assert.Equal("… 3 more", cut.Find("p.more").TextContent);
        Assert.True(cut.Find("p.more").ClassList.Contains("grey"));
    }

    // AC-10: exactly 100 hits has no more line.
    [Fact]
    public async Task Render_HundredHits_NoMoreLine()
    {
        for (int i = 0; i < SearchPanel.MaxRows; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Empty(cut.FindAll("p.more"));
    }

    // AC-10: no hit shows the grey line.
    [Fact]
    public async Task Render_NoMatch_GreyLine()
    {
        data.WriteNote("Plan", "something\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("nothing here");

        cut.WaitForAssertion(() => Assert.Equal("No notes contain nothing here.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-10: a context.db that is not a database shows the red line and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine()
    {
        data.WriteNote("Plan", "budget\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the search index.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10: blank, too short and too long texts are red lines, and nothing is read or written.
    [Theory]
    [InlineData("", "Type /search <text>.")]
    [InlineData("   ", "Type /search <text>.")]
    [InlineData(" ab ", "Type at least 3 characters to search.")]
    [InlineData(null, "Search text can be at most 200 characters.")]
    public async Task Render_BadText_RedLine_NothingRead(string? text, string expected)
    {
        data.WriteNote("Plan", "budget\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<SearchPanel> cut = Render(text ?? new string('x', 201));

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10: after the state changed the panel shows the new hit without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewHits()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("budget");
        cut.WaitForAssertion(() => Assert.Equal("No notes contain budget.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("Plan", "the budget\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
    }

    // AC-10: Loading… before the first result; a result for an older query is dropped; the load runs under indexGate.
    [Fact]
    public async Task QueryChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("Plan", "alpha\n");
        data.WriteNote("Review", "omega\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<SearchPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("alpha");
            cut.Render(p => p.Add(c => c.Text, "omega"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
        typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
    }

    // AC-10: names, lines and the query from disk or the user are text, never markup; controls become U+FFFD.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("<b>x<b>", "say <img src=x>\u0007 here\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("<img src=x>");

        cut.WaitForAssertion(() => Assert.Equal(["<b>x<b>"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img"));
        Assert.Equal("Search: <img src=x>", cut.Find("h2").TextContent);
        Assert.Equal("say <img src=x>� here", cut.Find(".search-hits .line").TextContent);
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    // AC-10: the panel only reads: it calls no Actions.
    [Fact]
    public void Source_CallsNoActions()
    {
        string root = RepoFiles.Root();
        string source = File.ReadAllText(Path.Combine(root, "src", "Context.Desktop", "SearchPanel.razor"));

        Assert.DoesNotContain("Actions.", source, StringComparison.Ordinal);
        Assert.Contains("State.Search(", source, StringComparison.Ordinal);
    }

    private IRenderedComponent<SearchPanel> Render(string text) =>
        data.Bunit.Render<SearchPanel>(p => p.Add(c => c.Text, text));

    private static string[] NoteNames(IRenderedComponent<SearchPanel> cut) =>
        cut.FindAll(".search-hits button").Select(b => b.TextContent).ToArray();
}
