using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Context.Tests;

// 094 AC-4 to AC-9: the note editor with the page's editor mocked through bunit's JS interop.
public sealed class NoteEditorTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-7: the note's text (LF) is mounted and the editor gets focus; no lossy line for a lossless note.
    [Fact]
    public async Task Open_MountsTextAndFocuses()
    {
        data.WriteNote("Plan", "# Plan\r\n\r\nText #work\r\n");
        EditorJs js = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Equal("# Plan\n\nText #work\n", js.MountedText());
        Assert.Single(js.Calls("ContextEditor.focus"));
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Empty(cut.FindAll(".lossy"));
        Assert.Empty(cut.FindAll(".unreadable"));
    }

    // AC-4: a note the editor would reformat shows the grey line.
    [Fact]
    public async Task Open_Lossy_GreyLine()
    {
        data.WriteNote("Plan", "* star\n");
        _ = new EditorJs(data.Bunit, lossless: false);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        IElement line = cut.Find(".lossy");
        Assert.Equal("Saving will reformat parts of this note.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
    }

    // AC-7: a note that can't be read shows the red line and mounts nothing.
    [Fact]
    public async Task Open_Unreadable_RedLineNoMount()
    {
        data.WriteNote("Bad", "text");
        EditorJs js = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        using FileStream held = new FileStream(
            Path.Combine(data.Root, "Bad.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Bad"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")), Wait);

        IElement line = cut.Find(".unreadable");
        Assert.Equal("Can't read Bad.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".editor-failed"));
    }

    // A note over 5 MB is not sent to the page editor: the red line, nothing mounted.
    [Fact]
    public async Task Open_OverLimit_RedLineNoMount()
    {
        data.WriteNote("Big", new string('a', (int)NoteStore.MaxEditBytes + 1));
        EditorJs js = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Big"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")), Wait);

        Assert.Equal("Can't read Big.", cut.Find(".unreadable").TextContent);
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.Empty(cut.FindAll(".editor-failed"));
    }

    // 143 AC-1, AC-5: the note was read but the page editor's mount failed: its own red line, not "Can't read",
    // and nothing of the exception's text.
    [Fact]
    public async Task Open_MountThrows_EditorFailedLine()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.mount", new JSException("boom-143"));

        IRenderedComponent<NoteEditor> cut = await RenderFailed("Plan");

        AssertFailedLine(cut, "Plan");
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Empty(js.Calls("ContextEditor.focus"));
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
    }

    // 143 AC-1: the name in the line is the one-line form of the note's name.
    [Fact]
    public async Task Open_MountThrows_LineNamesTheNoteOnOneLine()
    {
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.mount", new JSException("boom-143"));
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        string name = "Plan";
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".editor-failed")), Wait);

        Assert.Equal(
            $"The editor couldn't start. {DesktopText.OneLine(name)} was not changed.",
            cut.Find(".editor-failed").TextContent);
    }

    // 143 AC-2, AC-5: a failed isLossless shows the same line; nothing is mounted and no lossy line shows.
    [Fact]
    public async Task Open_IsLosslessThrows_EditorFailedLineNoMount()
    {
        data.WriteNote("Plan", "* star\n");
        EditorJs js = new EditorJs(data.Bunit, lossless: false);
        js.Fail("ContextEditor.isLossless", new JSException("boom-143"));

        IRenderedComponent<NoteEditor> cut = await RenderFailed("Plan");

        AssertFailedLine(cut, "Plan");
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.Empty(cut.FindAll(".lossy"));
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
    }

    // 143 AC-3: every exception a page call can end with gives the line, and rendering throws nothing.
    [Theory]
    [InlineData("js")]
    [InlineData("disconnected")]
    [InlineData("canceled")]
    public async Task Open_MountThrows_AnyPageException_EditorFailedLine(string kind)
    {
        data.WriteNote("Plan", "* star\n");
        EditorJs js = new EditorJs(data.Bunit, lossless: false);
        Exception thrown = kind switch
        {
            "js" => new JSException("boom-143"),
            "disconnected" => new JSDisconnectedException("boom-143"),
            _ => new TaskCanceledException("boom-143"),
        };
        js.Fail("ContextEditor.mount", thrown);

        IRenderedComponent<NoteEditor> cut = await RenderFailed("Plan");

        AssertFailedLine(cut, "Plan");
        Assert.Empty(cut.FindAll(".lossy"));
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
    }

    // 143 AC-4: after a failed start nothing can be saved, the page is not asked for dirt and the file is untouched.
    [Fact]
    public async Task Open_MountThrows_NothingToSave_FileUntouched()
    {
        data.WriteNote("Plan", "# Plan\r\n\r\nText\r\n");
        string path = Path.Combine(data.Root, "Plan.md");
        byte[] before = File.ReadAllBytes(path);
        EditorJs js = new EditorJs(data.Bunit, dirty: true, markdown: "changed\n");
        js.Fail("ContextEditor.mount", new JSException("boom-143"));

        IRenderedComponent<NoteEditor> cut = await RenderFailed("Plan");

        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
        Assert.Null(await cut.InvokeAsync(() => cut.Instance.PrepareSaveAsync()));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });
        Assert.Empty(js.Calls("ContextEditor.isDirty"));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // 143 AC-7: a mounted editor that only didn't take the focus works and shows no error.
    [Fact]
    public async Task Open_FocusThrows_EditorWorks_NoError()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.focus", new JSException("boom-143"));

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Single(js.Calls("ContextEditor.focus"));
        Assert.Empty(cut.FindAll(".editor-failed"));
        Assert.Empty(cut.FindAll(".unreadable"));
        Assert.False(cut.Find("button.save").HasAttribute("disabled"));
        SaveRequest? request = await cut.InvokeAsync(() => cut.Instance.PrepareSaveAsync());
        Assert.NotNull(request);
        Assert.Equal("Plan", request.Note.Name);
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
    }

    // 177 review nit: an empty status line has no child nodes at all, so `.status:empty` hides it.
    [Fact]
    public async Task EmptyStatus_HasNoChildNodes()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Equal(0, cut.Find(".status").ChildNodes.Length);
    }

    // AC-8: the Save button saves the editor's Markdown and says so.
    [Fact]
    public async Task SaveButton_SavesMarkdown()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMore #new\n");

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal("# Plan\n\nMore #new\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        IElement status = cut.Find(".status");
        Assert.Equal("Saved Plan.", status.TextContent);
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.Single(js.Calls("ContextEditor.markCleanIf"));
    }

    // AC-8: Ctrl+S and Cmd+S save; other keys don't.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CtrlOrCmdS_Saves(bool ctrl, bool meta)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nSaved by key\n");

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "a", CtrlKey = ctrl, MetaKey = meta });
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s" });
        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = ctrl, MetaKey = meta });

        Assert.Equal("# Plan\n\nSaved by key\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
    }

    // AC-5: saving without an edit writes nothing, even for a lossy CRLF + BOM note, and never asks for Markdown.
    [Fact]
    public async Task Save_NotDirty_WritesNothing()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(
            Path.Combine(data.Root, "Plan.md"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("* star\r\n__b__\r\n")]);
        EditorJs js = new EditorJs(data.Bunit, lossless: false);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
    }

    // AC-6: a note changed on disk is not overwritten; the red line names it and the editor keeps its text.
    [Fact]
    public async Task Save_ChangedOnDisk_RedLineEditorKept()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMine\n");
        data.WriteNote("Plan", "# Theirs\n");

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal("# Theirs\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        IElement status = cut.Find(".status");
        Assert.Equal("Plan changed on disk since you opened it. Nothing was saved.", status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Empty(js.Calls("ContextEditor.markClean"));
        Assert.Empty(js.Calls("ContextEditor.destroy"));
    }

    // AC-6: a deleted note is not written again.
    [Fact]
    public async Task Save_Deleted_RedLine()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMine\n");
        File.Delete(Path.Combine(data.Root, "Plan.md"));

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Plan no longer exists. Nothing was saved.", cut.Find(".status").TextContent);
    }

    // AC-9: a link to a missing note shows the grey line.
    [Fact]
    public async Task LinkClick_Missing_GreyLine()
    {
        data.WriteNote("Plan", "See [[Ghost]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        await cut.InvokeAsync(() => js.Page().OnLink("[[Ghost]]"));

        IElement status = cut.Find(".status");
        Assert.Equal("No note named Ghost.", status.TextContent);
        Assert.True(status.ClassList.Contains("grey"));
    }

    // AC-9: a link to an existing note asks to open the note resolved by NameLookup (case and .md ignored).
    [Theory]
    [InlineData("[[other]]")]
    [InlineData("[[Other.md]]")]
    [InlineData("[[Other|alias]]")]
    public async Task LinkClick_Existing_OpensResolvedName(string raw)
    {
        data.WriteNote("Plan", "See " + raw + "\n");
        data.WriteNote("Other", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        List<string> opened = [];
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnOpenNote, (string name) => opened.Add(name)));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        await cut.InvokeAsync(() => js.Page().OnLink(raw));

        if (raw == "[[Other|alias]]")
        {
            Assert.Empty(opened);
            Assert.Equal("No note named Other|alias.", cut.Find(".status").TextContent);
        }
        else
        {
            Assert.Equal(["Other"], opened);
        }
    }

    // AC-9: a tag click passes the tag as TagParser finds it.
    [Theory]
    [InlineData("#Work", "work")]
    [InlineData("#[Tag  With Spaces]", "tag with spaces")]
    public async Task TagClick_PassesTag(string raw, string tag)
    {
        data.WriteNote("Plan", raw + "\n");
        EditorJs js = new EditorJs(data.Bunit);
        List<string> tags = [];
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnTag, (string t) => tags.Add(t)));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        await cut.InvokeAsync(() => js.Page().OnTag(raw));

        Assert.Equal([tag], tags);
    }

    // 156 AC-10: a mention click passes the name as PersonParser finds it; raw text that is no mention passes nothing.
    [Theory]
    [InlineData("@[  Anna   Schmidt ]", "Anna Schmidt")]
    [InlineData("@[a/b]", null)]
    public async Task PersonClick_PassesName(string raw, string? person)
    {
        data.WriteNote("Plan", raw + "\n");
        EditorJs js = new EditorJs(data.Bunit);
        List<string> people = [];
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnPerson, (string name) => people.Add(name)));
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        await cut.InvokeAsync(() => js.Page().OnPerson(raw));

        Assert.Equal(person is null ? [] : [person], people);
        Assert.False(File.Exists(Path.Combine(data.Root, "people.md")));
    }

    // 141 AC-1: a freshly opened note shows no dot, plain or sensitive.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_NoDirtyDot(bool sensitive)
    {
        IRenderedComponent<NoteEditor> cut = await RenderPlan(sensitive);

        Assert.Empty(cut.FindAll(".dirty-dot"));
    }

    // 141 AC-2: the page's OnDirty(true) shows one labelled dot right after the title, not inside it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnDirtyTrue_ShowsOneDotAfterTheTitle(bool sensitive)
    {
        IRenderedComponent<NoteEditor> cut = await RenderPlan(sensitive);
        EditorInterop page = PageOf(cut);

        await cut.InvokeAsync(() => page.OnDirty(true));
        await cut.InvokeAsync(() => page.OnDirty(true));

        IElement dot = Assert.Single(cut.FindAll(".dirty-dot"));
        Assert.Equal("img", dot.GetAttribute("role"));
        Assert.Equal("Unsaved changes", dot.GetAttribute("aria-label"));
        Assert.Equal("Unsaved changes", dot.GetAttribute("title"));
        IElement title = cut.Find(".editor-bar h2.note-title");
        Assert.Equal(
            ["H2.note-title cut", "SPAN.dirty-dot", "BUTTON.save"],
            cut.Find(".editor-bar").Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
        Assert.Empty(title.QuerySelectorAll(".dirty-dot"));
        Assert.Equal("Plan", title.TextContent);
        Assert.Equal("Plan", title.GetAttribute("title"));
    }

    // 141 AC-3: a successful save (button or Ctrl/Cmd+S) removes the dot at once; the mocked page never calls back.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Save_Done_RemovesDot(bool ctrl, bool meta)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMore\n");
        await cut.InvokeAsync(() => js.Page().OnDirty(true));
        Assert.Single(cut.FindAll(".dirty-dot"));

        if (ctrl || meta)
        {
            await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = ctrl, MetaKey = meta });
        }
        else
        {
            await cut.Find("button.save").ClickAsync(new MouseEventArgs());
        }

        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
        Assert.Equal("# Plan\n\nMore\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(cut.FindAll(".dirty-dot"));
    }

    // 141 AC-4: a refused save (changed on disk, deleted) keeps the dot next to the red line.
    [Theory]
    [InlineData(false, "Plan changed on disk since you opened it. Nothing was saved.")]
    [InlineData(true, "Plan no longer exists. Nothing was saved.")]
    public async Task Save_Refused_KeepsDot(bool delete, string expected)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMine\n");
        await cut.InvokeAsync(() => js.Page().OnDirty(true));
        if (delete)
        {
            File.Delete(Path.Combine(data.Root, "Plan.md"));
        }
        else
        {
            data.WriteNote("Plan", "# Theirs\n");
        }

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        IElement status = cut.Find(".status");
        Assert.Equal(expected, status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Single(cut.FindAll(".dirty-dot"));
    }

    // 141 AC-5: the page's OnDirty(false) (everything undone) removes the dot.
    [Fact]
    public async Task OnDirtyFalse_RemovesDot()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await cut.InvokeAsync(() => js.Page().OnDirty(true));
        Assert.Single(cut.FindAll(".dirty-dot"));

        await cut.InvokeAsync(() => js.Page().OnDirty(false));

        Assert.Empty(cut.FindAll(".dirty-dot"));
    }

    // 141 AC-6: a reload that mounts fresh text leaves no dot.
    [Fact]
    public async Task Reload_MountsFreshText_NoDot()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("inbox");
        await cut.InvokeAsync(() => js.Page().OnDirty(true));
        Assert.Single(cut.FindAll(".dirty-dot"));
        data.WriteNote("inbox", "# inbox\n- captured\n");

        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());

        Assert.Equal(2, js.Calls("ContextEditor.mount").Count);
        Assert.Equal("# inbox\n- captured\n", js.MountedText());
        Assert.Empty(cut.FindAll(".dirty-dot"));
    }

    // 141 AC-7: a late OnDirty from the page after the editor was disposed neither throws nor renders.
    [Fact]
    public async Task OnDirty_AfterDispose_DoesNothing()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        EditorInterop page = js.Page();
        await data.Bunit.DisposeComponentsAsync();
        int renders = cut.RenderCount;

        await page.OnDirty(true);
        await page.OnDirty(false);

        Assert.Equal(renders, cut.RenderCount);
    }

    // 141 AC-8: OnDirty is callable from the page and only raises DirtyChanged; IsDirtyAsync still asks the page.
    [Fact]
    public async Task OnDirty_IsJSInvokable_AndOnlyRaisesDirtyChanged()
    {
        Assert.NotNull(typeof(EditorInterop).GetMethod(nameof(EditorInterop.OnDirty))!
            .GetCustomAttributes(typeof(Microsoft.JSInterop.JSInvokableAttribute), inherit: false)
            .SingleOrDefault());

        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        int calls = data.Bunit.JSInterop.Invocations.Count;
        List<bool> raised = [];
        js.Page().DirtyChanged += value =>
        {
            raised.Add(value);
            return Task.CompletedTask;
        };

        await cut.InvokeAsync(() => js.Page().OnDirty(true));

        Assert.Equal([true], raised);
        Assert.Equal(calls, data.Bunit.JSInterop.Invocations.Count);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.Single(cut.FindAll(".dirty-dot"));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Single(js.Calls("ContextEditor.isDirty"));
    }

    // 177 AC-7/AC-8: a sensitive note mounts with no images, even when a plain file of that name exists.
    [Fact]
    public async Task Sensitive_MountsWithNoImages()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "# Plan\n\n![shot.png](.attachments/shot.png)\n");
        data.WriteFile(Path.Combine(".attachments", "shot.png"), "x");
        File.WriteAllBytes(Path.Combine(data.Root, ".attachments", "shot.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1]);
        EditorJs js = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Sensitive, true));

        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")), Wait);

        Assert.Empty(js.MountedImages());
    }

    // 177 AC-6: the page's OnDrop only raises OnAttach once with the count and files, and writes nothing.
    [Fact]
    public async Task OnDrop_RaisesOnAttachOnce()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        List<DroppedFiles> raised = [];
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.OnAttach, (DroppedFiles d) => raised.Insert(raised.Count, d)));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        DroppedFile file = new DroppedFile("big.png", NoteAttachments.MaxBytes + 1, null);

        await cut.InvokeAsync(() => js.Page().OnDrop(3, [file]));

        DroppedFiles drop = Assert.Single(raised);
        Assert.Equal(3, drop.Count);
        Assert.Equal([file], drop.Files);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    private EditorInterop PageOf(IRenderedComponent<NoteEditor> cut) =>
        ((Microsoft.JSInterop.DotNetObjectReference<EditorInterop>)data.Bunit.JSInterop.Invocations
            .Last(i => i.Identifier == "ContextEditor.mount").Arguments[2]!).Value;

    // 202 AC-7: the page editor gets the note's name, as mount's 5th argument and as isLossless's 2nd.
    [Fact]
    public async Task Open_PassesNameToMountAndIsLossless()
    {
        data.WriteNote("Ideas", "# Ideas\n\nText\n");
        EditorJs js = new EditorJs(data.Bunit);

        await Render("Ideas");

        Assert.Equal("# Ideas\n\nText\n", js.MountedText());
        Assert.Equal("Ideas", js.MountedName());
        Assert.Equal(new object?[] { "# Ideas\n\nText\n", "Ideas" }, js.LosslessArguments());
    }

    // 202 AC-7: a subpage passes its full name; the page editor matches the title on its last segment.
    [Fact]
    public async Task Open_Subpage_PassesFullName()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        EditorJs js = new EditorJs(data.Bunit);

        await Render("people/Anna");

        Assert.Equal("people/Anna", js.MountedName());
        Assert.Equal(new object?[] { "# Anna\n", "people/Anna" }, js.LosslessArguments());
    }

    // 202 AC-7: a reload and a save pass the name again, so the title line stays hidden and kept.
    [Fact]
    public async Task ReloadAndSave_PassName()
    {
        data.WriteNote("inbox", "# inbox\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("inbox");
        data.WriteNote("inbox", "# inbox\n- captured\n");

        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());

        Assert.Equal(2, js.Calls("ContextEditor.mount").Count);
        Assert.Equal("inbox", js.MountedName());
        Assert.Equal(new object?[] { "# inbox\n- captured\n", "inbox" }, js.LosslessArguments());

        js.Edit("# inbox\n- captured\n- more\n");
        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal("Saved inbox.", cut.Find(".status").TextContent);
        Assert.Equal(new object?[] { "# inbox\n- captured\n- more\n", "inbox" }, js.LosslessArguments());
    }

    // 202 AC-7: saving a note that starts with "# <Name>" without an edit writes nothing, even when the page hands
    // back its whole text (the bundle's head + body round trip for that name).
    [Theory]
    [InlineData("# Plan\n\nText #work\n")]
    [InlineData("# Plan\n\n\nBody after two blank lines\n")]
    [InlineData("# plan  \n")]
    public async Task Save_UneditedTitledNote_WritesNothing(string text)
    {
        data.WriteNote("Plan", text);
        string page = new EditorBundle().Json($"ContextEditor.roundTrip({System.Text.Json.JsonSerializer.Serialize(text)}, \"Plan\")");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        Assert.Equal("Plan", js.MountedName());
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        js.Edit(System.Text.Json.JsonSerializer.Deserialize<string>(page)!);

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 202 AC-8: the page title is the only heading in the markup that shows the note's name.
    [Fact]
    public async Task Title_IsTheOnlyHeadingWithTheName()
    {
        data.WriteNote("Ideas", "# Ideas\n\nText\n");
        _ = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("Ideas");

        IElement title = Assert.Single(cut.FindAll("h1, h2, h3, h4, h5, h6"), h => h.TextContent.Contains("Ideas", StringComparison.Ordinal));
        Assert.Equal("H2", title.TagName);
        Assert.True(title.ClassList.Contains("note-title"));
        Assert.Empty(cut.FindAll("h1"));
    }

    // 203 AC-1: after a change, nothing is saved until the pause elapses; a change before then starts the pause again,
    // and the one save that follows writes the latest text, hides the dot and says grey "Saved.".
    [Fact]
    public async Task AutoSave_AfterPause_SavesLatestTextOnce()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        await js.Change(cut, "# Plan\n\nOne\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.Equal("# Plan\n", File.ReadAllText(PlanPath));
        await js.Change(cut, "# Plan\n\nTwo\n");
        cut.WaitForAssertion(() => Assert.Equal((2, 1), (data.AutoSave.Started, data.AutoSave.Pending)), Wait);
        Assert.Equal("# Plan\n", File.ReadAllText(PlanPath));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Equal(["status", "grey"], cut.Find(".status").ClassList.ToArray());
        Assert.Equal("# Plan\n\nTwo\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.Empty(cut.FindAll("span.dirty-dot"));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Equal(0, data.AutoSave.Pending);
    }

    // 203 AC-2: while a save runs the status reads grey "Saving…"; Ctrl+S and SaveFirstAsync wait for it and ask the
    // page nothing until it returns.
    [Fact]
    public async Task AutoSave_WhileRunning_OtherSavesWait()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };
        await js.Change(cut, "# Plan\n\nAuto\n");
        data.AutoSave.Elapse();
        Assert.True(entered.Wait(Wait));
        cut.WaitForAssertion(() => Assert.Equal("Saving…", cut.Find(".status").TextContent), Wait);
        Assert.Equal(["status", "grey"], cut.Find(".status").ClassList.ToArray());
        int asked = js.Calls("ContextEditor.isDirty").Count;

        Task key = cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });
        Task<bool> first = cut.InvokeAsync(() => cut.Instance.SaveFirstAsync());

        Assert.False(key.IsCompleted);
        Assert.False(first.IsCompleted);
        Assert.Equal(asked, js.Calls("ContextEditor.isDirty").Count);
        data.Actions.BeforeWrite = null;
        release.Set();
        await key;
        Assert.True(await first);
        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".status").TextContent), Wait);
        Assert.Equal("# Plan\n\nAuto\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.Empty(cut.FindAll(".status.red"));
    }

    // 203 AC-3: an edit typed while the save runs is not marked clean: the dot stays, and the next pause saves it.
    [Fact]
    public async Task AutoSave_EditDuringSave_StaysDirtyAndSavesAgain()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };
        await js.Change(cut, "# Plan\n\nA\n");
        data.AutoSave.Elapse();
        Assert.True(entered.Wait(Wait));
        await js.Change(cut, "# Plan\n\nAB\n");
        data.Actions.BeforeWrite = null;
        release.Set();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Equal("# Plan\n\nA\n", File.ReadAllText(PlanPath));
        Assert.Equal("# Plan\n\nA\n", Assert.Single(js.Calls("ContextEditor.markCleanIf")).Arguments[1]);
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.markCleanIf").Count), Wait);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
        Assert.Equal("# Plan\n\nAB\n", File.ReadAllText(PlanPath));
        Assert.Equal("Saved.", cut.Find(".status").TextContent);
    }

    // 203 AC-4: a refused auto save (changed on disk) shows its red text, keeps the editor's text without a remount,
    // stays dirty and starts no new save until the next edit.
    [Fact]
    public async Task AutoSave_ChangedOnDisk_RedTextKeptNoRetry()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\nMine\n");
        data.WriteNote("Plan", "# Plan\n\nElsewhere\n");

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), cut.Find(".status").TextContent), Wait);
        Assert.Equal(["status", "red"], cut.Find(".status").ClassList.ToArray());
        Assert.Equal("# Plan\n\nElsewhere\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Equal((1, 0), (data.AutoSave.Started, data.AutoSave.Pending));
        Assert.Empty(js.Calls("ContextEditor.markCleanIf"));

        await js.Change(cut, "# Plan\n\nMine again\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.getMarkdown").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), cut.Find(".status").TextContent), Wait);
        Assert.Single(js.Calls("ContextEditor.mount"));
    }

    // 203 AC-5: an auto save refused because another change runs shows no red text, leaves the status as it was and
    // saves after the next pause.
    [Fact]
    public async Task AutoSave_Busy_StatusKeptRetriedAfterNextPause()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nFirst\n");
        await cut.Find("button.save").ClickAsync(new MouseEventArgs());
        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };
        Task<ActionResult> capture = data.Actions.CaptureText("held");
        Assert.True(entered.Wait(Wait));
        await js.Change(cut, "# Plan\n\nBusy\n");

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal((2, 1), (data.AutoSave.Started, data.AutoSave.Pending)), Wait);
        cut.WaitForAssertion(() => Assert.Equal("Saved Plan.", cut.Find(".status").TextContent), Wait);
        Assert.Equal(["status", "accent"], cut.Find(".status").ClassList.ToArray());
        Assert.DoesNotContain(DesktopActions.BusyText, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("# Plan\n\nFirst\n", File.ReadAllText(PlanPath));
        data.Actions.BeforeWrite = null;
        release.Set();
        Assert.Equal(ActionKind.Done, (await capture).Kind);

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".status").TextContent), Wait);
        Assert.Equal("# Plan\n\nBusy\n", File.ReadAllText(PlanPath));
        Assert.Empty(cut.FindAll("span.dirty-dot"));
    }

    // 203 AC-6: the Save button, Ctrl+S and Cmd+S save at once with the accent text and cancel the pending pause, so
    // no second save follows.
    [Theory]
    [InlineData("button")]
    [InlineData("ctrl")]
    [InlineData("meta")]
    public async Task ExplicitSave_SavesAtOnceAndCancelsThePause(string how)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\nNow\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        if (how == "button")
        {
            await cut.Find("button.save").ClickAsync(new MouseEventArgs());
        }
        else
        {
            await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = how == "ctrl", MetaKey = how == "meta" });
        }

        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
        Assert.Equal(["status", "accent"], cut.Find(".status").ClassList.ToArray());
        Assert.Equal("# Plan\n\nNow\n", File.ReadAllText(PlanPath));
        Assert.Equal((1, 0), (data.AutoSave.Started, data.AutoSave.Pending));
        data.AutoSave.Elapse();
        await cut.InvokeAsync(() => { });
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.Single(js.Calls("ContextEditor.markCleanIf"));
        Assert.Equal("Saved Plan.", cut.Find(".status").TextContent);
    }

    // 203 AC-12: a change report without an edit saves nothing when the pause elapses; the file's bytes stay.
    [Fact]
    public async Task AutoSave_NeverEdited_SavesNothing()
    {
        data.WriteNote("Plan", "* star\r\n");
        byte[] before = File.ReadAllBytes(PlanPath);
        EditorJs js = new EditorJs(data.Bunit, lossless: false);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        EditorInterop page = js.Page();
        data.AutoSave.Elapse();
        await cut.InvokeAsync(() => page.OnChanged());
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);
        int asked = js.Calls("ContextEditor.isDirty").Count;

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(asked + 1, js.Calls("ContextEditor.isDirty").Count), Wait);
        Assert.Equal(before, File.ReadAllBytes(PlanPath));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
        Assert.Equal(string.Empty, cut.Find(".status").TextContent);
        Assert.Equal(0, data.AutoSave.Pending);
    }

    // 203 AC-12: an editor disposed while a pause is pending cancels it; nothing is saved afterwards.
    [Fact]
    public async Task AutoSave_DisposedWhilePending_SavesNothing()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\nLost\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        await data.Bunit.DisposeComponentsAsync();

        Assert.Equal(0, data.AutoSave.Pending);
        data.AutoSave.Elapse();
        Assert.Single(js.Calls("ContextEditor.destroy"));
        Assert.Equal("# Plan\n", File.ReadAllText(PlanPath));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
    }

    // 203 AC-9: the close guard sees unsaved changes while the editor is dirty or an automatic save is pending.
    [Fact]
    public async Task HasUnsaved_WhileDirtyOrPending()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        Assert.False(cut.Instance.HasUnsaved);

        await js.Change(cut, "# Plan\n\nKept\n");

        cut.WaitForAssertion(() => Assert.True(cut.Instance.HasUnsaved), Wait);
        Assert.True(await cut.InvokeAsync(() => cut.Instance.SaveFirstAsync()));
        Assert.False(cut.Instance.HasUnsaved);
        Assert.Equal("# Plan\n\nKept\n", File.ReadAllText(PlanPath));
    }

    private string PlanPath => Path.Combine(data.Root, "Plan.md");

    private async Task<IRenderedComponent<NoteEditor>> RenderPlan(bool sensitive)
    {
        if (!sensitive)
        {
            data.WriteNote("Plan", "# Plan\n");
            _ = new EditorJs(data.Bunit);
            return await Render("Plan");
        }

        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "# Plan\n");
        _ = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Sensitive, true));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        return cut;
    }

    private async Task<IRenderedComponent<NoteEditor>> RenderFailed(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".editor-failed")), Wait);
        return cut;
    }

    private static void AssertFailedLine(IRenderedComponent<NoteEditor> cut, string name)
    {
        IElement line = Assert.Single(cut.FindAll("p.editor-failed"));
        Assert.Equal(["red", "editor-failed"], line.ClassList.ToArray());
        Assert.Equal("alert", line.GetAttribute("role"));
        Assert.Equal($"The editor couldn't start. {name} was not changed.", line.TextContent);
        Assert.Empty(cut.FindAll(".unreadable"));
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".editor-host")), Wait);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        return cut;
    }
}
