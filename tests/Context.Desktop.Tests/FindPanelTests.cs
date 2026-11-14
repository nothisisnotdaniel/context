using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 157 AC-10: the panel of /find <key> <value>: the notes with that attribute line.
public sealed class FindPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Find on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-10: the heading, one typed button per match through OnOpen, Close; nothing is written but context.db.
    [Fact]
    public async Task Render_Matches_HeadingButtonsClose()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\naffiliation:: [[ACME]]\n");
        data.WriteNote("bob", "Affiliation:: acme\n");
        data.WriteNote("Carl", "affiliation:: Other\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<FindPanel> cut = data.Bunit.Render<FindPanel>(p => p
            .Add(c => c.Key, "affiliation")
            .Add(c => c.Value, "ACME")
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));

        cut.WaitForAssertion(() => Assert.Equal(["bob", "people/Anna"], NoteNames(cut)), Wait);
        Assert.Equal("affiliation:: ACME", cut.Find("h2").TextContent);
        Assert.Equal("Notes with this attribute", cut.Find("section.find-panel").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".find-notes button")[1].ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["people/Anna"], opened);
        Assert.Equal(1, closed);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-10: no match shows the grey line.
    [Fact]
    public async Task Render_NoMatch_GreyLine()
    {
        data.WriteNote("Plan", "role:: Chef\nrole::\n");
        await data.RefreshAsync();

        IRenderedComponent<FindPanel> cut = Render("role", "Vater");

        cut.WaitForAssertion(() => Assert.Equal("No notes have role:: Vater.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-10: a context.db that is not a database shows the red line and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine()
    {
        data.WriteNote("Plan", "role:: Chef\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<FindPanel> cut = Render("role", "Chef");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the attributes.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10: after the state changed the panel shows the new match without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewMatches()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<FindPanel> cut = Render("role", "Chef");
        cut.WaitForAssertion(() => Assert.Equal("No notes have role:: Chef.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("Plan", "role:: chef\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
    }

    // AC-10: Loading… before the first result; a result for an older query is dropped; the load runs under indexGate.
    [Fact]
    public async Task QueryChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("Plan", "role:: Chef\n");
        data.WriteNote("Review", "role:: Vater\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<FindPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("role", "Chef");
            cut.Render(p => p.Add(c => c.Value, "Vater"));
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

    // AC-10: values and names from disk are text, never markup; controls become U+FFFD.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("<b>x<b>", "k:: <img src=x>\n");
        await data.RefreshAsync();

        IRenderedComponent<FindPanel> cut = Render("k", "<img src=x>\u0007");

        cut.WaitForAssertion(() => Assert.Equal(
            "No notes have k:: <img src=x>�.", cut.Find("p[role=status]").TextContent), Wait);
        cut.Render(p => p.Add(c => c.Value, "<img src=x>"));
        cut.WaitForAssertion(() => Assert.Equal(["<b>x<b>"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img"));
        Assert.Equal("<b>x<b>", cut.Find(".find-notes button").GetAttribute("title"));
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<FindPanel> Render(string key, string value) =>
        data.Bunit.Render<FindPanel>(p => p.Add(c => c.Key, key).Add(c => c.Value, value));

    private static string[] NoteNames(IRenderedComponent<FindPanel> cut) =>
        cut.FindAll(".find-notes button").Select(b => b.TextContent).ToArray();
}
