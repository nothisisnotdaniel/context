using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 170 AC-10: the panel of /search <text>: the notes whose text contains it, with the first matching line.
public sealed class SearchPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Search on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-10: heading, Close, one note button (full name in title) and grey line (full text in title) per hit; nothing
    // is written but context.db.
    [Fact]
    public async Task Render_Hits_HeadingButtonsLinesClose()
    {
        data.WriteNote("projects", "# projects\n");
        data.WriteFile(Path.Combine("projects", "Plan.md"), "# Plan\nthe Budget 2027 line\nbudget again\n");
        data.WriteNote("bob", "budget\n");
        data.WriteNote("Carl", "nothing\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, " budget ")
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));

        cut.WaitForAssertion(() => Assert.Equal(["bob", "projects/Plan"], NoteNames(cut)), Wait);
        Assert.Equal("Search: budget", cut.Find("h2").TextContent);
        Assert.Equal("Notes containing this text", cut.Find("section.search-panel").GetAttribute("aria-label"));
        Assert.Equal("projects/Plan", cut.FindAll(".search-hits button")[1].GetAttribute("title"));
        AngleSharp.Dom.IElement line = cut.FindAll(".search-hits .line")[1];
        Assert.Equal("the Budget 2027 line", line.TextContent);
        Assert.Equal("the Budget 2027 line", line.GetAttribute("title"));
        Assert.True(line.ClassList.Contains("grey"));
        Assert.True(line.ClassList.Contains("cut"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".search-hits button")[1].ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["projects/Plan"], opened);
        Assert.Equal(1, closed);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // AC-10: at most 100 rows, then the grey "… n more" line.
    [Fact]
    public async Task Render_ManyHits_CappedWithMoreLine()
    {
        for (int i = 0; i < SearchPanel.MaxRows + 3; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Equal("n099", NoteNames(cut)[^1]);
        Assert.Equal("… 3 more", cut.Find("p.more").TextContent);
        Assert.True(cut.Find("p.more").ClassList.Contains("grey"));
    }

    // AC-10: exactly 100 hits has no more line.
    [Fact]
    public async Task Render_HundredHits_NoMoreLine()
    {
        for (int i = 0; i < SearchPanel.MaxRows; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Empty(cut.FindAll("p.more"));
    }

    // AC-10: no hit shows the grey line.
    [Fact]
    public async Task Render_NoMatch_GreyLine()
    {
        data.WriteNote("Plan", "something\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("nothing here");

        cut.WaitForAssertion(() => Assert.Equal("No notes contain nothing here.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-10: a context.db that is not a database shows the red line and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine()
    {
        data.WriteNote("Plan", "budget\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the search index.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10, 207 AC-4: a blank text is the grey hint line, and nothing is read or written.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Render_Blank_GreyHint_NothingRead(string text)
    {
        data.WriteNote("Plan", "budget\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<SearchPanel> cut = Render(text);

        cut.WaitForAssertion(() => Assert.Equal(
            "Type text or a tag filter like #project and not #done.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Equal("Search", cut.Find("h2").TextContent);
        Assert.Empty(cut.FindAll("button.search-save"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10, 207 AC-1: too short and too long texts (a tag filter too) are red lines, and nothing is read or written.
    [Theory]
    [InlineData(" ab ", "Type at least 3 characters to search.")]
    [InlineData(null, "Search text can be at most 200 characters.")]
    [InlineData("#tag-long", "Search text can be at most 200 characters.")]
    public async Task Render_BadText_RedLine_NothingRead(string? text, string expected)
    {
        data.WriteNote("Plan", "budget\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        string shown = text switch
        {
            null => new string('x', 201),
            "#tag-long" => "#a" + string.Concat(Enumerable.Repeat(" and #a", 29)),
            _ => text,
        };

        IRenderedComponent<SearchPanel> cut = Render(shown);

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("button.search-save"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-10: after the state changed the panel shows the new hit without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewHits()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("budget");
        cut.WaitForAssertion(() => Assert.Equal("No notes contain budget.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("Plan", "the budget\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
    }

    // AC-10: Loading… before the first result; a result for an older query is dropped; the load runs under indexGate.
    [Fact]
    public async Task QueryChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("Plan", "alpha\n");
        data.WriteNote("Review", "omega\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<SearchPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("alpha");
            cut.Render(p => p.Add(c => c.Text, "omega"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
        typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
    }

    // AC-10: names, lines and the query from disk or the user are text, never markup; controls become U+FFFD.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("<b>x<b>", "say <img src=x>\u0007 here\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("<img src=x>");

        cut.WaitForAssertion(() => Assert.Equal(["<b>x<b>"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img"));
        Assert.Equal("Search: <img src=x>", cut.Find("h2").TextContent);
        Assert.Equal("say <img src=x>� here", cut.Find(".search-hits .line").TextContent);
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    // AC-10, 207 AC-5: the panel reads through the state; its only action is SaveSearch.
    [Fact]
    public void Source_CallsOnlySaveSearch()
    {
        string root = RepoFiles.Root();
        string source = File.ReadAllText(Path.Combine(root, "src", "Context.Desktop", "SearchPanel.razor"));

        Assert.Equal(
            ["Actions.SaveSearch("],
            Regex.Matches(source, @"\bActions\.\w+\(").Select(m => m.Value).Distinct().ToArray());
        Assert.Contains("State.Search(", source, StringComparison.Ordinal);
        Assert.Contains("State.TagFilter(", source, StringComparison.Ordinal);
    }

    // 207 AC-4: the input: text, labelled, placeholder, at most 200 characters, holding the current text.
    [Fact]
    public async Task Input_Attributes_HoldText()
    {
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Equal("No notes contain budget.", cut.Find("p[role=status]").TextContent), Wait);
        AngleSharp.Dom.IElement input = cut.Find("input.search-input");
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("Search", input.GetAttribute("aria-label"));
        Assert.Equal("Search notes", input.GetAttribute("placeholder"));
        Assert.Equal("200", input.GetAttribute("maxlength"));
        Assert.Equal("budget", input.GetAttribute("value"));
    }

    // 207 AC-4: Return shows the typed text's results; Esc closes the panel.
    [Fact]
    public async Task Input_Return_Searches_Escape_Closes()
    {
        data.WriteNote("Plan", "the budget\n");
        data.WriteNote("Tagged", "#work\n");
        await data.RefreshAsync();
        int closed = 0;
        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, string.Empty)
            .Add(c => c.OnClose, () => closed++));

        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "budget" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        Assert.Equal("Search: budget", cut.Find("h2").TextContent);

        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "#WORK" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal(["Tagged"], NoteNames(cut)), Wait);
        Assert.Equal(0, closed);

        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closed);
    }

    // 207 AC-4: a tag filter lists the matching notes as note buttons, without lines; the save button shows.
    [Fact]
    public async Task Render_TagFilter_ListsMatchingNotes()
    {
        data.WriteNote("beta", "#job1234\n");
        data.WriteNote("Alpha", "#job1234 and more\n");
        data.WriteNote("Done", "#job1234 #done\n");
        data.WriteNote("Other", "#5678\n");
        await data.RefreshAsync();
        List<string> opened = [];
        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, " #job1234 AND NOT #Done ")
            .Add(c => c.OnOpen, (string name) => opened.Add(name)));

        cut.WaitForAssertion(() => Assert.Equal(["Alpha", "beta"], NoteNames(cut)), Wait);
        Assert.Equal("Search: #job1234 AND NOT #Done", cut.Find("h2").TextContent);
        Assert.Empty(cut.FindAll(".search-hits .line"));
        Assert.All(cut.FindAll(".search-hits button"), b => Assert.True(b.ClassList.Contains("note-button")));
        Assert.Equal("Save search", cut.Find("button.search-save").TextContent);

        await cut.FindAll(".search-hits button")[1].ClickAsync(new MouseEventArgs());
        Assert.Equal(["beta"], opened);
    }

    // 207 AC-4: a tag filter shows at most 100 notes, then the grey "… n more" line.
    [Fact]
    public async Task Render_TagFilter_ManyNotes_CappedWithMoreLine()
    {
        for (int i = 0; i < SearchPanel.MaxRows + 2; i++)
        {
            data.WriteNote($"n{i:D3}", "#shared\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("#shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Equal("n099", NoteNames(cut)[^1]);
        Assert.Equal("… 2 more", cut.Find("p.more").TextContent);
    }

    // 207 AC-4: no match is grey with the filter; an invalid filter is red, with no save button.
    [Theory]
    [InlineData("#nothing", "No notes match #nothing.", "grey", true)]
    [InlineData("#a or #b", "Type a tag filter like #project and not #done.", "red", false)]
    [InlineData("not #a", "Type a tag filter like #project and not #done.", "red", false)]
    [InlineData("#a and (#b)", "Type a tag filter like #project and not #done.", "red", false)]
    public async Task Render_TagFilter_StatusLines(string text, string expected, string css, bool saveable)
    {
        data.WriteNote("Plan", "#a #b\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render(text);

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains(css));
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal(saveable, cut.FindAll("button.search-save").Count == 1);
    }

    // 207 AC-4: a tag filter over a context.db that is not a database shows the red line.
    [Fact]
    public async Task Render_TagFilter_BadIndex_RedLine()
    {
        data.WriteNote("Plan", "#a\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("#a");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the search index.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("button.search-save"));
    }

    // 207 AC-5: Save search opens the focused name form; Return saves the trimmed query and shows the accent line.
    [Fact]
    public async Task Save_Return_SavesTrimmedQuery_AccentLine()
    {
        data.WriteNote("Plan", "budget\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("  budget  ");
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);

        await cut.Find("button.search-save").ClickAsync(new MouseEventArgs());

        AngleSharp.Dom.IElement name = cut.Find("input.search-save-name");
        Assert.Equal("text", name.GetAttribute("type"));
        Assert.Equal("Search name", name.GetAttribute("aria-label"));
        Assert.Equal("Search name", name.GetAttribute("placeholder"));
        Assert.Equal("50", name.GetAttribute("maxlength"));
        Assert.Empty(cut.FindAll("button.search-save"));
        cut.WaitForAssertion(() => Assert.Equal(
            name.GetAttribute("blazor:elementReference"),
            ((ElementReference)data.Bunit.JSInterop.Invocations["Blazor._internal.domWrapper.focus"][^1].Arguments[0]!).Id), Wait);

        await cut.Find("input.search-save-name").InputAsync(new ChangeEventArgs { Value = "  My   budget " });
        await cut.Find("input.search-save-name").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("Saved search My budget.", cut.Find("p.search-save-result").TextContent), Wait);
        AngleSharp.Dom.IElement result = cut.Find("p.search-save-result");
        Assert.True(result.ClassList.Contains("accent"));
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Empty(cut.FindAll("input.search-save-name"));
        SavedSearch saved = Assert.Single(new SettingsStore(data.Root).ReadSavedSearches());
        Assert.Equal("My budget", saved.Name);
        Assert.Equal("budget", saved.Query);
    }

    // 207 AC-5: Esc hides the form and saves nothing.
    [Fact]
    public async Task Save_Escape_HidesForm_SavesNothing()
    {
        data.WriteNote("Plan", "#a\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("#a");
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await cut.Find("button.search-save").ClickAsync(new MouseEventArgs());
        await cut.Find("input.search-save-name").InputAsync(new ChangeEventArgs { Value = "Mine" });
        await cut.Find("input.search-save-name").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("input.search-save-name"));
        Assert.Single(cut.FindAll("button.search-save"));
        Assert.Empty(cut.FindAll("p.search-save-result"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
    }

    // 207 AC-5: a refusal shows the action's text in red and keeps the form.
    [Fact]
    public async Task Save_Refused_RedLine_FormKept()
    {
        data.WriteNote("Plan", "#a\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("#a");
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);

        await cut.Find("button.search-save").ClickAsync(new MouseEventArgs());
        await cut.Find("input.search-save-name").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(
            DesktopActions.SearchNameBlankText, cut.Find("p.search-save-result").TextContent), Wait);
        Assert.True(cut.Find("p.search-save-result").ClassList.Contains("red"));
        Assert.Single(cut.FindAll("input.search-save-name"));
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
    }

    // 207 AC-9: a tag filter reloads after State.Changed, so a note given #done leaves it.
    [Fact]
    public async Task TagFilter_StateChanged_Reloads()
    {
        data.WriteNote("Plan", "#job1234\n");
        data.WriteNote("Review", "#job1234\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render("#job1234 and not #done");
        cut.WaitForAssertion(() => Assert.Equal(["Plan", "Review"], NoteNames(cut)), Wait);

        data.WriteNote("Plan", "#job1234 #done\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
    }

    private IRenderedComponent<SearchPanel> Render(string text) =>
        data.Bunit.Render<SearchPanel>(p => p.Add(c => c.Text, text));

    private static string[] NoteNames(IRenderedComponent<SearchPanel> cut) =>
        cut.FindAll(".search-hits button").Select(b => b.TextContent).ToArray();
}
