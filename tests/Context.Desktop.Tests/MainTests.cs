using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class MainTests : IDisposable
{
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

        cut.WaitForAssertion(() => Assert.Contains("Fresh todo", cut.Find(".todos").TextContent));
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

        cut.WaitForAssertion(() => Assert.Contains("Buy milk", cut.Find(".todos-region").TextContent));
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
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent));
        Assert.Equal("Created Plan", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")));
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

    // 134 AC-7: creating from the entry row while the open note has unsaved changes asks to discard first.
    [Fact]
    public async Task EntryRow_DirtyEditor_AsksBeforeOpeningTheNewNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.Edit("# Plan\n\nUnsaved\n");

        await cut.Find(".note-new-title").InputAsync(new ChangeEventArgs { Value = "Fresh" });
        await cut.Find("button.note-new").ClickAsync(new MouseEventArgs());

        Assert.True(File.Exists(Path.Combine(data.Root, "Fresh.md")));
        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Fresh", cut.Find(".note-title").TextContent));
    }

    // 134 AC-8: the toolbar shows only while a note is open, with its buttons (136: plus Mark sensitive).
    [Fact]
    public async Task Toolbar_OnlyWithAnOpenNote()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".note-toolbar"));

        await Open(cut, js, "Plan");

        IReadOnlyList<IElement> buttons = cut.FindAll(".note-toolbar button");
        Assert.Equal(
            ["Rename", "Delete", "Open in default editor", "Mark sensitive"], buttons.Select(b => b.TextContent).ToArray());
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
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

        cut.WaitForAssertion(() => Assert.Equal("Can't read Big.", cut.Find(".unreadable").TextContent));
        Assert.Equal(4, cut.FindAll(".note-toolbar button").Count);
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

        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-toolbar .rename-title").GetAttribute("value")));
        Assert.Equal("text", cut.Find(".rename-title").GetAttribute("type"));
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        if (enter)
        {
            await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find(".note-toolbar button.save-name").ClickAsync(new MouseEventArgs());
        }

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Project.md")));
        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent));
        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.mount").Count));
        Assert.Empty(cut.FindAll(".rename-title"));
        cut.WaitForAssertion(() => Assert.DoesNotContain(
            cut.FindAll(".notes-region button.name"), b => b.TextContent == "Plan"));
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
        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());
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
        Assert.Single(cut.FindAll(".note-toolbar button.rename"));
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
        js.Edit("# Plan\n\nUnsaved\n");

        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());

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
        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        js.Edit("# Plan\n\nUnsaved\n");

        await cut.Find(".note-toolbar button.save-name").ClickAsync(new MouseEventArgs());

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
        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "other" });

        await cut.Find(".note-toolbar button.save-name").ClickAsync(new MouseEventArgs());

        Assert.Equal("Note other already exists.", cut.Find(".main-status").TextContent);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("# Other\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
    }

    // 134 AC-8: Delete asks in ConfirmDialog; Cancel deletes nothing.
    [Fact]
    public async Task Delete_Cancel_DeletesNothing()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await cut.Find(".note-toolbar button.delete").ClickAsync(new MouseEventArgs());

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

        await cut.Find(".note-toolbar button.delete").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".current-region .current")));
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

        await cut.Find(".note-toolbar button.open-external").ClickAsync(new MouseEventArgs());
        Assert.Equal([Path.Combine(data.Root, "Plan.md")], data.Launcher.Launched);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);

        data.Launcher.Throws = new InvalidOperationException("boom");
        await cut.Find(".note-toolbar button.open-external").ClickAsync(new MouseEventArgs());

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
            cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).Order(StringComparer.Ordinal).ToArray()));
        IElement region = cut.Find(".current-region");
        Assert.True(region.Children.Index().Single(c => c.Item.ClassList.Contains("note-editor")).Index
            < region.Children.Index().Single(c => c.Item.ClassList.Contains("links-panel")).Index);
        Assert.Equal("Linked notes", cut.Find(".links-panel h2").TextContent);

        await cut.Find(".links-panel .link-tags button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(
            ["Other", "Plan"], cut.FindAll(".tag-panel .note-button").Select(b => b.TextContent).ToArray()));
        Assert.Equal("#work", cut.Find(".tag-panel h2 .accent").TextContent);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Single(js.Calls("ContextEditor.mount"));

        await cut.Find(".links-panel button.name").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Equal(
            ["Plan"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()));
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel button.name")));
        js.Edit("[[Other]]\n\nUnsaved\n");

        await cut.Find(".links-panel button.name").ClickAsync(new MouseEventArgs());

        Assert.Equal("Discard changes to Plan?", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Other", cut.Find(".note-title").TextContent));
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
        cut.WaitForAssertion(() => Assert.Equal("Plan has no linked notes.", cut.Find(".links-panel p[role=status]").TextContent));

        data.WriteNote("Third", "see [[Plan]] and [[Project]]\n");
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Equal(
            ["Third"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()));

        await cut.Find(".note-toolbar button.rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".rename-title").InputAsync(new ChangeEventArgs { Value = "Project" });
        await cut.Find(".rename-title").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Equal("Project", cut.Find(".note-title").TextContent));
        cut.WaitForAssertion(() => Assert.Equal(
            ["Third"], cut.FindAll(".links-panel .link-row .name").Select(n => n.TextContent).ToArray()));
        cut.WaitForAssertion(() => Assert.Equal(1, js.Calls("ContextEditor.mount").Count - 1));
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

        cut.WaitForAssertion(() => Assert.Equal("Sensitive notes have no links.", cut.Find(".links-panel p[role=status]").TextContent));
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

        cut.WaitForAssertion(() => Assert.Equal("Can't unlock Secret.", cut.Find(".unreadable").TextContent));
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel .link-row")));
        await RunPalette(cut, "/tags");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tags-panel .tag-list button")));
        await cut.Find(".tags-panel .tag-list button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".tag-panel .note-button").Count));
        await data.State!.Refresh();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".links-panel .link-row")));

        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 138 AC-1: the Settings button and /settings show the settings at the top of the main region; Close hides them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_ButtonOrPalette_ShowsAtTheTop_CloseHides(bool palette)
    {
        data.WriteNote("Plan", "# Plan\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".settings-panel"));
        Assert.Equal("Settings", cut.Find(".notes-region button.settings-open").TextContent);

        if (palette)
        {
            await RunPalette(cut, "/settings");
        }
        else
        {
            await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        }

        IElement panel = cut.Find("main.current-region").FirstElementChild!;
        Assert.True(panel.ClassList.Contains("settings-panel"));
        Assert.Equal("Settings", panel.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Exchange calendar", "Sensitive notes"], panel.QuerySelectorAll("h3").Select(h => h.TextContent).ToArray());
        Assert.Empty(cut.FindAll(".palette"));

        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".settings-panel"));
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
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".settings-panel")));

        await RunPalette(cut, "/settings");
        Assert.Single(cut.FindAll(".settings-panel"));
        await RunPalette(cut, "/current");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".settings-panel")));
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
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".settings-panel").TextContent, StringComparison.Ordinal));
        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(requests, data.Http.Requests);
        Assert.Equal(0, data.Keys.Agrees);
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
