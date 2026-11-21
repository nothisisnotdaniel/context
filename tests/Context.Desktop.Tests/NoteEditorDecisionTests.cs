using Bunit;

namespace Context.Tests;

// 343 AC-6, AC-8, AC-9 and 349 AC-3, AC-4, AC-7, AC-9, AC-11, AC-12: the note editor's side of numbered decisions, with the page's editor mocked through bunit's
// JS interop (EditorJs).
public sealed class NoteEditorDecisionTests : IDisposable
{
    // Saves, lookups and loads run behind Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static readonly DateTime Old = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public NoteEditorDecisionTests() => data.WriteDecisionFolder();

    public void Dispose() => data.Dispose();

    private static readonly string Line = DesktopFixture.DecisionFolderLine;

    private static readonly string Guid = DesktopFixture.DecisionFolderGuid.ToString("D");

    private string PlanPath => Path.Combine(data.Root, "Plan.md");

    // 349 AC-3, AC-4 (343 AC-6): a save that numbered markers replays the same edits on the page (not markCleanIf),
    // with the folder line, and the page is then clean.
    [Fact]
    public async Task Save_Numbered_AppliesEditsOnPage_Clean()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\n!d one\n");

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        string saved = $"# Plan\n{Line}\n\n!d ab12-1 one\n";
        Assert.Equal(saved, File.ReadAllText(PlanPath));
        Assert.Equal([("{\"set\":[[0,null,\"ab12-1\"]],\"folder\":\"" + Guid + "\"}", saved)], js.DecisionEdits());
        Assert.Empty(js.Calls("ContextEditor.markCleanIf"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
        Assert.False(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Equal(0, data.AutoSave.Pending);
    }

    // 343 AC-6: text typed during that save keeps the page dirty; the next save gives the numbered marker no new number.
    [Fact]
    public async Task Save_Numbered_TypedDuringSave_StaysDirty_NoNewNumber()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\n!d one\n");
        js.TypedDuringSave = true;

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Single(js.DecisionEdits());
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        // The page replayed the number under the typed text.
        js.TypedDuringSave = false;
        await js.Change(cut, $"# Plan\n{Line}\n\n!d ab12-1 one and more\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.markCleanIf")), Wait);
        Assert.Equal($"# Plan\n{Line}\n\n!d ab12-1 one and more\n", File.ReadAllText(PlanPath));
        Assert.Single(js.DecisionEdits());
    }

    // 349 AC-9: after mount the page gets the folder's own prefix, and the alias of the note's folder for a copy.
    [Fact]
    public async Task Mount_SetsDecisionPrefixes()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);

        await Render("Plan");

        Assert.Equal(["{\"own\":\"ab12\",\"alias\":null}"], js.DecisionPrefixes());
    }

    // 349 AC-7 (343 AC-8): after mount the editor's references are looked up: a known one gets its headline (one line),
    // an unknown one null; after a save they are looked up again.
    [Fact]
    public async Task Mount_LoadsDecisionTitles_AndAfterSave()
    {
        data.WriteNote("Owner", $"{Line}\n!d ab12-5 ship\u0007 it\nsecond line\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Owner.md"), Old);
        data.WriteNote("Plan", "# Plan\n\nsee #!d5 and #!d9\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetDecisionReferences("[\"5\",\"9\"]");

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal(["[[\"5\",\"ship\\uFFFD it\"],[\"9\",null]]"], js.DecisionTitles()), Wait);

        await js.Change(cut, "# Plan\n\nsee #!d5 and #!d9 now\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.DecisionTitles().Count), Wait);
    }

    // 343 AC-8: references the page reports in a bad shape are not looked up: no titles are drawn.
    [Theory]
    [InlineData("[]")]
    [InlineData("[5]")]
    [InlineData("[\"0\"]")]
    [InlineData("[\"5\",\"5\"]")]
    [InlineData("[\"#!d5\"]")]
    [InlineData("[\"AB12-5\"]")]
    [InlineData("{\"a\":1}")]
    [InlineData("not json")]
    public async Task Mount_NoOrBadReferences_EmptyTitles(string json)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetDecisionReferences(json);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal(["[]"], js.DecisionTitles()), Wait);
    }

    // 349 AC-7 (343 AC-8): a clicked reference owned by another note asks the parent to open it at the decision; an
    // unknown one says so in grey; one in this note jumps in place.
    [Fact]
    public async Task Click_Reference_OpensOwner_UnknownGrey_HereJumps()
    {
        data.WriteNote("Owner", $"{Line}\n!d ab12-5 ship\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Owner.md"), Old);
        data.WriteNote("Plan", $"# Plan\n{Line}\n\n!d ab12-3 here\n\nsee #!d5\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.HasDecision("ab12-3");
        List<(string Name, string Decision)> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnOpenDecision, ((string Name, string Decision) d) => opened.Add(d)));
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        EditorInterop page = js.Page();

        await cut.InvokeAsync(() => page.OnDecision("5"));
        cut.WaitForAssertion(() => Assert.Equal([("Owner", "ab12-5")], opened), Wait);

        await cut.InvokeAsync(() => page.OnDecision("9"));
        cut.WaitForAssertion(() => Assert.Equal("No decision #!d9.", cut.Find(".status").TextContent), Wait);
        Assert.True(cut.Find(".status").ClassList.Contains("grey"));

        await cut.InvokeAsync(() => page.OnDecision("ab12-3"));
        cut.WaitForAssertion(() => Assert.Equal(["ab12-9", "ab12-3"], js.DecisionJumps()), Wait);
        Assert.Equal([("Owner", "ab12-5")], opened);
    }

    // 349 AC-12: a Copy reference button that copied says so in grey; anything that is no reference is ignored.
    [Fact]
    public async Task CopiedDecision_GreyStatus()
    {
        data.WriteNote("Plan", $"# Plan\n{Line}\n\n!d ab12-3 here\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        EditorInterop page = js.Page();

        await cut.InvokeAsync(() => page.OnCopiedDecision("not a reference"));
        Assert.Equal(string.Empty, cut.Find(".status").TextContent);

        await cut.InvokeAsync(() => page.OnCopiedDecision("3"));
        cut.WaitForAssertion(() => Assert.Equal("Copied #!d3.", cut.Find(".status").TextContent), Wait);
        Assert.True(cut.Find(".status").ClassList.Contains("grey"));
    }

    // 349 AC-11: the picker's rows come from the log, as JSON, through the page's DecisionPicks call.
    [Fact]
    public async Task DecisionPicks_FromLog()
    {
        data.WriteNote("Owner", $"{Line}\n!d ab12-5 ship it\n");
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        await Render("Plan");
        EditorInterop page = js.Page();

        string rows = await page.DecisionPicks("ship");

        Assert.Contains("ship it", rows, StringComparison.Ordinal);
        Assert.Equal("[]", await page.DecisionPicks("nothing like it"));
        Assert.Equal("[]", await page.DecisionPicks(new string('x', 31)));
    }

    // 349 AC-9 (343 AC-9): opened at a decision, the editor jumps there once after mount; a token the note doesn't have
    // says so.
    [Theory]
    [InlineData("ab12-7", "")]
    [InlineData("ab12-8", "Plan has no decision #!dab12-8.")]
    public async Task Decision_JumpsAfterMount(string token, string status)
    {
        data.WriteNote("Plan", $"# Plan\n{Line}\n\n!d ab12-7 here\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.HasDecision("ab12-7");
        await data.RefreshAsync();

        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Decision, token));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal([token], js.DecisionJumps()), Wait);
        cut.WaitForAssertion(() => Assert.Equal(status, cut.Find(".status").TextContent), Wait);
    }

    // 349 AC-4, AC-3: a save of another folder's note or one that ran out of numbers says so in red after the saved
    // text; the note is saved as typed either way.
    [Fact]
    public async Task Save_ForeignOrRanOut_RedNotice()
    {
        string foreign = "decision-folder:: 99999999-8888-7777-6666-555555555555";
        data.WriteNote("Plan", $"# Plan\n{foreign}\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        await js.Change(cut, $"# Plan\n{foreign}\n\n!d hold\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(
            "Saved. " + DesktopActions.DecisionForeignText, cut.Find(".status").TextContent), Wait);
        Assert.True(cut.Find(".status").ClassList.Contains("red"));
        Assert.Equal($"# Plan\n{foreign}\n\n!d hold\n", File.ReadAllText(PlanPath));

        TagIndex index = new TagIndex(data.Root);
        Assert.Equal(1, index.ReserveDecisionNumbers("ab12", DecisionParser.MaxNumber));
        await js.Change(cut, "# Plan\n\n!d new\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.StartsWith(
            "Saved. " + DesktopActions.DecisionNumbersRanOutText, cut.Find(".status").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal("# Plan\n\n!d new\n", File.ReadAllText(PlanPath));
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".editor-host")), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        return cut;
    }
}
