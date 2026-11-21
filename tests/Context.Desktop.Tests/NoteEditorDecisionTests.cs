using Bunit;

namespace Context.Tests;

// 343 AC-6, AC-8, AC-9: the note editor's side of numbered decisions, with the page's editor mocked through bunit's
// JS interop (EditorJs).
public sealed class NoteEditorDecisionTests : IDisposable
{
    // Saves, lookups and loads run behind Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private static readonly DateTime Old = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string PlanPath => Path.Combine(data.Root, "Plan.md");

    // AC-6: a save that numbered markers replays the same edits on the page (not markCleanIf), which is then clean.
    [Fact]
    public async Task Save_Numbered_AppliesEditsOnPage_Clean()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\n#!d one\n");

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Equal("# Plan\n\n#!d1 one\n", File.ReadAllText(PlanPath));
        Assert.Equal([("{\"bare\":[1],\"strip\":[]}", "# Plan\n\n#!d1 one\n")], js.DecisionEdits());
        Assert.Empty(js.Calls("ContextEditor.markCleanIf"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
        Assert.False(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Equal(0, data.AutoSave.Pending);
    }

    // AC-6: text typed during that save keeps the page dirty; the next save gives the numbered marker no new number.
    [Fact]
    public async Task Save_Numbered_TypedDuringSave_StaysDirty_NoNewNumber()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\n#!d one\n");
        js.TypedDuringSave = true;

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Single(js.DecisionEdits());
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        // The page replayed the number under the typed text.
        js.TypedDuringSave = false;
        await js.Change(cut, "# Plan\n\n#!d1 one and more\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.markCleanIf")), Wait);
        Assert.Equal("# Plan\n\n#!d1 one and more\n", File.ReadAllText(PlanPath));
        Assert.Single(js.DecisionEdits());
    }

    // AC-8: after mount the editor's references are looked up: a known number gets its headline (one line), an
    // unknown one null; after a save they are looked up again.
    [Fact]
    public async Task Mount_LoadsDecisionTitles_AndAfterSave()
    {
        data.WriteNote("Owner", "#!d5 ship\u0007 it\nsecond line\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Owner.md"), Old);
        data.WriteNote("Plan", "# Plan\n\nsee #!d5 and #!d9\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetDecisionReferences("[5,9]");

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        cut.WaitForAssertion(() => Assert.Equal(["[[5,\"ship\\uFFFD it\"],[9,null]]"], js.DecisionTitles()), Wait);

        await js.Change(cut, "# Plan\n\nsee #!d5 and #!d9 now\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.DecisionTitles().Count), Wait);
    }

    // AC-8: references the page reports in a bad shape are not looked up: no titles are drawn.
    [Theory]
    [InlineData("[]")]
    [InlineData("[0]")]
    [InlineData("[5,5]")]
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

    // AC-8: a clicked reference owned by another note asks the parent to open it at the decision; an unknown one says
    // so in grey; one in this note jumps in place.
    [Fact]
    public async Task Click_Reference_OpensOwner_UnknownGrey_HereJumps()
    {
        data.WriteNote("Owner", "#!d5 ship\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Owner.md"), Old);
        data.WriteNote("Plan", "# Plan\n\n#!d3 here\n\nsee #!d5\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.HasDecision(3);
        List<(string Name, int Decision)> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnOpenDecision, ((string Name, int Decision) d) => opened.Add(d)));
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        EditorInterop page = js.Page();

        await cut.InvokeAsync(() => page.OnDecision(5));
        cut.WaitForAssertion(() => Assert.Equal([("Owner", 5)], opened), Wait);

        await cut.InvokeAsync(() => page.OnDecision(9));
        cut.WaitForAssertion(() => Assert.Equal("No decision #!d9.", cut.Find(".status").TextContent), Wait);
        Assert.True(cut.Find(".status").ClassList.Contains("grey"));

        await cut.InvokeAsync(() => page.OnDecision(3));
        cut.WaitForAssertion(() => Assert.Equal([5, 9, 3], js.DecisionJumps()), Wait);
        Assert.Equal([("Owner", 5)], opened);
    }

    // AC-9: opened at a decision, the editor jumps there once after mount; a number the note doesn't have says so.
    [Theory]
    [InlineData(7, "")]
    [InlineData(8, "Plan has no decision #!d8.")]
    public async Task Decision_JumpsAfterMount(int number, string status)
    {
        data.WriteNote("Plan", "# Plan\n\n#!d7 here\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.HasDecision(7);
        await data.RefreshAsync();

        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Decision, number));

        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal([number], js.DecisionJumps()), Wait);
        cut.WaitForAssertion(() => Assert.Equal(status, cut.Find(".status").TextContent), Wait);
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
