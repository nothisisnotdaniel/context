using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 180 AC-8, AC-9, 343 AC-10, 349 AC-5, AC-9: the panel of /decisions [tag]: every !d decision, newest first, with its
// date, label, note, people and headline, Copy reference, the notes holding a copy, the full text on demand.
public sealed class DecisionsPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Decisions on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public DecisionsPanelTests() => data.WriteDecisionFolder();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    // AC-8, 343 AC-10: heading, Close, per row a grey date, the number (or grey unnumbered), a note button with the
    // title (full name in title), grey people and the headline cut by CSS with it in title; nothing is written but
    // context.db.
    [Fact]
    public async Task Render_Rows_HeadingDateNumberNotePeopleText()
    {
        data.WriteNote("meetings", "# meetings\n");
        data.WriteFile(Path.Combine("meetings", "2026-11-10 Weekly sync.md"), "# x\n!d ab12-12 ship it with @[Anna] and @[Bo]\nsecond line\n");
        data.WriteNote("2026-11-12", "- !d paint the wall\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
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
        Assert.Equal("Decision 12", row.QuerySelector(".decision-number button")!.TextContent);
        AngleSharp.Dom.IElement unnumbered = cut.FindAll(".decision-rows li")[0].QuerySelector(".decision-number")!;
        Assert.Equal("unnumbered", unnumbered.TextContent);
        Assert.True(unnumbered.ClassList.Contains("grey"));
        Assert.Null(unnumbered.QuerySelector("button"));
        AngleSharp.Dom.IElement note = row.QuerySelector("button.note-button")!;
        Assert.Equal("2026-11-10 Weekly sync", note.TextContent);
        Assert.Equal("meetings/2026-11-10 Weekly sync", note.GetAttribute("title"));
        AngleSharp.Dom.IElement people = row.QuerySelector(".people")!;
        Assert.Equal("Anna, Bo", people.TextContent);
        Assert.True(people.ClassList.Contains("grey"));
        AngleSharp.Dom.IElement text = row.QuerySelector(".text")!;
        Assert.Equal("ship it with @[Anna] and @[Bo]", text.TextContent);
        Assert.Equal("ship it with @[Anna] and @[Bo]", text.GetAttribute("title"));
        Assert.True(text.ClassList.Contains("cut"));
        Assert.Null(cut.FindAll(".decision-rows li")[0].QuerySelector(".people"));
        Assert.Empty(cut.FindAll("p.decision-full"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await StaleClick.FireAsync(cut, () => cut.FindAll(".decision-rows li")[1].QuerySelector("button.note-button")!, e => e.ClickAsync(new MouseEventArgs()));
        await StaleClick.ClickAsync(cut, "button.close");

        Assert.Equal(["meetings/2026-11-10 Weekly sync"], opened);
        Assert.Equal(1, closed);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // 343 AC-10: Show full text unfolds the whole paragraph (each line one clean line) below the row and folds it again.
    [Fact]
    public async Task Expand_ShowsAndHidesFullText()
    {
        data.WriteNote("2026-11-10 Plan", "!d ab12-3 one\ntwo\u0007 words\n");
        await data.RefreshAsync();
        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);
        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);
        Assert.Equal("false", cut.Find("button.decision-expand").GetAttribute("aria-expanded"));
        Assert.Equal("Show full text", cut.Find("button.decision-expand").TextContent);

        await StaleClick.ClickAsync(cut, "button.decision-expand");

        Assert.Equal("true", cut.Find("button.decision-expand").GetAttribute("aria-expanded"));
        Assert.Equal("Hide full text", cut.Find("button.decision-expand").TextContent);
        Assert.Equal("one\ntwo\uFFFD words", cut.Find("p.decision-full").TextContent);

        await StaleClick.ClickAsync(cut, "button.decision-expand");

        Assert.Empty(cut.FindAll("p.decision-full"));
        Assert.Equal("false", cut.Find("button.decision-expand").GetAttribute("aria-expanded"));
    }

    // 343 AC-10: the number and the headline open the note at the decision; an unnumbered decision's headline opens
    // the note at the top.
    [Fact]
    public async Task Jump_NumberAndHeadline_OpenDecision_UnnumberedOpensNote()
    {
        data.WriteNote("2026-11-10 Plan", "!d AB12-3 go\n");
        data.WriteNote("2026-11-09 Old", "!d stay\n");
        await data.RefreshAsync();
        List<string> opened = [];
        List<(string Name, string Decision)> jumps = [];
        IRenderedComponent<DecisionsPanel> cut = data.Bunit.Render<DecisionsPanel>(p => p
            .Add(c => c.Tag, string.Empty)
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnOpenDecision, ((string Name, string Decision) d) => jumps.Add(d)));
        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan", "2026-11-09 Old"], NoteNames(cut)), Wait);

        await StaleClick.FireAsync(cut, () => cut.FindAll(".decision-rows li")[0].QuerySelector(".decision-number button")!, e => e.ClickAsync(new MouseEventArgs()));
        await StaleClick.FireAsync(cut, () => cut.FindAll(".decision-rows li")[0].QuerySelector(".text button")!, e => e.ClickAsync(new MouseEventArgs()));
        await StaleClick.FireAsync(cut, () => cut.FindAll(".decision-rows li")[1].QuerySelector(".text button")!, e => e.ClickAsync(new MouseEventArgs()));

        Assert.Equal([("2026-11-10 Plan", "ab12-3"), ("2026-11-10 Plan", "ab12-3")], jumps);
        Assert.Equal(["2026-11-09 Old"], opened);
    }

    // 349 AC-9: own decisions show as Decision <n>, other prefixes as Decision <p>-<n>; Copy reference copies #!d<ref>
    // through copyText and shows the grey Copied line; nothing is written.
    [Fact]
    public async Task Labels_AndCopyReference()
    {
        data.WriteNote("2026-11-10 Plan", $"{DesktopFixture.DecisionFolderLine}\n!d ab12-1234 we ship\n");
        data.WriteNote("2026-11-09 Theirs", "!d zz99-4 they ship\n");
        await data.RefreshAsync();
        data.Bunit.JSInterop.Setup<string>("ContextEditor.copyToClipboard", _ => true).SetResult("Copied");
        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);
        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan", "2026-11-09 Theirs"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("p[role=status]"));
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        Assert.Equal(
            ["Decision 1234", "Decision zz99-4"],
            cut.FindAll(".decision-number button").Select(b => b.TextContent));
        AngleSharp.Dom.IElement copy = cut.FindAll("button.decision-copy")[1];
        Assert.Equal("Copy", copy.TextContent);
        Assert.Equal("Copy reference to Decision zz99-4", copy.GetAttribute("aria-label"));

        await StaleClick.FireAsync(cut, () => cut.FindAll("button.decision-copy")[0], e => e.ClickAsync(new MouseEventArgs()));
        Bar.WaitForAssertion(() => Assert.Equal("Copied #!d1234.", data.StatusBar.Text), Wait);
        Assert.Equal("grey", data.StatusBar.Css);

        await StaleClick.FireAsync(cut, () => cut.FindAll("button.decision-copy")[1], e => e.ClickAsync(new MouseEventArgs()));
        Bar.WaitForAssertion(() => Assert.Equal("Copied #!dzz99-4.", data.StatusBar.Text), Wait);

        Assert.Equal(
            ["#!d1234", "#!dzz99-4"],
            data.JsCalls("ContextEditor.copyToClipboard").Select(i => (string)i.Arguments[0]!));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 349 AC-9: a clipboard that refuses shows the red line.
    [Fact]
    public async Task CopyReference_Refused_RedLine()
    {
        data.WriteNote("2026-11-10 Plan", $"{DesktopFixture.DecisionFolderLine}\n!d ab12-7 go\n");
        await data.RefreshAsync();
        data.Bunit.JSInterop.Setup<string>("ContextEditor.copyToClipboard", _ => true).SetResult("Can't copy");
        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);
        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);

        await StaleClick.ClickAsync(cut, "button.decision-copy");

        Bar.WaitForAssertion(() => Assert.Equal("Can't copy #!d7.", data.StatusBar.Text), Wait);
        Assert.Equal("red", data.StatusBar.Css);
    }

    // 349 AC-5: a copy (same token, same text) in other notes is one row for the owner, with Also in naming them.
    [Fact]
    public async Task Copies_OneRow_AlsoIn()
    {
        data.WriteNote("2026-11-10 Plan", $"{DesktopFixture.DecisionFolderLine}\n!d ab12-5 we ship\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "2026-11-10 Plan.md"), new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc));
        data.WriteNote("Copy B", $"{DesktopFixture.DecisionFolderLine}\n!d ab12-5 we ship\n");
        data.WriteNote("Copy A", $"{DesktopFixture.DecisionFolderLine}\n- !d ab12-5 we ship\n");
        await data.RefreshAsync();

        IRenderedComponent<DecisionsPanel> cut = Render(string.Empty);

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);
        AngleSharp.Dom.IElement places = cut.Find(".decision-places");
        Assert.Equal("Also in: Copy A, Copy B", places.TextContent);
        Assert.True(places.ClassList.Contains("grey"));
    }

    // AC-8: a tag filters and is in the heading.
    [Theory]
    [InlineData("work")]
    [InlineData("#work")]
    [InlineData("[work]")]
    public async Task Render_Tag_FiltersAndHeading(string tag)
    {
        data.WriteNote("2026-11-10 Plan", "#work\n\n!d go\n");
        data.WriteNote("2026-11-11 Home", "#home\n\n!d paint\n");
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
            data.WriteNote($"2026-11-10 n{i:D3}", "!d x\n");
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
            data.WriteNote($"2026-11-10 n{i:D3}", "!d x\n");
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
        data.WriteNote("Plan", "!d x\n");
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
        data.WriteNote("Plan", "!d x\n");
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

        data.WriteNote("2026-11-10 Plan", "!d go\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "2026-11-10 Plan.md"), new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["2026-11-10 Plan"], NoteNames(cut)), Wait);
    }

    // AC-9: Loading… before the first result; a result for an older tag is dropped; the load runs under indexGate.
    [Fact]
    public async Task TagChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("2026-11-10 Plan", "#alpha\n\n!d a\n");
        data.WriteNote("2026-11-11 Review", "#omega\n\n!d o\n");
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
        data.WriteNote("2026-11-10 <b>x<b>", "!d say <img src=x>\u0007 with @[<i>e</i>]\n");
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
        cut.FindAll(".decision-rows button.note-button").Select(b => b.GetAttribute("title")!).ToArray();
}
