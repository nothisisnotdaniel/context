using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class MainTests : IDisposable
{
    // 150: opening a note runs DesktopState.ReadForEdit on Task.Run, which can take over bUnit's 1 s default on a busy
    // runner. Every wait passes this bounded timeout; a real failure still fails within it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4 layout: notes on the left, /current in the middle, todos on the right.
    [Fact]
    public async Task Render_ThreeRegions()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal(
            ["notes-region", "current-region", "todos-region"],
            cut.Find(".layout").Children.Select(c => c.ClassName ?? string.Empty).ToArray());
    }

    // AC-10: a refresh that throws shows the fixed red line, never the exception message.
    [Fact]
    public async Task Render_RefreshThrows_FixedRedLine()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("secret detail " + FakeHttpHandler.PathToken);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("Can't read the data folder.", cut.Find("p.red").TextContent);
        Assert.DoesNotContain("secret detail", cut.Markup);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup);
        Assert.Empty(cut.FindAll(".layout"));
    }

    // Main re-renders when the state changes (the failure clears on the next good refresh).
    [Fact]
    public async Task Render_StateChanges_ReRenders()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("boom");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Single(cut.FindAll("p.red"));

        data.FeedClockThrows = null;
        data.AddTodo("Fresh todo", DesktopFixture.Today);
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Contains("Fresh todo", cut.Find(".todos").TextContent), Wait);
    }

    // 132 AC-5: a leftover key file of the removed feature still gives the normal three panels with the data.
    [Fact]
    public async Task Render_WithALeftoverKeyFile_NormalPanelsWithData()
    {
        data.WriteFile(RemovedFeature.KeyFileName, "{}");
        data.WriteNote("Alpha", "a");
        data.AddTodo("Buy milk", DesktopFixture.Today);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Contains("Buy milk", cut.Find(".todos-region").TextContent), Wait);
        Assert.Equal(
            ["notes-region", "current-region", "todos-region"],
            cut.Find(".layout").Children.Select(c => c.ClassName ?? string.Empty).ToArray());
        Assert.Contains("Alpha", cut.Find(".notes-region").TextContent);
        Assert.Empty(cut.FindAll(".locked"));
    }

    // 134 AC-7: /new <title> in the palette creates the note, opens it and shows the accent status.
    [Fact]
    public async Task PaletteNew_CreatesOpensAndShowsCreated()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/new Plan");

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("Created Plan", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
    }

    // 134 AC-7: /new without a title is a red usage line and writes nothing; a refusal shows its red text.
    [Theory]
    [InlineData("/new", "Type /new <title>.")]
    [InlineData("/new   ", "Type /new <title>.")]
    [InlineData("/new a/b", "A note title can't contain / \\ : or control characters.")]
    public async Task PaletteNew_NoOrBadTitle_RedLine(string line, string text)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        Assert.Equal(text, cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.False(Directory.Exists(data.Root));
    }

    // 165 AC-8: /new <title> --template <name> creates the note from the template, opens it and shows the accent status.
    [Theory]
    [InlineData("/new Weekly sync --template meeting")]
    [InlineData("/new   Weekly sync   --TEMPLATE   Meeting.md  ")]
    public async Task PaletteNewTemplate_CreatesOpensAndShowsCreated(string line)
    {
        data.WriteFile(Path.Combine(NoteTemplates.FolderName, "meeting.md"), "# {{title}}\n{{date}}\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal("Weekly sync", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("# Weekly sync\n2026-10-29\n", File.ReadAllText(Path.Combine(data.Root, "Weekly sync.md")));
        Assert.Equal("Created Weekly sync", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
    }

    // 165 AC-8: a blank title or template is the red usage line; a refusal shows its red text; nothing is written.
    [Theory]
    [InlineData("/new --template meeting", "Type /new <title> --template <template>.")]
    [InlineData("/new Plan --template", "Type /new <title> --template <template>.")]
    [InlineData("/new Plan --template   ", "Type /new <title> --template <template>.")]
    [InlineData("/new Plan --template standup", "No template called standup. Templates: meeting.")]
    [InlineData("/new a/b --template meeting", "A note title can't contain / \\ : or control characters.")]
    [InlineData("/new meeting --template meeting", "Note meeting already exists.")]
    public async Task PaletteNewTemplate_BlankOrRefused_RedLine(string line, string text)
    {
        data.WriteNote("Meeting", "# Meeting\n");
        data.WriteFile(Path.Combine(NoteTemplates.FolderName, "meeting.md"), "# {{title}}\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal(text, cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 165 AC-8: --template inside a word is part of the title, so /new without the token behaves as before.
    [Fact]
    public async Task PaletteNew_WithoutTemplateToken_PlainNote()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/new Plan--template");

        cut.WaitForAssertion(() => Assert.Equal("Created Plan--template", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal("# Plan--template\n", File.ReadAllText(Path.Combine(data.Root, "Plan--template.md")));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
    }

    // 165 AC-8: with unsaved changes in the open note, the new note from a template asks to discard first.
    [Fact]
    public async Task PaletteNewTemplate_DirtyEditor_AsksBeforeOpening()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteFile(Path.Combine(NoteTemplates.FolderName, "meeting.md"), "# {{title}}\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/new Fresh --template meeting");

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "Fresh.md")));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Fresh", cut.Find(".note-title").TextContent), Wait);
    }

    // 201 AC-5: a note todo's note button opens the note, after the "Discard changes" check for unsaved edits.
    [Fact]
    public async Task TodoNoteButton_DirtyEditor_AsksThenOpensTheNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Meeting", "# Meeting\n- [ ] call Anna\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await cut.Find(".todos-region button.todo-note").ClickAsync(new MouseEventArgs());

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Meeting", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal("# Meeting\n- [ ] call Anna\n", File.ReadAllText(Path.Combine(data.Root, "Meeting.md")));
    }

    // 134 AC-7: creating from the entry row while the open note has unsaved changes asks to discard first.
    [Fact]
    public async Task EntryRow_DirtyEditor_AsksBeforeOpeningTheNewNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "Fresh" });
        await cut.Find(".note-new-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(File.Exists(Path.Combine(data.Root, "Fresh.md")));
        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Fresh", cut.Find(".note-title").TextContent), Wait);
    }

    // 134 AC-8, 200 AC-2: a plain note's actions are the More actions items, in order (136: plus Mark sensitive);
    // the toolbar holds no action button and is absent while no form is open.
    [Fact]
    public async Task Toolbar_OnlyWithAnOpenNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".note-toolbar"));

        await Open(cut, js, "Plan");

        Assert.Empty(cut.FindAll(".note-toolbar"));
        await OpenMenuAsync(cut);
        IReadOnlyList<IElement> items = cut.FindAll(".page-menu button");
        Assert.Equal(
            ["rename", "delete", "history", "new-subpage", "move", "open-external", "export", "mark-sensitive"],
            items.Select(b => b.ClassName ?? string.Empty).ToArray());
        Assert.Equal(
            ["Rename", "Delete", "History", "New subpage", "Move", "Open in default editor", "Export", "Mark sensitive"], items.Select(b => b.TextContent).ToArray());
        Assert.All(items, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal("menuitemcheckbox", cut.Find(".page-menu button.history").GetAttribute("role"));
        Assert.All(items.Where(b => b.ClassName != "history"), b => Assert.Equal("menuitem", b.GetAttribute("role")));
    }

    // 134 AC-8: the toolbar also shows when the note can't be read, and Delete still works there.
    [Fact]
    public async Task Toolbar_UnreadableNote_StillShown()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Big.md"), new byte[NoteStore.MaxEditBytes + 1]);
        _ = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await NameButton(cut, "Big").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Can't read Big.", cut.Find(".unreadable").TextContent), Wait);
        await OpenMenuAsync(cut);
        Assert.Equal(8, cut.FindAll(".page-menu button").Count);
    }

    // 134 AC-8: Rename swaps in a prefilled field; Enter or Save name renames and the editor shows the new name.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_EnterOrSaveName_RenamesAndReopens(bool enter)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await ChooseAsync(cut, "rename");
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-toolbar .rename-title").GetAttribute("value")), Wait);
        Assert.Equal("text", cut.Find(".rename-title").GetAttribute("type"));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.True(cut.Find("button.page-menu-open").HasAttribute("disabled"));
        AssertFocused(cut, ".rename-title");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        if (enter)
        {
            await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Project.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Empty(cut.FindAll(".rename-title"));
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll(".notes-region button.name"), b => b.TextContent == "Plan"), Wait);
    }

    // 134 AC-8: Esc or Cancel restores the toolbar and renames nothing.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_EscOrCancel_RestoresToolbar(bool esc)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });

        if (esc)
        {
            await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find(".note-toolbar button.cancel-rename").ClickAsync(new MouseEventArgs());
        }

        Assert.Empty(cut.FindAll(".rename-title"));
        Assert.Empty(cut.FindAll(".note-toolbar"));
        Assert.False(cut.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.True(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Project.md")));
    }

    // 134 AC-8: with unsaved changes the rename is refused in red and nothing is written.
    [Fact]
    public async Task Rename_DirtyEditor_RefusedNothingWritten()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await ChooseAsync(cut, "rename");

        Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".rename-title"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 134 AC-8: an edit made while the name field is open is caught when saving the name.
    [Fact]
    public async Task Rename_EditedWhileRenaming_RefusedNothingWritten()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent);
        Assert.Equal("Project", cut.Find(".rename-title").GetAttribute("value"));
        Assert.True(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Project.md")));
    }

    // 134 AC-8: a rename onto another note's name shows the red refusal and keeps both notes.
    [Fact]
    public async Task Rename_Exists_RedLine()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "other" });

        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Note other already exists.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("# Other\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
    }

    // 206 AC-9: with links to update Enter asks first, focus on Cancel; Rename renames, updates the links and reopens.
    [Fact]
    public async Task Rename_WithLinks_AsksThenRenamesAndUpdatesLinks()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "see [[Plan]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForElement("[role=dialog]", Wait);
        Assert.Equal("Rename Plan to Project? Links in 1 note will be updated.", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Rename", cut.Find("[role=dialog] button.confirm").TextContent);
        AssertFocused(cut, "[role=dialog] button.cancel");
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));

        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("Renamed to", cut.Find(".main-status").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal("Renamed to Project. Updated links in 1 note.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Empty(cut.FindAll(".rename-title"));
        Assert.Equal("see [[Project]]\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
    }

    // 206 AC-9: Cancel or Esc in the question writes nothing and keeps the name field with its text.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_WithLinks_CancelOrEsc_KeepsField(bool esc)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "see [[Plan]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForElement("[role=dialog]", Wait);

        if (esc)
        {
            await cut.Find("[role=dialog]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        }

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=dialog]")), Wait);
        Assert.Equal("Project", cut.Find(".rename-title").GetAttribute("value"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 206 AC-9: links left after the rename show the Done text in red; the editor still opens the new name.
    [Fact]
    public async Task Rename_LinksLeft_RedLine_OpensNewName()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Work", "w\n");
        data.WriteFile(Path.Combine("Work", "A.md"), "[[Plan]]\n");
        data.WriteFile(Path.Combine("Work", ".history"), "in the way");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForElement("[role=dialog]", Wait);

        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("Renamed to", cut.Find(".main-status").TextContent, StringComparison.Ordinal), Wait);
        Assert.Equal(
            "Renamed to Project, but links in 1 note couldn't be updated: Work/A.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent), Wait);
    }

    // 206 AC-9: a refused rename after the question shows red and keeps the name field.
    [Fact]
    public async Task Rename_LinksCantBeWritten_RedLine_KeepsField()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "see [[Plan]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Pro#ject" });
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForElement("[role=dialog]", Wait);

        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "Links to Plan can't be written with the name Pro#ject. Nothing was renamed.",
            cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("Pro#ject", cut.Find(".rename-title").GetAttribute("value"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 134 AC-8: Delete asks in ConfirmDialog; Cancel deletes nothing.
    [Fact]
    public async Task Delete_Cancel_DeletesNothing()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await ChooseAsync(cut, "delete");

        Assert.Equal("Delete Plan? This can't be undone.", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Delete", cut.Find("[role=dialog] button.confirm").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.True(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
    }

    // 134 AC-8: confirmed, the note is deleted and the main region shows the current view again.
    [Fact]
    public async Task Delete_Confirm_DeletesAndShowsCurrent()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await ChooseAsync(cut, "delete");
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md"))), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll(".notes-region button.name"), b => b.TextContent == "Plan"), Wait);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".current-region .current")), Wait);
        Assert.Empty(cut.FindAll(".note-editor"));
        Assert.Empty(cut.FindAll(".note-toolbar"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    // 134 AC-8: Open in default editor launches the note's path; a failure shows the red text.
    [Fact]
    public async Task OpenInDefaultEditor_LaunchesOrShowsRedText()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await ChooseAsync(cut, "open-external");
        Assert.Equal([Path.Combine(data.Root, "Plan.md")], data.Launcher.Launched);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);

        data.Launcher.Throws = new InvalidOperationException("boom");
        await ChooseAsync(cut, "open-external");

        Assert.Equal("Can't open Plan in another app.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
    }

    // 137 AC-4, AC-5: the links panel sits under the open note; a name opens that note, a shared tag shows the
    // tag panel while the note stays open.
    [Fact]
    public async Task LinksPanel_UnderTheOpenNote_NameOpens_TagShowsTagPanel()
    {
        data.WriteNote("Plan", "#work [[Other]] [[Ghost]]\n");
        data.WriteNote("Other", "#work\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".links-panel"));
        await Open(cut, js, "Plan");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Ghost", "Other"],
            cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).Order(StringComparer.Ordinal).ToArray()), Wait);
        IElement region = cut.Find(".note-area");
        Assert.True(region.Children.Index().Single(c => c.Item.ClassList.Contains("note-editor")).Index
            < region.Children.Index().Single(c => c.Item.ClassList.Contains("links-panel")).Index);
        Assert.Equal("Linked notes", cut.Find(".links-panel h2").TextContent);

        await cut.Find(".links-panel .link-tags button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(
            ["Other", "Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("#work", cut.Find(".tag-panel h2 .accent").TextContent);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Single(js.Calls("ContextEditor.mount"));

        await cut.Find(".links-panel button.name").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Equal(
            ["Plan"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()), Wait);
        Assert.Equal("linked from", cut.Find(".links-panel .link-kind").TextContent);
    }

    // 137 AC-5: with unsaved changes, a linked note's name asks to discard first.
    [Fact]
    public async Task LinksPanel_DirtyEditor_AsksBeforeOpening()
    {
        data.WriteNote("Plan", "[[Other]]\n");
        data.WriteNote("Other", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel button.name")), Wait);
        js.EditUnsavable("[[Other]]\n\nUnsaved\n");

        await cut.Find(".links-panel button.name").ClickAsync(new MouseEventArgs());

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent), Wait);
    }

    // 137 AC-8: the panel follows the data, and after a rename it shows the links of the new name.
    [Fact]
    public async Task LinksPanel_FollowsRefreshAndRename()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "#work\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Equal("Plan has no linked notes.", cut.Find(".links-panel p[role=status]").TextContent), Wait);

        data.WriteNote("Third", "see [[Plan]] and [[Project]]\n");
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Third"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()), Wait);

        await ChooseAsync(cut, "rename");
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForElement("[role=dialog]", Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(
            ["Third"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()), Wait);
        cut.WaitForAssertion(() => Assert.Equal(1, js.Calls("ContextEditor.mount").Count - 1), Wait);
        Assert.DoesNotContain("Plan has no linked notes.", cut.Find(".links-panel").TextContent);
    }

    // 137 AC-7: a sensitive note, open or after it locked, shows only the grey line and costs no extra Agree.
    [Fact]
    public async Task LinksPanel_SensitiveNote_GreyLine_NoExtraAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "#work [[Other]]\n");
        data.WriteNote("Other", "#work [[Secret]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await Open(cut, js, "Secret");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal("Sensitive notes have no links.", cut.Find(".links-panel p[role=status]").TextContent), Wait);
        Assert.True(cut.Find(".links-panel p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll(".links-panel .link-rows"));
        Assert.Equal(1, data.Keys.Agrees);
    }

    // 137 AC-7: a sensitive note that can't be unlocked shows the same line; nothing but the open asked for the key.
    [Fact]
    public async Task LinksPanel_LockedSensitiveNote_GreyLine()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "#work\n");
        data.Keys.AgreeThrows = true;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await NameButton(cut, "Secret").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock Secret.", cut.Find(".unreadable").TextContent), Wait);
        Assert.Equal("Sensitive notes have no links.", cut.Find(".links-panel p[role=status]").TextContent);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Empty(js.Calls("ContextEditor.mount"));
    }

    // 137 AC-9: showing the tags view, a tag's notes and the links panel changes no user data.
    [Fact]
    public async Task TagsAndLinksPanels_ChangeNoUserData()
    {
        data.WriteNote("Plan", "#work [[Other]]\n");
        data.WriteNote("Other", "#work\n");
        data.AddTodo("Due #work", DesktopFixture.Today);
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await Open(cut, js, "Plan");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel .link-row")), Wait);
        await RunPalette(cut, "/tags");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tags-panel .tag-list button")), Wait);
        await cut.Find(".tags-panel .tag-list button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".tag-panel .note-button").Count), Wait);
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel .link-row")), Wait);

        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 138 AC-1: the Settings button and /settings show the settings at the top of the main region (140: right under
    // the capture box); Close hides them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_ButtonOrPalette_ShowsAtTheTop_CloseHides(bool palette)
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".settings-panel"));
        Assert.Equal("Settings", cut.Find(".notes-region button.settings-open").GetAttribute("aria-label"));

        if (palette)
        {
            await RunPalette(cut, "/settings");
        }
        else
        {
            await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        }

        Assert.Equal("top-bar", cut.Find("main.current-region").Children[0].ClassName);
        IElement panel = cut.Find("main.current-region").Children[1];
        Assert.True(panel.ClassList.Contains("settings-panel"));
        Assert.Equal("Settings", panel.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Exchange calendar", "Sensitive notes"], panel.QuerySelectorAll("h3").Select(h => h.TextContent).ToArray());
        Assert.Empty(cut.FindAll(".palette"));

        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".settings-panel"));
    }

    // 193 AC-1, AC-2, AC-3, AC-8: each page replaces the note area and the others; the x closes it.
    [Theory]
    [InlineData("triage-open", ".triage-panel")]
    [InlineData("tags-open", ".tags-panel")]
    [InlineData("settings-open", ".settings-panel")]
    public async Task Page_OpensInPlaceOfTheNoteArea_CloseShowsItAgain(string button, string page)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        await Open(cut, js, "Plan");

        Assert.Single(cut.FindAll("button.page-menu-open"));
        await cut.Find("button." + button).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(page).TextContent, StringComparison.Ordinal), Wait);

        Assert.Empty(cut.FindAll("button.page-menu-open"));
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Single(cut.FindAll(".page"));
        IElement close = cut.Find(".page > .panel-bar button.close");
        Assert.Equal("Close", close.GetAttribute("aria-label"));
        Assert.Equal("Close", close.GetAttribute("title"));
        Assert.Equal(string.Empty, close.TextContent);
        Assert.NotNull(close.QuerySelector("svg"));
        Assert.Equal("close", cut.Find(".page > .panel-bar").LastElementChild!.ClassName);
        Assert.Single(cut.FindAll(".page > .panel-bar button.close"));

        await close.ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".page"));
        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Single(cut.FindAll("button.page-menu-open"));
    }

    // 200 AC-7: Triage has no … button; its choices and × stay on the page.
    [Fact]
    public async Task Page_Triage_NoMenuButton_ChoicesStay()
    {
        data.WriteNote("inbox", "# inbox\n- call Anna\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Single(cut.FindAll("button.page-menu-open"));

        await cut.Find("button.triage-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".triage-panel .triage-item")), Wait);

        Assert.Empty(cut.FindAll("button.page-menu-open"));
        Assert.Equal(
            ["triage-note", "triage-todo", "triage-append", "triage-drop", "triage-later"],
            cut.FindAll(".triage-panel .triage-buttons > button").Select(b => b.ClassName ?? string.Empty).ToArray());
        Assert.Single(cut.FindAll(".triage-panel > .panel-bar button.close"));
    }

    // 193 AC-1: with the current view shown, the view hides and returns.
    [Fact]
    public async Task Page_OverTheCurrentView_HidesAndRestoresIt()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.tags-open").ClickAsync(new MouseEventArgs());
        Assert.True(cut.Find(".note-area").HasAttribute("hidden"));
        await cut.Find(".page > .panel-bar button.close").ClickAsync(new MouseEventArgs());

        Assert.False(cut.Find(".note-area").HasAttribute("hidden"));
    }

    // 193 AC-2: opening a second page (button or palette) replaces the first.
    [Fact]
    public async Task Page_OpeningAnother_ReplacesTheFirst()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.tags-open").ClickAsync(new MouseEventArgs());
        await cut.Find("button.triage-open").ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(".page"));
        Assert.Single(cut.FindAll(".triage-panel"));

        await RunPalette(cut, "/settings");
        Assert.Single(cut.FindAll(".page"));
        Assert.Single(cut.FindAll(".settings-panel"));

        await RunPalette(cut, "/tags");
        Assert.Single(cut.FindAll(".page"));
        Assert.Single(cut.FindAll(".tags-panel"));

        await RunPalette(cut, "/triage");
        Assert.Single(cut.FindAll(".page"));
        Assert.Single(cut.FindAll(".triage-panel"));
    }

    // 193 AC-4: opening and closing a page keeps unsaved text, writes nothing and asks nothing; another note still asks.
    [Fact]
    public async Task Page_DirtyEditor_SurvivesOpenAndClose_OtherNoteStillAsks()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, "context.db");

        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        await cut.Find(".page > .panel-bar button.close").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, "context.db"));
        Assert.Single(cut.FindAll(".note-editor"));

        await cut.Find("button.tags-open").ClickAsync(new MouseEventArgs());
        await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());
        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Other", cut.Find(".note-title").TextContent);
            Assert.Empty(cut.FindAll(".page"));
        }, Wait);
    }

    // 193 AC-5: Esc closes Triage.
    [Fact]
    public async Task Page_TriageEscape_ClosesIt()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find("button.triage-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".triage-panel").TextContent, StringComparison.Ordinal), Wait);

        await cut.Find(".triage-panel").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".page"));
    }

    // 138 AC-1: opening a note or the current view hides the settings.
    [Fact]
    public async Task Settings_OpeningANoteOrTheCurrentView_HidesThem()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(".settings-panel"));
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll(".settings-panel"));

        // The note that is already open hides them too.
        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(".settings-panel"));
        await NameButton(cut, "Plan").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".settings-panel")), Wait);

        await RunPalette(cut, "/settings");
        Assert.Single(cut.FindAll(".settings-panel"));
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".settings-panel")), Wait);
    }

    // 138 AC-1: opening the settings changes no file and sends no request, with or without a link stored.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_Opening_ChangesNoFileAndSendsNoRequest(bool stored)
    {
        data.WriteNote("Plan", "# Plan\n");
        if (stored)
        {
            data.SaveLink(FakeHttpHandler.Link);
            data.WriteCache(DesktopFixture.Calendar(), new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));
        }

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        int requests = data.Http.Requests;

        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".settings-panel").TextContent, StringComparison.Ordinal), Wait);
        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(requests, data.Http.Requests);
        Assert.Equal(0, data.Keys.Agrees);
    }

    private static IElement NameButton(IRenderedComponent<Main> cut, string name) =>
        cut.FindAll(".notes-region button.name").Single(b => b.TextContent == name);

    // 139 AC-8 / AC-9: Export (button or palette) asks the OS where to save, suggesting <name>.md, and writes the
    // saved text there; a typed argument is never a path.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_ButtonOrPalette_WritesTheChosenFile(bool palette)
    {
        data.WriteNote("Plan", "# Plan\n\nText\n");
        string target = data.Outside("Copy.md");
        string typed = data.Outside("typed.md");
        data.Dialog.ExportPath = target;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        if (palette)
        {
            await RunPalette(cut, "/export " + typed);
        }
        else
        {
            await OpenMenuAsync(cut);
            IElement button = cut.Find(".page-menu button.export");
            Assert.Equal("Export", button.TextContent);
            Assert.Equal("button", button.GetAttribute("type"));
            await button.ClickAsync(new MouseEventArgs());
        }

        Assert.Equal(1, data.Dialog.ExportSaves);
        Assert.Equal("Plan.md", data.Dialog.SuggestedName);
        Assert.Equal("# Plan\n\nText\n", File.ReadAllText(target));
        Assert.False(File.Exists(typed));
        IElement status = cut.Find(".main-status");
        Assert.Equal($"Exported Plan to {target}", status.TextContent);
        Assert.Equal(status.TextContent, status.GetAttribute("title"));
        Assert.True(status.ClassList.Contains("accent"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 139 AC-8: a cancelled save dialog does nothing; a refusal shows the action's red text.
    [Fact]
    public async Task Export_CancelledThenExistingTarget_NothingThenRedText()
    {
        data.WriteNote("Plan", "text\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await ChooseAsync(cut, "export");

        Assert.Equal(1, data.Dialog.ExportSaves);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);

        string target = data.Outside("Taken.md");
        File.WriteAllText(target, "theirs\n");
        data.Dialog.ExportPath = target;
        await ChooseAsync(cut, "export");

        Assert.Equal(2, data.Dialog.ExportSaves);
        Assert.Equal("That file already exists; choose a new name: " + target, cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal("theirs\n", File.ReadAllText(target));
    }

    // 139 AC-8 / AC-9: with unsaved changes Export asks to save first and opens no dialog.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_DirtyEditor_SaveFirst_NoDialog(bool palette)
    {
        data.WriteNote("Plan", "text\n");
        data.Dialog.ExportPath = data.Outside("Copy.md");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("unsaved\n");

        if (palette)
        {
            await RunPalette(cut, "/export");
        }
        else
        {
            await ChooseAsync(cut, "export");
        }

        Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(0, data.Dialog.ExportSaves);
        Assert.False(File.Exists(data.Outside("Copy.md")));
    }

    // 139 AC-9: /export with no note open is a red line and opens no dialog.
    [Fact]
    public async Task PaletteExport_NoNoteOpen_RedLine()
    {
        data.WriteNote("Plan", "text\n");
        data.Dialog.ExportPath = data.Outside("Copy.md");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/export Plan " + data.Outside("Copy.md"));

        Assert.Equal("Open a note first.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(0, data.Dialog.ExportSaves);
        Assert.False(File.Exists(data.Outside("Copy.md")));
    }

    // 139 AC-9: /import does what the Import button does; the typed argument is ignored and never used as a path.
    [Fact]
    public async Task PaletteImport_ChoosesFilesInTheDialog_IgnoresTheArgument()
    {
        string typed = data.Outside("Typed.md");
        File.WriteAllText(typed, "typed\n");
        string chosen = data.Outside("Chosen.md");
        File.WriteAllText(chosen, "chosen\n");
        data.Dialog.ImportPaths = [chosen];
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/import " + typed);

        Assert.Equal(1, data.Dialog.ImportOpens);
        Assert.Equal(["Chosen.md"], FileSnapshot.Take(data.Root, TagIndex.FileName).Keys);
        cut.WaitForAssertion(() => Assert.StartsWith(
            "Imported Chosen.", Assert.Single(cut.FindAll(".import-result p")).TextContent, StringComparison.Ordinal), Wait);
        cut.WaitForAssertion(() => Assert.Contains("Chosen", cut.Find(".note-list").TextContent), Wait);
    }

    // 139 AC-9: /import with a cancelled dialog changes nothing; a replacing file asks in the same dialog as the button.
    [Fact]
    public async Task PaletteImport_CancelledThenReplacing_NothingThenAsks()
    {
        data.WriteNote("Plan", "old\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/import");

        Assert.Equal(1, data.Dialog.ImportOpens);
        Assert.Empty(cut.FindAll(".import-result p"));
        Assert.Equal("status", cut.Find(".import-result").GetAttribute("role"));

        string source = data.Outside("Plan.md");
        File.WriteAllText(source, "new\n");
        data.Dialog.ImportPaths = [source];
        await RunPalette(cut, "/import");

        cut.WaitForAssertion(() => Assert.Equal("Replace Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.Equal("old\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Equal("new\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        cut.WaitForAssertion(() => Assert.StartsWith(
            "Replaced Plan.", Assert.Single(cut.FindAll(".import-result p")).TextContent, StringComparison.Ordinal), Wait);
    }

    // 177 AC-6: one OnDrop stores the file, inserts its link once and shows the accent line.
    [Fact]
    public async Task Drop_AttachesInsertsAndShowsLine()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnDrop(1, [new DroppedFile("shot.png", png.Length, png)]));

        cut.WaitForAssertion(() => Assert.Equal("Attached shot.png.", cut.Find(".note-editor .status .line").TextContent), Wait);
        Assert.True(cut.Find(".note-editor .status .line").ClassList.Contains("accent"));
        JSRuntimeInvocation insert = Assert.Single(js.Calls("ContextEditor.insertAttachment"));
        Assert.Equal(
            [".attachments/shot.png", "shot.png", true, "image/png", Convert.ToBase64String(png)],
            insert.Arguments.Skip(1));
        Assert.Equal(png, File.ReadAllBytes(Path.Combine(data.Root, ".attachments", "shot.png")));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // 177 AC-6: a drop of more than 10 files shows one red line and inserts nothing.
    [Fact]
    public async Task Drop_OverTheLimit_RedLine()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnDrop(11, []));

        cut.WaitForAssertion(() => Assert.Equal("Drop at most 10 files.", cut.Find(".note-editor .status").TextContent.Trim()), Wait);
        Assert.True(cut.Find(".note-editor .status").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.insertAttachment"));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // 177 AC-8: opening a note mounts it with its resolved images only.
    [Fact]
    public async Task Open_MountsResolvedImages()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        data.WriteNote("Plan", "# Plan\n\n![shot.png](.attachments/shot.png) ![x](.attachments/fake.png) ![y](other.png)\n");
        Directory.CreateDirectory(Path.Combine(data.Root, ".attachments"));
        File.WriteAllBytes(Path.Combine(data.Root, ".attachments", "shot.png"), png);
        File.WriteAllText(Path.Combine(data.Root, ".attachments", "fake.png"), "<html></html>");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await OpenFull(cut, js, "Plan");

        InlineImage image = Assert.Single(js.MountedImages());
        Assert.Equal((".attachments/shot.png", "image/png", Convert.ToBase64String(png)), (image.Src, image.Mime, image.Base64));
    }

    // 177 AC-9: a click on a PDF link launches it; anything else shows the red text in the main status.
    [Fact]
    public async Task AttachmentClick_LaunchesPdf_OrRedLine()
    {
        data.WriteNote("Plan", "# Plan\n\n[plan.pdf](.attachments/plan.pdf)\n");
        Directory.CreateDirectory(Path.Combine(data.Root, ".attachments"));
        File.WriteAllText(Path.Combine(data.Root, ".attachments", "plan.pdf"), "%PDF-1.7\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnAttachment(".attachments/plan.pdf"));
        cut.WaitForAssertion(() => Assert.Single(data.Launcher.Launched), Wait);
        Assert.Equal(Path.Combine(data.Root, ".attachments", "plan.pdf"), data.Launcher.Launched[0]);

        await cut.InvokeAsync(() => js.Page().OnAttachment(".attachments/missing.pdf"));
        cut.WaitForAssertion(() => Assert.Equal("Can't open missing.pdf.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Single(data.Launcher.Launched);
    }

    // 152: writes Meetings with the subpage Meetings/Meeting 1 (and Plan, Archive at the top level).
    private void WriteMeetings()
    {
        data.WriteNote("Meetings", "# Meetings\n");
        data.WriteFile(Path.Combine("Meetings", "Meeting 1.md"), "# Meeting 1\n");
        data.WriteNote("Archive", "# Archive\n");
    }

    // Waits until the element the selector finds (read in the render that created it) was given the focus.
    private void AssertFocused(IRenderedComponent<Main> cut, string selector)
    {
        string? id = cut.Find(selector).GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(id));
        cut.WaitForAssertion(
            () => Assert.Contains(
                data.Bunit.JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus"),
                i => i.Arguments[0] is ElementReference r && r.Id == id),
            Wait);
    }

    // Opens a note by its full name: expands its ancestors in the tree, then clicks its row.
    private static async Task OpenFull(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int slash = name.IndexOf('/');
        while (slash > 0)
        {
            string ancestor = name[..slash];
            if (cut.FindAll($".notes-region button.toggle[aria-label='Expand {ancestor}']").Count > 0)
            {
                await cut.Find($".notes-region button.toggle[aria-label='Expand {ancestor}']").ClickAsync(new MouseEventArgs());
            }

            slash = name.IndexOf('/', slash + 1);
        }

        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".notes-region button.name").Single(b => b.GetAttribute("title") == name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);

        // 175: the links panel loads behind Task.Run on its own schedule; its render re-parses the whole markup, so
        // a test that reads the page before it lands can compare elements from two renders. Wait until it is in.
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    // 152 AC-14: a subpage shows the breadcrumb above the toolbar; a top-level note shows none.
    [Fact]
    public async Task Breadcrumb_ChildOnly_AncestorsAsButtons_CurrentAsText()
    {
        WriteMeetings();
        data.WriteFile(Path.Combine("Meetings", "Meeting 1", "Notes.md"), "# Notes\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Meetings");
        Assert.Empty(cut.FindAll("nav.breadcrumb"));

        await OpenFull(cut, js, "Meetings/Meeting 1/Notes");

        cut.WaitForAssertion(() => Assert.Equal("Meetings/Meeting 1/Notes", cut.Find(".note-title").TextContent), Wait);
        IElement nav = cut.Find("nav[aria-label=Breadcrumb]");
        IReadOnlyList<IElement> crumbs = nav.QuerySelectorAll("button.crumb").ToList();
        Assert.Equal(["Meetings", "Meeting 1"], crumbs.Select(c => c.TextContent).ToArray());
        Assert.Equal(["Meetings", "Meetings/Meeting 1"], crumbs.Select(c => c.GetAttribute("title") ?? string.Empty).ToArray());
        Assert.All(crumbs, c => Assert.Equal("button", c.GetAttribute("type")));
        IElement current = nav.QuerySelector("[aria-current=page]")!;
        Assert.Equal("Notes", current.TextContent);
        Assert.Equal("SPAN", current.TagName);
        Assert.Equal(nav, cut.Find(".note-area").FirstElementChild);
        Assert.Empty(cut.FindAll(".note-toolbar"));

        await cut.Find("nav.breadcrumb button.crumb[title='Meetings/Meeting 1']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Meetings/Meeting 1", cut.Find(".note-title").TextContent), Wait);
    }

    // 152 AC-14: an ancestor crumb opens through RequestOpen, so a dirty editor asks first.
    [Fact]
    public async Task Breadcrumb_DirtyEditor_AsksFirst()
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");
        js.EditUnsavable("# Meeting 1\n\nUnsaved\n");

        await cut.Find("nav.breadcrumb button.crumb").ClickAsync(new MouseEventArgs());

        Assert.Equal("Discard changes to Meetings/Meeting 1?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Meetings", cut.Find(".note-title").TextContent), Wait);
    }

    // 152 AC-15: New subpage swaps in the title field; Enter or Create subpage creates and opens it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewSubpage_EnterOrButton_CreatesAndOpens(bool enter)
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Meetings");

        await ChooseAsync(cut, "new-subpage");
        IElement field = cut.Find(".note-toolbar input.subpage-title");
        AssertFocused(cut, "input.subpage-title");
        Assert.True(cut.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.Equal("Subpage title", field.GetAttribute("aria-label"));
        Assert.Equal("Subpage title", field.GetAttribute("placeholder"));
        Assert.Equal(string.Empty, field.GetAttribute("value"));
        Assert.Equal(["Cancel"], cut.FindAll(".note-toolbar button").Select(b => b.TextContent).ToArray());
        await field.InputAsync(new ChangeEventArgs { Value = "Meeting 2" });
        if (enter)
        {
            await cut.Find(".subpage-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find(".note-toolbar .subpage-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        Assert.Equal("# Meeting 2\n", File.ReadAllText(Path.Combine(data.Root, "Meetings", "Meeting 2.md")));
        cut.WaitForAssertion(() => Assert.Equal("Meetings/Meeting 2", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("Created Meetings/Meeting 2", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Empty(cut.FindAll(".subpage-title"));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".notes-region button.name"), b => b.GetAttribute("title") == "Meetings/Meeting 2"), Wait);
    }

    // 152 AC-15: Esc or Cancel restores the toolbar; a refusal shows red and keeps the field.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewSubpage_EscCancelOrRefused(bool esc)
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Meetings");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await ChooseAsync(cut, "new-subpage");
        await cut.Find(".subpage-title").InputAsync(new ChangeEventArgs { Value = "meeting 1" });

        await cut.Find(".note-toolbar .subpage-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Note Meetings/meeting 1 already exists.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal("meeting 1", cut.Find(".subpage-title").GetAttribute("value"));

        if (esc)
        {
            await cut.Find(".subpage-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find(".note-toolbar button.cancel-subpage").ClickAsync(new MouseEventArgs());
        }

        Assert.Empty(cut.FindAll(".subpage-title"));
        Assert.Empty(cut.FindAll(".note-toolbar"));
        Assert.False(cut.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 152 AC-15 / AC-16: with unsaved changes New subpage and Move are refused before any field opens.
    [Theory]
    [InlineData("new-subpage")]
    [InlineData("move")]
    public async Task NewSubpageAndMove_DirtyEditor_SaveFirst(string button)
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        js.EditUnsavable("# Meeting 1\n\nUnsaved\n");

        await ChooseAsync(cut, button);

        Assert.Equal("Save Meetings/Meeting 1 first.", cut.Find(".main-status").TextContent);
        Assert.Empty(cut.FindAll(".note-toolbar input"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 152 AC-16: Move is prefilled with the parent; Move here reopens the note under its new name.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Move_PrefilledWithParent_MovesAndReopens(bool enter)
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");

        await ChooseAsync(cut, "move");
        IElement field = cut.Find(".note-toolbar input.move-parent");
        AssertFocused(cut, "input.move-parent");
        Assert.True(cut.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.Equal("New parent", field.GetAttribute("aria-label"));
        Assert.Equal("Meetings", field.GetAttribute("value"));
        Assert.Equal(["Cancel"], cut.FindAll(".note-toolbar button").Select(b => b.TextContent).ToArray());
        await field.InputAsync(new ChangeEventArgs { Value = "archive" });
        if (enter)
        {
            await cut.Find(".move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find(".note-toolbar .move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        Assert.Equal("# Meeting 1\n", File.ReadAllText(Path.Combine(data.Root, "Archive", "Meeting 1.md")));
        cut.WaitForAssertion(() => Assert.Equal("Archive/Meeting 1", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("Moved to Archive/Meeting 1", cut.Find(".main-status").TextContent);
        Assert.Empty(cut.FindAll(".move-parent"));
    }

    // 152 AC-16: an empty field moves to the top level; a top-level note's field starts empty.
    [Fact]
    public async Task Move_Empty_ToTopLevel()
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");
        await ChooseAsync(cut, "move");
        await cut.Find(".move-parent").InputAsync(new ChangeEventArgs { Value = "" });

        await cut.Find(".note-toolbar .move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("Meeting 1", cut.Find(".note-title").TextContent), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "Meeting 1.md")));
        Assert.Empty(cut.FindAll("nav.breadcrumb"));
        await ChooseAsync(cut, "move");
        Assert.Equal(string.Empty, cut.Find(".move-parent").GetAttribute("value"));
    }

    // 152 AC-16: an unknown parent is refused in red, nothing moves; Esc or Cancel restores the toolbar.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Move_UnknownParent_RedLine_ThenCancel(bool esc)
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Archive");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await ChooseAsync(cut, "move");
        await cut.Find(".move-parent").InputAsync(new ChangeEventArgs { Value = "Nowhere" });

        await cut.Find(".note-toolbar .move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("No note called Nowhere. Nothing was moved.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        if (esc)
        {
            await cut.Find(".move-parent").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find(".note-toolbar button.cancel-move").ClickAsync(new MouseEventArgs());
        }

        Assert.Empty(cut.FindAll(".move-parent"));
        Assert.Equal("Archive", cut.Find(".note-title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 152 AC-16: Rename of a subpage prefills its title and reopens it under the new full name.
    [Fact]
    public async Task Rename_Subpage_PrefillsTitle_ReopensFullName()
    {
        WriteMeetings();
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");

        await ChooseAsync(cut, "rename");
        cut.WaitForAssertion(() => Assert.Equal("Meeting 1", cut.Find(".rename-title").GetAttribute("value")), Wait);
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Kickoff" });
        await cut.Find(".note-toolbar .rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(File.Exists(Path.Combine(data.Root, "Meetings", "Kickoff.md")));
        cut.WaitForAssertion(() => Assert.Equal("Meetings/Kickoff", cut.Find(".note-title").TextContent), Wait);
    }

    // 152 AC-17: a note with subpages asks with their count; on confirm the whole subtree is gone.
    [Theory]
    [InlineData(1, "Delete Meetings and its 1 subpage? This can't be undone.")]
    [InlineData(2, "Delete Meetings and its 2 subpages? This can't be undone.")]
    public async Task Delete_WithSubpages_AsksWithCount_DeletesSubtree(int count, string question)
    {
        WriteMeetings();
        if (count == 2)
        {
            data.WriteFile(Path.Combine("Meetings", "Meeting 1", "Notes.md"), "# Notes\n");
        }

        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Meetings");

        await ChooseAsync(cut, "delete");

        Assert.Equal(question, cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.False(File.Exists(Path.Combine(data.Root, "Meetings.md")));
        Assert.False(Directory.Exists(Path.Combine(data.Root, "Meetings")));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".current-region .current")), Wait);
        Assert.Equal(["Archive"], data.State!.Snapshot!.Notes.Select(n => n.Name).ToArray());
    }

    // 152 AC-12, 200 AC-2: a sensitive note's menu has exactly its items: no New subpage and no Move.
    [Fact]
    public async Task SensitiveNote_NoNewSubpageOrMove()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "# Secret\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await NameButton(cut, "Secret").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".links-panel p"), p => p.TextContent == "Sensitive notes have no links."), Wait);
        Assert.Empty(cut.FindAll(".note-toolbar"));
        Assert.Equal(["rename", "delete", "history", "make-plain", "lock"], await MenuIdsAsync(cut));
    }

    // 152 AC-14 / AC-16: Export of a subpage suggests its title as the file name.
    [Fact]
    public async Task Export_Subpage_SuggestsTitle()
    {
        WriteMeetings();
        data.Dialog.ExportPath = data.Outside("m1.md");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Meetings/Meeting 1");

        await ChooseAsync(cut, "export");

        Assert.Equal("Meeting 1.md", data.Dialog.SuggestedName);
        Assert.Equal("# Meeting 1\n", File.ReadAllText(data.Outside("m1.md")));
    }

    // 174 AC-12: History shows the panel under the editor and hides it again; another note starts without it.
    [Fact]
    public async Task History_Button_TogglesThePanel()
    {
        data.WriteNote("Plan", "now\n");
        data.WriteNote("Other", "o\n");
        WriteHistoryVersion("Plan", "20261029T090000000Z", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll(".history-panel"));

        await ChooseAsync(cut, "history");

        cut.WaitForAssertion(() => Assert.Equal(["2026-10-29 10:00:00"], cut.FindAll(".history-row button").Select(b => b.TextContent)), Wait);
        await OpenMenuAsync(cut);
        Assert.Equal("true", cut.Find(".page-menu button.history").GetAttribute("aria-checked"));
        await cut.Find(".page-menu button.history").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".history-panel"));
        Assert.Empty(cut.FindAll(".page-menu"));
        await OpenMenuAsync(cut);
        Assert.Equal("false", cut.Find(".page-menu button.history").GetAttribute("aria-checked"));
        await cut.Find(".page-menu-backdrop").ClickAsync(new MouseEventArgs());

        await ChooseAsync(cut, "history");
        await Open(cut, js, "Other");
        Assert.Empty(cut.FindAll(".history-panel"));
    }

    // 174 AC-13: Restore asks first; on confirm the version is saved, the editor reloads and the status says so.
    [Fact]
    public async Task Restore_AsksThenRestores_AndTheEditorReloads()
    {
        data.WriteNote("Plan", "now\n");
        WriteHistoryVersion("Plan", "20261029T090000000Z", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseFirstVersion(cut);

        await cut.Find("button.restore-version").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            "Restore Plan to the version from 2026-10-29 10:00:00? The current text is kept as a version.",
            cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Restore", cut.Find("[role=dialog] button.confirm").TextContent);
        Assert.Equal("now\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Restored Plan to 2026-10-29 10:00:00.", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("accent", cut.Find(".main-status").GetAttribute("class"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("old\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("now\n", File.ReadAllText(Path.Combine(data.Root, ".history", "Plan", "20261029T090000000Z-1.md")));
        Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count);
        Assert.Equal("old\n", js.MountedText());
    }

    // 174 AC-13: Cancel writes nothing; a dirty editor is refused before anything is asked.
    [Fact]
    public async Task Restore_CancelOrDirty_WritesNothing()
    {
        data.WriteNote("Plan", "now\n");
        WriteHistoryVersion("Plan", "20261029T090000000Z", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await ChooseFirstVersion(cut);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find("button.restore-version").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));

        js.EditUnsavable("typed\n");
        await cut.Find("button.restore-version").ClickAsync(new MouseEventArgs());

        Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 203 AC-7: with unsaved changes, opening another note, a mention, /today and /briefing save first; the target
    // opens with no Discard question and the file holds the edited text.
    [Theory]
    [InlineData("open")]
    [InlineData("mention")]
    [InlineData("today")]
    [InlineData("briefing")]
    public async Task Leave_DirtyEditor_SavesFirstThenOpens(string how)
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("@[Anna]\n\nSaved first\n");

        switch (how)
        {
            case "open":
                await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());
                break;
            case "mention":
                await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));
                break;
            default:
                await RunPalette(cut, "/" + how);
                break;
        }

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("@[Anna]\n\nSaved first\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("@[Anna]\n\nSaved first\n", Assert.Single(js.Calls("ContextEditor.markCleanIf")).Arguments[1]);
    }

    // 203 AC-7: when that save fails, the Discard question appears as before, next to the editor's red text.
    [Fact]
    public async Task Leave_DirtyEditor_SaveFails_AsksNextToTheRedText()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nMine\n");
        data.WriteNote("Plan", "# Plan\n\nElsewhere\n");

        await NameButton(cut, "Other").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), cut.Find(".note-editor .status").TextContent);
        Assert.Contains("red", cut.Find(".note-editor .status").ClassList);
        Assert.Equal("# Plan\n\nElsewhere\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Single(js.Calls("ContextEditor.mount"));
    }

    // 203 AC-8: Rename, New subpage, Move, Export, History's Restore and Mark sensitive save first and go on; they
    // refuse only when that save fails (the EditUnsavable tests above).
    [Theory]
    [InlineData("rename", ".rename-title")]
    [InlineData("new-subpage", ".note-toolbar input")]
    [InlineData("move", "input.move-parent")]
    [InlineData("mark-sensitive", "[role=dialog]")]
    [InlineData("export", null)]
    [InlineData("restore", "[role=dialog]")]
    public async Task SaveFirstActions_DirtyEditor_SaveThenGoOn(string button, string? shown)
    {
        data.WriteNote("Plan", "# Plan\n");
        data.Keys = FakeKeyStore.Ready();
        data.Dialog.ExportPath = data.Outside("Copy.md");
        WriteHistoryVersion("Plan", "20261029T080000000Z", "old\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        if (button == "restore")
        {
            await ChooseFirstVersion(cut);
        }

        js.Edit("# Plan\n\nSaved first\n");

        if (button == "restore")
        {
            await cut.Find("button.restore-version").ClickAsync(new MouseEventArgs());
        }
        else
        {
            await ChooseAsync(cut, button);
        }

        Assert.Equal("# Plan\n\nSaved first\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.DoesNotContain("first.", cut.Find(".main-status").TextContent, StringComparison.Ordinal);
        if (shown is null)
        {
            cut.WaitForAssertion(() => Assert.Equal(1, data.Dialog.ExportSaves), Wait);
            Assert.Equal("# Plan\n\nSaved first\n", File.ReadAllText(data.Outside("Copy.md")));
        }
        else
        {
            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(shown)), Wait);
        }
    }

    // 203 AC-8: a capture into the open inbox with unsaved changes saves them first, then adds the line.
    [Fact]
    public async Task Capture_DirtyOpenInbox_SavesFirstThenCaptures()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        js.Edit("# inbox\n\nTyped\n");

        await RunPalette(cut, "/capture call Anna");

        cut.WaitForAssertion(() => Assert.Equal("Captured.", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal("# inbox\n\nTyped\n- 2026-10-29 10:05 call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // 203 AC-6: /save saves at once with the accent text and cancels the pending pause.
    [Fact]
    public async Task PaletteSave_SavesAtOnceAndCancelsThePause()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await js.Change(cut, "# Plan\n\nBy palette\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        await RunPalette(cut, "/save");

        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Contains("accent", cut.Find(".note-editor .status").ClassList);
        Assert.Equal("# Plan\n\nBy palette\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(0, data.AutoSave.Pending);
        data.AutoSave.Elapse();
        await cut.InvokeAsync(() => { });
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
    }

    // 203 AC-9: closing the window with unsaved changes saves them and then closes once; with nothing unsaved the
    // close goes on at once.
    [Fact]
    public async Task WindowClose_Dirty_SavesThenClosesOnce()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        Assert.False(data.Guard.Closing());
        await js.Change(cut, "# Plan\n\nClosing\n");

        Assert.True(data.Guard.Closing());

        // 204: the close is counted off the renderer after the last render, so await it rather than a render.
        await data.Closed.WaitAsync(Wait);
        Assert.Equal(1, data.CloseCalls);
        Assert.Equal("# Plan\n\nClosing\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(data.Guard.Closing());
        Assert.Equal(1, data.CloseCalls);
    }

    // 204 AC-1/AC-3: the close counted only after the save's last render (status Saved.), with no render after it,
    // is still seen; a render-driven wait on the count timed out here (CI run 44204937842).
    [Fact]
    public async Task WindowClose_ClosedAfterLastRender_IsStillSeen()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await js.Change(cut, "# Plan\n\nClosing\n");
        TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        data.CloseAfter = () =>
        {
            asked.TrySetResult();
            return gate.Task;
        };

        Assert.True(data.Guard.Closing());

        await asked.Task.WaitAsync(Wait);
        Assert.Equal("Saved.", cut.Find(".note-editor .status").TextContent);
        Assert.Equal(0, data.CloseCalls);
        int renders = cut.RenderCount;
        gate.SetResult();

        await data.Closed.WaitAsync(Wait);
        Assert.Equal(1, data.CloseCalls);
        Assert.Equal(renders, cut.RenderCount);
        Assert.Equal("# Plan\n\nClosing\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(data.Guard.Closing());
    }

    // 203 AC-9: when the save before closing fails, the Discard question is shown; Cancel keeps the window open and
    // Discard closes it.
    [Fact]
    public async Task WindowClose_SaveFails_AsksCancelKeepsDiscardCloses()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await js.Change(cut, "# Plan\n\nMine\n");
        data.WriteNote("Plan", "# Plan\n\nElsewhere\n");

        Assert.True(data.Guard.Closing());

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), cut.Find(".note-editor .status").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(0, data.CloseCalls);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);

        Assert.True(data.Guard.Closing());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog] button.confirm")), Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, data.CloseCalls);
        Assert.Equal("# Plan\n\nElsewhere\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.False(data.Guard.Closing());
    }

    private static async Task ChooseFirstVersion(IRenderedComponent<Main> cut)
    {
        await ChooseAsync(cut, "history");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".history-row button")), Wait);
        await cut.Find(".history-row button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.restore-version")), Wait);
    }

    private void WriteHistoryVersion(string name, string id, string text)
    {
        string folder = Path.Combine(data.Root, NoteHistory.FolderName, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".md"), text);
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NameButton(cut, name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);

        // 200: the links panel loads behind Task.Run; wait until it is in before the menu is opened or compared.
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    // 200: opens the top bar's More actions menu.
    internal static async Task OpenMenuAsync(IRenderedComponent<Main> cut)
    {
        await cut.Find("button.page-menu-open").ClickAsync(new MouseEventArgs());
        Assert.Single(cut.FindAll(".page-menu[role=menu]"));
    }

    // 200: chooses a page action through the More actions menu (the item's class is the action's id).
    internal static async Task ChooseAsync(IRenderedComponent<Main> cut, string id)
    {
        await OpenMenuAsync(cut);
        await cut.Find(".page-menu button." + id).ClickAsync(new MouseEventArgs());
    }

    // 200: the ids of the menu's items; the menu is closed again afterwards.
    private static async Task<string[]> MenuIdsAsync(IRenderedComponent<Main> cut)
    {
        await OpenMenuAsync(cut);
        string[] ids = cut.FindAll(".page-menu button").Select(b => b.ClassName ?? string.Empty).ToArray();
        await cut.Find(".page-menu-backdrop").ClickAsync(new MouseEventArgs());
        return ids;
    }

    // 198 AC-1: the top bar is the first thing in the main region, whatever else is shown there.
    [Fact]
    public async Task TopBar_AlwaysAtTheTopOfTheMainRegion()
    {
        data.WriteNote("Plan", "# Plan\n\n#work\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        void AssertBox()
        {
            IElement first = cut.Find("main.current-region").Children[0];
            Assert.Equal("top-bar", first.ClassName);
            Assert.Single(cut.FindAll(".top-bar"));
            Assert.Empty(cut.FindAll(".capture-box"));
            Assert.Empty(cut.FindAll("input[aria-label=Capture]"));
        }

        AssertBox();
        await RunPalette(cut, "/settings");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".settings-panel")), Wait);
        AssertBox();
        await RunPalette(cut, "/tags");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tags-panel")), Wait);
        AssertBox();
        await RunPalette(cut, "/tags work");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tag-panel")), Wait);
        AssertBox();
        await Open(cut, js, "Plan");
        Assert.Single(cut.FindAll(".note-editor"));
        AssertBox();
    }

    // 198 AC-1: no top bar when the data folder can't be read.
    [Fact]
    public async Task TopBar_NotShownWhenTheFolderCantBeRead()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("boom");

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("Can't read the data folder.", cut.Find(".failed").TextContent);
        Assert.Empty(cut.FindAll(".top-bar"));
        Assert.Empty(cut.FindAll(".capture-box"));
    }

    // 140 AC-8: with unsaved changes in the open inbox a capture (box or palette) writes nothing and says to save.
    [Theory]
    [InlineData("inbox")]
    [InlineData("Inbox")]
    public async Task Capture_DirtyOpenInbox_RefusedNothingWritten(string name)
    {
        data.WriteNote(name, "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, name);
        js.EditUnsavable("# inbox\n\nUnsaved\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        int mounts = js.Calls("ContextEditor.mount").Count;

        await RunPalette(cut, "/capture call Anna");
        IElement status = cut.Find(".main-status");

        Assert.Equal($"Save {name} first.", status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
    }

    // 140 AC-8: with the inbox open and no unsaved changes the capture is written and the editor is mounted again
    // with the saved text (without taking the focus), so a later save is not refused as changed on disk.
    [Fact]
    public async Task Capture_CleanOpenInbox_RemountsWithTheNewLine_ThenSaves()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        int mounts = js.Calls("ContextEditor.mount").Count;
        int focuses = js.Calls("ContextEditor.focus").Count;

        await RunPalette(cut, "/capture call Anna");
        Assert.Equal("Captured.", cut.Find(".main-status").TextContent);

        string captured = "# inbox\n- 2026-10-29 10:05 call Anna\n";
        Assert.Equal(captured, File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal(captured, js.MountedText());
        Assert.Equal(focuses, js.Calls("ContextEditor.focus").Count);

        js.Edit(captured + "\nMore\n");
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal("Saved inbox.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Equal(captured + "\nMore\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // 140 AC-8: with another note open and dirty, the capture is written and that editor is left alone: not
    // mounted again, and its unsaved text still saves.
    [Fact]
    public async Task Capture_OtherDirtyNoteOpen_EditorUntouched()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nUnsaved\n");
        int mounts = js.Calls("ContextEditor.mount").Count;

        await RunPalette(cut, "/capture call Anna");

        Assert.Equal("Captured.", cut.Find(".main-status").TextContent);
        Assert.Equal("# inbox\n- 2026-10-29 10:05 call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
        Assert.Empty(js.Calls("ContextEditor.destroy"));

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Equal("# Plan\n\nUnsaved\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // 140 AC-9: /capture <text> in the palette captures and shows the result in the main status line.
    [Fact]
    public async Task PaletteCapture_WritesAndShowsCaptured()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/capture idea  #work");

        Assert.Equal("# inbox\n- 2026-10-29 10:05 idea  #work\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal("Captured.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Empty(cut.FindAll(".capture-status"));
    }

    // 140 AC-9: /capture without text is a red usage line; a refusal shows its red text; both write nothing.
    [Theory]
    [InlineData("/capture", "Type /capture <text>.")]
    [InlineData("/capture   ", "Type /capture <text>.")]
    [InlineData("/capture long", "A capture can be at most 1000 characters.")]
    public async Task PaletteCapture_NoTextOrRefused_RedLineNothingWritten(string line, string text)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line.EndsWith("long", StringComparison.Ordinal) ? "/capture " + new string('x', 1001) : line);

        Assert.Equal(text, cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.False(Directory.Exists(data.Root));
    }

    // 140 AC-9: the palette lists /capture with its usage and description.
    [Fact]
    public async Task Palette_OffersCapture()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/capture" });

        IElement row = cut.FindAll(".palette-row")[0];
        Assert.Equal("/capture <text>", row.QuerySelector(".usage")!.TextContent);
        Assert.Equal("Append a dated line to the inbox", row.QuerySelector(".description")!.TextContent);
    }

    // 164 AC-8: /today creates today's note, opens it in the editor and shows the accent status; an argument is ignored.
    [Theory]
    [InlineData("/today")]
    [InlineData("/today extra words")]
    public async Task PaletteToday_CreatesOpensAndShowsCreated(string line)
    {
        data.AddTodo("call Anna", DesktopFixture.Today);
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- No appointments.\n\n## Todos\n- call Anna\n",
            File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md")));
        Assert.Equal("Created 2026-10-29", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29 extra words.md")));
    }

    // 164 AC-8: an existing daily note opens as it is, with no "Created" line.
    [Fact]
    public async Task PaletteToday_Existing_OpensUnchanged()
    {
        data.WriteNote("2026-10-29", "# mine\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/today");

        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Equal("# mine\n", File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md")));
        Assert.DoesNotContain("Created", cut.Find(".main-status").TextContent, StringComparison.Ordinal);
    }

    // 164 AC-8: with unsaved changes it asks first and writes nothing until the user discards them.
    [Fact]
    public async Task PaletteToday_DirtyEditor_AsksFirst()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/today");

        cut.WaitForAssertion(
            () => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29.md")));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "2026-10-29.md")));
        Assert.Equal("Created 2026-10-29", cut.Find(".main-status").TextContent);
    }

    // 164 AC-8: a write failure shows the fixed red line and opens nothing.
    [Fact]
    public async Task PaletteToday_WriteFails_RedLine()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        string path = Path.Combine(data.Root, "2026-10-29.md");
        data.Actions!.BeforeWrite = () => Directory.CreateDirectory(path);

        await RunPalette(cut, "/today");

        cut.WaitForAssertion(
            () => Assert.Equal("Can't write 2026-10-29. Nothing was done.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.False(File.Exists(path));
    }

    // 164 AC-8: a sensitive daily note opens through the unlock flow, never rewritten.
    [Fact]
    public async Task PaletteToday_SensitiveNote_OpensThroughUnlock()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("2026-10-29", "# secret day\n");
        data.Keys.AgreeThrows = true;
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        byte[] before = File.ReadAllBytes(Path.Combine(data.Root, "2026-10-29.ctxs"));

        await RunPalette(cut, "/today");

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock 2026-10-29.", cut.Find(".unreadable").TextContent), Wait);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(data.Root, "2026-10-29.ctxs")));
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29.md")));
    }

    // 172 AC-8: /briefing creates today's briefing, opens it and shows the accent status; an argument is ignored.
    [Theory]
    [InlineData("/briefing")]
    [InlineData("/briefing extra words")]
    public async Task PaletteBriefing_CreatesOpensAndShowsCreated(string line)
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal("Created briefings/2026-10-29", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.StartsWith(
            "# 2026-10-29\n\n## Appointments\n",
            File.ReadAllText(Path.Combine(data.Root, "briefings", "2026-10-29.md")),
            StringComparison.Ordinal);
        Assert.Null(data.State!.TakeBriefing());
        Assert.False(File.Exists(Path.Combine(data.Root, "briefings", "2026-10-29 extra words.md")));
    }

    // 172 AC-8: with unsaved changes it asks first and writes nothing until the user discards them.
    [Fact]
    public async Task PaletteBriefing_DirtyEditor_AsksFirst()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/briefing");

        cut.WaitForAssertion(
            () => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.False(File.Exists(Path.Combine(data.Root, "briefings.md")));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Created briefings/2026-10-29", cut.Find(".main-status").TextContent), Wait);
        Assert.True(File.Exists(Path.Combine(data.Root, "briefings", "2026-10-29.md")));
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
    }

    // 172 AC-8: keeping the unsaved changes writes nothing.
    [Fact]
    public async Task PaletteBriefing_DirtyEditor_KeepWritesNothing()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await RunPalette(cut, "/briefing");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog] button.cancel")), Wait);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.False(File.Exists(Path.Combine(data.Root, "briefings.md")));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
    }

    // 172 AC-8: a sensitive briefings note and a failed write show the fixed red lines and open nothing.
    [Fact]
    public async Task PaletteBriefing_SensitiveAndFailure_RedLines()
    {
        data.WriteFile("briefings.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/briefing");

        cut.WaitForAssertion(
            () => Assert.Equal("briefings is sensitive, so no briefing can be written.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));

        File.Delete(Path.Combine(data.Root, "briefings.ctxs"));
        data.WriteNote("briefings", "# briefings\n");
        data.WriteFile("briefings", "a file where the folder goes");
        await RunPalette(cut, "/briefing");

        cut.WaitForAssertion(
            () => Assert.Equal("Can't write briefings/2026-10-29.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
    }

    // 172 AC-8: the palette lists /briefing with its hint.
    [Fact]
    public async Task Palette_OffersBriefing()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "k", CtrlKey = true });

        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/briefing" });

        IElement row = cut.FindAll(".palette-row")[0];
        Assert.Equal("/briefing", row.QuerySelector(".usage")!.TextContent);
        Assert.Equal("Open today's briefing", row.QuerySelector(".description")!.TextContent);
    }

    // 172 AC-7: a briefing the automatic write offers opens when no note has unsaved changes.
    [Fact]
    public async Task OfferedBriefing_CleanEditor_Opens()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        ActionResult result = await data.Actions!.AutoBriefing();

        Assert.Equal(ActionReason.None, result.Reason);
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.NotEqual("Plan", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Null(data.State!.TakeBriefing());
    }

    // 172 AC-7: a briefing offered before Main renders (the start-up call) opens on the first render.
    [Fact]
    public async Task OfferedBriefing_BeforeRender_OpensFirst()
    {
        EditorJs js = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        await data.Actions!.AutoBriefing();

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Null(data.State!.TakeBriefing());
    }

    // 172 AC-7: with unsaved changes the editor is left alone and the accent status points to /briefing.
    [Fact]
    public async Task OfferedBriefing_DirtyEditor_ShowsReadyStatus()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nUnsaved\n");

        await data.Actions!.AutoBriefing();

        cut.WaitForAssertion(
            () => Assert.Equal(
                "Today's briefing is ready: briefings/2026-10-29. Type /briefing to open it.",
                cut.Find(".main-status").TextContent),
            Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    // 156 AC-10, AC-11: a mention opens the person page (creating people and the page), whose PersonPanel sits
    // above the links panel and lists the mentioning note; that note's button opens it again.
    [Fact]
    public async Task Mention_OpensPersonPage_PanelAboveLinks()
    {
        data.WriteNote("Plan", "Call @[Anna] about [[Other]]\n");
        data.WriteNote("Other", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll(".person-panel"));
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));

        cut.WaitForAssertion(() => Assert.Equal("people/Anna", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("# people\n", File.ReadAllText(Path.Combine(data.Root, "people.md")));
        Assert.Equal("# Anna\n\naliases::\naffiliation::\nrole::\n", File.ReadAllText(Path.Combine(data.Root, "people", "Anna.md")));
        cut.WaitForAssertion(() => Assert.Equal(
            ["Plan"], cut.FindAll(".person-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("Mentioned in", cut.Find(".person-panel h2").TextContent);
        IElement region = cut.Find(".note-area");
        int person = region.Children.Index().Single(c => c.Item.ClassList.Contains("person-panel")).Index;
        Assert.True(region.Children.Index().Single(c => c.Item.ClassList.Contains("note-editor")).Index < person);
        Assert.True(person < region.Children.Index().Single(c => c.Item.ClassList.Contains("links-panel")).Index);

        await cut.Find(".person-panel .note-button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".person-panel"));
    }

    // 156 AC-10: with unsaved changes a mention asks to discard first; nothing is created until the user agrees,
    // and keeping the edits creates nothing.
    [Fact]
    public async Task Mention_DirtyEditor_AsksFirst()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("@[Anna]\n\nUnsaved\n");

        await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        Assert.False(File.Exists(Path.Combine(data.Root, "people.md")));
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.False(File.Exists(Path.Combine(data.Root, "people.md")));

        await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("people/Anna", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("# Anna\n\naliases::\naffiliation::\nrole::\n", File.ReadAllText(Path.Combine(data.Root, "people", "Anna.md")));
    }

    // 159 AC-8: a mention in the todo list opens the person page through OpenMention (creating it on this click).
    [Fact]
    public async Task TodoMention_OpensPersonPage()
    {
        data.AddTodo("Call @[Anna] now", DesktopFixture.Today);
        _ = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".todos-region button.mention").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("people/Anna", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal("# Anna\n\naliases::\naffiliation::\nrole::\n", File.ReadAllText(Path.Combine(data.Root, "people", "Anna.md")));
        Assert.Empty(cut.FindAll(".todos-region input[type=checkbox][checked]"));
        cut.WaitForAssertion(() => Assert.Equal(
            ["Open todos"], cut.FindAll(".person-panel h2").Select(h => h.TextContent).ToArray()), Wait);
    }

    // 159 AC-8: with unsaved changes a todo mention asks to discard first, like an editor mention.
    [Fact]
    public async Task TodoMention_DirtyEditor_AsksFirst()
    {
        data.WriteNote("Plan", "x\n");
        data.AddTodo("Call @[Anna]", DesktopFixture.Today);
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("x\n\nUnsaved\n");

        await cut.Find(".todos-region button.mention").ClickAsync(new MouseEventArgs());

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        Assert.False(File.Exists(Path.Combine(data.Root, "people.md")));
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("people/Anna", cut.Find(".note-title").TextContent), Wait);
    }

    // 156 AC-10: a sensitive people note gives the red refusal; the open note stays and nothing is written.
    [Fact]
    public async Task Mention_SensitivePeople_RefusedRedLine()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("people", "# people\n");
        data.WriteNote("Plan", "@[Anna]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll(".red"), e => e.TextContent == "Can't make a page for Anna. Nothing was done."), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.False(Directory.Exists(Path.Combine(data.Root, "people")));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // 158 AC-8: an alias chip opens the person's page; an ambiguous one shows the red hint and writes nothing.
    [Fact]
    public async Task Mention_Alias_OpensPerson_AmbiguousRedHint()
    {
        data.WriteNote("people", "# people\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "people"));
        File.WriteAllText(Path.Combine(data.Root, "people", "Anna Schmidt.md"), "# Anna Schmidt\naliases:: AS, Anna\n");
        File.WriteAllText(Path.Combine(data.Root, "people", "Anna Berg.md"), "# Anna Berg\naliases:: Anna\n");
        data.WriteNote("Plan", "@[AS] @[Anna]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.InvokeAsync(() => js.Page().OnPerson("@[Anna]"));

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll(".red"),
            e => e.TextContent == "@[Anna] is an alias of more than one person: Anna Berg, Anna Schmidt."), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));

        await cut.InvokeAsync(() => js.Page().OnPerson("@[AS]"));

        cut.WaitForAssertion(() => Assert.Equal("people/Anna Schmidt", cut.Find(".note-title").TextContent), Wait);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 157 AC-10: the palette lists /find with its usage.
    [Fact]
    public void Palette_ListsFind()
    {
        PaletteCommand find = Assert.Single(CommandPalette.Commands, c => c.Name == "/find");
        Assert.Equal("/find <attribute> <value>", find.Usage);
    }

    // 157 AC-10: /find shows FindPanel in the main region; its button opens the note, which hides the panel.
    [Fact]
    public async Task Find_ShowsFindPanel_OpenHidesIt()
    {
        data.WriteNote("Anna", "role:: Bereichsleiter\n");
        data.WriteNote("Plan", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/find Role   bereichsleiter");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Anna"], cut.FindAll(".find-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("role:: bereichsleiter", cut.Find(".find-panel h2").TextContent);

        await cut.Find(".find-panel .note-button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Anna", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".find-panel"));

        await RunPalette(cut, "/find role nobody");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".find-panel")), Wait);
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".find-panel")), Wait);
    }

    // 157 AC-10: /find with a missing value or a bad key is the red usage line, and no panel.
    [Theory]
    [InlineData("/find")]
    [InlineData("/find role")]
    [InlineData("/find role   ")]
    [InlineData("/find 1role x")]
    [InlineData("/find role:: x")]
    public async Task Find_BadQuery_RedUsage(string line)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal("Type /find <attribute> <value>.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".find-panel"));
    }

    // 170 AC-10: the palette lists /search with its usage.
    [Fact]
    public void Palette_ListsSearch()
    {
        PaletteCommand search = Assert.Single(CommandPalette.Commands, c => c.Name == "/search");
        Assert.Equal("/search <text>", search.Usage);
    }

    // 170 AC-10: /search shows SearchPanel; its button opens the note, which hides the panel; /current hides it too.
    [Fact]
    public async Task Search_ShowsSearchPanel_OpenHidesIt()
    {
        data.WriteNote("Anna", "# Anna\nBudget 2027\n");
        data.WriteNote("Plan", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/search budget");

        cut.WaitForAssertion(() => Assert.Equal(
            ["Anna"], cut.FindAll(".search-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("Search: budget", cut.Find(".search-panel h2").TextContent);
        Assert.Equal("Budget 2027", cut.Find(".search-panel .line").TextContent);

        await cut.Find(".search-panel .note-button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Anna", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".search-panel"));

        await RunPalette(cut, "/search nobody");
        cut.WaitForAssertion(() => Assert.Equal(
            "No notes contain nobody.", cut.Find(".search-panel p[role=status]").TextContent), Wait);
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".search-panel")), Wait);
    }

    // 170 AC-10, 207 AC-4: /search alone shows the panel with the grey hint line.
    [Theory]
    [InlineData("/search")]
    [InlineData("/search    ")]
    public async Task Search_Blank_GreyHint(string line)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Equal(
            "Type text or a tag filter like #project and not #done.", cut.Find(".search-panel p[role=status]").TextContent), Wait);
        Assert.True(cut.Find(".search-panel p[role=status]").ClassList.Contains("grey"));
    }

    // 207 AC-3: the sidebar's Search button shows the panel with an empty, focused input and the grey hint.
    [Fact]
    public async Task SearchButton_ShowsPanel_EmptyInputFocused()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find(".notes-bar-buttons button.search-open").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-panel")), Wait);
        IElement input = cut.Find(".search-panel input.search-input");
        Assert.Equal(string.Empty, input.GetAttribute("value"));
        Assert.Equal("Search", cut.Find(".search-panel h2").TextContent);
        Assert.Equal(
            "Type text or a tag filter like #project and not #done.", cut.Find(".search-panel p[role=status]").TextContent);
        JSRuntimeInvocation focus = data.Bunit.JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal(
            input.GetAttribute("blazor:elementReference"), ((ElementReference)focus.Arguments[0]!).Id);
    }

    // 207 AC-4: Return in the panel's input shows the typed text's results, as /search would.
    [Fact]
    public async Task SearchInput_Return_ShowsResults()
    {
        data.WriteNote("Anna", "# Anna\nBudget 2027\n#work\n");
        data.WriteNote("Plan", "x\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await cut.Find(".notes-bar-buttons button.search-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-panel")), Wait);

        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "budget" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal(
            ["Anna"], cut.FindAll(".search-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("Search: budget", cut.Find(".search-panel h2").TextContent);

        await cut.Find(".search-input").InputAsync(new ChangeEventArgs { Value = "#work" });
        await cut.Find(".search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("Search: #work", cut.Find(".search-panel h2").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".search-panel .line")), Wait);
        Assert.Equal(["Anna"], cut.FindAll(".search-panel .note-button").Select(b => b.TextContent).ToArray());
    }

    // 207 AC-8, AC-9: a saved search from settings.json shows in the sidebar; clicking it shows its current results,
    // which follow a note edited to add #done; removing it takes it out of the sidebar and the file.
    [Fact]
    public async Task SavedSearch_ShowsCurrentResults_ThenRemoved()
    {
        data.WriteNote("Alpha", "# Alpha\n#job1234\n");
        data.WriteNote("Beta", "# Beta\n#job1234\n");
        data.WriteNote("Gamma", "# Gamma\n#job1234 #done\n");
        data.WriteFile(
            SettingsStore.FileName,
            "{\"dayEnd\":{},\"savedSearches\":[{\"name\":\"Open job1234\",\"query\":\"#job1234 and not #done\"}]}");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Open job1234"], cut.FindAll("nav.saved-searches button.saved-search").Select(b => b.TextContent).ToArray()), Wait);
        await cut.Find("nav.saved-searches button.saved-search").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-panel")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".search-panel").TextContent), Wait);
        Assert.Equal("#job1234 and not #done", cut.Find(".search-panel .search-input").GetAttribute("value"));
        Assert.Equal(["Alpha", "Beta"], cut.FindAll(".search-panel .note-button").Select(b => b.TextContent).ToArray());

        EditableNote? opened = await data.State!.ReadForEdit("Alpha");
        Assert.NotNull(opened);
        ActionResult saved = await data.Actions!.SaveNote(opened, "# Alpha\n#job1234 #done\n");
        Assert.Equal(ActionKind.Done, saved.Kind);

        cut.WaitForAssertion(() => Assert.Equal(
            ["Beta"], cut.FindAll(".search-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);

        await cut.Find("nav.saved-searches button.saved-search-remove").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("nav.saved-searches")), Wait);
        cut.WaitForAssertion(() => Assert.Equal(
            "Removed saved search Open job1234.", cut.Find("p.saved-search-result").TextContent), Wait);
        Assert.True(cut.Find("p.saved-search-result").ClassList.Contains("grey"));
        Assert.Empty(new SettingsStore(data.Root).ReadSavedSearches());
        Assert.Contains("dayEnd", File.ReadAllText(Path.Combine(data.Root, SettingsStore.FileName)), StringComparison.Ordinal);
    }

    // 180 AC-8: the palette lists /decisions with its usage.
    [Fact]
    public void Palette_ListsDecisions()
    {
        PaletteCommand decisions = Assert.Single(CommandPalette.Commands, c => c.Name == "/decisions");
        Assert.Equal("/decisions [tag]", decisions.Usage);
    }

    // 180 AC-8: /decisions shows DecisionsPanel; its note button opens the note, which hides the panel; /current hides
    // it too; a tag filters.
    [Fact]
    public async Task Decisions_ShowsDecisionsPanel_OpenHidesIt()
    {
        data.WriteNote("2026-11-10 Anna", "# Anna\n#work\nDecision: ship it\n");
        data.WriteNote("2026-11-11 Plan", "Decision: paint\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/decisions");

        cut.WaitForAssertion(() => Assert.Equal(
            ["2026-11-11 Plan", "2026-11-10 Anna"], cut.FindAll(".decisions-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("Decisions", cut.Find(".decisions-panel h2").TextContent);

        await cut.FindAll(".decisions-panel .note-button")[1].ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("2026-11-10 Anna", cut.Find(".note-title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".decisions-panel"));

        await RunPalette(cut, "/decisions #work");
        cut.WaitForAssertion(() => Assert.Equal(
            ["2026-11-10 Anna"], cut.FindAll(".decisions-panel .note-button").Select(b => b.TextContent).ToArray()), Wait);
        Assert.Equal("Decisions: #work", cut.Find(".decisions-panel h2").TextContent);
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".decisions-panel")), Wait);
    }

    // 180 AC-8: an invalid tag shows the panel with the red usage line; Close hides it.
    [Fact]
    public async Task Decisions_InvalidTag_RedUsage_CloseHides()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/decisions #1x");

        cut.WaitForAssertion(() => Assert.Equal(
            "Type /decisions [tag].", cut.Find(".decisions-panel p[role=status]").TextContent), Wait);
        Assert.True(cut.Find(".decisions-panel p[role=status]").ClassList.Contains("red"));
        await cut.Find(".decisions-panel button.close").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".decisions-panel")), Wait);
    }

    // 169 AC-7: the palette lists /review with its description.
    [Fact]
    public void Palette_ListsReview()
    {
        PaletteCommand review = Assert.Single(CommandPalette.Commands, c => c.Name == "/review");
        Assert.Equal("/review", review.Usage);
        Assert.Equal("Show the last week and the next one", review.Description);
    }

    // 169 AC-7, AC-9: /review (argument ignored) shows the panel in the main region under the capture box; Close hides it.
    [Theory]
    [InlineData("/review")]
    [InlineData("/review next")]
    public async Task Review_ShowsPanel_CloseHides(string line)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".review-panel"));

        await RunPalette(cut, line);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-panel .review-range")), Wait);
        Assert.Equal("top-bar", cut.Find("main.current-region").Children[0].ClassName);
        Assert.True(cut.Find("main.current-region").Children[1].ClassList.Contains("review-panel"));
        Assert.Equal("Week review", cut.Find(".review-panel h2").TextContent);
        Assert.Empty(cut.FindAll(".palette"));
        Assert.Empty(cut.FindAll(".main-status.red"));

        await cut.Find(".review-panel button.close").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".review-panel"));
    }

    // 169 AC-9: opening a note (also the one already open) or /current hides the review.
    [Fact]
    public async Task Review_OpeningANoteOrTheCurrentView_HidesIt()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/review");
        Assert.Single(cut.FindAll(".review-panel"));
        await Open(cut, js, "Plan");
        Assert.Empty(cut.FindAll(".review-panel"));

        await RunPalette(cut, "/review");
        Assert.Single(cut.FindAll(".review-panel"));
        await NameButton(cut, "Plan").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".review-panel")), Wait);

        await RunPalette(cut, "/review");
        Assert.Single(cut.FindAll(".review-panel"));
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".review-panel")), Wait);
    }

    // 169 AC-8: a changed note's button opens it in the editor, which hides the review.
    [Fact]
    public async Task Review_NoteButton_OpensTheNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await RunPalette(cut, "/review");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-panel .review-note button")), Wait);

        await cut.Find(".review-panel .review-note button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);
        Assert.Empty(cut.FindAll(".review-panel"));
    }

    // 169 AC-8: with unsaved changes in the open note, a changed note's button asks to discard first.
    [Fact]
    public async Task Review_NoteButton_DirtyEditor_AsksFirst()
    {
        data.WriteNote("Plan", "# Plan\n");
        data.WriteNote("Other", "# Other\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Other.md"), new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");
        await RunPalette(cut, "/review");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-panel .review-note button")), Wait);

        await cut.Find(".review-panel .review-note button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent), Wait);
    }

    // 198 AC-2: both buttons sit in .top-bar-buttons after an empty .top-bar-start, each with an inline svg.
    [Fact]
    public async Task TopBar_HasTodayAndPickADayButtons()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        IElement bar = cut.Find(".top-bar");
        Assert.Equal(["top-bar-start", "top-bar-buttons"], bar.Children.Select(c => c.ClassName ?? string.Empty).ToArray());
        Assert.Equal(string.Empty, cut.Find(".top-bar-start").InnerHtml);
        foreach ((string css, string label) in new[] { ("today-open", "Today"), ("day-pick-open", "Pick a day") })
        {
            IElement button = cut.Find(".top-bar-buttons button." + css);
            Assert.Equal(label, button.GetAttribute("aria-label"));
            Assert.Equal(label, button.GetAttribute("title"));
            Assert.Equal("true", button.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        }
    }

    // 198 AC-3: Today closes an open note, a page and the review panel and shows today's view.
    [Fact]
    public async Task TopBar_Today_ClosesNotePagesAndReview()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent), Wait);
        Assert.Empty(cut.FindAll(".note-editor"));
        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".settings-panel")), Wait);
        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".settings-panel"));
        await RunPalette(cut, "/tags");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tags-panel")), Wait);
        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".tags-panel"));
        Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent);
    }

    // 198 AC-4: Today with unsaved changes asks; Keep leaves the note, Discard shows today's view.
    [Fact]
    public async Task TopBar_Today_DirtyEditor_KeepOrDiscard()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent), Wait);
    }

    // 198 AC-5: the popover opens and closes by button, Esc and backdrop; the view and the open note stay.
    [Fact]
    public async Task TopBar_Picker_OpensAndCloses_NothingElseChanges()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        Assert.Equal("date", cut.Find("input[aria-label=Day]").GetAttribute("type"));
        Assert.Equal("1900-01-01", cut.Find("input[aria-label=Day]").GetAttribute("min"));
        Assert.Equal("2100-12-31", cut.Find("input[aria-label=Day]").GetAttribute("max"));
        Assert.Equal("true", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
        await cut.Find("button.day-pick-open").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
        Assert.Equal("false", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        await cut.Find("input[aria-label=Day]").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        await cut.Find(".day-pick-backdrop").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("input[aria-label=Day]"));
        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("input[aria-label=Day]"));

        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
    }

    private static async Task PickDate(IRenderedComponent<Main> cut, string value)
    {
        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        await cut.Find("input[aria-label=Day]").ChangeAsync(new ChangeEventArgs { Value = value });
    }

    // 212: picks value and waits until the day's note named name is in the editor, mounted and ready, with its links
    // panel loaded.
    private static async Task PickAndOpen(IRenderedComponent<Main> cut, EditorJs js, string value, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await PickDate(cut, value);
        cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }

    // 212: types text into the open editor, lets the auto save pause end and waits for grey Saved.
    private async Task TypeAndAutoSave(IRenderedComponent<Main> cut, EditorJs js, string text)
    {
        await js.Change(cut, text);
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);
        data.AutoSave.Elapse();
        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Contains("grey", cut.Find(".note-editor .status").ClassList);
    }

    // 212 AC-1: a picked day with no note creates it with that day's compose and opens it in the page editor.
    [Fact]
    public async Task TopBar_PickDay_CreatesThatDaysNoteAndOpensTheEditor()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Planning", "20261120T090000Z", "20261120T093000Z"),
            DesktopFixture.Event("Today only", "20261029T090000Z", "20261029T093000Z")));
        data.AddTodo("Ship", new DateOnly(2026, 11, 20));
        data.AddTodo("Other day", new DateOnly(2026, 11, 21));
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");

        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
        Assert.Empty(cut.FindAll("input[aria-label=Day]"));
        Assert.Equal("Created 2026-11-20", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Equal(
            "# 2026-11-20\n\n## Appointments\n- 10:00-10:30 Planning\n\n## Todos\n- Ship\n",
            File.ReadAllText(Path.Combine(data.Root, "2026-11-20.md")));
        Assert.Single(cut.FindAll(".note-editor"));
        Assert.Empty(cut.FindAll("h1.title"));
        Assert.Empty(cut.FindAll(".current"));
    }

    // 212 AC-2: the story's test: typing into a picked day's note is saved by auto save.
    [Fact]
    public async Task TopBar_PickDay_TypingIsAutoSaved()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");

        await TypeAndAutoSave(cut, js, "# 2026-11-20\n\nTyped at once\n");

        Assert.Equal("# 2026-11-20\n\nTyped at once\n", File.ReadAllText(Path.Combine(data.Root, "2026-11-20.md")));
    }

    // 212 AC-3: an existing plain day note opens as it is: nothing written, no Created text.
    [Fact]
    public async Task TopBar_PickDay_ExistingNote_OpensUnchanged()
    {
        data.WriteNote("2026-11-20", "# mine\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");

        Assert.Equal("# mine\n", js.MountedText());
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.DoesNotContain("Created", cut.Find(".main-status").TextContent, StringComparison.Ordinal);
    }

    // 212 AC-3: an existing sensitive day note goes through the unlock flow like /today and is never rewritten.
    [Fact]
    public async Task TopBar_PickDay_SensitiveNote_OpensThroughUnlock()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("2026-11-20", "# secret day\n");
        data.Keys.AgreeThrows = true;
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await PickDate(cut, "2026-11-20");

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock 2026-11-20.", cut.Find(".unreadable").TextContent), Wait);
        Assert.Equal(1, data.Keys.Agrees);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-11-20.md")));
        Assert.DoesNotContain("Created", cut.Find(".main-status").TextContent, StringComparison.Ordinal);
    }

    // 212 AC-4: picking today's date opens today's daily note in the editor, and typing is auto saved.
    [Fact]
    public async Task TopBar_PickToday_OpensTodaysNoteInTheEditor()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await PickAndOpen(cut, js, "2026-10-29", "2026-10-29");

        Assert.Empty(cut.FindAll("h1.title"));
        Assert.Equal("Created 2026-10-29", cut.Find(".main-status").TextContent);
        await TypeAndAutoSave(cut, js, "# 2026-10-29\n\nToday typed\n");
        Assert.Equal("# 2026-10-29\n\nToday typed\n", File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md")));
        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent), Wait);
        Assert.Equal(["end-day"], await MenuIdsAsync(cut));
    }

    // 200 AC-6 (171 AC-13 moved here): End the day in the menu closes today; the result is grey in the main status line.
    [Fact]
    public async Task EndDay_FromTheMenu_ClosesTheDay_GreyStatus()
    {
        Directory.CreateDirectory(data.TimesFolder);
        File.WriteAllText(
            Path.Combine(data.TimesFolder, "2026-10-29.json"),
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"},{\"kind\":\"leaving\",\"time\":\"16:00\"}]}");
        data.AddTodo("call Anna", DesktopFixture.Today);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Equal(string.Empty, cut.Find("p.main-status").TextContent);

        await OpenMenuAsync(cut);
        IElement item = cut.Find(".page-menu button.end-day");
        Assert.Equal("End the day", item.TextContent);
        Assert.Equal("menuitem", item.GetAttribute("role"));
        await item.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.NotEqual(string.Empty, cut.Find("p.main-status").TextContent), Wait);
        IElement line = cut.Find("p.main-status");
        Assert.Equal("Day closed. Moved 1 todo(s) to 2026-10-30.", line.TextContent);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Equal(line.TextContent, line.GetAttribute("title"));
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Contains(
            "## Worked\n- Arrived 08:00, left 16:00, worked 8:00\n",
            File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md")),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(cut.FindAll(".day-end-status"));
    }

    // 200 AC-6 (171 AC-13 moved here): a refusal shows the red main status line.
    [Fact]
    public async Task EndDay_FromTheMenu_Sensitive_RedStatus()
    {
        data.WriteFile("2026-10-29.ctxs", "{\"v\":1,\"text\":\"c2VhbGVk\"}");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await ChooseAsync(cut, "end-day");

        cut.WaitForAssertion(() => Assert.NotEqual(string.Empty, cut.Find("p.main-status").TextContent), Wait);
        IElement line = cut.Find("p.main-status");
        Assert.Equal("2026-10-29 is sensitive. Nothing was written to it.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // 212 AC-5: another note's unsaved changes are saved first, then the picked day's note opens.
    [Fact]
    public async Task TopBar_PickDay_DirtyEditor_SavesFirstThenOpens()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nTyped\n");

        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("# Plan\n\nTyped\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.True(File.Exists(Path.Combine(data.Root, "2026-11-20.md")));
    }

    // 212 AC-5: when that save fails the discard dialog asks: Cancel keeps the note and its text, Confirm opens the day.
    [Fact]
    public async Task TopBar_PickDay_SaveFails_CancelOrConfirm()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        int mounts = js.Calls("ContextEditor.mount").Count;
        js.EditUnsavable("# Plan\n\nUnsaved\n");

        await PickDate(cut, "2026-11-20");

        cut.WaitForAssertion(() => Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent), Wait);
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-11-20.md")));
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
        Assert.True(js.Dirty);
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-11-20.md")));

        await PickDate(cut, "2026-11-20");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")), Wait);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("2026-11-20", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        Assert.Equal("Created 2026-11-20", cut.Find(".main-status").TextContent);
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
    }

    // 212 AC-5: picking the day of the note already open changes nothing: no dialog, no remount, unsaved text kept.
    [Fact]
    public async Task TopBar_PickDay_SameDay_KeepsTheEditor()
    {
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");
        js.EditUnsavable("# 2026-11-20\n\nUnsaved\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await PickDate(cut, "2026-11-20");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("input[aria-label=Day]")), Wait);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("2026-11-20", cut.Find(".note-title").TextContent);
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.True(js.Dirty);
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 212 AC-6: a day note that can't be written gives the red line, opens no editor and keeps the shown view.
    [Fact]
    public async Task TopBar_PickDay_WriteFails_RedLineViewKept()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        string first = Path.Combine(data.Root, "2026-11-20.md");
        string second = Path.Combine(data.Root, "2026-11-21.md");
        data.Actions!.BeforeWrite = () =>
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
        };

        await PickDate(cut, "2026-11-20");

        cut.WaitForAssertion(
            () => Assert.Equal("Can't write 2026-11-20. Nothing was done.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent);
        Assert.False(File.Exists(first));

        await Open(cut, js, "Plan");
        int mounts = js.Calls("ContextEditor.mount").Count;
        await PickDate(cut, "2026-11-21");
        cut.WaitForAssertion(
            () => Assert.Equal("Can't write 2026-11-21. Nothing was done.", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(mounts, js.Calls("ContextEditor.mount").Count);
        Assert.False(File.Exists(second));
    }

    // 198 AC-8, 212 AC-9: a bad value does nothing: the popover stays open and nothing is written.
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("1899-12-31")]
    [InlineData("2101-01-01")]
    [InlineData("20261120")]
    public async Task TopBar_PickDay_BadValue_Ignored(string value)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await PickDate(cut, value);

        Assert.Single(cut.FindAll("input[aria-label=Day]"));
        Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 212 AC-7: Today then a note name, and Today then /today, end in an editor whose change auto save saves.
    [Fact]
    public async Task Routes_TodayThenNoteOrToday_EditorSaves()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        await Open(cut, js, "Plan");
        await TypeAndAutoSave(cut, js, "# Plan\n\nFrom a name\n");
        Assert.Equal("# Plan\n\nFrom a name\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));

        await cut.Find("button.today-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent), Wait);
        int mounts = js.Calls("ContextEditor.mount").Count;
        await RunPalette(cut, "/today");
        cut.WaitForAssertion(() => Assert.Equal("2026-10-29", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);
        await TypeAndAutoSave(cut, js, "# 2026-10-29\n\nFrom today\n");
        Assert.Equal("# 2026-10-29\n\nFrom today\n", File.ReadAllText(Path.Combine(data.Root, "2026-10-29.md")));
    }

    // 212 AC-7: day A then day B opens B (A left as saved); a day then a note name opens the note; each saves.
    [Fact]
    public async Task Routes_DayThenDayOrNote_EditorSaves()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");
        await TypeAndAutoSave(cut, js, "# 2026-11-20\n\nDay A\n");
        await PickAndOpen(cut, js, "2026-11-21", "2026-11-21");
        Assert.Equal("# 2026-11-20\n\nDay A\n", File.ReadAllText(Path.Combine(data.Root, "2026-11-20.md")));
        await TypeAndAutoSave(cut, js, "# 2026-11-21\n\nDay B\n");
        Assert.Equal("# 2026-11-21\n\nDay B\n", File.ReadAllText(Path.Combine(data.Root, "2026-11-21.md")));

        await Open(cut, js, "Plan");
        await TypeAndAutoSave(cut, js, "# Plan\n\nAfter a day\n");
        Assert.Equal("# Plan\n\nAfter a day\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("# 2026-11-21\n\nDay B\n", File.ReadAllText(Path.Combine(data.Root, "2026-11-21.md")));
    }

    // 212 AC-8: a picked day's note has the plain-note menu, a sensitive one the sensitive-note menu; no End the day.
    [Fact]
    public async Task TopBar_PickDay_MenuHasTheNotesItems()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("2026-11-21", "# secret day\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await PickAndOpen(cut, js, "2026-11-20", "2026-11-20");
        Assert.Equal(
            ["rename", "delete", "history", "new-subpage", "move", "open-external", "export", "mark-sensitive"],
            await MenuIdsAsync(cut));

        await PickDate(cut, "2026-11-21");
        cut.WaitForAssertion(() => Assert.Equal("2026-11-21", cut.Find(".note-title").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".links-panel p"), p => p.TextContent == "Sensitive notes have no links."), Wait);
        Assert.Equal(["rename", "delete", "history", "make-plain", "lock"], await MenuIdsAsync(cut));
    }

    // 199 AC-1: Capture is the first icon button, before Today and Pick a day; no popover at first.
    [Fact]
    public async Task TopBar_CaptureButtonFirst_NoPopoverAtFirst()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal(
            ["capture-open", "today-open", "day-pick-open", "page-menu-open"],
            cut.FindAll(".top-bar-buttons > button").Select(b => b.ClassName ?? string.Empty).ToArray());
        IElement button = cut.Find("button.capture-open");
        Assert.Equal("Capture", button.GetAttribute("aria-label"));
        Assert.Equal("Capture", button.GetAttribute("title"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        IElement svg = Assert.Single(button.QuerySelectorAll("svg"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("18", svg.GetAttribute("width"));
        Assert.Equal("18", svg.GetAttribute("height"));
        Assert.Empty(cut.FindAll(".capture-pop"));
        Assert.Empty(cut.FindAll(".capture-pop-backdrop"));
    }

    // 199 AC-2: Capture opens the popover with its text field, which takes the focus.
    [Fact]
    public async Task TopBar_Capture_OpensPopoverAndFocusesTheField()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        Assert.Single(cut.FindAll(".capture-pop-backdrop"));
        IElement input = cut.Find(".capture-pop input[aria-label=Capture]");
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("Capture to inbox", input.GetAttribute("placeholder"));
        Assert.Equal("1000", input.GetAttribute("maxlength"));
        Assert.Equal("true", cut.Find("button.capture-open").GetAttribute("aria-expanded"));
        cut.WaitForAssertion(
            () => Assert.Contains(
                data.Bunit.JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus"),
                i => i.Arguments[0] is ElementReference r && r.Id == input.GetAttribute("blazor:elementReference")),
            Wait);
    }

    // 199 AC-3: Esc, the backdrop and a second click each close the popover; nothing is written.
    [Fact]
    public async Task TopBar_Capture_ClosesByEscBackdropAndButton_NothingWritten()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        await cut.Find(".capture-pop input").InputAsync(new ChangeEventArgs { Value = "buy milk" });
        await cut.Find(".capture-pop input").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop")), Wait);
        Assert.Equal("false", cut.Find("button.capture-open").GetAttribute("aria-expanded"));

        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        await cut.Find(".capture-pop-backdrop").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop-backdrop")), Wait);

        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop")), Wait);

        Assert.False(File.Exists(Path.Combine(data.Root, "inbox.md")));
    }

    // 199 AC-4: opening one popover closes the other; at most one exists at any time.
    [Fact]
    public async Task TopBar_CaptureAndDayPick_OnlyOneOpen()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".day-pick")), Wait);
        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));

        await cut.Find("button.day-pick-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".day-pick")), Wait);
        Assert.Empty(cut.FindAll(".capture-pop"));
        Assert.Empty(cut.FindAll(".capture-pop-backdrop"));
        Assert.Equal("false", cut.Find("button.capture-open").GetAttribute("aria-expanded"));
    }

    private static async Task CaptureByIcon(IRenderedComponent<Main> cut, string text)
    {
        if (cut.FindAll(".capture-pop").Count == 0)
        {
            await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        }

        await cut.Find(".capture-pop input").InputAsync(new ChangeEventArgs { Value = text });
        await cut.Find(".capture-pop input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    // 199 AC-5: Return captures to a new inbox, says Captured., closes the popover and empties the field.
    [Fact]
    public async Task TopBar_Capture_Return_WritesClosesAndEmpties()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await CaptureByIcon(cut, "buy milk");

        cut.WaitForAssertion(() => Assert.Equal("Captured.", cut.Find(".main-status").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop")), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Equal("# inbox\n- 2026-10-29 10:05 buy milk\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));

        await cut.Find("button.capture-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".capture-pop")), Wait);
        Assert.Equal(string.Empty, cut.Find(".capture-pop input").GetAttribute("value") ?? string.Empty);
    }

    // 199 AC-6: a dirty open inbox refuses, writes nothing and keeps the popover and the text.
    [Fact]
    public async Task TopBar_Capture_DirtyOpenInbox_RefusedPopoverKept()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        js.EditUnsavable("# inbox\n\nUnsaved\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await CaptureByIcon(cut, "buy milk");

        cut.WaitForAssertion(() => Assert.Equal("Save inbox first.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Single(cut.FindAll(".capture-pop"));
        Assert.Equal("buy milk", cut.Find(".capture-pop input").GetAttribute("value"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 199 AC-6: a clean open inbox is reloaded after the capture and shows the new line.
    [Fact]
    public async Task TopBar_Capture_CleanOpenInbox_Reloads()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        int mounts = js.Calls("ContextEditor.mount").Count;

        await CaptureByIcon(cut, "buy milk");

        cut.WaitForAssertion(() => Assert.Equal("Captured.", cut.Find(".main-status").TextContent), Wait);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop")), Wait);
        Assert.Equal("# inbox\n- 2026-10-29 10:05 buy milk\n", js.MountedText());
    }

    // 199 AC-7: an empty field is refused by CaptureText; a dirty inbox is reported first; popover and text kept.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TopBar_Capture_Empty_RefusedPopoverKept(string text)
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await CaptureByIcon(cut, text);

        cut.WaitForAssertion(() => Assert.Equal("Type something to capture.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Single(cut.FindAll(".capture-pop"));
        Assert.Equal(text, cut.Find(".capture-pop input").GetAttribute("value") ?? string.Empty);
        Assert.False(File.Exists(Path.Combine(data.Root, "inbox.md")));
    }

    // 199 AC-7: with a dirty inbox open, an empty Return reports the unsaved inbox (CaptureHeld runs first).
    [Fact]
    public async Task TopBar_Capture_EmptyWithDirtyInbox_SaveFirst()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "inbox");
        js.EditUnsavable("# inbox\n\nUnsaved\n");

        await CaptureByIcon(cut, " ");

        cut.WaitForAssertion(() => Assert.Equal("Save inbox first.", cut.Find(".main-status").TextContent), Wait);
        Assert.Single(cut.FindAll(".capture-pop"));
    }

    // 199 AC-7: a sensitive inbox is refused without any key-store call; popover and text kept.
    [Fact]
    public async Task TopBar_Capture_SensitiveInbox_RefusedNoKeyStore()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("inbox", "# inbox\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await CaptureByIcon(cut, "buy milk");

        cut.WaitForAssertion(() => Assert.Equal("inbox is sensitive. Nothing was captured.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Single(cut.FindAll(".capture-pop"));
        Assert.Equal("buy milk", cut.Find(".capture-pop input").GetAttribute("value"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 199 AC-7: a failed save is a red line; popover and text kept.
    [Fact]
    public async Task TopBar_Capture_SaveFails_RedLinePopoverKept()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        data.Actions!.BeforeCaptureSave = () => throw new IOException("disk full");

        await CaptureByIcon(cut, "buy milk");

        cut.WaitForAssertion(() => Assert.Equal("Can't write inbox. Nothing was captured.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Single(cut.FindAll(".capture-pop"));
        Assert.Equal("buy milk", cut.Find(".capture-pop input").GetAttribute("value"));
        Assert.DoesNotContain("disk full", cut.Markup);
    }

    // 199 AC-8: the palette's /capture x still captures through the same flow.
    [Fact]
    public async Task PaletteCapture_StillCaptures_AfterTopBarCapture()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await CaptureByIcon(cut, "first");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".capture-pop")), Wait);
        await RunPalette(cut, "/capture x");

        cut.WaitForAssertion(
            () => Assert.Equal("# inbox\n- 2026-10-29 10:05 first\n- 2026-10-29 10:05 x\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md"))),
            Wait);
        Assert.Equal("Captured.", cut.Find(".main-status").TextContent);
    }
}
