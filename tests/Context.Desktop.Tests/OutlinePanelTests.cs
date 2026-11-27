using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Context.Tests;

// 205 AC-9 and AC-10, 210 AC-1 to AC-6: the outline of the open note, alone and in Main's right sidebar, with the page's
// editor mocked: headings returns canned JSON (or a held answer) and every scrollToHeading call is recorded.
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

    // 210 AC-1, AC-2: the outline shows in the right sidebar before the todos, not in the note area; a click jumps the
    // mounted editor by index without a remount, keeping unsaved text, and moves no focus.
    [Fact]
    public async Task Main_OutlineInSidebar_ClickScrolls()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        IElement nav = cut.Find("aside.todos-region nav.outline-panel");
        Assert.Equal("Outline", nav.GetAttribute("aria-label"));
        Assert.Equal("todos-region", nav.ParentElement?.ParentElement?.ClassName);
        Assert.Equal("todos", nav.NextElementSibling?.ClassName);
        Assert.Equal("SECTION", nav.NextElementSibling?.TagName);
        Assert.Empty(cut.FindAll(".note-area nav.outline-panel"));
        IReadOnlyList<IElement> buttons = cut.FindAll("aside.todos-region nav.outline-panel button");
        Assert.Equal(["Top", "<b>Goals</b>", "Deep line"], buttons.Select(b => b.TextContent));
        Assert.Equal(["Top", "<b>Goals</b>", "Deep line"], buttons.Select(b => b.GetAttribute("title")));
        Assert.Equal(["level-1", "level-2", "level-6"], buttons.Select(b => b.ClassList.Single(c => c.StartsWith("level-", StringComparison.Ordinal))));
        Assert.All(buttons, b => Assert.Contains("cut", b.ClassList));

        js.Edit("# Plan\n\nunsaved\n");
        int mounts = js.Calls("ContextEditor.mount").Count;
        int focuses = js.Calls("ContextEditor.focus").Count;
        await cut.FindAll("aside.todos-region nav.outline-panel button")[1].ClickAsync(new MouseEventArgs());

        JSRuntimeInvocation jump = Assert.Single(js.Calls("ContextEditor.scrollToHeading"));
        Assert.Equal(js.Calls("ContextEditor.mount")[^1].Arguments[0], jump.Arguments[0]);
        Assert.Equal([1], js.Jumps());
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
        Assert.Equal(focuses, js.Calls("ContextEditor.focus").Count);
        Assert.True(await cut.InvokeAsync(() => cut.FindComponent<NoteEditor>().Instance.IsDirtyAsync()));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // 210 AC-3: fewer than three headings, or headings that fail in the page, show no outline; the todos still do.
    [Theory]
    [InlineData("two")]
    [InlineData("fail")]
    public async Task Main_FewOrFailingHeadings_NoOutline(string kind)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        if (kind == "two")
        {
            js.SetHeadings("""[{"level":2,"text":"A"},{"level":2,"text":"B"}]""");
        }
        else
        {
            js.Fail("ContextEditor.headings", new JSException("gone"));
        }

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        Assert.NotEmpty(js.Calls("ContextEditor.headings"));
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Single(cut.FindAll("aside.todos-region section.todos"));
        Assert.Empty(cut.FindAll(".editor-failed"));
    }

    // 210 AC-3: with no note open (the placeholder), an editor that didn't start, or a note that can't be read, the
    // sidebar has no outline and the todos still render; no headings are asked for.
    [Fact]
    public async Task Main_NoNoteFailedOrUnreadable_NoOutline()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Bad", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Single(cut.FindAll("aside.todos-region section.todos"));

        js.Fail("ContextEditor.mount", new JSException("no page"));
        await NoteOpener.OpenAsync(cut, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".editor-failed")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Single(cut.FindAll("aside.todos-region section.todos"));

        using (FileStream held = new FileStream(
            Path.Combine(data.Root, "Bad.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await NoteOpener.OpenAsync(cut, "Bad");
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")), Wait);
        }

        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Single(cut.FindAll("aside.todos-region section.todos"));
        Assert.Empty(js.Calls("ContextEditor.headings"));
    }

    // 210 AC-4: opening note B after note A (three headings, B none) leaves no outline; closing the note (deleting it)
    // removes it too.
    [Fact]
    public async Task Main_OpenOtherNoteOrClose_OutlineGone()
    {
        data.WriteNote("Alpha", "# Alpha\n");
        data.WriteNote("Beta", "# Beta\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Alpha");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        js.SetHeadings("[]");
        int reads = js.Calls("ContextEditor.headings").Count;
        await Open(cut, js, "Beta");
        cut.WaitForAssertion(() => Assert.Equal(reads + 1, js.Calls("ContextEditor.headings").Count), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        js.SetHeadings(ThreeHeadings);
        await Open(cut, js, "Alpha");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        // 216: no note is open after the open note is deleted (Today opens today's note now).
        await MainTests.ChooseAsync(cut, "delete");
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Single(cut.FindAll(".current-region p.no-note"));
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.Single(cut.FindAll("aside.todos-region section.todos"));
    }

    // 210 AC-4: a heading list note A's editor reports after note B is open never shows.
    [Fact]
    public async Task Main_StaleReportFromOldEditor_Ignored()
    {
        data.WriteNote("Alpha", "# Alpha\n");
        data.WriteNote("Beta", "# Beta\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Alpha");
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        // A reload of A asks for its headings again; that answer is held until B is open.
        (JSRuntimeInvocationHandler<string> held, Task asked) = js.HoldHeadings();
        int reads = js.Calls("ContextEditor.headings").Count;
        NoteEditor alpha = cut.FindComponent<NoteEditor>().Instance;
        Task reload = cut.InvokeAsync(() => alpha.ReloadAsync());
        await asked.WaitAsync(Wait);

        js.SetHeadings("[]");
        await Open(cut, js, "Beta");
        cut.WaitForAssertion(() => Assert.Equal(reads + 2, js.Calls("ContextEditor.headings").Count), Wait);
        held.SetResult(ThreeHeadings);
        await reload.WaitAsync(Wait);

        Assert.Equal("Beta", js.MountedName());
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.DoesNotContain("Goals", cut.Markup, StringComparison.Ordinal);
    }

    // 210 AC-5: an automatic save that is Done and changes the headings shows the new list in the sidebar.
    [Fact]
    public async Task Main_AutoSave_SidebarShowsNewHeadings()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings("""[{"level":2,"text":"Old A"},{"level":2,"text":"Old B"},{"level":2,"text":"Old C"}]""");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        js.SetHeadings(ThreeHeadings);
        await js.Change(cut, "# Plan\n\n# Top\n\n## Goals\n\n###### Deep\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".main-status").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(
            ["Top", "<b>Goals</b>", "Deep line"],
            cut.FindAll("aside.todos-region nav.outline-panel button").Select(b => b.TextContent)), Wait);
    }

    // 210 AC-5: an explicit save that is Done reloads the sidebar outline.
    [Fact]
    public async Task Main_ExplicitSave_SidebarShowsNewHeadings()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        js.SetHeadings(ThreeHeadings);
        js.Edit("# Plan\n\nmore\n");
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);
        Assert.Equal("Saved Plan.", cut.Find(".main-status").TextContent);
    }

    // 205 AC-10: a reload (the note written elsewhere) mounts again and reloads the outline.
    [Fact]
    public async Task Main_Reload_SidebarShowsNewHeadings()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        js.SetHeadings(ThreeHeadings);
        data.WriteNote("Plan", "# Plan\n\n# Top\n");
        NoteEditor editor = cut.FindComponent<NoteEditor>().Instance;
        await cut.InvokeAsync(() => editor.ReloadAsync());

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);
        Assert.Equal(2, js.Calls("ContextEditor.mount").Count);
    }

    // 210 AC-6: while a page (settings, search) is shown the sidebar has no outline; it returns when the page closes
    // over the same note, without a remount.
    [Fact]
    public async Task Main_PageShown_OutlineHiddenThenBack()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".page")), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        await cut.Find(".page > .panel-bar button.close").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);

        await cut.Find(".top-bar button.search-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-pop input")), Wait);
        await cut.Find(".search-pop input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-panel")), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        await cut.Find(".search-panel button.close").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
    }

    // 205 AC-9, 210 AC-6: locking a sensitive note takes its sidebar outline down with the editor; no heading text stays.
    [Fact]
    public async Task Main_SensitiveLocked_OutlineGone()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings("""[{"level":1,"text":"Secret A"},{"level":2,"text":"Secret B"},{"level":2,"text":"Secret C"}]""");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("aside.todos-region nav.outline-panel button").Count), Wait);
        Assert.Empty(cut.FindAll(".note-area nav.outline-panel"));

        data.Session!.Lock();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-editor")), Wait);
        Assert.Empty(cut.FindAll("nav.outline-panel"));
        Assert.DoesNotContain("Secret", cut.Markup, StringComparison.Ordinal);
    }

    // 210: the editor itself renders no outline; it reports each list through OnOutline, and an empty one when it goes.
    [Fact]
    public async Task Editor_ReportsOutline_NoPanel_EmptyOnDispose()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        List<IReadOnlyList<OutlineHeading>> reports = [];
        TaskCompletionSource reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnOutline, (IReadOnlyList<OutlineHeading> list) =>
            {
                reports.Add(list);
                reported.TrySetResult();
            }));
        await reported.Task.WaitAsync(Wait);

        Assert.Empty(cut.FindAll("nav.outline-panel"));
        IReadOnlyList<OutlineHeading> first = Assert.Single(reports);
        Assert.Equal(["Top", "<b>Goals</b>", "Deep\nline"], first.Select(h => h.Text));

        await cut.InvokeAsync(() => cut.Instance.JumpToIndexAsync(2));
        Assert.Equal([2], js.Jumps());

        await data.Bunit.DisposeComponentsAsync();
        Assert.Equal(2, reports.Count);
        Assert.Empty(reports[^1]);
    }

    // Opens a note from the notes panel, waits for its mount and ready editor, and for the links panel to land.
    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }
}
