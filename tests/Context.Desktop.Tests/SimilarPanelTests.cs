using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 268 AC-4, AC-5: /similar shows SimilarPanel through Main: the pairs with their reasons, Keep buttons that ask before a
// merge, the guard for unsaved notes and the reload after a merge.
public sealed class SimilarPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The panel loads DesktopState.Similar on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4: the palette lists /similar.
    [Fact]
    public void Palette_ListsSimilar()
    {
        PaletteCommand similar = Assert.Single(CommandPalette.Commands, c => c.Name == "/similar");
        Assert.Equal("/similar", similar.Usage);
    }

    // AC-4: heading, Close, per pair two note buttons with the full name in title, the grey reason and the Keep
    // buttons; the reason texts; nothing but context.db is written; Close hides the panel.
    [Fact]
    public async Task Similar_Rows_ReasonsAndButtons_CloseHides()
    {
        string shared = Words("w", 20);
        data.WriteNote("Budget plan", shared);
        data.WriteNote("budget plans", shared);
        data.WriteNote("Alpha", Words("s", 20));
        data.WriteNote("Omega", Words("s", 20));
        data.WriteNote("Meeting notes", "one\n");
        data.WriteNote("meeting note", "two\n");
        data.WriteNote("Kappa", Words("p", 16) + " " + Words("k", 4));
        data.WriteNote("Sigma", Words("p", 16) + " " + Words("g", 4));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = Files();

        await ShowAsync(cut);

        Assert.Equal("Similar notes", cut.Find(".similar-panel h2").TextContent);
        Assert.Equal("Similar notes", cut.Find("section.similar-panel").GetAttribute("aria-label"));
        Assert.Equal(
            [
                "Budget plan|budget plans|similar title, 100% same text",
                "Alpha|Omega|100% same text",
                "Kappa|Sigma|63% same text",
                "meeting note|Meeting notes|similar title",
            ],
            Rows(cut));
        AngleSharp.Dom.IElement row = cut.FindAll(".similar-rows li")[0];
        Assert.Equal("Budget plan", row.QuerySelectorAll("button.note-button")[0].GetAttribute("title"));
        Assert.True(row.QuerySelector(".reason")!.ClassList.Contains("grey"));
        Assert.Equal("Keep Budget plan", row.QuerySelector("button.keep-first")!.TextContent);
        Assert.Equal("Keep budget plans", row.QuerySelector("button.keep-second")!.TextContent);
        Assert.All(cut.FindAll(".similar-panel button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(before, Files());

        await StaleClick.ClickAsync(cut, ".similar-panel button.close");

        Assert.Empty(cut.FindAll(".similar-panel"));
    }

    // AC-4: a note button opens the note and hides the panel; Today hides it too.
    [Fact]
    public async Task Similar_OpenOrToday_HidesPanel()
    {
        data.WriteNote("Project plan", "a\n");
        data.WriteNote("Project plans", "b\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await ShowAsync(cut);

        // Main renders again by itself (its own loads), which can drop the handler of a button found before that
        // render (job 491), so the click finds the button again then.
        await StaleClick.ClickAsync(cut, ".similar-rows .note-button[title=\"Project plans\"]");

        cut.WaitForAssertion(() => Assert.Equal("Project plans", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".similar-panel"));

        await ShowAsync(cut);
        await StaleClick.ClickAsync(cut, "button.today-open");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".similar-panel")), Wait);
    }

    // AC-4: at most 100 rows, then the grey "… n more" line.
    [Fact]
    public async Task Similar_ManyPairs_CappedWithMoreLine()
    {
        string text = Words("same", 12);
        for (int i = 0; i < 15; i++)
        {
            data.WriteNote("n" + Letters(i), text);
        }

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await ShowAsync(cut);

        Assert.Equal(SimilarPanel.MaxRows, cut.FindAll(".similar-rows li").Count);
        Assert.Equal("… 5 more", cut.Find(".similar-panel p.more").TextContent);
        Assert.True(cut.Find(".similar-panel p.more").ClassList.Contains("grey"));
    }

    // AC-4: no pair shows the grey line; a bad context.db the red one and is left alone.
    [Fact]
    public async Task Similar_NoneOrBadIndex_StatusLines()
    {
        data.WriteNote("Plan", "a\n");
        data.WriteNote("Other", "b\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await ShowAsync(cut);

        AngleSharp.Dom.IElement none = cut.Find(".similar-panel p[role=status]");
        Assert.Equal("No similar notes.", none.TextContent);
        Assert.True(none.ClassList.Contains("grey"));

        await StaleClick.ClickAsync(cut, ".similar-panel button.close");
        File.WriteAllText(Path.Combine(data.Root, TagIndex.FileName), NotADatabase);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        await ShowAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal("Can't read the notes index.", cut.Find(".similar-panel p[role=status]").TextContent), Wait);
        Assert.True(cut.Find(".similar-panel p[role=status]").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-5: a Keep button asks with the count of notes whose links change (focus on Cancel); Cancel writes nothing.
    [Fact]
    public async Task Keep_AsksWithCount_CancelWritesNothing()
    {
        data.WriteNote("Project plan", "a\n");
        data.WriteNote("Project plans", "b\n");
        data.WriteNote("A", "[[Project plans]]\n");
        data.WriteNote("B", "[[project plans#H]]\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await ShowAsync(cut);
        SortedDictionary<string, string> before = Files();
        int focused = Focuses();

        await StaleClick.ClickAsync(cut, ".similar-rows button.keep-first");

        cut.WaitForAssertion(() => Assert.Equal(
            "Merge Project plans into Project plan? Its text is added to Project plan, links in 2 notes are updated and Project plans stays in its history.",
            cut.Find(".similar-panel [role=dialog] .question").TextContent), Wait);
        Assert.Equal("Merge", cut.Find(".similar-panel [role=dialog] button.confirm").TextContent);
        cut.WaitForAssertion(() => Assert.Equal(focused + 1, Focuses()), Wait);

        await StaleClick.ClickAsync(cut, ".similar-panel [role=dialog] button.cancel");

        Assert.Empty(cut.FindAll(".similar-panel [role=dialog]"));
        Assert.Equal(2, cut.FindAll(".similar-rows .note-button").Count);
        Assert.Equal(before, Files());
    }

    // AC-5, AC-6: Merge merges, shows the accent result and the pair is gone after the reload.
    [Fact]
    public async Task Keep_Merge_DoneAndPairGone()
    {
        data.WriteNote("Project plan", "a\n");
        data.WriteNote("Project plans", "b\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await ShowAsync(cut);

        await StaleClick.ClickAsync(cut, ".similar-rows button.keep-second");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".similar-panel [role=dialog]")), Wait);
        await StaleClick.ClickAsync(cut, ".similar-panel [role=dialog] button.confirm");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".similar-rows li")), Wait);
        cut.WaitForAssertion(() => Assert.Equal("No similar notes.", cut.Find(".similar-panel p.grey[role=status]").TextContent), Wait);
        Assert.Empty(cut.FindAll(".similar-panel p.similar-result"));
        AngleSharp.Dom.IElement result = cut.Find(".main-status");
        Assert.Equal("Merged Project plan into Project plans.", result.TextContent);
        Assert.True(result.ClassList.Contains("accent"));
        Assert.Equal("b\n\na\n", File.ReadAllText(Path.Combine(data.Root, "Project plans.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Project plan.md")));
    }

    // AC-5: with either note open and unsaved the red "Save <name> first." shows and no dialog opens.
    [Theory]
    [InlineData("Project plan")]
    [InlineData("Project plans")]
    public async Task Keep_OpenNoteUnsaved_SaveFirst(string open)
    {
        data.WriteNote("Project plan", "a\n");
        data.WriteNote("Project plans", "b\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenAsync(cut, js, open);
        js.Edit("changed\n");
        await ShowAsync(cut);
        SortedDictionary<string, string> before = Files();

        await StaleClick.ClickAsync(cut, ".similar-rows button.keep-first");

        cut.WaitForAssertion(() => Assert.Equal("Save " + open + " first.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".similar-panel [role=dialog]"));
        Assert.Equal(before, Files());
    }

    // AC-6: merging away the open (clean) source opens the kept note.
    [Fact]
    public async Task Keep_Merge_OpenSourceOpensTarget()
    {
        data.WriteNote("Project plan", "a\n");
        data.WriteNote("Project plans", "b\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenAsync(cut, js, "Project plans");
        await ShowAsync(cut);

        await StaleClick.ClickAsync(cut, ".similar-rows button.keep-first");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".similar-panel [role=dialog]")), Wait);
        await StaleClick.ClickAsync(cut, ".similar-panel [role=dialog] button.confirm");

        cut.WaitForAssertion(() => Assert.Equal("Project plan", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("a\n\nb\n", File.ReadAllText(Path.Combine(data.Root, "Project plan.md")));
    }

    // Runs /similar and waits until the panel has loaded.
    private static async Task ShowAsync(IRenderedComponent<Main> cut)
    {
        await StaleClick.KeyDownAsync(cut, ".layout", new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await StaleClick.InputAsync(cut, ".palette-input", new ChangeEventArgs { Value = "/similar" });
        await StaleClick.KeyDownAsync(cut, ".palette-input", new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(
            () => Assert.DoesNotContain("Loading…", cut.Find(".similar-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    private static async Task OpenAsync(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    private static string[] Rows(IRenderedComponent<Main> cut) =>
        cut.FindAll(".similar-rows li")
            .Select(li => string.Join(
                '|',
                li.QuerySelectorAll("button.note-button").Select(b => b.TextContent)
                    .Append(li.QuerySelector(".reason")!.TextContent)))
            .ToArray();

    // count distinct letter-only words <prefix>a, <prefix>b, ..., joined by spaces.
    private static string Words(string prefix, int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => prefix + Letters(i)));

    private static string Letters(int number)
    {
        string text = string.Empty;
        do
        {
            text = (char)('a' + (number % 26)) + text;
            number /= 26;
        }
        while (number > 0);
        return text;
    }

    // The FocusAsync calls so far (ConfirmDialog focuses its Cancel button when it opens).
    private int Focuses() => data.JsCalls().Count(i => i.Identifier == "Blazor._internal.domWrapper.focus");

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
}
