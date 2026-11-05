using System.Text;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 094 AC-4 to AC-9: the note editor with the page's editor mocked through bunit's JS interop.
public sealed class NoteEditorTests : IDisposable
{
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
        Directory.CreateDirectory(data.Root);
        File.WriteAllBytes(Path.Combine(data.Root, "Bad.md"), [0x00, 0x43, 0x54, 0x58, 0x45, 0x01, 1, 2, 3, 4]);
        EditorJs js = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Bad"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")));

        IElement line = cut.Find(".unreadable");
        Assert.Equal("Can't read Bad.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.mount"));
        Assert.True(cut.Find("button.save").HasAttribute("disabled"));
    }

    // A note over 5 MB is not sent to the page editor: the red line, nothing mounted.
    [Fact]
    public async Task Open_OverLimit_RedLineNoMount()
    {
        data.WriteNote("Big", new string('a', (int)NoteStore.MaxEditBytes + 1));
        EditorJs js = new EditorJs(data.Bunit);

        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, "Big"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".unreadable")));

        Assert.Equal("Can't read Big.", cut.Find(".unreadable").TextContent);
        Assert.Empty(js.Calls("ContextEditor.mount"));
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
        Assert.Single(js.Calls("ContextEditor.markClean"));
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

    // AC-6: with encryption.json the save is refused with the locked text.
    [Fact]
    public async Task Save_Locked_RefusedLockedText()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan");
        js.Edit("# Plan\n\nMine\n");
        Vault.Create(data.Root, "a long enough passphrase", 1000).Dispose();

        await cut.Find("button.save").ClickAsync(new MouseEventArgs());

        Assert.Equal("# Plan\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(DesktopActions.LockedText, cut.Find(".status").TextContent);
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
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")));

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
        cut.WaitForAssertion(() => Assert.Single(js.Calls("ContextEditor.mount")));

        await cut.InvokeAsync(() => js.Page().OnTag(raw));

        Assert.Equal([tag], tags);
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(string name)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p => p.Add(e => e.Name, name));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".editor-host")));
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")));
        return cut;
    }
}
