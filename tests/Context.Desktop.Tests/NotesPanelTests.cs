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

    // AC-4: NoteStore.List() names with their last-write time, in that order.
    [Fact]
    public async Task Render_Notes_NamesAndTimesInListOrder()
    {
        data.WriteNote("beta", "b");
        data.WriteNote("Alpha", "a");
        data.WriteFile(Path.Combine("sub", "Hidden.md"), "x");
        DateTime alpha = new(2026, 10, 20, 8, 5, 0, DateTimeKind.Local);
        DateTime beta = new(2026, 10, 21, 17, 45, 0, DateTimeKind.Local);
        File.SetLastWriteTime(Path.Combine(data.Root, "Alpha.md"), alpha);
        File.SetLastWriteTime(Path.Combine(data.Root, "beta.md"), beta);

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        IReadOnlyList<IElement> items = cut.FindAll("li.note");
        Assert.Equal(["Alpha", "beta"], items.Select(i => i.QuerySelector(".name")!.TextContent).ToArray());
        Assert.Equal(
            [Time(alpha), Time(beta)],
            items.Select(i => i.QuerySelector(".time")!.TextContent).ToArray());
        Assert.Equal("Alpha", items[0].QuerySelector(".name")!.GetAttribute("title"));
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

    // AC-4: a listing that throws shows the fixed red line.
    [Fact]
    public async Task Render_ListingThrows_RedLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions only; Windows ACLs would need their own setup.
        }

        Directory.CreateDirectory(data.Root);
        data.WriteNote("Alpha", "a");
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

    // AC-5: a hostile note name is encoded text with control chars shown as U+FFFD.
    [Fact]
    public async Task Render_HostileName_Encoded()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows file names can't hold < > or control chars.
        }

        data.WriteNote("<script>alert(1)<\\script> <img src=x onerror=alert(1)> \u0007", "x");

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<script>alert(1)<\\script> <img src=x onerror=alert(1)> �", cut.Find(".name").TextContent);
    }

    // 134 AC-7: the entry row is a plain text field (no @bind) and a New note button.
    [Fact]
    public async Task EntryRow_TextFieldAndButton()
    {
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal("text", cut.Find(".note-entry input").GetAttribute("type"));
        IElement button = cut.Find(".note-entry button.note-new");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("New note", button.TextContent);
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
            await cut.Find("button.note-new").ClickAsync(new MouseEventArgs());
        }

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(["Plan"], opened);
        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Find(".note-new-title").GetAttribute("value")));
        IElement status = cut.Find(".note-entry-result");
        Assert.Equal("Created Plan", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        Assert.Equal("status", status.GetAttribute("role"));
        cut.WaitForAssertion(() => Assert.Contains("Plan", cut.Find(".note-list").TextContent));
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
        await cut.Find("button.note-new").ClickAsync(new MouseEventArgs());

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
        Assert.Equal("Tags", button.TextContent);
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
        Assert.Equal("Settings", button.TextContent);
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
        cut.WaitForAssertion(() => Assert.Equal(
            ["Alpha", "Beta"], cut.FindAll("li.note button.name").Select(n => n.TextContent).ToArray()));
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

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private void WriteTree()
    {
        data.WriteNote("Meetings", "m");
        data.WriteFile(Path.Combine("Meetings", "Meeting 1.md"), "1");
        data.WriteFile(Path.Combine("Meetings", "Meeting 1", "Notes.md"), "n");
        data.WriteNote("Plan", "p");
    }

    private static string[] Titles(IRenderedComponent<NotesPanel> cut) =>
        cut.FindAll("li.note .name").Select(b => b.TextContent).ToArray();

    // 152 AC-13: notes start collapsed; a note with subpages has a toggle button with aria-expanded and aria-label.
    [Fact]
    public async Task Tree_StartsCollapsed_ToggleButtons()
    {
        WriteTree();

        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        Assert.Equal(["Meetings", "Plan"], Titles(cut));
        IElement toggle = Assert.Single(cut.FindAll("button.toggle"));
        Assert.Equal("button", toggle.GetAttribute("type"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Expand Meetings", toggle.GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll("ul.note-children"));
    }

    // 152 AC-13: expanding shows nested lists with each row's title and the full name in title; collapsing hides them.
    [Fact]
    public async Task Tree_Toggle_ShowsNestedLists_TitleAndFullName()
    {
        WriteTree();
        IRenderedComponent<NotesPanel> cut = await data.RenderAsync<NotesPanel>();

        await cut.Find("button.toggle[aria-label='Expand Meetings']").ClickAsync(new MouseEventArgs());
        await cut.Find("button.toggle[aria-label='Expand Meetings/Meeting 1']").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meetings", "Meeting 1", "Notes", "Plan"], Titles(cut));
        IElement deep = cut.Find("ul.note-children ul.note-children li.note .name");
        Assert.Equal("Notes", deep.TextContent);
        Assert.Equal("Meetings/Meeting 1/Notes", deep.GetAttribute("title"));
        IElement top = cut.Find("button.toggle[aria-label='Collapse Meetings']");
        Assert.Equal("true", top.GetAttribute("aria-expanded"));

        await top.ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meetings", "Plan"], Titles(cut));
        Assert.Equal("false", cut.Find("button.toggle").GetAttribute("aria-expanded"));
    }

    // 152 AC-13: clicking a subpage opens it by full name.
    [Fact]
    public async Task Tree_ClickSubpage_OpensFullName()
    {
        WriteTree();
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnOpen, (string name) => opened.Add(name)));

        await cut.Find("button.toggle").ClickAsync(new MouseEventArgs());
        await cut.Find("button.name[title='Meetings/Meeting 1']").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Meetings/Meeting 1"], opened);
    }

    // 152 AC-13: opening a note expands its ancestors; the expanded state survives State.Changed.
    [Fact]
    public async Task Tree_OpenNameExpandsAncestors_SurvivesChanged()
    {
        WriteTree();
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OpenName, "Meetings/Meeting 1/Notes"));

        Assert.Equal(["Meetings", "Meeting 1", "Notes", "Plan"], Titles(cut));

        data.WriteNote("Alpha", "a");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Alpha", "Meetings", "Meeting 1", "Notes", "Plan"], Titles(cut)), Wait);

        await cut.Find("button.toggle[aria-label='Collapse Meetings']").ClickAsync(new MouseEventArgs());
        await data.State.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Alpha", "Meetings", "Plan"], Titles(cut)), Wait);
    }

    private static string Time(DateTime local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
