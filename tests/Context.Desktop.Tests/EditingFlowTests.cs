using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 094 AC-7, AC-9, AC-11: opening notes from the list, tag and link clicks, the discard question and the palette,
// through Main with the page's editor mocked.
public sealed class EditingFlowTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-7: clicking a note's name opens it in the main region instead of /current.
    [Fact]
    public async Task NoteName_Click_OpensEditor()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Single(cut.FindAll(".current-region .current"));

        await Open(cut, js, "Plan");

        Assert.Equal("Plan", cut.Find(".current-region .note-title").TextContent);
        Assert.Empty(cut.FindAll(".current-region .current"));
        Assert.Equal("# Plan\n", js.MountedText());
        Assert.Equal(
            ["notes-region", "current-region", "todos-region"],
            cut.Find(".layout").Children.Select(c => c.ClassName ?? string.Empty).ToArray());
    }

    // AC-7: with unsaved changes, opening another note asks first; Cancel keeps the editor, Discard opens the other.
    [Fact]
    public async Task OpenOther_Dirty_AsksCancelKeepsDiscardOpens()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nUnsaved\n");

        await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());

        IElement dialog = cut.Find("[role=dialog]");
        Assert.Equal("Discard changes to Plan?", dialog.QuerySelector(".question")!.TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Single(js.Calls("ContextEditor.mount"));

        await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count));
        Assert.Equal("Other", cut.Find(".note-title").TextContent);
        Assert.Equal("# Other\n", js.MountedText());
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // AC-7: a clean editor switches without asking.
    [Fact]
    public async Task OpenOther_Clean_NoQuestion()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent));
    }

    // AC-7 / AC-11: /current from the palette with unsaved changes asks, then shows /current.
    [Fact]
    public async Task PaletteCurrent_Dirty_AsksThenShowsCurrent()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/current");

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".current-region .current")));
        Assert.Empty(cut.FindAll(".note-editor"));
    }

    // AC-9: a tag click lists the notes with that tag, sorted ignoring case; clicking one opens it.
    [Fact]
    public async Task TagClick_ListsNotesAndOpensOne()
    {
        data.WriteNote("Plan", "#work\n");
        data.WriteNote("beta", "Also #Work\n");
        data.WriteNote("Alpha", "#work too\n");
        data.WriteNote("Gamma", "#play\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnTag("#work"));

        cut.WaitForAssertion(() => Assert.Equal(
            ["Alpha", "beta", "Plan"],
            cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Equal("#work", cut.Find(".tag-panel h2 .accent").TextContent);

        await cut.FindAll(".tag-panel .note-button")[1].ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("beta", cut.Find(".note-title").TextContent));
        Assert.Empty(cut.FindAll(".tag-panel"));
    }

    // AC-9: a link click opens the linked note.
    [Fact]
    public async Task LinkClick_OpensNote()
    {
        data.WriteNote("Plan", "See [[other]]\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnLink("[[other]]"));

        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent));
    }

    // AC-11: Ctrl+K and Cmd+K open the palette as a dialog with focus in its text field; Esc closes it.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CtrlOrCmdK_OpensPalette(bool ctrl, bool meta)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = ctrl, MetaKey = meta });

        IElement dialog = cut.Find(".palette");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        JSRuntimeInvocation focus = data.Bunit.JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal(
            cut.Find(".palette-input").GetAttribute("blazor:elementReference"),
            ((Microsoft.AspNetCore.Components.ElementReference)focus.Arguments[0]!).Id);
        Assert.Equal(
            ["/capture <text>", "/current", "/export", "/import", "/new <title>", "/open <note>", "/save", "/settings", "/tags [tag]", "/todo <yyyy-MM-dd|today|tomorrow> <text>"],
            cut.FindAll(".palette-row .usage").Select(u => u.TextContent).ToArray());

        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".palette"));
    }

    // AC-11: typing ranks like the console (a typo still lists /open); Up/Down move the selection.
    [Fact]
    public async Task Palette_TypoAndArrows()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/opne" });
        Assert.Equal(["/open <note>"], cut.FindAll(".palette-row .usage").Select(u => u.TextContent).ToArray());

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/t" });
        Assert.Equal(["/tags [tag]", "/todo <yyyy-MM-dd|today|tomorrow> <text>"], cut.FindAll(".palette-row .usage").Select(u => u.TextContent).ToArray());
        Assert.Equal("true", cut.FindAll(".palette-row")[0].GetAttribute("aria-selected"));
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("true", cut.FindAll(".palette-row")[1].GetAttribute("aria-selected"));
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        Assert.Equal("true", cut.FindAll(".palette-row")[0].GetAttribute("aria-selected"));

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/xyzzy" });
        Assert.Equal("No matching command.", cut.Find(".palette .none").TextContent);
    }

    // AC-11: /opne <note> + Enter runs /open with the rest of the line.
    [Fact]
    public async Task Palette_OpenWithTypo_OpensNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/opne plan");

        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent));
        Assert.Empty(cut.FindAll(".palette"));
    }

    // AC-11: /open with an unknown name says so.
    [Fact]
    public async Task Palette_OpenMissing_GreyLine()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/open Ghost");

        Assert.Equal("No note named Ghost.", cut.Find(".main-status").TextContent);
    }

    // AC-11: /save saves the open note.
    [Fact]
    public async Task Palette_Save_SavesOpenNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nFrom the palette\n");

        await RunPalette(cut, "/save");

        Assert.Equal("# Plan\n\nFrom the palette\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Saved Plan.", cut.Find(".note-editor .status").TextContent);
    }

    // AC-11: /tags <tag> shows the tag panel.
    [Fact]
    public async Task Palette_Tags_ShowsTagPanel()
    {
        data.WriteNote("Plan", "#[big project]\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/tags #[Big  Project]");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Equal("#[big project]", cut.Find(".tag-panel h2 .accent").TextContent);
        await cut.Find(".tag-panel button.close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".tag-panel"));
    }

    // 137 AC-1, AC-2: the Tags button and /tags show the tags view; a tag click shows its notes below the list;
    // the tag panel's Close hides only itself, the tags view's Close hides both.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TagsView_ButtonOrPalette_ListsTags_ClickShowsNotes_CloseRules(bool button)
    {
        data.WriteNote("Plan", "#work #[big project]\n");
        data.WriteNote("Alpha", "#Work\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        IReadOnlyList<TagCount> expected = (await data.State!.Tags()).Tags;
        Assert.Empty(cut.FindAll(".tags-panel"));

        if (button)
        {
            await cut.Find(".notes-region button.tags-open").ClickAsync(new MouseEventArgs());
        }
        else
        {
            await RunPalette(cut, "/tags");
        }

        cut.WaitForAssertion(() => Assert.Equal(
            expected.Select(t => TagParser.Format(t.Tag) + " " + t.Count).ToArray(),
            cut.FindAll(".current-region .tags-panel .tag-row").Select(r => r.QuerySelector("button")!.TextContent + " " + r.QuerySelector(".count")!.TextContent).ToArray()));
        Assert.Equal(["#[big project] 1", "#work 2"], expected.Select(t => TagParser.Format(t.Tag) + " " + t.Count).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Tags", cut.Find(".tags-panel h2").TextContent);
        Assert.Empty(cut.FindAll(".tag-panel"));
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);

        await TagButton(cut, "#work").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            ["Alpha", "Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Equal("true", TagButton(cut, "#work").GetAttribute("aria-pressed"));
        Assert.Equal("false", TagButton(cut, "#[big project]").GetAttribute("aria-pressed"));
        IElement region = cut.Find(".current-region");
        Assert.True(region.Children.Index().Single(c => c.Item.ClassList.Contains("tags-panel")).Index
            < region.Children.Index().Single(c => c.Item.ClassList.Contains("tag-panel")).Index);

        await TagButton(cut, "#[big project]").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(["Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Equal("true", TagButton(cut, "#[big project]").GetAttribute("aria-pressed"));
        Assert.Equal("false", TagButton(cut, "#work").GetAttribute("aria-pressed"));

        await cut.Find(".tag-panel button.close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".tag-panel"));
        Assert.Single(cut.FindAll(".tags-panel"));
        Assert.Empty(cut.FindAll(".tag-list button[aria-pressed=true]"));

        await TagButton(cut, "#work").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-panel")));
        await cut.Find(".tags-panel button.close").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".tags-panel"));
        Assert.Empty(cut.FindAll(".tag-panel"));
    }

    // 137 AC-1: /tags <tag> still opens the tag panel directly, without the tags view.
    [Fact]
    public async Task Palette_TagsWithATag_OpensOnlyTheTagPanel()
    {
        data.WriteNote("Plan", "#work\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/tags work");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Empty(cut.FindAll(".tags-panel"));
    }

    // 137 AC-2: opening a note, or the current view, hides the tags view and the tag panel.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TagsView_OpeningANoteOrTheCurrentView_HidesBoth(bool note)
    {
        data.WriteNote("Plan", "#work\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        if (!note)
        {
            await Open(cut, js, "Plan");
        }

        await cut.Find(".notes-region button.tags-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-list button")));
        await TagButton(cut, "#work").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-panel .note-button")));

        if (note)
        {
            await cut.Find(".tag-panel .note-button").ClickAsync(new MouseEventArgs());
            cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent));
        }
        else
        {
            await RunPalette(cut, "/current");
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".current-region .current")));
        }

        Assert.Empty(cut.FindAll(".tags-panel"));
        Assert.Empty(cut.FindAll(".tag-panel"));
    }

    // 137 AC-8: a shown tags view follows the data: new tags and counts appear after a refresh.
    [Fact]
    public async Task TagsView_FollowsTheData()
    {
        data.WriteNote("Plan", "#work\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await RunPalette(cut, "/tags");
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".tag-row .count").TextContent));

        data.WriteNote("Other", "#work #play\n");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".tag-row").Count));
        Assert.Equal("2", cut.FindAll(".tag-row").Single(r => r.QuerySelector("button")!.TextContent == "#work").QuerySelector(".count")!.TextContent);
    }

    // AC-11: /todo adds a todo with today, tomorrow or a date; a bad date writes nothing.
    [Theory]
    [InlineData("/todo tomorrow Buy milk", "2026-10-30")]
    [InlineData("/todo today Buy milk", "2026-10-29")]
    [InlineData("/todo 2026-12-01 Buy milk", "2026-12-01")]
    public async Task Palette_Todo_Adds(string line, string due)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        TodoItem added = Assert.Single(data.Todos!.List());
        Assert.Equal("Buy milk", added.Text);
        Assert.Equal(DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), added.Due);
        Assert.Equal("Added: Buy milk", cut.Find(".main-status").TextContent);
    }

    [Fact]
    public async Task Palette_TodoBadDate_WritesNothing()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/todo someday Buy milk");

        Assert.False(File.Exists(Path.Combine(data.Root, TodoStore.FileName)));
        Assert.Equal("Type /todo <yyyy-MM-dd|today|tomorrow> <text>.", cut.Find(".main-status").TextContent);
    }

    // AC-7: the note list shows names as buttons that open the note.
    [Fact]
    public async Task NotesPanel_NameButtonCallsOnOpen()
    {
        data.WriteNote("Plan", "x");
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NotesPanel> cut = data.Bunit.Render<NotesPanel>(p => p.Add(n => n.OnOpen, (string name) => opened.Add(name)));

        IElement button = cut.Find("li.note button.name");
        Assert.Equal("button", button.GetAttribute("type"));
        await button.ClickAsync(new MouseEventArgs());

        Assert.Equal(["Plan"], opened);
    }

    private static IElement NameButton(IRenderedComponent<Main> cut, string name) =>
        cut.FindAll(".notes-region button.name").Single(b => b.TextContent == name);

    private static IElement TagButton(IRenderedComponent<Main> cut, string label) =>
        cut.FindAll(".tags-panel .tag-list button").Single(b => b.TextContent == label);

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NameButton(cut, name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")));
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }
}
