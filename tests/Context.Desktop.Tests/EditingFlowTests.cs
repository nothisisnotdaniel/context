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
            ["/current", "/new <title>", "/open <note>", "/save", "/tags <tag>", "/todo <yyyy-MM-dd|today|tomorrow> <text>"],
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
        Assert.Equal(["/tags <tag>", "/todo <yyyy-MM-dd|today|tomorrow> <text>"], cut.FindAll(".palette-row .usage").Select(u => u.TextContent).ToArray());
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
