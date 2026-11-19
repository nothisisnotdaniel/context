using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 250 AC-10, AC-11: Share through Main (More actions or /share): the checks before the dialog, ShareDialog itself, the
// save dialog and the status line. The page's editor is mocked; the save dialog is FakeFileDialog.
public sealed class ShareDialogTests : IDisposable
{
    // Opening a note and running the action go through Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public ShareDialogTests()
    {
        data.Keys = FakeKeyStore.Ready();
    }

    public void Dispose() => data.Dispose();

    // AC-10: with no note open, /share shows red "Open a note first." and no dialog.
    [Fact]
    public async Task Share_NoNoteOpen_OpenANoteFirst()
    {
        data.WriteNote("Plan", "text\n");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await RunPalette(cut, "/share");

        cut.WaitForAssertion(() => Assert.Equal("Open a note first.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".dialog"));
        Assert.Equal(0, data.Dialog.ShareSaves);
    }

    // AC-10: with unsaved changes that can't be saved, Share asks to save first and opens no dialog.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Share_DirtyEditor_SaveFirst(bool palette)
    {
        data.WriteNote("Plan", "text\n");
        data.Dialog.SharePath = data.Outside("Plan.html");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        js.EditUnsavable("unsaved\n");

        await StartShare(cut, palette);

        cut.WaitForAssertion(() => Assert.Equal("Save Plan first.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".dialog"));
        Assert.Equal(0, data.Dialog.ShareSaves);
        Assert.False(File.Exists(data.Outside("Plan.html")));
    }

    // AC-10, AC-11: a plain note: the dialog has the checked checkbox and the two buttons; Choose file
    // suggests <name>.html and the status shows the accent Done text.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Share_PlainNote_DialogThenWritesPage(bool palette)
    {
        data.WriteNote("Plan", "# Plan\n\nkept\n\nsecret #private\n");
        string target = data.Outside("Plan.html");
        data.Dialog.SharePath = target;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");

        await StartShare(cut, palette);

        IElement dialog = cut.WaitForElement(".dialog[role=dialog]", Wait);
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        IElement box = cut.Find(".dialog input[type=checkbox]");
        Assert.True(box.HasAttribute("checked"));
        Assert.Equal("Leave out lines tagged #private", box.ParentElement!.TextContent.Trim());
        Assert.Equal("LABEL", box.ParentElement.TagName);
        Assert.Equal(["Choose file…", "Cancel"], cut.FindAll(".dialog button").Select(b => b.TextContent).ToArray());
        Assert.All(cut.FindAll(".dialog button"), b => Assert.Equal("button", b.GetAttribute("type")));

        await cut.Find(".dialog button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal($"Shared Plan as HTML: {target}", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("accent"));
        Assert.Equal(1, data.Dialog.ShareSaves);
        Assert.Equal("Plan.html", data.Dialog.ShareSuggestedName);
        Assert.Empty(cut.FindAll(".dialog"));
        string html = File.ReadAllText(target);
        Assert.Contains("<p>kept</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.Ordinal);
    }

    // AC-10: unchecking the box keeps the #private lines.
    [Fact]
    public async Task Share_Unchecked_KeepsPrivateLines()
    {
        data.WriteNote("Plan", "secret #private\n");
        string target = data.Outside("Plan.html");
        data.Dialog.SharePath = target;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await MainTests.ChooseAsync(cut, "share");
        cut.WaitForElement(".dialog input[type=checkbox]", Wait);

        await cut.Find(".dialog input[type=checkbox]").ChangeAsync(new ChangeEventArgs { Value = false });
        Assert.False(cut.Find(".dialog input[type=checkbox]").HasAttribute("checked"));
        await cut.Find(".dialog button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal($"Shared Plan as HTML: {target}", cut.Find(".main-status").TextContent), Wait);
        Assert.Contains("<p>secret #private</p>", File.ReadAllText(target), StringComparison.Ordinal);
    }

    // AC-10, AC-11: Cancel and Esc close the dialog, ask for no file, write nothing and show no status.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Share_CancelOrEsc_NothingWritten(bool escape)
    {
        data.WriteNote("Plan", "text\n");
        data.Dialog.SharePath = data.Outside("Plan.html");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await MainTests.ChooseAsync(cut, "share");
        cut.WaitForElement(".dialog[role=dialog]", Wait);

        if (escape)
        {
            await cut.Find(".dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        }
        else
        {
            await cut.Find(".dialog button.cancel").ClickAsync(new MouseEventArgs());
        }

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dialog")), Wait);
        Assert.Equal(0, data.Dialog.ShareSaves);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);
        Assert.False(File.Exists(data.Outside("Plan.html")));
    }

    // AC-11: a cancelled save dialog writes nothing and shows no status.
    [Fact]
    public async Task Share_SaveDialogCancelled_NothingWritten()
    {
        data.WriteNote("Plan", "text\n");
        data.Dialog.SharePath = null;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await MainTests.ChooseAsync(cut, "share");
        cut.WaitForElement(".dialog button.confirm", Wait);

        await cut.Find(".dialog button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dialog")), Wait);
        Assert.Equal(1, data.Dialog.ShareSaves);
        Assert.Equal(string.Empty, cut.Find(".main-status").TextContent);
        Assert.Empty(Directory.GetFiles(data.Outside(string.Empty), "*.html"));
    }

    // AC-11: a refusal shows the action's text in red, on one line.
    [Fact]
    public async Task Share_ExistingTarget_RedText()
    {
        data.WriteNote("Plan", "text\n");
        string target = data.Outside("Taken.html");
        File.WriteAllText(target, "theirs\n");
        data.Dialog.SharePath = target;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Plan");
        await MainTests.ChooseAsync(cut, "share");
        cut.WaitForElement(".dialog button.confirm", Wait);

        await cut.Find(".dialog button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(
            () => Assert.Equal("That file already exists; choose a new name: " + target, cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Equal("theirs\n", File.ReadAllText(target));
    }

    // AC-10: a sensitive note has no Share item, and /share shows Export's sensitive text in red with no dialog; no
    // file is asked for or written and the key is not asked for again.
    [Fact]
    public async Task Share_SensitiveNote_NoItem_PaletteRefused()
    {
        data.WriteSensitive("Secret", "# Secret\n\nseen\n");
        string target = data.Outside("Secret.html");
        data.Dialog.SharePath = target;
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Secret");
        int agrees = data.Keys.Agrees;
        byte[] sealedBytes = File.ReadAllBytes(Path.Combine(data.Root, "Secret" + NoteStore.SensitiveExtension));

        await MainTests.OpenMenuAsync(cut);
        Assert.Empty(cut.FindAll(".page-menu button.share"));
        await cut.Find(".page-menu-backdrop").ClickAsync(new MouseEventArgs());

        await RunPalette(cut, "/share");

        cut.WaitForAssertion(
            () => Assert.Equal("Secret is sensitive. Make it plain to export it.", cut.Find(".main-status").TextContent), Wait);
        Assert.True(cut.Find(".main-status").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".dialog"));
        Assert.Equal(0, data.Dialog.ShareSaves);
        Assert.False(File.Exists(target));
        Assert.Equal(agrees, data.Keys.Agrees);
        Assert.Equal(sealedBytes, File.ReadAllBytes(Path.Combine(data.Root, "Secret" + NoteStore.SensitiveExtension)));
    }

    private static async Task StartShare(IRenderedComponent<Main> cut, bool palette)
    {
        if (palette)
        {
            await RunPalette(cut, "/share");
        }
        else
        {
            await MainTests.ChooseAsync(cut, "share");
        }
    }

    private static async Task RunPalette(IRenderedComponent<Main> cut, string line)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = line });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);

        // The links panel loads behind Task.Run; wait until it is in before the menu is opened.
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }
}
