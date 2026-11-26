using System.Text;

namespace Context.Tests;

// 435: DesktopActions.PasteImages stores pasted pictures as paste-<clock second>.<sniffed extension>, with the drop rules.
public sealed class PasteImageActionsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 4, 5];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a\u0001\u0000");
    private static readonly byte[] WebP = Encoding.ASCII.GetBytes("RIFF\u0004\u0000\u0000\u0000WEBPVP8 ");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%...");
    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

    private readonly DesktopFixture data = new() { LocalNow = new DateTime(2026, 11, 26, 9, 30, 0, DateTimeKind.Local) };

    public void Dispose() => data.Dispose();

    // AC-3: one PNG is stored under the clock's name with its bytes; the note and context.db stay as they were.
    [Fact]
    public async Task PasteImages_Png_StoredUnderClockName()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = data.SnapshotUnderIndexGate(syncFirst: true, ".attachments");

        ActionResult result = await data.Actions!.PasteImages("Plan", 1, [new DroppedFile("image.png", Png.Length, Png)]);

        Assert.Equal(ActionKind.Done, result.Kind);
        AttachmentLine line = Assert.Single(result.Attached!);
        Assert.Equal(
            ("Attached paste-20261126-093000.png.", ".attachments/paste-20261126-093000.png", "paste-20261126-093000.png",
                true, "image/png", Convert.ToBase64String(Png)),
            (line.Text, line.Src, line.Name, line.IsImage, line.Mime, line.Base64));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(data.Root, ".attachments", "paste-20261126-093000.png")));
        Assert.Equal(before, FileSnapshot.Take(data.Root, ".attachments"));
        Assert.True(before.ContainsKey("Plan.md"));
        Assert.True(before.ContainsKey("context.db"));
    }

    // AC-3: a note on a lower level stores the picture in its own level's .attachments.
    [Fact]
    public async Task PasteImages_ChildNote_StoredInItsLevel()
    {
        data.WriteNote("Plan", "# Plan\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "Plan"));
        data.WriteNote("Plan/Step", "# Step\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.PasteImages("Plan/Step", 1, [new DroppedFile("x.png", Png.Length, Png)]);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.True(File.Exists(Path.Combine(data.Root, "Plan", ".attachments", "paste-20261126-093000.png")));
    }

    // AC-4: two pastes in the same second get -2; a JPEG is stored as .jpg whatever the clipboard called it; GIF and
    // WebP get their own extensions.
    [Fact]
    public async Task PasteImages_ClashAndExtensions()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult first = await data.Actions!.PasteImages("Plan", 1, [new DroppedFile("image.png", Png.Length, Png)]);
        ActionResult second = await data.Actions.PasteImages("Plan", 1, [new DroppedFile("image.png", Png.Length, Png)]);
        ActionResult jpeg = await data.Actions.PasteImages("Plan", 1, [new DroppedFile("clip.PNG", Jpeg.Length, Jpeg)]);
        ActionResult others = await data.Actions.PasteImages("Plan", 2,
            [new DroppedFile("a", Gif.Length, Gif), new DroppedFile("b.png", WebP.Length, WebP)]);

        Assert.Equal("paste-20261126-093000.png", Assert.Single(first.Attached!).Name);
        Assert.Equal("paste-20261126-093000-2.png", Assert.Single(second.Attached!).Name);
        AttachmentLine jpg = Assert.Single(jpeg.Attached!);
        Assert.Equal(("paste-20261126-093000.jpg", "image/jpeg"), (jpg.Name, jpg.Mime));
        Assert.Equal(["paste-20261126-093000.gif", "paste-20261126-093000.webp"], others.Attached!.Select(l => l.Name));
        Assert.Equal(
            ["paste-20261126-093000-2.png", "paste-20261126-093000.gif", "paste-20261126-093000.jpg",
                "paste-20261126-093000.png", "paste-20261126-093000.webp"],
            Directory.GetFiles(Path.Combine(data.Root, ".attachments")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(Jpeg, File.ReadAllBytes(Path.Combine(data.Root, ".attachments", "paste-20261126-093000.jpg")));
    }

    // AC-5: over 10 MB by size, by unread (null) bytes or by the bytes themselves, nothing is attached.
    [Fact]
    public async Task PasteImages_OverTenMegabytes_Refused()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        byte[] big = new byte[NoteAttachments.MaxBytes + 1];
        Png.CopyTo(big, 0);

        ActionResult result = await data.Actions!.PasteImages("Plan", 3,
        [
            new DroppedFile("a.png", NoteAttachments.MaxBytes + 1, null),
            new DroppedFile("b.png", NoteAttachments.MaxBytes + 1, Png),
            new DroppedFile("c.png", big.Length, big),
        ]);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(
            [
                "paste-20261126-093000 is over 10 MB. Nothing was attached.",
                "paste-20261126-093000.png is over 10 MB. Nothing was attached.",
                "paste-20261126-093000.png is over 10 MB. Nothing was attached.",
            ],
            result.Attached!.Select(l => l.Text));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // AC-5: more than 10 files stores none.
    [Theory]
    [InlineData(11, 0)]
    [InlineData(11, 11)]
    public async Task PasteImages_OverTheLimit_AttachesNone(int count, int sent)
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.PasteImages("Plan", count,
            Enumerable.Range(0, sent).Select(i => new DroppedFile($"s{i}.png", Png.Length, Png)).ToArray());

        Assert.Equal((ActionKind.Refused, "Drop at most 10 files."), (result.Kind, result.Text));
        Assert.Null(result.Attached);
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // AC-5: bytes that are no PNG/JPEG/GIF/WebP (a PDF, SVG text labelled PNG) are refused with the unsupported text.
    [Fact]
    public async Task PasteImages_NotAnImage_Refused()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.PasteImages("Plan", 2,
            [new DroppedFile("doc.pdf", Pdf.Length, Pdf), new DroppedFile("image.png", Svg.Length, Svg)]);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(
            [
                "paste-20261126-093000 isn't a PNG, JPEG, GIF, WebP or PDF file. Nothing was attached.",
                "paste-20261126-093000 isn't a PNG, JPEG, GIF, WebP or PDF file. Nothing was attached.",
            ],
            result.Attached!.Select(l => l.Text));
        Assert.All(result.Attached!, l => Assert.Equal(ActionKind.Refused, l.Kind));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }

    // AC-6: a sensitive note is refused, nothing is written and the key store is never asked.
    [Fact]
    public async Task PasteImages_Sensitive_RefusedWithoutKeyStore()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("Secret", "# Secret\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = data.SnapshotUnderIndexGate(syncFirst: true);
        (int agrees, int details, int created) = (data.Keys.Agrees, data.Keys.DetailsReads, data.Keys.MachineKeysCreated);

        ActionResult result = await data.Actions!.PasteImages("Secret", 1, [new DroppedFile("image.png", Png.Length, Png)]);

        Assert.Equal((ActionKind.Refused, "Attachments can't be added to a sensitive note."), (result.Kind, result.Text));
        Assert.Equal((agrees, details, created), (data.Keys.Agrees, data.Keys.DetailsReads, data.Keys.MachineKeysCreated));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-6: a missing note gives the note-missing text.
    [Fact]
    public async Task PasteImages_MissingNote_Refused()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.PasteImages("Gone", 1, [new DroppedFile("image.png", Png.Length, Png)]);

        Assert.Equal((ActionKind.Refused, ActionReason.Missing, DesktopActions.NoteMissingText("Gone")),
            (result.Kind, result.Reason, result.Text));
        Assert.False(Directory.Exists(Path.Combine(data.Root, ".attachments")));
    }
}
