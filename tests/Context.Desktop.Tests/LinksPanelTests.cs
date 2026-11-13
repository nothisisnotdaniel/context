using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 137: the linked notes under the open note.
public sealed class LinksPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4: one row per NoteLink in the view's order: name, link text (grey) and the shared tags, formatted.
    [Fact]
    public async Task Render_Links_RowsInViewOrder()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        LinksView expected = await data.State!.Links("Plan");
        Assert.Equal(LinksViewKind.Links, expected.Kind);

        IRenderedComponent<LinksPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal(
            expected.Links.Select(l => l.Note).ToArray(),
            cut.FindAll(".link-row .name").Select(n => n.TextContent).ToArray()));
        Assert.Equal("Linked notes", cut.Find("h2").TextContent);
        Assert.Equal(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["Back"] = "linked from|",
                ["Ghost"] = "links to (missing)|",
                ["Mutual"] = "both ways|#[big project]",
                ["Tagged"] = "|#[big project] #work",
                ["Target"] = "links to|#work",
            },
            new SortedDictionary<string, string>(
                cut.FindAll(".link-row").ToDictionary(
                    r => r.QuerySelector(".name")!.TextContent,
                    r => r.QuerySelector(".link-kind")!.TextContent + "|"
                        + string.Join(" ", r.QuerySelectorAll(".link-tags button").Select(b => b.TextContent))),
                StringComparer.Ordinal));
        Assert.All(cut.FindAll(".link-kind"), k => Assert.True(k.ClassList.Contains("grey")));
        Assert.Empty(cut.FindAll("p[role=status]"));
    }

    // AC-5: an existing note's name is a typed button that reports the note; a missing target is grey text; a
    // shared tag is a typed button that reports the tag.
    [Fact]
    public async Task Row_NameAndTagButtons_MissingIsText()
    {
        WriteLinkedNotes();
        await data.RefreshAsync();
        List<string> opened = [];
        List<string> tags = [];
        IRenderedComponent<LinksPanel> cut = data.Bunit.Render<LinksPanel>(p => p
            .Add(l => l.Name, "Plan")
            .Add(l => l.OnOpen, (string name) => opened.Add(name))
            .Add(l => l.OnTag, (string tag) => tags.Add(tag)));
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".link-row").Count));

        IElement ghost = Row(cut, "Ghost").QuerySelector(".name")!;
        Assert.Equal("SPAN", ghost.TagName);
        Assert.True(ghost.ClassList.Contains("grey"));
        Assert.Empty(Row(cut, "Ghost").QuerySelectorAll("button"));
        Assert.Equal(
            ["Back", "Mutual", "Tagged", "Target"],
            cut.FindAll(".link-row button.name").Select(b => b.TextContent).Order(StringComparer.Ordinal).ToArray());
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await Row(cut, "Target").QuerySelector("button.name")!.ClickAsync(new MouseEventArgs());
        await Row(cut, "Mutual").QuerySelector(".link-tags button")!.ClickAsync(new MouseEventArgs());

        Assert.Equal(["Target"], opened);
        Assert.Equal(["#[big project]"], tags);
    }

    // AC-6: a note without links, and a name that is no indexed note, show the grey line.
    [Theory]
    [InlineData("Alone")]
    [InlineData("Nowhere")]
    public async Task Render_NoLinksOrNoSuchNote_GreyLine(string name)
    {
        data.WriteNote("Alone", "nothing\n");
        data.WriteNote("Other", "#work\n");
        await data.RefreshAsync();

        IRenderedComponent<LinksPanel> cut = Render(name);

        cut.WaitForAssertion(() => Assert.Equal(name + " has no linked notes.", cut.Find("p[role=status]").TextContent));
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".link-rows"));
    }

    // AC-6: a context.db that is not a database shows the fixed red line and is left alone.
    [Fact]
    public async Task Render_CorruptIndex_RedLine_FileUnchanged()
    {
        data.WriteNote("Plan", "#work [[Other]]\n");
        data.WriteNote("Other", "#work\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<LinksPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the links.", cut.Find("p[role=status]").TextContent));
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: Loading… only before the first result for a note; a reload keeps the rows.
    [Fact]
    public async Task Loading_OnlyBeforeTheFirstResult()
    {
        data.WriteNote("Plan", "[[Other]]\n");
        data.WriteNote("Other", "x\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<LinksPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("Plan");
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Other"], Names(cut)));
        int renders = cut.RenderCount;
        Monitor.Enter(gate);
        try
        {
            // A second load for the same note waits on the index: the rows stay, without a loading line.
            typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
            cut.WaitForAssertion(() => Assert.True(cut.RenderCount > renders));
            Assert.Equal(["Other"], Names(cut));
            Assert.Empty(cut.FindAll("p[role=status]"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Other"], Names(cut)));
    }

    // AC-7: a sensitive note shows only the grey line; the index is not asked (a broken one gives no red line)
    // and the key store is never asked to agree.
    [Fact]
    public async Task Sensitive_OnlyTheGreyLine_NoLookup_NoAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "#work [[Other]]\n");
        data.WriteNote("Other", "#work [[Secret]]\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();

        IRenderedComponent<LinksPanel> cut = data.Bunit.Render<LinksPanel>(p => p
            .Add(l => l.Name, "Secret")
            .Add(l => l.Sensitive, true));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.True(cut.RenderCount >= 2));

        IElement line = Assert.Single(cut.FindAll("p"));
        Assert.Equal("Sensitive notes have no links.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Empty(cut.FindAll(".link-rows"));
        Assert.Empty(cut.FindAll(".red"));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-8: after the state changed the panel shows the new rows without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewRows()
    {
        data.WriteNote("Plan", "plain\n");
        data.WriteNote("Other", "#work\n");
        await data.RefreshAsync();
        IRenderedComponent<LinksPanel> cut = Render("Plan");
        cut.WaitForAssertion(() => Assert.Equal("Plan has no linked notes.", cut.Find("p[role=status]").TextContent));

        // A save that adds a tag and a link.
        data.WriteNote("Plan", "now #work and [[Other]]\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(["Other"], Names(cut)));
        Assert.Equal("links to", Row(cut, "Other").QuerySelector(".link-kind")!.TextContent);
        Assert.Equal(["#work"], Row(cut, "Other").QuerySelectorAll(".link-tags button").Select(b => b.TextContent).ToArray());

        // Another note gains a link to the open one.
        data.WriteNote("Third", "see [[Plan]]\n");
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(["Other", "Third"], Names(cut).Order(StringComparer.Ordinal).ToArray()));
        Assert.Equal("linked from", Row(cut, "Third").QuerySelector(".link-kind")!.TextContent);
    }

    // AC-8: another name shows that note's links; a result for the note that is no longer open is dropped.
    [Fact]
    public async Task NameChanges_ShowsTheNewNotesLinks_StaleResultDropped()
    {
        data.WriteNote("Plan", "[[One]]\n");
        data.WriteNote("Project", "[[Two]]\n");
        data.WriteNote("One", "x\n");
        data.WriteNote("Two", "x\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<LinksPanel> cut;

        Monitor.Enter(gate);
        try
        {
            // Both loads wait on the index; Plan's result arrives when Project is already the note.
            cut = Render("Plan");
            cut.Render(p => p.Add(l => l.Name, "Project"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Two"], Names(cut)));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(["Two"], Names(cut)));

        cut.Render(p => p.Add(l => l.Name, "Plan"));
        cut.WaitForAssertion(() => Assert.Equal(["One"], Names(cut)));
    }

    // AC-9: showing the links changes no user data.
    [Fact]
    public async Task Render_ChangesNoUserData()
    {
        WriteLinkedNotes();
        data.AddTodo("Due #work", DesktopFixture.Today);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        IRenderedComponent<LinksPanel> cut = Render("Plan");
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".link-row").Count));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".link-row").Count));

        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-10: names from disk are text, never markup; controls become U+FFFD; the full text is in the title.
    [Fact]
    public async Task Render_HostileNames_AsText()
    {
        string hostile = "<b>x<\\b> <img src=x onerror=alert(1)> \u0007";
        string shown = "<b>x<\\b> <img src=x onerror=alert(1)> �";
        bool written = true;
        try
        {
            data.WriteNote(hostile, "#work\n");
        }
        catch (IOException)
        {
            written = false; // Windows refuses such a file name; the missing target below still covers the rule.
        }

        data.WriteNote("Plan", "#work [[<b>x</b>]] [[" + new string('n', 100) + "]]\n");
        await data.RefreshAsync();

        IRenderedComponent<LinksPanel> cut = Render("Plan");

        cut.WaitForAssertion(() => Assert.Contains("<b>x</b>", Names(cut)));
        Assert.Empty(cut.FindAll(".link-rows b"));
        Assert.Empty(cut.FindAll(".link-rows img"));
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
        IElement longName = Row(cut, new string('n', 100)).QuerySelector(".name")!;
        Assert.Equal(new string('n', 100), longName.GetAttribute("title"));
        Assert.True(longName.ClassList.Contains("cut"));
        if (written)
        {
            IElement button = Row(cut, shown).QuerySelector("button.name")!;
            Assert.Equal(shown, button.GetAttribute("title"));
        }
    }

    // AC-10: the open note's own name in the empty line is text as well.
    [Fact]
    public async Task Render_HostileOpenName_AsText()
    {
        await data.RefreshAsync();

        IRenderedComponent<LinksPanel> cut = Render("<b>x</b>\u0001");

        cut.WaitForAssertion(() => Assert.Equal("<b>x</b>� has no linked notes.", cut.Find("p[role=status]").TextContent));
        Assert.Empty(cut.FindAll("b"));
    }

    // Plan links to Target (shared #work), Mutual (both ways, shared #[big project]) and the missing Ghost; Back
    // links to it; Tagged only shares both tags.
    private void WriteLinkedNotes()
    {
        data.WriteNote("Plan", "#work #[big project] [[Target]] [[Mutual]] [[Ghost]]\n");
        data.WriteNote("Target", "#work\n");
        data.WriteNote("Mutual", "#[Big Project] back to [[Plan]]\n");
        data.WriteNote("Back", "see [[plan]]\n");
        data.WriteNote("Tagged", "#work #[big project]\n");
        data.WriteNote("Unrelated", "#play\n");
    }

    private IRenderedComponent<LinksPanel> Render(string name) =>
        data.Bunit.Render<LinksPanel>(p => p.Add(l => l.Name, name));

    private static string[] Names(IRenderedComponent<LinksPanel> cut) =>
        cut.FindAll(".link-row .name").Select(n => n.TextContent).ToArray();

    private static IElement Row(IRenderedComponent<LinksPanel> cut, string name) =>
        cut.FindAll(".link-row").Single(r => r.QuerySelector(".name")!.TextContent == name);
}
