using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Context.Tests;

// 205 AC-9 and AC-10: the outline of the open note, alone and in the editor, with the page's editor mocked: headings
// returns canned JSON and every scrollToHeading call is recorded.
public sealed class OutlinePanelTests : IDisposable
{
    // Opening a note reads it on Task.Run; every wait passes this bounded timeout, a real failure still fails within it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string ThreeHeadings =
        """[{"level":1,"text":"Top"},{"level":2,"text":"<b>Goals</b>"},{"level":6,"text":"Deep\nline"}]""";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-9: one plain-text button per heading, in order, with its level class; inside nav.outline-panel.
    [Fact]
    public void Panel_ThreeHeadings_ButtonsInOrder()
    {
        IRenderedComponent<OutlinePanel> cut = data.Bunit.Render<OutlinePanel>(p => p
            .Add(e => e.Headings, OutlineHeading.Parse(ThreeHeadings)));

        IElement nav = cut.Find("nav.outline-panel");
        Assert.Equal("Outline", nav.GetAttribute("aria-label"));
        IReadOnlyList<IElement> buttons = cut.FindAll("nav.outline-panel button");
        Assert.Equal(["Top", "<b>Goals</b>", "Deep line"], buttons.Select(b => b.TextContent));
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(["level-1", "level-2", "level-6"], buttons.Select(b => b.ClassList.Single(c => c.StartsWith("level-", StringComparison.Ordinal))));
        Assert.Empty(cut.FindAll("b"));
        Assert.Equal(3, OutlinePanel.MinHeadings);
    }

    // AC-9: fewer than MinHeadings headings show no outline.
    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"level":2,"text":"A"},{"level":2,"text":"B"}]""")]
    public void Panel_FewHeadings_NoOutline(string json)
    {
        IRenderedComponent<OutlinePanel> cut = data.Bunit.Render<OutlinePanel>(p => p
            .Add(e => e.Headings, OutlineHeading.Parse(json)));

        Assert.Empty(cut.FindAll("nav"));
    }

    // AC-9: the editor's list is read all or nothing.
    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[{"level":0,"text":"A"},{"level":2,"text":"B"},{"level":2,"text":"C"}]""")]
    [InlineData("""[{"level":7,"text":"A"},{"level":2,"text":"B"},{"level":2,"text":"C"}]""")]
    [InlineData("""[{"level":2},{"level":2,"text":"B"},{"level":2,"text":"C"}]""")]
    [InlineData("""[{"level":"2","text":"A"},{"level":2,"text":"B"},{"level":2,"text":"C"}]""")]
    [InlineData("""[{"level":2,"text":1},{"level":2,"text":"B"},{"level":2,"text":"C"}]""")]
    [InlineData("""[1,2,3]""")]
    public void Parse_Malformed_Empty(string json)
    {
        Assert.Empty(OutlineHeading.Parse(json));
    }

    // AC-9: a list longer than MaxHeadings is not shown at all; a record never prints its text.
    [Fact]
    public void Parse_TooMany_Empty_ToStringHidesText()
    {
        string many = "[" + string.Join(",", Enumerable.Repeat("""{"level":2,"text":"A"}""", OutlineHeading.MaxHeadings + 1)) + "]";
        string max = "[" + string.Join(",", Enumerable.Repeat("""{"level":2,"text":"A"}""", OutlineHeading.MaxHeadings)) + "]";

        Assert.Empty(OutlineHeading.Parse(many));
        Assert.Equal(OutlineHeading.MaxHeadings, OutlineHeading.Parse(max).Count);
        Assert.Equal(nameof(OutlineHeading), new OutlineHeading(2, "Secret").ToString());
    }

    // AC-6: the match trims, collapses whitespace runs and ignores case; the first match wins.
    [Fact]
    public void IndexOf_FoldsSpacesAndCase_FirstWins()
    {
        IReadOnlyList<OutlineHeading> list =
            [new OutlineHeading(2, "Intro"), new OutlineHeading(2, " next \t GOALS "), new OutlineHeading(3, "Next goals")];

        Assert.Equal(1, OutlineHeading.IndexOf(list, "Next goals"));
        Assert.Equal(0, OutlineHeading.IndexOf(list, "  intro"));
        Assert.Equal(-1, OutlineHeading.IndexOf(list, "Goals"));
    }

    // AC-10: clicking the n-th button jumps to index n.
    [Fact]
    public async Task Panel_Click_RaisesIndex()
    {
        List<int> jumps = [];
        IRenderedComponent<OutlinePanel> cut = data.Bunit.Render<OutlinePanel>(p => p
            .Add(e => e.Headings, OutlineHeading.Parse(ThreeHeadings))
            .Add(e => e.OnJump, (int index) => jumps.Add(index)));

        await cut.FindAll("nav.outline-panel button")[2].ClickAsync(new MouseEventArgs());
        await cut.FindAll("nav.outline-panel button")[0].ClickAsync(new MouseEventArgs());

        Assert.Equal([2, 0], jumps);
    }

    // AC-9/10: in the editor, the outline comes from headings after the mount, before the editor host, and a click
    // calls scrollToHeading with the editor's element and the index; the focus stays where the mount put it.
    [Fact]
    public async Task Editor_OutlineAfterMount_ClickScrolls()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("nav.outline-panel button").Count), Wait);

        Assert.Equal("editor-host", cut.Find("nav.outline-panel").NextElementSibling?.ClassName);
        Assert.Single(js.Calls("ContextEditor.focus"));
        await cut.FindAll("nav.outline-panel button")[1].ClickAsync(new MouseEventArgs());

        JSRuntimeInvocation jump = Assert.Single(js.Calls("ContextEditor.scrollToHeading"));
        Assert.Equal(js.Calls("ContextEditor.mount")[^1].Arguments[0], jump.Arguments[0]);
        Assert.Equal([1], js.Jumps());
    }

    // AC-9: fewer than three headings in the editor show no outline.
    [Fact]
    public async Task Editor_TwoHeadings_NoOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings("""[{"level":2,"text":"A"},{"level":2,"text":"B"}]""");

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.NotEmpty(js.Calls("ContextEditor.headings"));
        Assert.Empty(cut.FindAll("nav.outline-panel"));
    }

    // AC-9: an editor that didn't start, or a note that can't be read, has no outline and asks for no headings.
    [Fact]
    public async Task Editor_FailedOrUnreadable_NoOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Bad", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        js.Fail("ContextEditor.mount", new JSException("no page"));
        await data.RefreshAsync();

        IRenderedComponent<NoteEditor> failed = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Plan"));
        failed.WaitForAssertion(() => Assert.Single(failed.FindAll(".editor-failed")), Wait);
        IRenderedComponent<NoteEditor> unreadable;
        using (FileStream held = new FileStream(
            Path.Combine(data.Root, "Bad.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            unreadable = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Bad"));
            unreadable.WaitForAssertion(() => Assert.Single(unreadable.FindAll(".unreadable")), Wait);
        }

        Assert.Empty(failed.FindAll("nav.outline-panel"));
        Assert.Empty(unreadable.FindAll("nav.outline-panel"));
        Assert.Empty(js.Calls("ContextEditor.headings"));
    }

    // AC-9: headings that fail in the page show no outline; the editor still works.
    [Fact]
    public async Task Editor_HeadingsFail_NoOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.headings", new JSException("gone"));

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Empty(cut.FindAll(".editor-failed"));
    }

    // AC-10: an automatic save that is Done reloads the outline.
    [Fact]
    public async Task Editor_AutoSave_ReloadsOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        js.SetHeadings(ThreeHeadings);
        await js.Change(cut, "# Plan\n\n# Top\n\n## Goals\n\n###### Deep\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("nav.outline-panel button").Count), Wait);
        Assert.Equal("Saved.", cut.Find(".status").TextContent);
    }

    // AC-10: an explicit save that is Done reloads the outline; a refused one doesn't.
    [Fact]
    public async Task Editor_ExplicitSave_ReloadsOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        int reads = js.Calls("ContextEditor.headings").Count;

        js.SetHeadings(ThreeHeadings);
        js.Edit("# Plan\n\nmore\n");
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("nav.outline-panel button").Count), Wait);
        Assert.Equal(reads + 1, js.Calls("ContextEditor.headings").Count);
    }

    // AC-10: a reload (the note written elsewhere) mounts again and reloads the outline.
    [Fact]
    public async Task Editor_Reload_ReloadsOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        js.SetHeadings(ThreeHeadings);
        data.WriteNote("Plan", "# Plan\n\n# Top\n");
        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("nav.outline-panel button").Count), Wait);
        Assert.Equal(2, js.Calls("ContextEditor.mount").Count);
    }

    // AC-9: locking a sensitive note takes its outline down with the editor.
    [Fact]
    public async Task Main_SensitiveLocked_OutlineGone()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings("""[{"level":1,"text":"Secret A"},{"level":2,"text":"Secret B"},{"level":2,"text":"Secret C"}]""");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".notes-region button.name").Single(b => b.TextContent == "Plan").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("nav.outline-panel button").Count), Wait);
        Assert.Equal("note-editor", cut.Find("nav.outline-panel").ParentElement?.ClassName);
        Assert.NotNull(cut.Find("nav.outline-panel").Closest(".note-area"));

        data.Session!.Lock();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.DoesNotContain("Secret", cut.Markup, StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        return cut;
    }
}
