using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class NotesPanelTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // 240 AC-1: the stored entries that name an existing note, newest first; an entry naming no note (deleted or
    // renamed outside the app) is left out. 192 AC-4: rows show no date.
    [Fact]
    public async Task Render_Recent_StoredOrder_MissingLeftOut_NoDate()
    {
        data.WriteNote("beta", "b");
        data.WriteNote("Alpha", "a");
        data.WriteNote("Gamma", "c");
        data.WriteRecent("beta", "Gone", "Alpha", "alpha");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.note");
        Assert.Equal(["beta", "Alpha"], items.Select(i => i.QuerySelector(".name")!.TextContent).ToArray());
        Assert.Equal("beta", items[0].QuerySelector(".name")!.GetAttribute("title"));
        Assert.Equal("Recent notes", Assert.Single(cut.FindAll("ul.note-list")).GetAttribute("aria-label"));
        Assert.True(cut.Find("ul.note-list").ClassList.Contains("recent-notes"));
        Assert.Empty(cut.FindAll("li.note .time"));
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}", cut.Find(".note-list").TextContent);
    }

    // 192 AC-4, 240 AC-1 / AC-3: a row is the name button, then locked for a sensitive note, without a time; there
    // is no toggle and no nested list, even for a note with subpages.
    [Fact]
    public async Task Row_NameThenLocked_NoToggle_NoTime()
    {
        data.WriteNote("Plan", "p");
        data.WriteFile(Path.Combine("Plan", "Sub.md"), "s");
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "x");
        data.WriteRecent("Plan", "Secret", "Plan/Sub");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IReadOnlyList<IElement> rows = cut.FindAll("li.note");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["name"], rows[0].Children.Select(c => c.ClassList.First()).ToArray());
        Assert.Equal(["name", "locked"], rows[1].Children.Select(c => c.ClassList.First()).ToArray());
        Assert.Equal("locked", rows[1].QuerySelector(".locked")!.TextContent);
        Assert.Equal(["name"], rows[2].Children.Select(c => c.ClassList.First()).ToArray());
        Assert.All(rows, r => Assert.True(r.QuerySelector("button.name")!.ClassList.Contains("cut")));
        Assert.All(rows, r => Assert.Equal("button", r.QuerySelector("button.name")!.GetAttribute("type")));
        Assert.Empty(cut.FindAll("button.toggle"));
        Assert.Empty(cut.FindAll("ul.note-children"));
        Assert.Empty(cut.FindAll("li.note .time"));
    }

    // 197 AC-1, AC-3: the bar holds only the icon buttons; the list keeps its label.
    [Fact]
    public async Task Bar_OnlyIconButtons_NoLogo_ListLabelled()
    {
        data.WriteNote("Plan", "p");
        data.WriteFile(Path.Combine("Plan", "Sub.md"), "s");
        data.WriteRecent("Plan/Sub", "Plan");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IElement bar = cut.Find(".notes-bar");
        Assert.Single(bar.Children);
        Assert.True(bar.Children[0].ClassList.Contains("notes-bar-buttons"));
        Assert.Empty(bar.QuerySelectorAll("svg.logo"));
        Assert.All(bar.QuerySelectorAll("svg"), s => Assert.NotNull(s.Closest(".notes-bar-buttons")));
        Assert.DoesNotContain(cut.FindAll("h2"), h => h.TextContent.Contains("Notes"));
        Assert.DoesNotContain("Notes", bar.TextContent);
        Assert.Equal("Recent notes", Assert.Single(cut.FindAll("ul.note-list")).GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll("ul.note-children"));
        AssertBarButtons(bar);
    }

    // 197 AC-1: the same without notes.
    [Fact]
    public async Task Bar_NoNotes_OnlyIconButtons()
    {
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal("No notes yet", cut.Find(".notes p").TextContent);
        IElement bar = cut.Find(".notes-bar");
        Assert.Single(bar.Children);
        Assert.Empty(bar.QuerySelectorAll("svg.logo"));
        AssertBarButtons(bar);
    }

    private static void AssertBarButtons(IElement bar)
    {
        IElement[] buttons = bar.QuerySelectorAll(".notes-bar-buttons button").ToArray();
        Assert.Equal(
            ["triage-open", "people-open", "tags-open", "settings-open"], buttons.Select(b => b.ClassList.First()).ToArray());
        Assert.Equal(["Triage", "People", "Tags", "Settings"], buttons.Select(b => b.GetAttribute("aria-label")!).ToArray());
    }

    // 192 AC-1, AC-2: icon buttons with names, typed, svg child, no visible text, safe decorative svg.
    [Theory]
    [InlineData("triage-open", "Triage")]
    [InlineData("people-open", "People")]
    [InlineData("tags-open", "Tags")]
    [InlineData("settings-open", "Settings")]
    public async Task BarButtons_AreIconOnly_WithSafeSvg(string cls, string label)
    {
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IElement button = cut.Find($".notes-bar-buttons button.{cls}");
        Assert.Equal(label, button.GetAttribute("aria-label"));
        Assert.Equal(label, button.GetAttribute("title"));
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal(string.Empty, button.TextContent.Trim());
        IElement svg = Assert.Single(button.QuerySelectorAll("svg"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        string markup = svg.OuterHtml;
        foreach (string banned in new[] { "<script", "href", "src=", "url(", "style" })
        {
            Assert.DoesNotContain(banned, markup, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("currentColor", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("#", markup, StringComparison.Ordinal);
    }

    // 218 AC-2: Search moved to the top bar; the sidebar has no Search button.
    [Fact]
    public async Task Bar_HasNoSearchButton()
    {
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("button.search-open"));
    }

    // 207 AC-8: the saved searches sit between the bar and the note list: a cut name with the query as title, and an
    // icon remove button; clicking an entry asks for its query.
    [Fact]
    public async Task SavedSearches_BetweenBarAndList_ClickAsksForQuery()
    {
        data.WriteNote("Plan", "p");
        data.WriteFile(
            SettingsStore.FileName,
            "{\"x\":1,\"savedSearches\":[{\"name\":\"Open  job1234\",\"query\":\"#job1234 and not #done\"},"
            + "{\"name\":\"<b>Budget</b>\",\"query\":\"budget\"}]}");
        data.WriteRecent("Plan");
        List<string> searched = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnSearch, (string text) => searched.Add(text)));

        IElement nav = cut.Find("nav.saved-searches");
        Assert.Equal("Saved searches", nav.GetAttribute("aria-label"));
        IElement[] order = cut.Find("section.notes").Children.ToArray();
        int navAt = Array.FindIndex(order, e => e.ClassList.Contains("saved-searches"));
        Assert.Equal(1, navAt);
        Assert.True(order[0].ClassList.Contains("notes-bar"));
        Assert.True(order[2].ClassList.Contains("note-list"));
        IElement[] entries = nav.QuerySelectorAll("button.saved-search").ToArray();
        Assert.Equal(["Open job1234", "<b>Budget</b>"], entries.Select(b => b.TextContent).ToArray());
        Assert.Empty(nav.QuerySelectorAll("b"));
        Assert.Equal(["#job1234 and not #done", "budget"], entries.Select(b => b.GetAttribute("title")!).ToArray());
        Assert.All(entries, b => Assert.True(b.ClassList.Contains("cut")));
        IElement[] removes = nav.QuerySelectorAll("button.saved-search-remove").ToArray();
        Assert.Equal(["Remove Open job1234", "Remove <b>Budget</b>"], removes.Select(b => b.GetAttribute("aria-label")!).ToArray());
        Assert.Equal(["Remove Open job1234", "Remove <b>Budget</b>"], removes.Select(b => b.GetAttribute("title")!).ToArray());
        Assert.All(removes, b => Assert.Equal("×", b.TextContent));
        Assert.All(nav.QuerySelectorAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await entries[0].ClickAsync(new MouseEventArgs());

        Assert.Equal(["#job1234 and not #done"], searched);
    }

    // 207 AC-8: no entries (no file, no property, an empty list) shows no nav.
    [Theory]
    [InlineData(null)]
    [InlineData("{\"dayEnd\":{}}")]
    [InlineData("{\"savedSearches\":[]}")]
    public async Task SavedSearches_None_NoNav(string? json)
    {
        data.WriteNote("Plan", "p");
        if (json is not null)
        {
            data.WriteFile(SettingsStore.FileName, json);
        }

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("nav.saved-searches"));
        Assert.Empty(cut.FindAll(".saved-searches-unreadable"));
    }

    // 207 AC-8: an unreadable settings.json shows the red line instead of the list.
    [Theory]
    [InlineData("{")]
    [InlineData("{\"savedSearches\":{}}")]
    [InlineData("{\"savedSearches\":[{\"name\":\"a\"}]}")]
    public async Task SavedSearches_Unreadable_RedLine(string json)
    {
        data.WriteNote("Plan", "p");
        data.WriteFile(SettingsStore.FileName, json);
        data.WriteRecent("Plan");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("nav.saved-searches"));
        IElement line = cut.Find(".notes > p.saved-searches-unreadable");
        Assert.Equal("Can't read saved searches.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Single(cut.FindAll("ul.note-list"));
    }

    // 207 AC-9: remove takes the entry out (grey line); a name no longer stored is refused in red.
    [Fact]
    public async Task SavedSearches_Remove_GreyLine_GoneIsRed()
    {
        data.WriteNote("Plan", "p");
        data.WriteFile(
            SettingsStore.FileName,
            "{\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"},{\"name\":\"Home\",\"query\":\"#home\"}]}");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.saved-search-remove[aria-label='Remove Work']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            ["Home"], cut.FindAll("button.saved-search").Select(b => b.TextContent).ToArray()), Wait);
        cut.WaitForAssertion(() => Assert.Equal(
            "Removed saved search Work.", cut.Find("p.saved-search-result").TextContent), Wait);
        Assert.True(cut.Find("p.saved-search-result").ClassList.Contains("grey"));
        Assert.Equal("status", cut.Find("p.saved-search-result").GetAttribute("role"));
        Assert.Null(cut.Find("p.saved-search-result").Closest("nav"));

        // Removed outside the app, not yet refreshed: the button is still shown.
        data.WriteFile(SettingsStore.FileName, "{\"savedSearches\":[]}");
        await cut.Find("button.saved-search-remove[aria-label='Remove Home']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "That saved search is gone. Nothing was done.", cut.Find("p.saved-search-result").TextContent), Wait);
        Assert.True(cut.Find("p.saved-search-result").ClassList.Contains("red"));
    }

    // AC-4: an empty folder shows the empty state.
    [Fact]
    public async Task Render_NoNotes_EmptyLine()
    {
        Directory.CreateDirectory(data.Root);

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IElement line = cut.Find("p");
        Assert.Equal("No notes yet", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
    }

    // AC-4: a missing data folder shows the empty states (all panels) and is not created.
    [Fact]
    public async Task Render_MissingFolder_EmptyAndNotCreated()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("No notes yet", cut.Find(".notes p").TextContent);
        Assert.Equal("No todos yet", cut.Find(".todos p").TextContent);
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-4, 240 AC-6: a listing that throws shows the fixed red line.
    [Fact]
    public async Task Render_ListingThrows_RedLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions only; Windows ACLs would need their own setup.
        }

        Directory.CreateDirectory(data.Root);
        data.WriteNote("Alpha", "a");
        data.WriteRecent("Alpha");
        File.SetUnixFileMode(data.Root, UnixFileMode.UserWrite);
        try
        {
            IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

            IElement line = cut.Find("p");
            Assert.Equal("Can't read the notes folder.", line.TextContent);
            Assert.True(line.ClassList.Contains("red"));
        }
        finally
        {
            File.SetUnixFileMode(
                data.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // AC-5: a hostile note name is encoded text, in the title and in a subpage's parent. (240: recent.json only holds
    // names that pass the note name rules, so a name with a control character or a backslash is never listed.)
    [Fact]
    public async Task Render_HostileName_Encoded()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows file names can't hold < or >.
        }

        const string Hostile = "<script>alert(1)<script> <img src=x onerror=alert(1)>";
        data.WriteNote(Hostile, "x");
        data.WriteFile(Path.Combine(Hostile, "<img src=y>.md"), "y");
        data.WriteRecent(Hostile + "/<img src=y>", Hostile);

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        IReadOnlyList<IElement> names = cut.FindAll(".name");
        Assert.Equal("<img src=y>", Title(names[0]));
        Assert.Equal(Hostile, names[0].QuerySelector(".note-parent")!.TextContent);
        Assert.Equal(Hostile, names[1].TextContent);
        Assert.Equal(Hostile, names[1].GetAttribute("title"));
    }

    // 134 AC-7: the entry row is a plain text field (no @bind) and a New note button.
    [Fact]
    public async Task EntryRow_TextFieldWithoutButton()
    {
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal("text", cut.Find(".note-entry input").GetAttribute("type"));
        Assert.Empty(cut.FindAll("button.note-new"));
        Assert.Equal("New note title", cut.Find(".note-new-title").GetAttribute("placeholder"));
    }

    // 134 AC-7: Enter or the button creates the note, clears the field, opens the note and shows the accent status.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EntryRow_Create_ClearsOpensAndShowsCreated(bool enter)
    {
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnOpen, (string name) => opened.Add(name)));

        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "Plan.md" });
        if (enter)
        {
            await cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(["Plan"], opened);
        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Find(".note-new-title").GetAttribute("value")), Wait);
        IElement status = cut.Find(".note-entry-result");
        Assert.Equal("Created Plan", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        Assert.Equal("status", status.GetAttribute("role"));

        // 240: OnOpen is the test's handler here, so nothing records the open; RecentNotesActionsTests covers the
        // New note field through Main (AC-9).
        cut.WaitForAssertion(() => Assert.Equal("No recently opened notes.", cut.Find(".recent-empty").TextContent), Wait);
    }

    // 134 AC-7: a refusal shows the fixed red text, keeps the typed title and opens nothing.
    [Theory]
    [InlineData("a/b", "A note title can't contain / \\ : or control characters.")]
    [InlineData("alpha", "Note alpha already exists.")]
    public async Task EntryRow_Refused_RedLine(string title, string text)
    {
        data.WriteNote("Alpha", "a");
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnOpen, (string name) => opened.Add(name)));

        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = title });
        await cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        IElement status = cut.Find(".note-entry-result");
        Assert.Equal(text, status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal(title, cut.Find(".note-new-title").GetAttribute("value"));
        Assert.Equal(text, status.GetAttribute("title"));
        Assert.Empty(opened);
        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "x" });
        Assert.Empty(cut.FindAll(".note-entry-result"));
        Assert.Equal("a", File.ReadAllText(Path.Combine(data.Root, "Alpha.md")));
    }

    // 239 AC-3: the People button calls OnPeople once; it changes nothing.
    [Fact]
    public async Task PeopleButton_CallsOnPeople()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        int asked = 0;
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnPeople, () => asked++));

        IElement button = cut.Find(".notes button.people-open");
        Assert.Equal("button", button.GetAttribute("type"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(1, asked);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // 137 AC-1: the Tags button is a typed button that calls OnTags; it changes nothing.
    [Fact]
    public async Task TagsButton_CallsOnTags()
    {
        data.WriteNote("Plan", "#work\n");
        int asked = 0;
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnTags, () => asked++));

        IElement button = cut.Find(".notes button.tags-open");
        Assert.Equal("Tags", button.GetAttribute("aria-label"));
        Assert.Equal("button", button.GetAttribute("type"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(1, asked);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // 138 AC-1: the Settings button is a typed button that calls OnSettings; it changes nothing.
    [Fact]
    public async Task SettingsButton_CallsOnSettings()
    {
        data.WriteNote("Plan", "#work\n");
        int asked = 0;
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnSettings, () => asked++));

        IElement button = cut.Find(".notes button.settings-open");
        Assert.Equal("Settings", button.GetAttribute("aria-label"));
        Assert.Equal("button", button.GetAttribute("type"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(1, asked);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // 139 AC-6: the Import button is a typed button; a cancelled dialog calls no action and shows no text.
    [Fact]
    public async Task Import_Cancelled_NothingHappens()
    {
        data.WriteNote("Plan", "text\n");
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        IElement button = cut.Find(".notes button.import");
        Assert.Equal("Import", button.TextContent);
        Assert.Equal("button", button.GetAttribute("type"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(1, data.Dialog.ImportOpens);
        Assert.Empty(cut.FindAll(".import-result p"));
        Assert.Equal("status", cut.Find(".import-result").GetAttribute("role"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // 139 AC-6: each chosen file gets one line in the chosen order (accent done, red refused); new notes are listed.
    [Fact]
    public async Task Import_SeveralFiles_OneLineEachInOrder()
    {
        string beta = Source("Beta.md", "b\n");
        string missing = data.Outside("Gone.md");
        string alpha = Source("Alpha.md", "a\n");
        data.Dialog.ImportPaths = [beta, missing, alpha];
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, data.Dialog.ImportOpens);
        IElement region = cut.Find(".import-result");
        Assert.Equal("status", region.GetAttribute("role"));
        IReadOnlyList<IElement> lines = cut.FindAll(".import-result p");
        Assert.Equal(
            [
                "Imported Beta. The source is unchanged; delete it if you don't need the plain copy: " + beta,
                "No such file: " + missing,
                "Imported Alpha. The source is unchanged; delete it if you don't need the plain copy: " + alpha,
            ],
            lines.Select(l => l.TextContent).ToArray());
        Assert.Equal([true, false, true], lines.Select(l => l.ClassList.Contains("accent")).ToArray());
        Assert.Equal([false, true, false], lines.Select(l => l.ClassList.Contains("red")).ToArray());
        Assert.All(lines, l => Assert.Equal(l.TextContent, l.GetAttribute("title")));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(["Alpha.md", "Beta.md"], Files().Keys);
        Assert.Equal("b\n", File.ReadAllText(Path.Combine(data.Root, "Beta.md")));
        // 240: importing opens nothing, so the imported notes are not listed as recently opened.
        cut.WaitForAssertion(() => Assert.Equal("No recently opened notes.", cut.Find(".recent-empty").TextContent), Wait);
        Assert.Empty(cut.FindAll("li.note"));
    }

    // 139 AC-6: more than 100 files are refused as a whole.
    [Fact]
    public async Task Import_Over100Files_RefusedNothingImported()
    {
        data.Dialog.ImportPaths = Enumerable.Range(0, 101).Select(i => Source($"N{i}.md", "x\n")).ToList();
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        IElement line = Assert.Single(cut.FindAll(".import-result p"));
        Assert.Equal("Choose at most 100 files.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.False(Directory.Exists(data.Root));
    }

    // 139 AC-6: exactly 100 files are all imported.
    [Fact]
    public async Task Import_100Files_AllImported()
    {
        data.Dialog.ImportPaths = Enumerable.Range(0, 100).Select(i => Source($"N{i}.md", "x\n")).ToList();
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        Assert.Equal(100, cut.FindAll(".import-result p.accent").Count);
        Assert.Equal(100, Files().Count);
    }

    // 139 AC-7: a file that would replace a note is not written on the first pass; one dialog asks, and Confirm
    // replaces exactly that note.
    [Fact]
    public async Task Import_ReplacingFile_AsksThenReplaces()
    {
        data.WriteNote("Plan", "old\n");
        string plan = Source("plan.md", "new\n");
        string other = Source("Other.md", "other\n");
        data.Dialog.ImportPaths = [plan, other];
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        Assert.Equal("old\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("other\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
        IElement dialog = Assert.Single(cut.FindAll("[role=dialog]"));
        Assert.Equal("Replace Plan?", dialog.QuerySelector(".question")!.TextContent);
        Assert.Equal("Replace", dialog.QuerySelector("button.confirm")!.TextContent);
        Assert.StartsWith("Imported Other.", Assert.Single(cut.FindAll(".import-result p")).TextContent, StringComparison.Ordinal);

        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("new\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("other\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
        Assert.Equal(["Other.md", "Plan.md"], Files().Keys);
        IReadOnlyList<IElement> lines = cut.FindAll(".import-result p");
        Assert.Equal(
            ["Replaced Plan. The source is unchanged; delete it if you don't need the plain copy: " + plan],
            lines.Take(1).Select(l => l.TextContent).ToArray());
        Assert.True(lines[0].ClassList.Contains("accent"));
        Assert.StartsWith("Imported Other.", lines[1].TextContent, StringComparison.Ordinal);
        Assert.Equal(1, data.Dialog.ImportOpens);
    }

    // 139 AC-7: several replacing files are asked about together; Cancel or Esc leaves every one of them unchanged.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_ReplacingFiles_CancelOrEsc_LeavesThemUnchanged(bool esc)
    {
        data.WriteNote("Plan", "old plan\n");
        data.WriteNote("Ideas", "old ideas\n");
        data.Dialog.ImportPaths = [Source("Plan.md", "new\n"), Source("Fresh.md", "fresh\n"), Source("Ideas.md", "new\n")];
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        Assert.Equal("Replace 2 notes? Plan, Ideas", cut.Find("[role=dialog] .question").TextContent);
        SortedDictionary<string, string> before = Files();
        if (esc)
        {
            await cut.Find("[role=dialog]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        }

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, Files());
        Assert.Equal("old plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("old ideas\n", File.ReadAllText(Path.Combine(data.Root, "Ideas.md")));
        IReadOnlyList<IElement> lines = cut.FindAll(".import-result p");
        Assert.Equal("Not replaced: Plan", lines[0].TextContent);
        Assert.StartsWith("Imported Fresh.", lines[1].TextContent, StringComparison.Ordinal);
        Assert.Equal("Not replaced: Ideas", lines[2].TextContent);
        Assert.False(lines[0].ClassList.Contains("accent"));
    }

    // 139 AC-11: a path with a control character or a line break is shown on one line with it replaced.
    [Fact]
    public async Task Import_ControlCharacterInThePath_ShownOnOneLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows paths can't hold control characters.
        }

        string folder = data.Outside("odd\u0007\nfolder");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Plan.md");
        File.WriteAllText(source, "text\n");
        data.Dialog.ImportPaths = [source, Path.Combine(folder, "Gone.md")];
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.import").ClickAsync(new MouseEventArgs());

        IReadOnlyList<IElement> lines = cut.FindAll(".import-result p");
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l =>
        {
            Assert.Contains("odd\uFFFD folder", l.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', l.TextContent);
            Assert.DoesNotContain('\u0007', l.TextContent);
            Assert.DoesNotContain('\u0007', l.GetAttribute("title")!);
        });
    }

    private string Source(string name, string text)
    {
        string path = data.Outside(name);
        File.WriteAllText(path, text);
        return path;
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private void WriteTree()
    {
        data.WriteNote("Meetings", "m");
        data.WriteFile(Path.Combine("Meetings", "Meeting 1.md"), "1");
        data.WriteFile(Path.Combine("Meetings", "Meeting 1", "Notes.md"), "n");
        data.WriteNote("Plan", "p");
    }

    // A row's title: the button's own text, without the parent span.
    private static string Title(IElement button) =>
        string.Concat(button.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent));

    private static string[] Titles(IRenderedComponent<NotesPanel> cut) =>
        cut.FindAll("li.note .name").Select(Title).ToArray();

    // 240 AC-3: a subpage row shows its title and its parent's full name in a grey span; its title attribute is the
    // full name. A top-level row has no parent span.
    [Fact]
    public async Task Recent_SubpageRow_TitleParentAndFullName()
    {
        WriteTree();
        data.WriteRecent("Meetings/Meeting 1/Notes", "Plan", "Meetings/Meeting 1");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal(["Notes", "Plan", "Meeting 1"], Titles(cut));
        IReadOnlyList<IElement> names = cut.FindAll("li.note button.name");
        Assert.Equal(["Meetings/Meeting 1/Notes", "Plan", "Meetings/Meeting 1"], names.Select(b => b.GetAttribute("title")!).ToArray());
        Assert.Equal("Meetings/Meeting 1", names[0].QuerySelector("span.note-parent")!.TextContent);
        Assert.Null(names[1].QuerySelector(".note-parent"));
        Assert.Equal("Meetings", names[2].QuerySelector("span.note-parent")!.TextContent);
        Assert.Empty(cut.FindAll("button.toggle"));
        Assert.Empty(cut.FindAll("ul.note-children"));
    }

    // 240 AC-3: two subpages with the same title under different parents can be told apart.
    [Fact]
    public async Task Recent_SameTitleTwoParents_ToldApart()
    {
        data.WriteNote("A", "a");
        data.WriteFile(Path.Combine("A", "Notes.md"), "an");
        data.WriteNote("B", "b");
        data.WriteFile(Path.Combine("B", "Notes.md"), "bn");
        data.WriteRecent("A/Notes", "B/Notes");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IReadOnlyList<IElement> names = cut.FindAll("li.note button.name");
        Assert.Equal(["Notes", "Notes"], Titles(cut));
        Assert.Equal(["A", "B"], names.Select(b => b.QuerySelector(".note-parent")!.TextContent).ToArray());
        Assert.Equal(["A/Notes", "B/Notes"], names.Select(b => b.GetAttribute("title")!).ToArray());
    }

    // 240 AC-3: the open note's row, and only that one, has aria-current="page".
    [Fact]
    public async Task Recent_OpenRow_AriaCurrentPage()
    {
        WriteTree();
        data.WriteRecent("Plan", "Meetings/Meeting 1", "Meetings");
        await data.RefreshAsync();

        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OpenName, "Meetings/Meeting 1"));

        Assert.Equal(
            ["-", "page", "-"],
            cut.FindAll("li.note button.name").Select(b => b.GetAttribute("aria-current") ?? "-").ToArray());
    }

    // 240: clicking a row opens it by its full name.
    [Fact]
    public async Task Recent_ClickSubpage_OpensFullName()
    {
        WriteTree();
        data.WriteRecent("Plan", "Meetings/Meeting 1");
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnOpen, (string name) => opened.Add(name)));

        await cut.Find("button.name[title='Meetings/Meeting 1']").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meetings/Meeting 1"], opened);
    }

    // 240 AC-6: notes but no listed entry (no file, a broken file, or only names of missing notes) show the grey empty
    // line and no error.
    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{\"recent\":\"Plan\"}")]
    [InlineData("{\"recent\":[\"Gone\"]}")]
    public async Task Recent_NothingListed_GreyEmptyLine(string? json)
    {
        WriteTree();
        if (json is not null)
        {
            data.WriteFile(RecentNotesStore.FileName, json);
        }

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IElement line = cut.Find("p.recent-empty");
        Assert.Equal("No recently opened notes.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul.note-list"));
        Assert.Empty(cut.FindAll(".red"));
    }

    // 240 AC-6: with no notes at all the empty state stays "No notes yet", whatever recent.json holds.
    [Fact]
    public async Task Recent_NoNotes_NoNotesYet()
    {
        Directory.CreateDirectory(data.Root);
        data.WriteRecent("Plan");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal("No notes yet", cut.Find(".notes p").TextContent);
        Assert.Empty(cut.FindAll("p.recent-empty"));
    }
}
