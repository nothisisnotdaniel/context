using Bunit;

namespace Context.Tests;

// 380 AC-7: the note editor's side of glossary terms, with the page's editor mocked through bunit's JS interop
// (EditorJs).
public sealed class NoteEditorGlossaryTests : IDisposable
{
    // Saves and loads run behind Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

    public void Dispose() => data.Dispose();

    // AC-7: after mount the page gets every term with its meanings, one line each, as "<meaning> (<note title>)".
    [Fact]
    public async Task Mount_SetsGlossary_TermWithMeanings()
    {
        data.WriteNote("A", "API:: first\n");
        data.WriteNote("people", "# people\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "people"));
        data.WriteNote("people/b", "Api:: second\u0007 one\n");
        data.WriteNote("Plan", "# Plan\n\nthe API\n");
        EditorJs js = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        cut.WaitForAssertion(
            () => Assert.Equal(["[[\"API\",\"first (A)\\nsecond\\uFFFD one (b)\"]]"], js.Glossary()),
            Wait);
    }

    // AC-7: a save that adds Foo:: bar gives Foo to the next mounted note.
    [Fact]
    public async Task Save_AddsTerm_NextMountedNoteGetsIt()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n\nFoo here\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        cut.WaitForAssertion(() => Assert.Equal(["[]"], js.Glossary()), Wait);

        await js.Change(cut, "# Plan\n\nFoo:: bar\n");
        data.AutoSave.Elapse();
        Bar.WaitForAssertion(() => Assert.Equal("Saved.", data.StatusBar.Text), Wait);

        IRenderedComponent<NoteEditor> other = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Other"));
        other.WaitForAssertion(() => Assert.True(other.Instance.IsReady), Wait);
        other.WaitForAssertion(
            () => Assert.Equal("[[\"Foo\",\"bar (Plan)\"]]", js.Glossary()[^1]),
            Wait);
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
