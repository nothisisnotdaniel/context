using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Context.Tests;

// 094 AC-4 to AC-9: the note editor with the page's editor mocked through bunit's JS interop.
public sealed class NoteEditorTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: the editor's messages go to the status bar, which Main renders; tests render the bar next to the editor.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

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

    // 210 AC-8: a note the editor would reformat (CRLF + BOM, "* list") shows no reformat line, and the page is never
    // asked whether it is lossless.
    [Fact]
    public async Task Open_LossyNote_NoReformatLine()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(
            Path.Combine(data.Root, "Plan.md"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("* star\r\n__b__\r\n")]);
        EditorJs js = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Empty(cut.FindAll(".lossy"));
        Assert.DoesNotContain("Saving will reformat", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(js.Calls("ContextEditor.isLossless"));

        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());
        js.Edit("* star\n\n__b__ more\n");
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal("Saved Plan.", data.StatusBar.Text);
        Assert.Empty(cut.FindAll(".lossy"));
        Assert.Empty(js.Calls("ContextEditor.isLossless"));
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
        Assert.False(cut.Instance.IsReady);
        Assert.Empty(cut.FindAll(".editor-failed"));
        AssertNoSaveButton(cut);
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
        AssertNoSaveButton(cut);
        Assert.False(cut.Instance.IsReady);
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

    // 143 AC-2, AC-5, 210 AC-9: a failed mount of a note the editor would reformat shows the red line, no reformat line,
    // never asks isLossless, and nothing can be saved.
    [Fact]
    public async Task Open_LossyNoteMountThrows_EditorFailedLineNoLossy()
    {
        data.WriteNote("Plan", "* star\r\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.mount", new JSException("boom-143"));

        IRenderedComponent<NoteEditor> cut = await RenderFailed("Plan");

        AssertFailedLine(cut, "Plan");
        Assert.Empty(cut.FindAll(".lossy"));
        Assert.Empty(js.Calls("ContextEditor.isLossless"));
        Assert.False(cut.Instance.IsReady);
        Assert.Null(await cut.InvokeAsync(() => cut.Instance.SaveAsync(false)));
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
        EditorJs js = new EditorJs(data.Bunit);
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

        Assert.False(cut.Instance.IsReady);
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
        Assert.True(cut.Instance.IsReady);
        SaveRequest? request = await cut.InvokeAsync(() => cut.Instance.PrepareSaveAsync());
        Assert.NotNull(request);
        Assert.Equal("Plan", request.Note.Name);
        Assert.DoesNotContain("boom-143", cut.Markup, StringComparison.Ordinal);
    }

    // 177 review nit, 455 AC-3: the editor has no status line of its own; the empty bar has no child nodes at all.
    [Fact]
    public async Task EmptyStatus_HasNoChildNodes()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        Assert.Empty(cut.FindAll("p.status"));
        Assert.Equal(0, Bar.Find(".main-status").ChildNodes.Length);
    }

    // AC-8: an explicit save (Ctrl+S) saves the editor's Markdown and says so.
    [Fact]
    public async Task ExplicitSave_SavesMarkdown()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMore #new\n");

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal("# Plan\n\nMore #new\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        IElement status = Bar.Find(".main-status");
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
        Assert.Equal("Saved Plan.", data.StatusBar.Text);
        Assert.Equal("accent", data.StatusBar.Css);
    }

    // AC-5: saving without an edit writes nothing, even for a lossy CRLF + BOM note, and never asks for Markdown.
    [Fact]
    public async Task Save_NotDirty_WritesNothing()
    {
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(
            Path.Combine(data.Root, "Plan.md"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("* star\r\n__b__\r\n")]);
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
        Assert.Empty(js.Calls("ContextEditor.getMarkdown"));
        Assert.Equal("Saved Plan.", data.StatusBar.Text);
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

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal("# Theirs\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        IElement status = Bar.Find(".main-status");
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

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.False(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal("Plan no longer exists. Nothing was saved.", data.StatusBar.Text);
    }

    // AC-9: a link to a missing note shows the grey line.
    [Fact]
    public async Task LinkClick_Missing_GreyLine()
    {
        data.WriteNote("Plan", "See [[Ghost]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");

        await cut.InvokeAsync(() => js.Page().OnLink("[[Ghost]]"));

        IElement status = Bar.Find(".main-status");
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
            Assert.Equal("No note named Other|alias.", data.StatusBar.Text);
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

    // 209 AC-1, AC-2: an open note, plain or sensitive, has no Save button; a clean editor bar holds only the title
    // (and, since 283, the formatting toolbar).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_NoSaveButton_CleanBarHoldsOnlyTheTitle(bool sensitive)
    {
        IRenderedComponent<NoteEditor> cut = await RenderPlan(sensitive);

        AssertNoSaveButton(cut);
        Assert.Equal(
            ["H2.note-title cut", "DIV.format-bar"],
            cut.Find(".editor-bar").Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
    }

    // 209 AC-7: IsReady is false until the page's mount ends, true once a readable note is mounted and false while a
    // save runs.
    [Fact]
    public async Task IsReady_FalseBeforeMountAndWhileSaving()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        TaskCompletionSource mountAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        JSRuntimeInvocationHandler mount = data.Bunit.JSInterop.SetupVoid("ContextEditor.mount", _ =>
        {
            mountAsked.TrySetResult();
            return true;
        });
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Plan"));

        Assert.False(cut.Instance.IsReady);
        await mountAsked.Task.WaitAsync(Wait);
        Assert.False(cut.Instance.IsReady);

        await cut.InvokeAsync(() => mount.SetVoidResult());

        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        js.Edit("# Plan\n\nHeld\n");
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };
        Task key = cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });
        Assert.True(entered.Wait(Wait));
        Bar.WaitForAssertion(() => Assert.Equal("Saving…", data.StatusBar.Text), Wait);

        Assert.False(cut.Instance.IsReady);

        data.Actions.BeforeWrite = null;
        release.Set();
        await key;
        Bar.WaitForAssertion(() => Assert.Equal("Saved Plan.", data.StatusBar.Text), Wait);
        Assert.True(cut.Instance.IsReady);
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
        AssertNoSaveButton(cut);
        Assert.Equal(
            ["H2.note-title cut", "SPAN.dirty-dot", "DIV.format-bar"],
            cut.Find(".editor-bar").Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
        Assert.Empty(title.QuerySelectorAll(".dirty-dot"));
        Assert.Equal("Plan", title.TextContent);
        Assert.Equal("Plan", title.GetAttribute("title"));
    }

    // 489 AC-5: the sticky bar's markup is unchanged: div.editor-bar is a direct child of section.note-editor and holds
    // the title, the dirty dot and the format bar; the attribute box and the editor host follow it as siblings.
    [Fact]
    public async Task EditorBar_StickyMarkup_BarThenAttributesThenHost()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n\nBody\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);

        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");
        await cut.InvokeAsync(() => PageOf(cut).OnDirty(true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".dirty-dot")), Wait);

        Assert.Single(cut.FindAll("div.editor-host"));
        Assert.Empty(cut.FindAll("p.editor-failed"));
        IElement section = cut.Find("section.note-editor");
        IElement bar = Assert.Single(cut.FindAll("div.editor-bar"));
        Assert.Equal(("SECTION", "note-editor"), (bar.ParentElement!.TagName, bar.ParentElement.ClassName));
        Assert.Equal(
            ["H2.note-title cut", "SPAN.dirty-dot", "DIV.format-bar"],
            bar.Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
        Assert.Equal(
            ["DIV.editor-bar", "DETAILS.note-attributes", "DIV.editor-host"],
            section.Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
        Assert.Empty(bar.QuerySelectorAll(".note-attributes, .editor-host"));
    }

    // 141 AC-3: a successful save (SaveAsync, as /save, or Ctrl/Cmd+S) removes the dot at once; the mocked page never
    // calls back.
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
            await cut.InvokeAsync(() => cut.Instance.SaveAsync(false));
        }

        Assert.Equal("Saved Plan.", data.StatusBar.Text);
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

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        IElement status = Bar.Find(".main-status");
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
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
        int calls = data.JsCalls().Count;
        List<bool> raised = [];
        js.Page().DirtyChanged += value =>
        {
            raised.Add(value);
            return Task.CompletedTask;
        };

        await cut.InvokeAsync(() => js.Page().OnDirty(true));

        Assert.Equal([true], raised);
        Assert.Equal(calls, data.JsCalls().Count);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
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
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        DroppedFile file = new DroppedFile("big.png", NoteAttachments.MaxBytes + 1, null);

        await cut.InvokeAsync(() => js.Page().OnDrop(3, [file]));

        DroppedFiles drop = Assert.Single(raised);
        Assert.Equal(3, drop.Count);
        Assert.Equal([file], drop.Files);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    private EditorInterop PageOf(IRenderedComponent<NoteEditor> cut) =>
        ((Microsoft.JSInterop.DotNetObjectReference<EditorInterop>)data.JsCalls()
            .Last(i => i.Identifier == "ContextEditor.mount").Arguments[2]!).Value;

    // 202 AC-7: the page editor gets the note's name as mount's 5th argument (210: isLossless is never asked).
    [Fact]
    public async Task Open_PassesNameToMount()
    {
        data.WriteNote("Ideas", "# Ideas\n\nText\n");
        EditorJs js = new EditorJs(data.Bunit);

        await Render("Ideas");

        Assert.Equal("# Ideas\n\nText\n", js.MountedText());
        Assert.Equal("Ideas", js.MountedName());
        Assert.Empty(js.Calls("ContextEditor.isLossless"));
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
        Assert.Empty(js.Calls("ContextEditor.isLossless"));
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
        Assert.Equal("# inbox\n- captured\n", js.MountedText());

        js.Edit("# inbox\n- captured\n- more\n");
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal("Saved inbox.", data.StatusBar.Text);
        Assert.Equal("# inbox\n- captured\n- more\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Empty(js.Calls("ContextEditor.isLossless"));
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
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
        js.Edit(System.Text.Json.JsonSerializer.Deserialize<string>(page)!);

        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        Assert.Equal("Saved Plan.", data.StatusBar.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName));
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

    // 235 AC-1: a subpage's title shows only its own name; the tooltip holds the full name.
    [Fact]
    public async Task Title_Subpage_ShowsLastSegment_TooltipFullName()
    {
        WriteSubpage("c", "# c\n");
        _ = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("a/b/c");

        IElement title = cut.Find("h2.note-title");
        Assert.Equal("c", title.TextContent);
        Assert.Equal("a/b/c", title.GetAttribute("title"));
    }

    // 235 AC-2: a top-level note shows its whole name in both, and the bar is still the title, then the dirty dot
    // (then the formatting toolbar, 283).
    [Fact]
    public async Task Title_TopLevel_ShowsWholeName_BarUnchanged()
    {
        IRenderedComponent<NoteEditor> cut = await RenderPlan(false);

        IElement title = cut.Find("h2.note-title");
        Assert.Equal("Plan", title.TextContent);
        Assert.Equal("Plan", title.GetAttribute("title"));
        Assert.Equal(
            ["H2.note-title cut", "DIV.format-bar"],
            cut.Find(".editor-bar").Children.Select(c => c.TagName + "." + c.ClassName).ToArray());

        await cut.InvokeAsync(() => PageOf(cut).OnDirty(true));

        Assert.Equal(
            ["H2.note-title cut", "SPAN.dirty-dot", "DIV.format-bar"],
            cut.Find(".editor-bar").Children.Select(c => c.TagName + "." + c.ClassName).ToArray());
    }

    // 235 AC-3: a last segment with control characters (no such note can be on disk, so it shows Can't read) is
    // cleaned like the full name: no raw control character reaches the title or its tooltip.
    [Fact]
    public async Task Title_Subpage_ControlCharsInSegment_AreCleaned()
    {
        const string Segment = "c\u0007evil\u001b\u0085x";
        _ = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "a/b/" + Segment));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")), Wait);

        IElement title = cut.Find("h2.note-title");
        Assert.Equal("c\uFFFDevil\uFFFD\uFFFDx", title.TextContent);
        Assert.Equal(DesktopText.OneLine("a/b/" + Segment), title.GetAttribute("title"));
        Assert.DoesNotContain(title.TextContent, char.IsControl);
        Assert.DoesNotContain(title.GetAttribute("title")!, char.IsControl);
    }

    // 235 AC-3: a last segment with bidi controls shows them as placeholders; no raw bidi character reaches the title or
    // its tooltip.
    [Fact]
    public async Task Title_Subpage_BidiSegment_NoRawBidi()
    {
        const string Segment = "c\u202Eevil\u2066x";
        WriteSubpage(Segment, "text\n");
        _ = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("a/b/" + Segment);

        IElement title = cut.Find("h2.note-title");
        Assert.Equal("c\uFFFDevil\uFFFDx", title.TextContent);
        Assert.Equal("a/b/c\uFFFDevil\uFFFDx", title.GetAttribute("title"));
        Assert.DoesNotContain(title.TextContent, IsBidiControl);
        Assert.DoesNotContain(title.GetAttribute("title")!, IsBidiControl);
        Assert.DoesNotContain(cut.Markup, IsBidiControl);
    }

    // 235 AC-4: an unreadable subpage names the full name in its red line.
    [Fact]
    public async Task Open_SubpageUnreadable_LineNamesFullName()
    {
        WriteSubpage("c", "text");
        _ = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        using FileStream held = new FileStream(
            Path.Combine(data.Root, "a", "b", "c.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "a/b/c"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")), Wait);

        Assert.Equal("Can't read a/b/c.", cut.Find(".unreadable").TextContent);
    }

    // 235 AC-4: an editor start failure on a subpage names the full name.
    [Fact]
    public async Task Open_SubpageMountThrows_LineNamesFullName()
    {
        WriteSubpage("c", "# c\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.Fail("ContextEditor.mount", new JSException("boom-235"));

        IRenderedComponent<NoteEditor> cut = await RenderFailed("a/b/c");

        AssertFailedLine(cut, "a/b/c");
    }

    // 235 AC-7 (202 AC-8): for a subpage, the title is still the only heading with the shown name, and there is no h1.
    [Fact]
    public async Task Title_Subpage_IsTheOnlyHeadingWithTheName()
    {
        WriteSubpage("c", "# c\n\nText\n");
        _ = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("a/b/c");

        IElement title = Assert.Single(cut.FindAll("h1, h2, h3, h4, h5, h6"), h => h.TextContent.Contains('c', StringComparison.Ordinal));
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

        Bar.WaitForAssertion(() => Assert.Equal("Saved.", data.StatusBar.Text), Wait);
        Assert.Equal("grey", data.StatusBar.Css);
        Assert.Equal("# Plan\n\nTwo\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
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
        Bar.WaitForAssertion(() => Assert.Equal("Saving…", data.StatusBar.Text), Wait);
        Assert.Equal("grey", data.StatusBar.Css);
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
        Bar.WaitForAssertion(() => Assert.Equal("Saved Plan.", data.StatusBar.Text), Wait);
        Assert.Equal("# Plan\n\nAuto\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.NotEqual("red", data.StatusBar.Css);
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

        Bar.WaitForAssertion(() => Assert.Equal("Saved.", data.StatusBar.Text), Wait);
        Assert.Equal("# Plan\n\nA\n", File.ReadAllText(PlanPath));
        Assert.Equal("# Plan\n\nA\n", Assert.Single(js.Calls("ContextEditor.markCleanIf")).Arguments[1]);
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.markCleanIf").Count), Wait);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
        Assert.Equal("# Plan\n\nAB\n", File.ReadAllText(PlanPath));
        Assert.Equal("Saved.", data.StatusBar.Text);
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

        Bar.WaitForAssertion(() => Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), data.StatusBar.Text), Wait);
        Assert.Equal("red", data.StatusBar.Css);
        Assert.Equal("# Plan\n\nElsewhere\n", File.ReadAllText(PlanPath));
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.IsDirtyAsync()));
        Assert.Equal((1, 0), (data.AutoSave.Started, data.AutoSave.Pending));
        Assert.Empty(js.Calls("ContextEditor.markCleanIf"));

        await js.Change(cut, "# Plan\n\nMine again\n");
        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.getMarkdown").Count), Wait);
        Bar.WaitForAssertion(() => Assert.Equal(DesktopActions.ChangedOnDiskText("Plan"), data.StatusBar.Text), Wait);
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
        await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = true });
        Assert.Equal("Saved Plan.", data.StatusBar.Text);
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
        Bar.WaitForAssertion(() => Assert.Equal("Saved Plan.", data.StatusBar.Text), Wait);
        Assert.Equal("accent", data.StatusBar.Css);
        Assert.DoesNotContain(DesktopActions.BusyText, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("# Plan\n\nFirst\n", File.ReadAllText(PlanPath));
        data.Actions.BeforeWrite = null;
        release.Set();
        Assert.Equal(ActionKind.Done, (await capture).Kind);

        data.AutoSave.Elapse();

        Bar.WaitForAssertion(() => Assert.Equal("Saved.", data.StatusBar.Text), Wait);
        Assert.Equal("# Plan\n\nBusy\n", File.ReadAllText(PlanPath));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
    }

    // 203 AC-6: an explicit SaveAsync (as /save), Ctrl+S and Cmd+S save at once with the accent text and cancel the pending pause, so
    // no second save follows.
    [Theory]
    [InlineData("explicit")]
    [InlineData("ctrl")]
    [InlineData("meta")]
    public async Task ExplicitSave_SavesAtOnceAndCancelsThePause(string how)
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        await js.Change(cut, "# Plan\n\nNow\n");
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        if (how == "explicit")
        {
            await cut.InvokeAsync(() => cut.Instance.SaveAsync(false));
        }
        else
        {
            await cut.Find(".note-editor").KeyDownAsync(new KeyboardEventArgs { Key = "s", CtrlKey = how == "ctrl", MetaKey = how == "meta" });
        }

        Assert.Equal("Saved Plan.", data.StatusBar.Text);
        Assert.Equal("accent", data.StatusBar.Css);
        Assert.Equal("# Plan\n\nNow\n", File.ReadAllText(PlanPath));
        Assert.Equal((1, 0), (data.AutoSave.Started, data.AutoSave.Pending));
        data.AutoSave.Elapse();
        await cut.InvokeAsync(() => { });
        Assert.Single(js.Calls("ContextEditor.getMarkdown"));
        Assert.Single(js.Calls("ContextEditor.markCleanIf"));
        Assert.Equal("Saved Plan.", data.StatusBar.Text);
    }

    // 203 AC-12: a change report without an edit saves nothing when the pause elapses; the file's bytes stay.
    [Fact]
    public async Task AutoSave_NeverEdited_SavesNothing()
    {
        data.WriteNote("Plan", "* star\r\n");
        byte[] before = File.ReadAllBytes(PlanPath);
        EditorJs js = new EditorJs(data.Bunit);
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
        Assert.Equal(string.Empty, data.StatusBar.Text);
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

    private const string PersonRows = "[{\"key\":\"aliases\",\"value\":\"A, B\"},{\"key\":\"Role\",\"value\":\"\"}]";

    // 236 AC-5, AC-6: a note with rows shows the collapsed box right after the bar: its count, and per row the key, a
    // plain text value field and a Remove button.
    [Fact]
    public async Task Attributes_ShownCollapsedAfterTheBar()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n\nBody\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);

        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        IElement box = cut.Find("details.note-attributes");
        Assert.False(box.HasAttribute("open"));
        Assert.Equal("editor-bar", box.PreviousElementSibling!.ClassName);
        Assert.Equal("DIV", box.PreviousElementSibling.TagName);
        Assert.Equal("Attributes (2)", box.QuerySelector("summary")!.TextContent);
        Assert.Equal(["aliases", "Role"], cut.FindAll(".attribute-key").Select(k => k.TextContent));
        IElement value = cut.FindAll("input.attribute-value")[0];
        Assert.Equal("text", value.GetAttribute("type"));
        Assert.Equal("400", value.GetAttribute("maxlength"));
        Assert.Equal("aliases value", value.GetAttribute("aria-label"));
        Assert.Equal("A, B", value.GetAttribute("value"));
        Assert.Equal(["Remove aliases", "Remove Role"], cut.FindAll("button.attribute-remove").Select(b => b.GetAttribute("aria-label")));
        Assert.All(cut.FindAll(".note-attributes button"), b => Assert.Equal("button", b.GetAttribute("type")));
        IElement key = cut.Find("input.attribute-new-key");
        Assert.Equal(("New attribute name", "50"), (key.GetAttribute("aria-label"), key.GetAttribute("maxlength")));
        IElement newValue = cut.Find("input.attribute-new-value");
        Assert.Equal(("New attribute value", "400"), (newValue.GetAttribute("aria-label"), newValue.GetAttribute("maxlength")));
        Assert.Equal("Add", cut.Find("button.attribute-add-button").TextContent);
        Assert.Empty(cut.FindAll(".attribute-error"));
    }

    // 236 AC-6: a key and value are shown on one line through OneLine.
    [Fact]
    public async Task Attributes_KeyShownOnOneLine()
    {
        data.WriteNote("Plan", "x:: y\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows("[{\"key\":\"a\\nb\",\"value\":\"c\\nd\"}]");

        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        Assert.Equal("a b", cut.Find(".attribute-key").TextContent);
        Assert.Equal("c d", cut.Find("input.attribute-value").GetAttribute("value"));
    }

    // 236 AC-5: no rows, a malformed list or a failing read show no box.
    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("[{\"key\":1,\"value\":\"x\"}]")]
    public async Task Attributes_NoRows_NoBox(string rows)
    {
        data.WriteNote("Plan", "# Plan\n\nBody\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(rows);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        cut.WaitForAssertion(() => Assert.NotEmpty(js.Calls("ContextEditor.headings")), Wait);

        Assert.Single(js.Calls("ContextEditor.attributes"));
        Assert.Empty(cut.FindAll(".note-attributes"));
    }

    // 236 AC-5: an unreadable note and an editor that didn't start show no box and never ask for rows.
    [Fact]
    public async Task Attributes_UnreadableOrFailed_NoBox()
    {
        data.WriteNote("Plan", "# Plan\n\nrole:: x\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        js.Fail("ContextEditor.mount", new JSException("no page"));

        IRenderedComponent<NoteEditor> failed = await RenderFailed("Plan");

        Assert.Empty(failed.FindAll(".note-attributes"));
        Assert.Empty(js.Calls("ContextEditor.attributes"));

        data.WriteNote("Big", new string('a', (int)NoteStore.MaxEditBytes + 1));
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> big = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Big"));
        big.WaitForAssertion(() => Assert.Single(big.FindAll(".unreadable")), Wait);

        Assert.Empty(big.FindAll(".note-attributes"));
        Assert.Empty(js.Calls("ContextEditor.attributes"));
    }

    // 236 AC-5: the rows are read again after a reload and a shown restore.
    [Fact]
    public async Task Attributes_RereadAfterReloadAndShowSaved()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        js.SetAttributeRows("[{\"key\":\"type\",\"value\":\"person\"}]");
        await cut.InvokeAsync(() => cut.Instance.ReloadAsync());
        cut.WaitForAssertion(() => Assert.Equal("Attributes (1)", cut.Find(".note-attributes summary").TextContent), Wait);
        Assert.Equal(2, js.Calls("ContextEditor.attributes").Count);

        js.SetAttributeRows("[]");
        EditableNote saved = (await data.State!.ReadForEdit("Plan"))!;
        await cut.InvokeAsync(() => cut.Instance.ShowSavedAsync(saved));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-attributes")), Wait);
        Assert.Equal(3, js.Calls("ContextEditor.attributes").Count);
    }

    // 236 AC-6, AC-7: a changed value sends every row (the changed one trimmed and collapsed), shows the dot, restarts
    // the pause, and the save after it writes getMarkdown.
    [Fact]
    public async Task Attributes_ChangedValue_SetsAndSavesAfterPause()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit, markdown: "# Plan\n\naliases:: A, B\nRole:: lead dev\n");
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");
        Assert.Empty(cut.FindAll("span.dirty-dot"));

        await cut.FindAll("input.attribute-value")[1].ChangeAsync(new ChangeEventArgs { Value = "  lead \t  dev " });

        Assert.Equal(
            ["[{\"line\":0,\"key\":\"aliases\",\"value\":\"A, B\"},{\"line\":1,\"key\":\"Role\",\"value\":\"lead dev\"}]"],
            js.AttributeEdits());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("span.dirty-dot")), Wait);
        Assert.Equal(1, data.AutoSave.Pending);
        Assert.Equal(2, js.Calls("ContextEditor.attributes").Count);
        Assert.Equal("# Plan\n\naliases:: A, B\nRole::\n", File.ReadAllText(PlanPath));

        data.AutoSave.Elapse();

        Bar.WaitForAssertion(() => Assert.Equal("Saved.", data.StatusBar.Text), Wait);
        Assert.Equal("# Plan\n\naliases:: A, B\nRole:: lead dev\n", File.ReadAllText(PlanPath));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("span.dirty-dot")), Wait);
    }

    // 236 review nit: a value longer than the field allows is cut to 400 chars before it reaches the page.
    [Fact]
    public async Task Attributes_LongValue_CappedAt400()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        await cut.FindAll("input.attribute-value")[1].ChangeAsync(new ChangeEventArgs { Value = new string('v', 500) });

        string edits = Assert.Single(js.AttributeEdits());
        Assert.Contains("\"value\":\"" + new string('v', 400) + "\"}", edits, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('v', 401), edits, StringComparison.Ordinal);
    }

    // 236 AC-6: Remove sends every other row as it is.
    [Fact]
    public async Task Attributes_Remove_SendsTheOtherRows()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        await cut.FindAll("button.attribute-remove")[0].ClickAsync(new MouseEventArgs());

        Assert.Equal(["[{\"line\":1,\"key\":\"Role\",\"value\":\"\"}]"], js.AttributeEdits());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("span.dirty-dot")), Wait);
        Assert.Equal(1, data.AutoSave.Pending);
    }

    // 236 AC-7: a name AttributeParser refuses shows the red line in the box and sends nothing.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1st")]
    [InlineData("due date")]
    [InlineData("role::")]
    public async Task Attributes_AddInvalidName_RedLineNothingSent(string name)
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        await cut.Find("input.attribute-new-key").InputAsync(new ChangeEventArgs { Value = name });
        await cut.Find("input.attribute-new-value").InputAsync(new ChangeEventArgs { Value = "x" });
        await cut.Find("button.attribute-add-button").ClickAsync(new MouseEventArgs());

        IElement error = cut.Find(".note-attributes p.attribute-error");
        Assert.Equal("Type an attribute name that starts with a letter.", error.TextContent);
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.True(error.ClassList.Contains("red"));
        Assert.Empty(js.AttributeEdits());
        Assert.Empty(cut.FindAll("span.dirty-dot"));
    }

    // 236 AC-7: a valid name adds a row with the normalized key and the trimmed, collapsed value, after every row.
    [Fact]
    public async Task Attributes_AddValidName_AppendsNormalizedRow()
    {
        data.WriteNote("Plan", "# Plan\n\naliases:: A, B\nRole::\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows(PersonRows);
        IRenderedComponent<NoteEditor> cut = await RenderAttributes("Plan");

        await cut.Find("input.attribute-new-key").InputAsync(new ChangeEventArgs { Value = "  Start-Date_2 " });
        await cut.Find("input.attribute-new-value").InputAsync(new ChangeEventArgs { Value = "  2026  11 " });
        await cut.Find("button.attribute-add-button").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            ["[{\"line\":0,\"key\":\"aliases\",\"value\":\"A, B\"},{\"line\":1,\"key\":\"Role\",\"value\":\"\"},{\"line\":-1,\"key\":\"start-date_2\",\"value\":\"2026 11\"}]"],
            js.AttributeEdits());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("span.dirty-dot")), Wait);
        Assert.Empty(cut.FindAll(".attribute-error"));
        Assert.Equal(string.Empty, cut.Find("input.attribute-new-key").GetAttribute("value"));
    }

    // 236: a sensitive note's rows go with its editor when the session locks.
    [Fact]
    public async Task Attributes_SensitiveLock_RowsGone()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "# Plan\n\nrole:: x\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetAttributeRows("[{\"key\":\"role\",\"value\":\"x\"}]");
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Sensitive, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".note-attributes")), Wait);

        data.Session!.Lock();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".note-attributes")), Wait);
    }

    // 283 AC-3: the formatting toolbar in the editor bar: two buttons with the platform's shortcut names in their titles.
    [Theory]
    [InlineData(true, "Inline code (Cmd+E)", "Code block (Cmd+Option+C)")]
    [InlineData(false, "Inline code (Ctrl+E)", "Code block (Ctrl+Alt+C)")]
    public async Task FormatBar_ShowsButtonsWithShortcuts(bool isMac, string inlineTitle, string blockTitle)
    {
        data.IsMac = isMac;
        data.WriteNote("Plan", "# Plan\n\nText\n");
        EditorJs js = new EditorJs(data.Bunit);

        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        cut.WaitForAssertion(() => Assert.NotEmpty(js.Calls("ContextEditor.mount")), Wait);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".editor-bar > div.format-bar")), Wait);

        IElement bar = cut.Find(".editor-bar > div.format-bar");
        Assert.Equal("toolbar", bar.GetAttribute("role"));
        Assert.Equal("Formatting", bar.GetAttribute("aria-label"));
        IReadOnlyList<IElement> buttons = bar.QuerySelectorAll("button").ToList();
        Assert.Equal(["Inline code", "Code block"], buttons.Select(b => b.TextContent));
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal([inlineTitle, blockTitle], buttons.Select(b => b.GetAttribute("title")));
    }

    // 283 AC-3: a click runs the command on the editor's own element (the one mounted); mousedown keeps the selection.
    [Theory]
    [InlineData("Inline code", "ContextEditor.toggleInlineCode", "ContextEditor.toggleCodeBlock")]
    [InlineData("Code block", "ContextEditor.toggleCodeBlock", "ContextEditor.toggleInlineCode")]
    public async Task FormatBar_ClickRunsTheCommand(string label, string called, string notCalled)
    {
        data.WriteNote("Plan", "# Plan\n\nText\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        cut.WaitForAssertion(() => Assert.NotEmpty(js.Calls("ContextEditor.mount")), Wait);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".format-bar")), Wait);

        await cut.FindAll(".format-bar button").Single(b => b.TextContent == label).ClickAsync(new MouseEventArgs());

        JSRuntimeInvocation call = Assert.Single(js.Calls(called));
        Assert.Equal(js.Calls("ContextEditor.mount")[^1].Arguments[0], call.Arguments[0]);
        Assert.Empty(js.Calls(notCalled));
        string razor = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "NoteEditor.razor"));
        Assert.Equal(2, Regex.Matches(razor, @"<button type=""button"" class=""format-[a-z-]+""[^>]*@onmousedown:preventDefault=""true""").Count);
    }

    // 283 AC-3: no toolbar for an unreadable note or an editor that failed to start.
    [Fact]
    public async Task FormatBar_UnreadableOrFailed_NoBar()
    {
        data.WriteNote("Bad", "text");
        EditorJs js = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        using (new FileStream(Path.Combine(data.Root, "Bad.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            IRenderedComponent<NoteEditor> unreadable = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Bad"));
            unreadable.WaitForAssertion(() => Assert.Single(unreadable.FindAll(".unreadable")), Wait);
            Assert.Empty(unreadable.FindAll(".format-bar"));
        }

        data.WriteNote("Plan", "# Plan\n");
        js.Fail("ContextEditor.mount", new JSException("boom-283"));
        IRenderedComponent<NoteEditor> failed = await RenderFailed("Plan");
        Assert.Empty(failed.FindAll(".format-bar"));
    }

    // 283 AC-3: a sensitive note's toolbar goes when the session locks.
    [Fact]
    public async Task FormatBar_SensitiveLock_BarGone()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Plan", "# Plan\n\nText\n");
        EditorJs js = new EditorJs(data.Bunit);
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p
            .Add(e => e.Name, "Plan")
            .Add(e => e.Sensitive, true));
        cut.WaitForAssertion(() => Assert.NotEmpty(js.Calls("ContextEditor.mount")), Wait);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".format-bar")), Wait);

        data.Session!.Lock();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".format-bar")), Wait);
    }

    private async Task<IRenderedComponent<NoteEditor>> RenderAttributes(string name)
    {
        IRenderedComponent<NoteEditor> cut = await Render(name);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".note-attributes")), Wait);
        return cut;
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
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        return cut;
    }

    // 209 AC-1: neither a .save button nor any button whose text is Save.
    private static void AssertNoSaveButton(IRenderedComponent<NoteEditor> cut)
    {
        Assert.Empty(cut.FindAll("button.save"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Save");
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

    private static bool IsBidiControl(char c) => c is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069';

    // The pages a, a/b and a/b/<last> on disk.
    private void WriteSubpage(string last, string text)
    {
        data.WriteNote("a", "# a\n");
        data.WriteFile(Path.Combine("a", "b.md"), "# b\n");
        data.WriteFile(Path.Combine("a", "b", last + ".md"), text);
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".editor-host")), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.Instance.IsReady), Wait);
        return cut;
    }
}
