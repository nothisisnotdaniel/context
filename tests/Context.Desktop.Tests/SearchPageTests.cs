using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 285 AC-2, AC-6 to AC-8: the Search page in place of the note area, and Back to search above a note opened from it.
public sealed class SearchPageTests : IDisposable
{
    // Opening a note and the search run on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private const string ThreeHeadings =
        """[{"level":1,"text":"Top"},{"level":2,"text":"Goals"},{"level":2,"text":"Risks"}]""";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-2: the top bar's Search shows the page: the note area is hidden and the outline is gone; Close shows the note
    // area and the outline again.
    [Fact]
    public async Task TopBarSearch_ShowsPage_HidesNoteAreaAndOutline_CloseShowsThem()
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(ThreeHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("nav.outline-panel")), Wait);

        await TopBarSearch(cut, "budget");

        WaitForHits(cut, 2);
        Assert.True(cut.Find("section.search-panel").ClassList.Contains("page"));
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll("nav.outline-panel"));

        await Click(cut, () => cut.Find(".search-panel .panel-bar button.close"));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".search-panel")), Wait);
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("nav.outline-panel")), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").GetAttribute("title"));
    }

    // AC-2: /search and a saved search show the page too; Esc in its input closes it and shows the note area.
    [Fact]
    public async Task PaletteAndSavedSearch_ShowPage_EscapeCloses()
    {
        WriteNotes();
        data.WriteFile(SettingsStore.FileName, "{\"dayEnd\":{},\"savedSearches\":[{\"name\":\"Budget\",\"query\":\"budget\"}]}");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/search budget");
        WaitForHits(cut, 2);
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        await cut.Find(".search-panel .search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".search-panel")), Wait);
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("nav.saved-searches button.saved-search")), Wait);
        await Click(cut, () => cut.Find("nav.saved-searches button.saved-search"));
        WaitForHits(cut, 2);
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Equal("Search: budget", cut.Find(".search-panel h2").TextContent);
    }

    // AC-2: another page replaces the Search page.
    [Theory]
    [InlineData("tags-open", ".tags-panel")]
    [InlineData("people-open", ".people-panel")]
    [InlineData("settings-open", ".settings-panel")]
    [InlineData("triage-open", ".triage-panel")]
    public async Task OtherPage_ReplacesSearchPage(string button, string panel)
    {
        WriteNotes();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);

        await Click(cut, () => cut.Find("button." + button));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(panel)), Wait);
        Assert.Empty(cut.FindAll(".search-panel"));
        Assert.Single(cut.FindAll("section.page"));
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
    }

    // AC-2: the board and an appointment page replace it too.
    [Fact]
    public async Task BoardAndAppointment_ReplaceSearchPage()
    {
        WriteNotes();
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(DesktopFixture.Event("Budget", "20261029T110000", "20261029T120000")));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".day-appointments .appointment-item")), Wait);
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);

        await RunPalette(cut, "/board work");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".board-panel")), Wait);
        Assert.Empty(cut.FindAll(".search-panel"));

        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);
        Assert.Empty(cut.FindAll(".board-panel"));
        await Click(cut, () => cut.Find(".day-appointments .appointment-item"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".appointment-page")), Wait);
        Assert.Empty(cut.FindAll(".search-panel"));
    }

    // AC-2: opening and closing the page keeps the open note's unsaved text: no dialog, no write, the same editor.
    [Fact]
    public async Task DirtyNote_SurvivesSearchOpenAndClose()
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");
        await js.Change(cut, "# Plan\n\nTyped, not saved\n");
        js.EditUnsavable("# Plan\n\nTyped, not saved\n");
        int mounts = js.Calls("ContextEditor.mount").Count;
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);
        await Click(cut, () => cut.Find(".search-panel .panel-bar button.close"));

        cut.WaitForAssertion(() => Assert.False(cut.Find(".note-area").HasAttribute("hidden")), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
        Assert.True(await cut.FindComponent<NoteEditor>().Instance.IsDirtyAsync());
        Assert.Equal("# Plan\n\nTyped, not saved\n", js.Markdown[..("# Plan\n\nTyped, not saved\n".Length)]);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-6, AC-7: a result opens in place of the page with Back to search above it; Back shows the page again for the
    // query typed and run on the page, read again, with the opened note's row as the tab stop and focused.
    [Fact]
    public async Task OpenResult_BackToSearch_ShowsTypedQuery_FocusesOpenedRow()
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await TopBarSearch(cut, "nothing");
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".search-panel").TextContent, StringComparison.Ordinal), Wait);
        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "budget" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        WaitForHits(cut, 2);
        Assert.Equal(["Anna", "Plan"], HitNames(cut));
        Assert.Empty(cut.FindAll("button.search-back"));
        int mounts = js.Calls("ContextEditor.mount").Count;

        await Click(cut, () => cut.FindAll(".search-hits .note-button")[1]);

        WaitForNote(cut, js, mounts, "Plan");
        Assert.Empty(cut.FindAll(".search-panel"));
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        IElement back = cut.Find(".note-area button.search-back");
        Assert.Equal("Back to search", back.TextContent);
        Assert.Equal("button", back.GetAttribute("type"));

        // The results are read again: a new matching note shows when the page comes back.
        data.WriteNote("Carl", "budget for Carl\n");
        await data.State!.Refresh();
        await Click(cut, () => cut.Find("button.search-back"));

        cut.WaitForAssertion(() => Assert.Equal(["Anna", "Carl", "Plan"], HitNames(cut)), Wait);
        WaitForHits(cut, 3);
        Assert.Equal("Search: budget", cut.Find(".search-panel h2").TextContent);
        Assert.Equal("budget", cut.Find(".search-panel .search-input").GetAttribute("value"));
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll("button.search-back"));
        Assert.Equal(["-1", "-1", "0"], TabIndexes(cut));
        AssertRowFocused(cut, 2);
        Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count);
    }

    // AC-7: when the opened note no longer matches, the first row is the tab stop and gets the focus.
    [Fact]
    public async Task BackToSearch_RowGone_FocusesFirstRow()
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);
        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.FindAll(".search-hits .note-button")[1]);
        WaitForNote(cut, js, mounts, "Plan");

        data.WriteNote("Plan", "# Plan\nno longer\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();
        await Click(cut, () => cut.Find("button.search-back"));

        cut.WaitForAssertion(() => Assert.Equal(["Anna"], HitNames(cut)), Wait);
        WaitForHits(cut, 1);
        Assert.Equal(["0"], TabIndexes(cut));
        AssertRowFocused(cut, 0);
    }

    // AC-6: with unsaved changes that can't be saved, the result asks first; Discard opens it with Back to search.
    // Keep editing leaves the page and shows no Back button.
    [Fact]
    public async Task OpenResult_UnsavedChanges_AsksFirst_ThenBackShows()
    {
        WriteNotes();
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Other");
        js.EditUnsavable("# Other\n\nUnsaved\n");
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);

        await Click(cut, () => cut.FindAll(".search-hits .note-button")[0]);
        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Other?", cut.Find("[role=dialog] .question").TextContent), Wait);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Single(cut.FindAll(".search-panel"));
        Assert.Empty(cut.FindAll("button.search-back"));

        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.FindAll(".search-hits .note-button")[0]);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        WaitForNote(cut, js, mounts, "Anna");
        Assert.Single(cut.FindAll("button.search-back"));
    }

    // AC-8: Back to search goes when another note, Today, a day or another page opens.
    [Theory]
    [InlineData("note")]
    [InlineData("today")]
    [InlineData("day")]
    [InlineData("page")]
    public async Task BackToSearch_GoesWhenSomethingElseOpens(string what)
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);
        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.FindAll(".search-hits .note-button")[1]);
        WaitForNote(cut, js, mounts, "Plan");
        Assert.Single(cut.FindAll("button.search-back"));

        switch (what)
        {
            case "note":
                await OpenFull(cut, js, "Anna");
                break;
            case "today":
                await Click(cut, () => cut.Find("button.today-open"));
                cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").GetAttribute("title")), Wait);
                break;
            case "day":
                await Click(cut, () => cut.Find("button.day-pick-open"));
                await Click(cut, () => cut.Find(".day-pick button.day[tabindex='0']"));
                cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").GetAttribute("title")), Wait);
                break;
            default:
                await Click(cut, () => cut.Find("button.tags-open"));
                cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tags-panel")), Wait);
                await Click(cut, () => cut.Find(".tags-panel .panel-bar button.close"));
                cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".tags-panel")), Wait);
                Assert.Equal("Plan", cut.Find(".note-title").GetAttribute("title"));
                break;
        }

        Assert.Empty(cut.FindAll("button.search-back"));
    }

    // AC-8: showing the Search page itself ends Back to search, even when it is closed again over the same note.
    [Fact]
    public async Task BackToSearch_GoesWhenSearchPageShows()
    {
        WriteNotes();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await TopBarSearch(cut, "budget");
        WaitForHits(cut, 2);
        int mounts = js.Calls("ContextEditor.mount").Count;
        await Click(cut, () => cut.FindAll(".search-hits .note-button")[1]);
        WaitForNote(cut, js, mounts, "Plan");

        await Click(cut, () => cut.Find("button.search-back"));
        WaitForHits(cut, 2);
        await Click(cut, () => cut.Find(".search-panel .panel-bar button.close"));

        cut.WaitForAssertion(() => Assert.False(cut.Find(".note-area").HasAttribute("hidden")), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").GetAttribute("title"));
        Assert.Empty(cut.FindAll("button.search-back"));
    }

    private void WriteNotes()
    {
        data.WriteNote("Plan", "# Plan\n## Goals\nThe budget for 2027\n## Risks\n");
        data.WriteNote("Anna", "# Anna\nBudget talk #work\n");
    }

    private static string[] HitNames(IRenderedComponent<Main> cut) =>
        cut.FindAll(".search-panel .search-hits .note-button").Select(b => b.TextContent).ToArray();

    private static string[] TabIndexes(IRenderedComponent<Main> cut) =>
        cut.FindAll(".search-panel .search-hits .note-button").Select(b => b.GetAttribute("tabindex") ?? "").ToArray();

    // "the page shows no Loading… and .search-hits has n rows".
    private static void WaitForHits(IRenderedComponent<Main> cut, int count) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".search-panel").TextContent, StringComparison.Ordinal);
            Assert.Equal(count, cut.FindAll(".search-panel .search-hits li").Count);
        }, Wait);

    // "the note's editor is mounted and ready, .links-panel shows no Loading… and .note-title is the name".
    private static void WaitForNote(IRenderedComponent<Main> cut, EditorJs js, int mountsBefore, string name)
    {
        cut.WaitForAssertion(() => Assert.Equal(mountsBefore + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
    }

    private static async Task OpenFull(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        WaitForNote(cut, js, mounts, name);
    }

    // "the reload count is 0", then the element is found and clicked in one dispatcher call.
    private async Task Click(IRenderedComponent<Main> cut, Func<IElement> find)
    {
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => find().ClickAsync(new MouseEventArgs()));
    }

    // Waits until the Search page's row button (the element its rowButtons field holds) was given the focus.
    private void AssertRowFocused(IRenderedComponent<Main> cut, int row)
    {
        object buttons = typeof(SearchPanel).GetField("rowButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(cut.FindComponent<SearchPanel>().Instance)!;
        string id = ((ElementReference[])buttons)[row].Id;
        Assert.False(string.IsNullOrEmpty(id));
        cut.WaitForAssertion(
            () => Assert.Contains(
                data.Bunit.JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus"),
                i => i.Arguments[0] is ElementReference r && r.Id == id),
            Wait);
    }

    private static async Task TopBarSearch(IRenderedComponent<Main> cut, string text)
    {
        await cut.Find(".top-bar button.search-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-pop input")), Wait);
        await cut.Find(".search-pop input").InputAsync(new ChangeEventArgs { Value = text });
        await cut.Find(".search-pop input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }
}
