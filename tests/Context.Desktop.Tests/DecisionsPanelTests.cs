using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 180 AC-8, AC-9: the panel of /decisions [tag]: every Decision: line, newest first, with its date, note and people.
public sealed class DecisionsPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Decisions on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-8: heading, Close, per row a grey date, a note button (full name in title), grey people and the text cut by
    // CSS with the full text in title; nothing is written but context.db.
    [Fact]
    public async Task Render_Rows_HeadingDateNotePeopleText()
    {
        data.WriteNote("meetings", "# meetings\n");
        data.WriteFile(Path.Combine("meetings", "2026-11-10 Weekly sync.md"), "# x\nDecision: ship it with @[Anna] and @[Bo]\n");
        data.WriteNote("2026-11-12", "- decision: paint the wall\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<DecisionsPanel> cut = data.Bunit.Render<DecisionsPanel>(p => p
            .Add(c => c.Tag, string.Empty)
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-12", "meetings/2026-11-10 Weekly sync"], NoteNames(cut)), Wait);
        Assert.Equal("Decisions", cut.Find("h2").TextContent);
        Assert.Equal("Decisions", cut.Find("section.decisions-panel").GetAttribute("aria-label"));
        AngleSharp.Dom.IElement row = cut.FindAll(".decision-rows li")[1];
        AngleSharp.Dom.IElement date = row.QuerySelector(".date")!;
        Assert.Equal("2026-11-10", date.TextContent);
        Assert.True(date.ClassList.Contains("grey"));
        Assert.Equal("meetings/2026-11-10 Weekly sync", row.QuerySelector("button.note-button")!.GetAttribute("title"));
        AngleSharp.Dom.IElement people = row.QuerySelector(".people")!;
        Assert.Equal("Anna, Bo", people.TextContent);
        Assert.True(people.ClassList.Contains("grey"));
        AngleSharp.Dom.IElement text = row.QuerySelector(".text")!;
        Assert.Equal("ship it with @[Anna] and @[Bo]", text.TextContent);
        Assert.Equal("ship it with @[Anna] and @[Bo]", text.GetAttribute("title"));
        Assert.True(text.ClassList.Contains("cut"));
        Assert.Null(cut.FindAll(".decision-rows li")[0].QuerySelector(".people"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await row.QuerySelector("button.note-button")!.ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["meetings/2026-11-10 Weekly sync"], opened);
        Assert.Equal(1, closed);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-8: a tag filters and is in the heading.
    [Theory]
    [InlineData("work")]
    [InlineData("#work")]
    [InlineData("[work]")]
    public async Task Render_Tag_FiltersAndHeading(string tag)
    {
        data.WriteNote("2026-11-10 Plan", "#work\nDecision: go\n");
        data.WriteNote("2026-11-11 Home", "#home\nDecision: paint\n");
        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(tag);

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);
        Assert.Equal("Decisions: #work", cut.Find("h2").TextContent);
    }

    // AC-8: at most 100 rows, then the grey "… n more" line.
    [Fact]
    public async Task Render_ManyDecisions_CappedWithMoreLine()
    {
        for (int i = 0; i < DecisionsPanel.MaxRows + 3; i++)
        {
            data.WriteNote($"2026-11-10 n{i:D3}", "Decision: x\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Equal("2026-11-10 n099", NoteNames(cut)[^1]);
        Assert.Equal("… 3 more", cut.Find("p.more").TextContent);
        Assert.True(cut.Find("p.more").ClassList.Contains("grey"));
    }

    // AC-8: exactly 100 decisions has no more line.
    [Fact]
    public async Task Render_HundredDecisions_NoMoreLine()
    {
        for (int i = 0; i < DecisionsPanel.MaxRows; i++)
        {
            data.WriteNote($"2026-11-10 n{i:D3}", "Decision: x\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Empty(cut.FindAll("p.more"));
    }

    // AC-8: no decision shows the grey line, with or without a tag.
    [Theory]
    [InlineData("", "No decisions yet.")]
    [InlineData("#[project alpha]", "No decisions in notes tagged #[project alpha].")]
    public async Task Render_None_GreyLine(string tag, string expected)
    {
        data.WriteNote("Plan", "nothing decided\n");
        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(tag);

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-8: a context.db that is not a database shows the red line and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine()
    {
        data.WriteNote("Plan", "Decision: x\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);

        cut.WaitForAssertion(() => Assert.Equal("Can't read the decisions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-8: an invalid tag is the red usage line, and nothing is read or written.
    [Theory]
    [InlineData("#1x")]
    [InlineData("#")]
    public async Task Render_InvalidTag_RedLine_NothingRead(string tag)
    {
        data.WriteNote("Plan", "Decision: x\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<DecisionsPanel> cut = Render(tag);

        cut.WaitForAssertion(() => Assert.Equal("Type /decisions [tag].", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-9: after the state changed the panel shows the new decision without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewDecision()
    {
        data.WriteNote("2026-11-10 Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);
        cut.WaitForAssertion(() => Assert.Equal("No decisions yet.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("2026-11-10 Plan", "Decision: go\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "2026-11-10 Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);
    }

    // AC-9: Loading… before the first result; a result for an older tag is dropped; the load runs under indexGate.
    [Fact]
    public async Task TagChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("2026-11-10 Plan", "#alpha\nDecision: a\n");
        data.WriteNote("2026-11-11 Review", "#omega\nDecision: o\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<DecisionsPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("alpha");
            cut.Render(p => p.Add(c => c.Tag, "omega"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-11 Review"], NoteNames(cut)), Wait);
        typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
        cut.WaitForAssertion(() => Assert.Equal(["2026-11-11 Review"], NoteNames(cut)), Wait);
    }

    // AC-8: names, people and texts from disk are text, never markup; controls become U+FFFD.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("2026-11-10 <b>x<b>", "Decision: say <img src=x>\u0007 with @[<i>e</i>]\n");
        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 <b>x<b>"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img, i"));
        Assert.Equal("say <img src=x>� with @[<i>e</i>]", cut.Find(".decision-rows .text").TextContent);
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    // AC-9: the panel only reads: it calls no Actions.
    [Fact]
    public void Source_CallsNoActions()
    {
        string root = RepoFiles.Root();
        string source = File.ReadAllText(Path.Combine(root, "src", "Context.Desktop", "DecisionsPanel.razor"));

        Assert.DoesNotContain("Actions.", source, StringComparison.Ordinal);
        Assert.Contains("State.Decisions(", source, StringComparison.Ordinal);
    }

    private IRenderedComponent<DecisionsPanel> Render(string tag) =>
        data.Bunit.Render<DecisionsPanel>(p => p.Add(c => c.Tag, tag));

    private static string[] NoteNames(IRenderedComponent<DecisionsPanel> cut) =>
        cut.FindAll(".decision-rows button.note-button").Select(b => b.TextContent).ToArray();
}
