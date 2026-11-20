using System.Globalization;
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
            .Add(c => c.OnOpen, ((string Note, string Text) hit) => opened.Add(hit.Note))
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
        // 295: the only status line is the count above the list.
        Assert.Equal("2 notes found", Assert.Single(cut.FindAll("p[role=status]")).TextContent);

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
            .Add(c => c.OnOpen, ((string Note, string Text) hit) => opened.Add(hit.Note)));

        cut.WaitForAssertion(() => Assert.Equal(["Alpha", "beta"], NoteNames(cut)), Wait);
        Assert.Equal("Search: #job1234 AND NOT #Done", cut.Find("h2").TextContent);
        Assert.Empty(cut.FindAll(".search-hits .line"));
        Assert.All(cut.FindAll(".search-hits button"), b => Assert.True(b.ClassList.Contains("note-button")));
        Assert.Equal("Save search", cut.Find("button.search-save").TextContent);

        await cut.FindAll(".search-hits button")[1].ClickAsync(new MouseEventArgs());
        Assert.Equal(["beta"], opened);
    }

    // 338 AC-6: in text and tag-filter results a person page's button shows only the person's name, with the full
    // name in its title, and opens the page; other notes, people/Anna/Notes too, keep their full name.
    [Theory]
    [InlineData("budget")]
    [InlineData("#money")]
    public async Task Render_PersonPage_ShowsPersonName_FullNameInTitle(string query)
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\nbudget #money\n");
        data.WriteFile(Path.Combine("people", "Anna", "Notes.md"), "# Notes\nbudget #money\n");
        data.WriteNote("Plan", "budget #money\n");
        await data.RefreshAsync();
        List<string> opened = [];
        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, query)
            .Add(c => c.OnOpen, ((string Note, string Text) hit) => opened.Add(hit.Note)));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find("section.search-panel").TextContent, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("p.search-count"));
            Assert.Equal(3, cut.FindAll(".search-hits button.note-button").Count);
        }, Wait);
        Dictionary<string, string> shown = cut.FindAll(".search-hits button.note-button")
            .ToDictionary(b => b.GetAttribute("title")!, b => b.TextContent);
        Assert.Equal("Anna", shown["people/Anna"]);
        Assert.Equal("people/Anna/Notes", shown["people/Anna/Notes"]);
        Assert.Equal("Plan", shown["Plan"]);

        await cut.FindAll(".search-hits button.note-button").Single(b => b.GetAttribute("title") == "people/Anna").ClickAsync(new MouseEventArgs());
        Assert.Equal(["people/Anna"], opened);
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

    // 285 AC-3: a text hit's row: name button, grey types and date (in the state's zone), and the line with the match
    // marked in its own case; no type span without types.
    [Fact]
    public async Task Render_Hit_TypeDateAndMarkedLine()
    {
        data.WriteNote("Plan", "# Plan\ntype:: Project\ntype:: Launch\nthe BuDGet line\n");
        data.WriteNote("Plain", "a budget here\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), new DateTime(2026, 3, 4, 23, 30, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plain.md"), new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc));
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Equal(["Plain", "Plan"], NoteNames(cut)), Wait);
        AngleSharp.Dom.IElement plan = cut.FindAll(".search-hits li")[1];
        Assert.Equal("Plan", plan.QuerySelector(".note-button")!.GetAttribute("title"));
        AngleSharp.Dom.IElement type = plan.QuerySelector("span.type")!;
        Assert.Equal("project, launch", type.TextContent);
        Assert.Equal("project, launch", type.GetAttribute("title"));
        Assert.True(type.ClassList.Contains("grey"));
        AngleSharp.Dom.IElement date = plan.QuerySelector("span.date")!;
        Assert.Equal("2026-03-05", date.TextContent);
        Assert.True(date.ClassList.Contains("grey"));
        AngleSharp.Dom.IElement line = plan.QuerySelector("span.line")!;
        Assert.Equal("the BuDGet line", line.TextContent);
        Assert.Equal("the BuDGet line", line.GetAttribute("title"));
        Assert.Equal("BuDGet", line.QuerySelector("mark")!.TextContent);
        Assert.Equal("the <mark>BuDGet</mark> line", line.InnerHtml);

        AngleSharp.Dom.IElement plain = cut.FindAll(".search-hits li")[0];
        Assert.Null(plain.QuerySelector("span.type"));
        Assert.Equal("2026-03-04", plain.QuerySelector("span.date")!.TextContent);
    }

    // 285 AC-3: over 40 characters before the hit, the snippet starts with "…" and the last 40; a surrogate pair at the
    // cut is never split.
    [Fact]
    public async Task Render_LongPrefix_CutWithEllipsis_KeepsSurrogatePair()
    {
        string forty = new string('a', 39) + "b";
        data.WriteNote("Long", "x" + new string('y', 9) + forty + "budget end\n");
        data.WriteNote("Pair", "\U0001F600" + new string('c', 39) + "budget\n");
        data.WriteNote("Short", new string('d', 40) + "budget\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Equal(["Long", "Pair", "Short"], NoteNames(cut)), Wait);
        IReadOnlyList<AngleSharp.Dom.IElement> lines = cut.FindAll(".search-hits .line");
        Assert.Equal("…" + forty + "<mark>budget</mark> end", lines[0].InnerHtml);
        // The last 40 would start with the pair's low half, so the cut moves past the whole pair.
        Assert.Equal("…" + new string('c', 39) + "<mark>budget</mark>", lines[1].InnerHtml);
        Assert.Equal(new string('d', 40) + "<mark>budget</mark>", lines[2].InnerHtml);
        Assert.Equal("x" + new string('y', 9) + forty + "budget end", lines[0].GetAttribute("title"));
    }

    // 285 AC-3: the split itself, on the raw line.
    [Fact]
    public void SnippetOf_SplitsAroundTheMatch()
    {
        SearchPanel.Snippet s = SearchPanel.SnippetOf(new SearchHit("n", 1, "ab\u0007CdE fg", 3, [], null), 3);
        Assert.Equal("ab\uFFFD", s.Before);
        Assert.Equal("CdE", s.Match);
        Assert.Equal(" fg", s.After);
    }

    // 285 AC-3: tag-filter rows stay name-only.
    [Fact]
    public async Task Render_TagFilter_RowsNameOnly()
    {
        data.WriteNote("Plan", "type:: project\n#job1234\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("#job1234");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll(".search-hits .type, .search-hits .date, .search-hits .line, .search-hits mark"));
    }

    // 285 AC-4: markup, control and bidi characters in names, types and lines render as text.
    [Fact]
    public async Task Render_HostileTypeAndLine_AsText()
    {
        data.WriteNote("<i>n<i>\u202E", "type:: <b>t</b>\u202E\u0007\n<img src=x>\u202Ebudget<script>x</script>\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("budget");

        cut.WaitForAssertion(() => Assert.Single(NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("i, b, img, script"));
        Assert.Contains("<b>t</b>", cut.Find(".search-hits .type").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("<img src=x>", cut.Find(".search-hits .line").TextContent, StringComparison.Ordinal);
        Assert.Equal("budget", cut.Find(".search-hits mark").TextContent);
        Assert.EndsWith("<script>x</script>", cut.Find(".search-hits .line").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);

        // The name shows bidi controls as placeholders (NoteName); in the type and the line they stay text inside their
        // own span, which app.css isolates (unicode-bidi: isolate), so they can't reorder the rest of the row.
        Assert.Equal("<i>n<i>\uFFFD", cut.Find(".search-hits .note-button").TextContent);
        Assert.Equal("<b>t</b>\u202E\uFFFD", cut.Find(".search-hits .type").TextContent);
        Assert.Equal("<img src=x>\u202E", cut.Find(".search-hits .line").ChildNodes[0].TextContent);
    }

    // 285 AC-5: ArrowDown in the input focuses the first row; the arrows move the one tab stop, stopping at the last;
    // ArrowUp on the first goes back to the input.
    [Theory]
    [InlineData("budget")]
    [InlineData("#job1234")]
    public async Task Keyboard_RovingTabIndex(string text)
    {
        data.WriteNote("A", "budget #job1234\n");
        data.WriteNote("B", "budget #job1234\n");
        data.WriteNote("C", "budget #job1234\n");
        await data.RefreshAsync();
        IRenderedComponent<SearchPanel> cut = Render(text);
        cut.WaitForAssertion(() => Assert.Equal(["A", "B", "C"], NoteNames(cut)), Wait);
        Assert.Equal(["0", "-1", "-1"], TabIndexes(cut));

        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        AssertFocusedRow(cut, 0, 1);

        await cut.FindAll(".search-hits .note-button")[0].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(["-1", "0", "-1"], TabIndexes(cut));
        AssertFocusedRow(cut, 1, 1);
        await cut.FindAll(".search-hits .note-button")[1].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await cut.FindAll(".search-hits .note-button")[2].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(["-1", "-1", "0"], TabIndexes(cut));
        AssertFocusedRow(cut, 2, 2);

        await cut.FindAll(".search-hits .note-button")[2].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        await cut.FindAll(".search-hits .note-button")[1].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal(["0", "-1", "-1"], TabIndexes(cut));
        AssertFocusedRow(cut, 0, 2);

        int inputFocus = FocusCount(cut, ".search-input");
        await cut.FindAll(".search-hits .note-button")[0].KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.WaitForAssertion(() => Assert.Equal(inputFocus + 1, FocusCount(cut, ".search-input")), Wait);
        Assert.Equal(["0", "-1", "-1"], TabIndexes(cut));
    }

    // 285 AC-5, AC-7: Enter opens the row once (the click the browser sends after it does nothing) with the shown
    // query; a later click opens it again; a query typed and run with Return is the one reported.
    [Fact]
    public async Task Enter_OpensOnce_WithShownQuery()
    {
        data.WriteNote("A", "budget money\n");
        data.WriteNote("B", "budget\n");
        await data.RefreshAsync();
        List<(string Note, string Text)> opened = [];
        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, "budget")
            .Add(c => c.OnOpen, ((string Note, string Text) hit) => opened.Add(hit)));
        cut.WaitForAssertion(() => Assert.Equal(["A", "B"], NoteNames(cut)), Wait);

        await cut.FindAll(".search-hits .note-button")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await cut.FindAll(".search-hits .note-button")[1].ClickAsync(new MouseEventArgs());
        Assert.Equal([("B", "budget")], opened);
        await cut.FindAll(".search-hits .note-button")[1].KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });
        await cut.FindAll(".search-hits .note-button")[1].ClickAsync(new MouseEventArgs());
        Assert.Equal([("B", "budget"), ("B", "budget")], opened);

        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "money" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal(["A"], NoteNames(cut)), Wait);
        await cut.Find(".search-hits .note-button").ClickAsync(new MouseEventArgs());
        Assert.Equal(("A", "money"), opened[^1]);
    }

    // 285 AC-7: Focus makes that note's row the tab stop and focuses it; a note no longer listed gives the first row.
    [Theory]
    [InlineData("C", 2)]
    [InlineData("Gone", 0)]
    public async Task Focus_FocusesThatRow_OrTheFirst(string focus, int row)
    {
        data.WriteNote("A", "budget\n");
        data.WriteNote("B", "budget\n");
        data.WriteNote("C", "budget\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = data.Bunit.Render<SearchPanel>(p => p
            .Add(c => c.Text, "budget")
            .Add(c => c.Focus, focus));

        cut.WaitForAssertion(() => Assert.Equal(["A", "B", "C"], NoteNames(cut)), Wait);
        string[] expected = ["-1", "-1", "-1"];
        expected[row] = "0";
        Assert.Equal(expected, TabIndexes(cut));
        AssertFocusedRow(cut, row, 1);
        Assert.Equal(0, FocusCount(cut, ".search-input"));
    }

    private static string[] TabIndexes(IRenderedComponent<SearchPanel> cut) =>
        cut.FindAll(".search-hits .note-button").Select(b => b.GetAttribute("tabindex") ?? "").ToArray();

    // Waits until the row's button was given the focus that many times.
    private void AssertFocusedRow(IRenderedComponent<SearchPanel> cut, int row, int times) =>
        cut.WaitForAssertion(
            () => Assert.Equal(times, FocusCount(cut, $".search-hits li:nth-child({row + 1}) .note-button")),
            Wait);

    // Counts the focus calls on the element the panel holds in that field (a row button or the input).
    private int FocusCount(IRenderedComponent<SearchPanel> cut, string selector)
    {
        ElementReference target = selector == ".search-input"
            ? (ElementReference)Field(cut, "input")
            : ((ElementReference[])Field(cut, "rowButtons"))[int.Parse(selector[(selector.IndexOf('(') + 1)..selector.IndexOf(')')], CultureInfo.InvariantCulture) - 1];
        Assert.False(string.IsNullOrEmpty(target.Id));
        return data.Bunit.JSInterop.Invocations.Count(i =>
            i.Identifier == "Blazor._internal.domWrapper.focus" && i.Arguments[0] is ElementReference r && r.Id == target.Id);
    }

    private static object Field(IRenderedComponent<SearchPanel> cut, string name) =>
        typeof(SearchPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cut.Instance)!;

    // 295 AC-9: with hits, a grey role=status count line comes before the labelled list: the full count, not capped.
    [Theory]
    [InlineData(1, "1 note found")]
    [InlineData(3, "3 notes found")]
    [InlineData(103, "103 notes found")]
    public async Task Render_Hits_CountLineBeforeLabelledList(int count, string expected)
    {
        for (int i = 0; i < count; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(Math.Min(count, SearchPanel.MaxRows), NoteNames(cut).Length), Wait);
        AngleSharp.Dom.IElement line = cut.Find("p.search-count");
        Assert.Equal(expected, line.TextContent);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.True(line.ClassList.Contains("grey"));
        AngleSharp.Dom.IElement list = cut.Find("ul.search-hits");
        Assert.Equal("Search results", list.GetAttribute("aria-label"));
        Assert.True(line.NextElementSibling!.ClassList.Contains("search-hits"));
    }

    // 295 AC-9: a tag filter's results have the count line and the labelled list too.
    [Fact]
    public async Task Render_TagFilter_CountLine()
    {
        data.WriteNote("Alpha", "#shared\n");
        data.WriteNote("beta", "#shared\n");
        data.WriteNote("Other", "#other\n");
        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("#shared");

        cut.WaitForAssertion(() => Assert.Equal(["Alpha", "beta"], NoteNames(cut)), Wait);
        AngleSharp.Dom.IElement line = cut.Find("p.search-count");
        Assert.Equal("2 notes found", line.TextContent);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Equal("Search results", cut.Find("ul.search-hits").GetAttribute("aria-label"));
    }

    // 295 AC-9: blank, no matches and an unreadable index have their own status line and no count line.
    [Theory]
    [InlineData("", "Type text or a tag filter like #project and not #done.", false)]
    [InlineData("nothing here", "No notes contain nothing here.", false)]
    [InlineData("budget", "Can't read the search index.", true)]
    public async Task Render_NoHits_NoCountLine(string text, string status, bool badIndex)
    {
        data.WriteNote("Plan", "budget\n");
        if (badIndex)
        {
            data.WriteFile(TagIndex.FileName, NotADatabase);
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render(text);

        cut.WaitForAssertion(() => Assert.Equal(status, cut.Find("p[role=status]").TextContent), Wait);
        Assert.Empty(cut.FindAll("p.search-count"));
        Assert.Single(cut.FindAll("p[role=status]"));
    }

    // 295 AC-6: 250 hits show 100 rows, then "… 150 more"; the count line gives all 250.
    [Fact]
    public async Task Render_250Hits_HundredRowsAndMoreLine()
    {
        for (int i = 0; i < 250; i++)
        {
            data.WriteNote($"n{i:D3}", "shared word\n");
        }

        await data.RefreshAsync();

        IRenderedComponent<SearchPanel> cut = Render("shared");

        cut.WaitForAssertion(() => Assert.Equal(100, NoteNames(cut).Length), Wait);
        Assert.Equal("n000", NoteNames(cut)[0]);
        Assert.Equal("n099", NoteNames(cut)[^1]);
        Assert.Equal("… 150 more", cut.Find("p.more").TextContent);
        Assert.Equal("250 notes found", cut.Find("p.search-count").TextContent);
    }

    private IRenderedComponent<SearchPanel> Render(string text) =>
        data.Bunit.Render<SearchPanel>(p => p.Add(c => c.Text, text));

    private static string[] NoteNames(IRenderedComponent<SearchPanel> cut) =>
        cut.FindAll(".search-hits button").Select(b => b.TextContent).ToArray();
}
