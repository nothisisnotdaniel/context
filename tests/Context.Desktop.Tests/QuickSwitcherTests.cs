using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 364: the quick switcher (Ctrl/Cmd+P) through Main, against real files in the fixture's own temp folder, with the
// fixture's clocks.
public sealed class QuickSwitcherTests : IDisposable
{
    // Opening a note, its panels' loads and recent.json writes run on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    // The layout's element reference id, read in the first render (later renders leave the attribute empty).
    private string? layoutId;

    public void Dispose() => data.Dispose();

    // AC-4: Ctrl+P or Cmd+P on the layout opens the dialog with its input focused.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CtrlOrCmdP_OpensWithInputFocused(bool mac)
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = !mac, MetaKey = mac });

        IElement dialog = cut.Find(".dialog.switcher");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Open note", dialog.GetAttribute("aria-label"));
        IElement input = cut.Find(".switcher-input");
        Assert.Equal("Note name", input.GetAttribute("aria-label"));
        Assert.Equal("Type a note name", input.GetAttribute("placeholder"));
        Assert.Equal("listbox", cut.Find(".switcher ul").GetAttribute("role"));
        AssertFocused(cut, ".switcher-input");
    }

    // AC-4: Ctrl/Cmd+P from the note editor opens it too.
    [Fact]
    public async Task CtrlP_FromTheEditor_Opens()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "p", MetaKey = true });

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".dialog.switcher")), Wait);
    }

    // AC-4: not while the palette is open; and Ctrl/Cmd+K does not open the palette while the switcher is open.
    [Fact]
    public async Task PaletteAndSwitcher_NeverBoth()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = true });
        Assert.Single(cut.FindAll(".palette"));
        Assert.Empty(cut.FindAll(".switcher"));

        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = true });
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", MetaKey = true });
        Assert.Single(cut.FindAll(".switcher"));
        Assert.Empty(cut.FindAll(".palette"));
    }

    // AC-4: not while a delete, share or discard dialog is open.
    [Theory]
    [InlineData("delete")]
    [InlineData("share")]
    [InlineData("discard")]
    public async Task CtrlP_WhileADialogAsks_DoesNothing(string dialog)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        if (dialog == "discard")
        {
            js.EditUnsavable("# Plan\n\nToo big");
            await NoteOpener.OpenAsync(cut, "Other");
            cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        }
        else
        {
            await MainTests.ChooseAsync(cut, dialog);
            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=dialog]")), Wait);
        }

        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = true });

        Assert.Empty(cut.FindAll(".switcher"));
    }

    // AC-5: plain and sensitive notes, the name through OneLine, a sensitive one with the visible locked.
    [Fact]
    public async Task Rows_PlainAndSensitive_LockedShown()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "# Secret\n");
        IRenderedComponent<Main> cut = await OpenSwitcher();

        IReadOnlyList<IElement> rows = cut.FindAll(".switcher-row");
        Assert.Equal(["Plan", "Secret"], rows.Select(r => r.QuerySelector(".name")!.TextContent).ToArray());
        Assert.Null(rows[0].QuerySelector(".locked"));
        Assert.Equal("locked", rows[1].QuerySelector(".locked")!.TextContent);
    }

    // AC-5, AC-2: recent first, then pinned, then the rest, as the switcher lists them.
    [Fact]
    public async Task Rows_RecentThenPinnedThenRest()
    {
        data.WriteNote("Alpha", "# Alpha\n");
        data.WriteNote("Beta", "# Beta\n");
        data.WriteNote("Gamma", "# Gamma\n");
        data.WriteNote("Delta", "# Delta\n");
        data.WriteRecent("Gamma");
        data.WritePinned("Delta");
        IRenderedComponent<Main> cut = await OpenSwitcher();

        Assert.Equal(["Gamma", "Delta", "Alpha", "Beta"], RowNames(cut));
    }

    // AC-6: arrows move the selected row and wrap; typing resets it to the first row.
    [Fact]
    public async Task Arrows_MoveAndWrap_TypingResets()
    {
        data.WriteNote("A1", "x\n");
        data.WriteNote("A2", "x\n");
        data.WriteNote("A3", "x\n");
        IRenderedComponent<Main> cut = await OpenSwitcher();
        Assert.Equal(0, Selected(cut));

        await Key(cut, "ArrowDown");
        Assert.Equal(1, Selected(cut));
        await Key(cut, "ArrowUp");
        await Key(cut, "ArrowUp");
        Assert.Equal(2, Selected(cut));
        await Key(cut, "ArrowDown");
        Assert.Equal(0, Selected(cut));
        await Key(cut, "ArrowDown");

        await cut.Find(".switcher-input").InputAsync(new ChangeEventArgs { Value = "a" });
        Assert.Equal(0, Selected(cut));
        Assert.Single(cut.FindAll(".switcher-row[aria-selected='true']"));
    }

    // AC-6: Enter opens the selected note like a sidebar click; it becomes the newest recent entry.
    [Fact]
    public async Task Enter_OpensTheNote_RecordsItRecent()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        data.WriteRecent("Plan");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await OpenSwitcher();
        await cut.Find(".switcher-input").InputAsync(new ChangeEventArgs { Value = "oth" });

        await Key(cut, "Enter");

        await WaitOpened(cut, js, "Other");
        Assert.Empty(cut.FindAll(".switcher"));
        AssertFocused(cut, ".layout");
        data.WaitForRecentWrites();
        Assert.Equal(["Other", "Plan"], new RecentNotesStore(data.Root).Read());
    }

    // AC-6: a click on a row opens it; a dirty editor saves first.
    [Fact]
    public async Task Click_DirtyEditor_SavesFirstThenOpens()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await js.Change(cut, "# Plan\n\nTyped\n");
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = true });

        await cut.Find(".switcher-row .name[title='Other']").ParentElement!.ClickAsync(new MouseEventArgs());

        await WaitOpened(cut, js, "Other");
        Assert.Empty(cut.FindAll(".switcher"));
        Assert.Equal("# Plan\n\nTyped\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        data.WaitForRecentWrites();
        Assert.Equal("Other", new RecentNotesStore(data.Root).Read()[0]);
    }

    // AC-7: Escape or a click on the backdrop closes it and changes nothing; a click inside the dialog keeps it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapeOrBackdrop_ClosesChangingNothing(bool backdrop)
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await OpenSwitcher();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        // A click inside the dialog stops there, so only the backdrop outside it closes.
        Assert.NotNull(cut.Find(".dialog.switcher").GetAttribute("blazor:onclick:stoppropagation"));

        if (backdrop)
        {
            await cut.Find(".dialog-backdrop").ClickAsync(new MouseEventArgs());
        }
        else
        {
            await Key(cut, "Escape");
        }

        Assert.Empty(cut.FindAll(".switcher"));
        Assert.Empty(cut.FindAll(".note-title"));
        AssertFocused(cut, ".layout");
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-8: a query that matches nothing offers Create "<query>"; Enter or a click creates, opens and says Created.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoMatch_Create_CreatesOpensAndSaysCreated(bool click)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await OpenSwitcher();
        await cut.Find(".switcher-input").InputAsync(new ChangeEventArgs { Value = "  Fresh idea  " });
        IElement row = cut.Find(".switcher-row");
        Assert.Equal("Create \"Fresh idea\"", row.TextContent.Trim());
        Assert.Equal("true", row.GetAttribute("aria-selected"));

        if (click)
        {
            await cut.Find(".switcher-row").ClickAsync(new MouseEventArgs());
        }
        else
        {
            await Key(cut, "Enter");
        }

        Assert.Empty(cut.FindAll(".switcher"));
        Assert.Equal("# Fresh idea\n", File.ReadAllText(Path.Combine(data.Root, "Fresh idea.md")));
        await WaitOpened(cut, js, "Fresh idea");
        Assert.Equal("Created Fresh idea", cut.Find(".main-status").TextContent);
        Assert.Contains("accent", cut.Find(".main-status").ClassList);
    }

    // AC-8: a refused create shows its red text as /new does and writes nothing.
    [Fact]
    public async Task NoMatch_RefusedCreate_RedTextNothingWritten()
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await OpenSwitcher();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await cut.Find(".switcher-input").InputAsync(new ChangeEventArgs { Value = "x:y" });

        await Key(cut, "Enter");

        Assert.Empty(cut.FindAll(".switcher"));
        Assert.Equal("A note title can't contain / \\ : or control characters.", cut.Find(".main-status").TextContent);
        Assert.Contains("red", cut.Find(".main-status").ClassList);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-9: no notes and a blank query: grey No notes yet., and Enter does nothing.
    [Fact]
    public async Task NoNotes_Blank_GreyLine_EnterDoesNothing()
    {
        IRenderedComponent<Main> cut = await OpenSwitcher();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        IElement none = cut.Find(".switcher .none");
        Assert.Equal("No notes yet.", none.TextContent);
        Assert.Contains("grey", none.ClassList);
        await Key(cut, "Enter");

        Assert.Single(cut.FindAll(".switcher"));
        Assert.Empty(cut.FindAll(".switcher-row"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-10: the editor swallows Mod-p and the page blocks the web view's print shortcut.
    [Fact]
    public void Bundle_SwallowsModP_IndexBlocksPrint()
    {
        string root = RepoFiles.Root();
        string view = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "editor", "src", "view.js"));
        string index = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "editor", "src", "index.js"));
        string bundle = File.ReadAllText(Path.Combine(root, RepoFiles.DesktopFolder, "wwwroot", "editor", "editor.js"));

        Assert.Contains("\"Mod-p\": swallow", view, StringComparison.Ordinal);
        Assert.Contains("\"Ctrl-p\": swallow", view, StringComparison.Ordinal);
        Assert.Contains("\"Mod-p\"", bundle, StringComparison.Ordinal);
        Assert.Contains("\"Ctrl-p\"", bundle, StringComparison.Ordinal);
        Assert.Contains("globalThis.addEventListener(\"keydown\"", index, StringComparison.Ordinal);
        Assert.Contains("event.key.toLowerCase() === \"p\"", index, StringComparison.Ordinal);
        Assert.Contains("!event.altKey && !event.shiftKey", index, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", index, StringComparison.Ordinal);
        Assert.Contains("toLowerCase()===\"p\"&&e.preventDefault()", bundle, StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<Main>> OpenSwitcher()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        layoutId = cut.Find(".layout").GetAttribute("blazor:elementReference");
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "p", CtrlKey = true });
        Assert.Single(cut.FindAll(".dialog.switcher"));
        return cut;
    }

    private static Task Key(IRenderedComponent<Main> cut, string key) =>
        cut.Find(".switcher-input").KeyDownAsync(new KeyboardEventArgs { Key = key });

    private static string[] RowNames(IRenderedComponent<Main> cut) =>
        cut.FindAll(".switcher-row .name").Select(n => n.TextContent).ToArray();

    private static int Selected(IRenderedComponent<Main> cut)
    {
        IReadOnlyList<IElement> rows = cut.FindAll(".switcher-row");
        Assert.Single(rows, r => r.GetAttribute("aria-selected") == "true");
        return rows.ToList().FindIndex(r => r.GetAttribute("aria-selected") == "true");
    }

    // Opens a note through /open and waits until its editor and the page's own loads have rendered.
    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        await WaitMounted(cut, js, name, mounts);
    }

    // Waits until the note the switcher opened shows in the editor and the links panel has loaded.
    private static Task WaitOpened(IRenderedComponent<Main> cut, EditorJs js, string name) => WaitMounted(cut, js, name, 0);

    private static Task WaitMounted(IRenderedComponent<Main> cut, EditorJs js, string name, int mountsBefore)
    {
        cut.WaitForAssertion(() => Assert.True(js.Calls("ContextEditor.mount").Count > mountsBefore), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        return Task.CompletedTask;
    }

    // Waits until the element the selector finds (read in the render that created it) was given the focus.
    private void AssertFocused(IRenderedComponent<Main> cut, string selector)
    {
        string? id = selector == ".layout" ? layoutId : cut.Find(selector).GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(id));
        cut.WaitForAssertion(
            () => Assert.Contains(
                data.Bunit.JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus"),
                i => i.Arguments[0] is ElementReference r && r.Id == id),
            Wait);
    }
}
