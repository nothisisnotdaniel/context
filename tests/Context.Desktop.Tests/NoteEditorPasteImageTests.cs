using Bunit;

namespace Context.Tests;

// 435 AC-7: a paste of pictures in the open note goes OnPasteImages -> Main.AttachPasted -> DesktopActions.PasteImages ->
// NoteEditor.ShowAttachedAsync, which inserts each stored image; the next auto save writes the link.
public sealed class NoteEditorPasteImageTests : IDisposable
{
    // 150: opening a note runs on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 4, 5];

    private readonly DesktopFixture data = new() { LocalNow = new DateTime(2026, 11, 26, 9, 30, 0, DateTimeKind.Local) };

    public void Dispose() => data.Dispose();

    // AC-7: one insertAttachment per stored image with its src, name, isImage and base64, the accent line, and the
    // inserted link saved by the next auto save.
    [Fact]
    public async Task PasteImages_InsertsShowsLineAndSaves()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");

        await cut.InvokeAsync(() => js.Page().OnPasteImages(2,
            [new DroppedFile("image.png", Png.Length, Png), new DroppedFile("image.png", Jpeg.Length, Jpeg)]));

        cut.WaitForAssertion(() => Assert.Equal(2, js.Calls("ContextEditor.insertAttachment").Count), Wait);
        cut.WaitForAssertion(
            () => Assert.StartsWith("Attached paste-", cut.Find(".main-status .line").TextContent, StringComparison.Ordinal),
            Wait);
        Assert.True(cut.Find(".main-status .line").ClassList.Contains("accent"));
        Assert.Equal(
            [
                [".attachments/paste-20261126-093000.png", "paste-20261126-093000.png", true, "image/png", Convert.ToBase64String(Png)],
                [".attachments/paste-20261126-093000.jpg", "paste-20261126-093000.jpg", true, "image/jpeg", Convert.ToBase64String(Jpeg)],
            ],
            js.Calls("ContextEditor.insertAttachment").Select(c => c.Arguments.Skip(1).ToArray()));
        Assert.Equal("# Plan\n", File.ReadAllText(PlanPath));

        // The page's insert transactions make the editor dirty and report the change, as every edit does.
        string inserted = "# Plan\n\n![paste-20261126-093000.png](.attachments/paste-20261126-093000.png)"
            + "![paste-20261126-093000.jpg](.attachments/paste-20261126-093000.jpg)\n";
        await js.Change(cut, inserted);
        cut.WaitForAssertion(() => Assert.Equal(1, data.AutoSave.Pending), Wait);

        data.AutoSave.Elapse();

        cut.WaitForAssertion(() => Assert.Equal("Saved.", cut.Find(".main-status").TextContent), Wait);
        Assert.Equal(inserted, File.ReadAllText(PlanPath));
    }

    // AC-7 (refusal): a paste whose bytes are no image shows the red line and inserts nothing.
    [Fact]
    public async Task PasteImages_NotAnImage_RedLineNoInsert()
    {
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await OpenFull(cut, js, "Plan");
        byte[] svg = "<svg/>"u8.ToArray();

        await cut.InvokeAsync(() => js.Page().OnPasteImages(1, [new DroppedFile("image.png", svg.Length, svg)]));

        cut.WaitForAssertion(
            () => Assert.Equal(
                "paste-20261126-093000 isn't a PNG, JPEG, GIF, WebP or PDF file. Nothing was attached.",
                cut.Find(".main-status .line").TextContent),
            Wait);
        Assert.True(cut.Find(".main-status .line").ClassList.Contains("red"));
        Assert.Empty(js.Calls("ContextEditor.insertAttachment"));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    private string PlanPath => Path.Combine(data.Root, "Plan.md");

    private static async Task OpenFull(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await NoteOpener.OpenAsync(cut, name);
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.True(cut.FindComponent<NoteEditor>().Instance.IsReady), Wait);

        // The links panel loads behind Task.Run on its own schedule; wait until it is in before reading the page.
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }
}
