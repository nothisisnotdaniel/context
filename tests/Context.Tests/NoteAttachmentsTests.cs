using System.Text;

namespace Context.Tests;

public sealed class NoteAttachmentsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a....");
    private static readonly byte[] WebP = Encoding.ASCII.GetBytes("RIFF\u0010\0\0\0WEBPVP8 ");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%...");
    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");
    private static readonly byte[] Html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

    // Generous: the linear scans take well under a second; the old quadratic ones took hours.
    private static readonly TimeSpan ScanBound = TimeSpan.FromSeconds(60);

    private const string Letters100 =
        "abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghij";

    private readonly string parent;
    private readonly string root;
    private readonly NoteStore store;
    private readonly NoteAttachments attachments;

    public NoteAttachmentsTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-attach-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        store = new NoteStore(root);
        attachments = new NoteAttachments(store);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // AC-1
    [Theory]
    [InlineData("shot.png", "shot.png")]
    [InlineData("a..b.png", "a-b.png")]
    [InlineData("con.tar.png", "con-tar.png")]
    [InlineData("CON.png", "attachment.png")]
    [InlineData("../x.PNG", "x.png")]
    [InlineData("C:\\Users\\me\\My Shot (1).JPG", "My-Shot-1.jpg")]
    [InlineData("--a b--.pdf", "a-b.pdf")]
    [InlineData(".png", "attachment.png")]
    [InlineData("README", "README")]
    [InlineData("notes.", "notes")]
    [InlineData("Grüße.gif", "Gr-e.gif")]
    [InlineData("a.P-N!G", "a.png")]
    public void CleanName_FollowsTheStoredNameRule(string fileName, string expected)
    {
        Assert.Equal(expected, NoteAttachments.CleanName(fileName));
    }

    // AC-1: a stem cut at 100 that ends in '-' loses it after the cut.
    [Fact]
    public void CleanName_TrimsTrailingDashAfterTheCut()
    {
        string stem = new string('a', 99) + " b";
        string clean = NoteAttachments.CleanName(stem + ".png");

        Assert.Equal(new string('a', 99) + ".png", clean);
        Assert.True(NoteAttachments.IsStoredName(clean));
    }

    // AC-2
    [Theory]
    [InlineData("a.png", "png", "Png")]
    [InlineData("a.jpg", "jpeg", "Jpeg")]
    [InlineData("a.jpeg", "jpeg", "Jpeg")]
    [InlineData("a.gif", "gif", "Gif")]
    [InlineData("a.webp", "webp", "WebP")]
    [InlineData("a.pdf", "pdf", "Pdf")]
    [InlineData("a.pdf", "png", "Unsupported")]
    [InlineData("a.png", "pdf", "Unsupported")]
    [InlineData("a.svg", "svg", "Unsupported")]
    [InlineData("a.png", "svg", "Unsupported")]
    [InlineData("a.html", "html", "Unsupported")]
    [InlineData("a.png", "html", "Unsupported")]
    [InlineData("a", "png", "Unsupported")]
    public void Sniff_UsesBytesAndRequiresTheMatchingExtension(string name, string bytes, string expected)
    {
        Assert.Equal(Enum.Parse<AttachmentType>(expected), NoteAttachments.Sniff(name, Bytes(bytes)));
    }

    // AC-3/AC-5: each type is stored with its link and resolves.
    [Theory]
    [InlineData("shot.png", "png", true, "image/png")]
    [InlineData("photo.JPG", "jpeg", true, "image/jpeg")]
    [InlineData("anim.gif", "gif", true, "image/gif")]
    [InlineData("pic.webp", "webp", true, "image/webp")]
    [InlineData("plan.pdf", "pdf", false, "application/pdf")]
    public void Add_StoresFile_ReturnsLink_AndResolves(string fileName, string bytes, bool image, string mime)
    {
        store.Create("Note");
        byte[] noteBytes = File.ReadAllBytes(NotePath("Note"));

        AttachmentAdd added = attachments.Add("Note", fileName, Bytes(bytes));

        string name = NoteAttachments.CleanName(fileName);
        Assert.Equal(AttachmentAddKind.Added, added.Kind);
        Assert.Equal(name, added.Name);
        Assert.Equal(".attachments/" + name, added.Src);
        Assert.Equal((image ? "!" : string.Empty) + $"[{name}](.attachments/{name})", added.Markdown);
        Assert.Equal(Bytes(bytes), File.ReadAllBytes(Path.Combine(root, ".attachments", name)));
        Assert.Equal(noteBytes, File.ReadAllBytes(NotePath("Note")));
        Assert.Equal(["Note"], store.List().Select(n => n.Name));
        Assert.Equal([name], Directory.GetFiles(Path.Combine(root, ".attachments")).Select(Path.GetFileName));
        ResolvedAttachment? resolved = attachments.TryResolve("Note", added.Src!);
        Assert.NotNull(resolved);
        Assert.Equal(image, resolved.IsImage);
        Assert.Equal(mime, resolved.Mime);
        Assert.Equal(Bytes(bytes), resolved.Bytes);
    }

    // AC-3: a subpage stores in its own folder's .attachments.
    [Fact]
    public void Add_Subpage_StoresInItsLevel()
    {
        store.Create("Parent");
        store.CreateChild("Parent", "Child");

        AttachmentAdd added = attachments.Add("Parent/Child", "shot.png", Png);

        Assert.True(File.Exists(Path.Combine(root, "Parent", ".attachments", "shot.png")));
        Assert.NotNull(attachments.TryResolve("Parent/Child", added.Src!));
        Assert.Null(attachments.TryResolve("Parent", added.Src!));
        Assert.Equal(["Parent", "Parent/Child"], store.List().Select(n => n.Name));
    }

    // AC-3: clashes get -2, -3 and never overwrite.
    [Fact]
    public void Add_Clash_AddsSuffix_NeverOverwrites()
    {
        store.Create("Note");
        byte[] other = [.. Png, 9];

        AttachmentAdd first = attachments.Add("Note", "shot.png", Png);
        AttachmentAdd second = attachments.Add("Note", "shot.png", other);
        AttachmentAdd third = attachments.Add("Note", "shot?.png", Png);

        Assert.Equal("shot.png", first.Name);
        Assert.Equal("shot-2.png", second.Name);
        Assert.Equal("shot-3.png", third.Name);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, ".attachments", "shot.png")));
        Assert.Equal(other, File.ReadAllBytes(Path.Combine(root, ".attachments", "shot-2.png")));
        Assert.NotNull(attachments.TryResolve("Note", second.Src!));
        Assert.NotNull(attachments.TryResolve("Note", third.Src!));
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(root, ".attachments")), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // AC-3/AC-5: a 100-letter stem that clashes is stored as <97 letters>-2 and resolves.
    [Fact]
    public void Add_HundredLetterStemClash_StaysWithinTheLimit()
    {
        store.Create("Note");

        AttachmentAdd first = attachments.Add("Note", Letters100 + ".png", Png);
        AttachmentAdd second = attachments.Add("Note", Letters100 + ".png", Png);

        Assert.Equal(Letters100 + ".png", first.Name);
        Assert.Equal(Letters100[..98] + "-2.png", second.Name);
        Assert.Equal(100, second.Name!.IndexOf('.'));
        Assert.NotNull(attachments.TryResolve("Note", first.Src!));
        Assert.NotNull(attachments.TryResolve("Note", second.Src!));
    }

    // AC-3: the clash cut trims a '-' it leaves at the end before adding the suffix.
    [Fact]
    public void ClashName_TrimsDashLeftByTheCut()
    {
        string stem = new string('a', 97) + "-bb";

        Assert.Equal(new string('a', 97) + "-2", NoteAttachments.ClashName(stem, 2));
        Assert.Equal(new string('a', 96) + "-10", NoteAttachments.ClashName(new string('a', 96) + "-cc", 10));
        Assert.Equal("shot", NoteAttachments.ClashName("shot", 1));
        Assert.Equal("shot-99", NoteAttachments.ClashName("shot", 99));
    }

    // AC-3: every clash name up to -99, then it throws and writes nothing more.
    [Fact]
    public void Add_AllClashNamesTaken_Throws_LeavesNoTemp()
    {
        store.Create("Note");
        string folder = Path.Combine(root, ".attachments");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.png"), Png);
        for (int n = 2; n <= 99; n++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"a-{n}.png"), Png);
        }

        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Assert.ThrowsAny<IOException>(() => attachments.Add("Note", "a.png", Png));
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-3: refusals write nothing.
    [Theory]
    [InlineData("Note", "shot.svg", "svg", "Unsupported")]
    [InlineData("Note", "shot.png", "svg", "Unsupported")]
    [InlineData("Note", "page.html", "html", "Unsupported")]
    [InlineData("Note", "page.png", "html", "Unsupported")]
    [InlineData("Note", "shot.pdf", "png", "Unsupported")]
    [InlineData("Note", "README", "png", "Unsupported")]
    [InlineData("Note", "shot.", "png", "Unsupported")]
    [InlineData("Missing", "shot.png", "png", "NoNote")]
    [InlineData("calendar", "shot.png", "png", "NoNote")]
    public void Add_Refused_WritesNothing(string note, string fileName, string bytes, string expected)
    {
        store.Create("Note");
        SortedDictionary<string, string> before = FileSnapshot.Take(parent);

        AttachmentAdd added = attachments.Add(note, fileName, Bytes(bytes));

        Assert.Equal(Enum.Parse<AttachmentAddKind>(expected), added.Kind);
        Assert.Null(added.Name);
        Assert.Null(added.Markdown);
        Assert.Equal(before, FileSnapshot.Take(parent));
        Assert.False(Directory.Exists(Path.Combine(root, ".attachments")));
    }

    // AC-3: over MaxBytes is refused and writes nothing.
    [Fact]
    public void Add_TooLarge_WritesNothing()
    {
        store.Create("Note");
        byte[] big = new byte[NoteAttachments.MaxBytes + 1];
        Png.CopyTo(big, 0);
        SortedDictionary<string, string> before = FileSnapshot.Take(parent);

        AttachmentAdd added = attachments.Add("Note", "big.png", big);

        Assert.Equal(AttachmentAddKind.TooLarge, added.Kind);
        Assert.Equal(before, FileSnapshot.Take(parent));
        Assert.False(Directory.Exists(Path.Combine(root, ".attachments")));
    }

    // AC-3: a .attachments that is a symlink is refused (where links can be made).
    [Fact]
    public void Add_SymlinkedFolder_Refused_WritesNothing()
    {
        store.Create("Note");
        string elsewhere = Path.Combine(parent, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, ".attachments"), elsewhere);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        AttachmentAdd added = attachments.Add("Note", "shot.png", Png);

        Assert.Equal(AttachmentAddKind.Unsafe, added.Kind);
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere));
    }

    // AC-4: a note with subpages carries its child .attachments on rename and move, and List never shows them.
    [Fact]
    public void RenameAndMove_CarryChildAttachments()
    {
        store.Create("Parent");
        store.CreateChild("Parent", "Child");
        store.Create("Owner");
        attachments.Add("Parent/Child", "shot.png", Png);

        store.Rename("Parent", "Renamed");
        Assert.True(File.Exists(Path.Combine(root, "Renamed", ".attachments", "shot.png")));

        store.Move("Renamed", "Owner");
        Assert.True(File.Exists(Path.Combine(root, "Owner", "Renamed", ".attachments", "shot.png")));
        Assert.NotNull(attachments.TryResolve("Owner/Renamed/Child", ".attachments/shot.png"));
        Assert.DoesNotContain(store.List(), n => n.Name.Contains(".attachments", StringComparison.Ordinal));
    }

    // AC-4: Delete with removeAttachments removes the child folder's .attachments, and the emptied folder.
    [Fact]
    public void Delete_RemoveAttachments_RemovesChildFolder()
    {
        store.Create("Parent");
        store.CreateChild("Parent", "Child");
        attachments.Add("Parent/Child", "shot.png", Png);

        Assert.True(store.Delete("Parent", removeHistory: true, removeAttachments: true));

        Assert.False(Directory.Exists(Path.Combine(root, "Parent")));
        Assert.Empty(store.List());
    }

    // AC-4: without the flag the child .attachments keeps its folder.
    [Fact]
    public void Delete_WithoutFlag_KeepsChildAttachments()
    {
        store.Create("Parent");
        store.CreateChild("Parent", "Child");
        attachments.Add("Parent/Child", "shot.png", Png);

        Assert.True(store.Delete("Parent"));

        Assert.True(File.Exists(Path.Combine(root, "Parent", ".attachments", "shot.png")));
    }

    // AC-4: a symlinked child .attachments is never followed by the delete.
    [Fact]
    public void Delete_RemoveAttachments_DoesNotFollowLink()
    {
        store.Create("Parent");
        store.CreateChild("Parent", "Child");
        string elsewhere = Path.Combine(parent, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllBytes(Path.Combine(elsewhere, "keep.png"), Png);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "Parent", ".attachments"), elsewhere);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        store.Delete("Parent", removeHistory: true, removeAttachments: true);

        Assert.True(File.Exists(Path.Combine(elsewhere, "keep.png")));
    }

    // AC-5: TryResolve refuses everything outside the stored shape and checked files.
    [Theory]
    [InlineData("../x.png")]
    [InlineData(".attachments/../x.png")]
    [InlineData(".attachments/a/b.png")]
    [InlineData(".attachments/a\\b.png")]
    [InlineData(".attachments/A.PNG")]
    [InlineData(".attachments/-a.png")]
    [InlineData(".attachments/a-.png")]
    [InlineData(".attachments/a--b.png")]
    [InlineData(".attachments/a.b.png")]
    [InlineData(".attachments/CON.png")]
    [InlineData(".attachments/.png")]
    [InlineData(".attachments/a")]
    [InlineData(".attachments/missing.png")]
    [InlineData(".attachments/html.png")]
    [InlineData(".attachments/empty.png")]
    [InlineData("attachments/shot.png")]
    [InlineData("/tmp/shot.png")]
    public void TryResolve_Refuses(string src)
    {
        store.Create("Note");
        string folder = Path.Combine(root, ".attachments");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "html.png"), Html);
        File.WriteAllBytes(Path.Combine(folder, "empty.png"), []);
        File.WriteAllBytes(Path.Combine(folder, "a.b.png"), Png);
        File.WriteAllBytes(Path.Combine(parent, "x.png"), Png);

        Assert.Null(attachments.TryResolve("Note", src));
        Assert.Null(attachments.TryResolve("Note", Path.Combine(folder, "a.b.png")));
    }

    // AC-5: the shape check accepts every name the rule makes.
    [Theory]
    [InlineData("shot.png", true)]
    [InlineData("_a_.png", true)]
    [InlineData("a-2.jpeg", true)]
    [InlineData("A1.webp", true)]
    [InlineData("README", false)]
    [InlineData("a.PNG", false)]
    [InlineData("a b.png", false)]
    [InlineData("nul.pdf", false)]
    public void IsStoredName_ChecksTheShape(string name, bool expected)
    {
        Assert.Equal(expected, NoteAttachments.IsStoredName(name));
        Assert.True(NoteAttachments.IsStoredName(Letters100 + ".png"));
        Assert.False(NoteAttachments.IsStoredName(Letters100 + "a.png"));
    }

    // AC-5: a symlinked attachment file is refused (where links can be made).
    [Fact]
    public void TryResolve_SymlinkedFile_Refused()
    {
        store.Create("Note");
        string folder = Path.Combine(root, ".attachments");
        Directory.CreateDirectory(folder);
        string outside = Path.Combine(parent, "outside.png");
        File.WriteAllBytes(outside, Png);
        try
        {
            File.CreateSymbolicLink(Path.Combine(folder, "link.png"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Null(attachments.TryResolve("Note", ".attachments/link.png"));
    }

    // AC-8: image sources are distinct stored-shape image names after "](", at most 20.
    [Fact]
    public void ImageSources_DistinctImagesOnly_Capped()
    {
        string text = "![a](.attachments/a.png) ![a](.attachments/a.png) [p](.attachments/p.pdf) "
            + "![x](.attachments/../x.png) ![b](.attachments/b.gif) ![c](other/c.png)\n"
            + string.Concat(Enumerable.Range(1, 30).Select(i => $"![i](.attachments/i{i}.jpg)"));

        IReadOnlyList<string> sources = NoteAttachments.ImageSources(text);

        Assert.Equal(NoteAttachments.MaxInlineImages, sources.Count);
        Assert.Equal([".attachments/a.png", ".attachments/b.gif", ".attachments/i1.jpg"], sources.Take(3));
    }

    // AC-11
    [Fact]
    public void LinkedNames_DistinctOrdinal_ImageAndPlainLinks()
    {
        string text = "![Shot.png](.attachments/Shot.png) [s](.attachments/shot.png \"t\") `[c](.attachments/code.pdf)` "
            + "[x](.attachments/shot.png) [bad](.attachments/../x.png) [no](.attachments/open.png";

        Assert.Equal(["Shot.png", "shot.png", "code.pdf"], NoteAttachments.LinkedNames(text));
    }

    // AC-12: a sole-linked attachment moves with the note; the link is unchanged and the source goes.
    [Fact]
    public void MoveWithNote_SoleLinked_MovesFile()
    {
        store.Create("Target");
        store.Create("Other");
        WriteNoteWith("Note", "shot.png", Png);
        byte[] noteBytes = File.ReadAllBytes(NotePath("Note"));

        NoteInfo? moved = attachments.MoveWithNote("Note", "Target");

        Assert.Equal("Target/Note", moved?.Name);
        Assert.Equal(noteBytes, File.ReadAllBytes(Path.Combine(root, "Target", "Note.md")));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, "Target", ".attachments", "shot.png")));
        Assert.False(Directory.Exists(Path.Combine(root, ".attachments")));
        Assert.NotNull(attachments.TryResolve("Target/Note", ".attachments/shot.png"));
    }

    // AC-12: an attachment a sibling still links is copied and its source stays.
    [Fact]
    public void MoveWithNote_SharedAttachment_Copied()
    {
        store.Create("Target");
        WriteNoteWith("Note", "shot.png", Png);
        WriteNote("Other", "# Other\n\n![shot.png](.attachments/SHOT.png)\n");

        attachments.MoveWithNote("Note", "Target");

        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, ".attachments", "shot.png")));
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, "Target", ".attachments", "shot.png")));
        Assert.NotNull(attachments.TryResolve("Target/Note", ".attachments/shot.png"));
    }

    // AC-12: a move back to the top level works the same way, and a move within the level touches nothing.
    [Fact]
    public void MoveWithNote_UpAndWithinLevel()
    {
        store.Create("Owner");
        store.CreateChild("Owner", "Note");
        File.WriteAllText(NotePath("Owner/Note"), "# Note\n\n![a.png](.attachments/a.png)\n");
        attachments.Add("Owner/Note", "a.png", Png);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Assert.Equal("Owner/Note", attachments.MoveWithNote("Owner/Note", "Owner")?.Name);
        Assert.Equal(before, FileSnapshot.Take(root));

        Assert.Equal("Note", attachments.MoveWithNote("Owner/Note", null)?.Name);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, ".attachments", "a.png")));
        Assert.False(File.Exists(Path.Combine(root, "Owner", ".attachments", "a.png")));
        Assert.NotNull(attachments.TryResolve("Note", ".attachments/a.png"));
    }

    // AC-12: a link TryResolve refuses is left as written and nothing is copied for it.
    [Fact]
    public void MoveWithNote_RefusedLink_LeftAsWritten()
    {
        store.Create("Target");
        WriteNote("Note", "# Note\n\n![x](.attachments/fake.png)\n");
        Directory.CreateDirectory(Path.Combine(root, ".attachments"));
        File.WriteAllBytes(Path.Combine(root, ".attachments", "fake.png"), Html);

        attachments.MoveWithNote("Note", "Target");

        Assert.Equal("# Note\n\n![x](.attachments/fake.png)\n", File.ReadAllText(Path.Combine(root, "Target", "Note.md")));
        Assert.True(File.Exists(Path.Combine(root, ".attachments", "fake.png")));
        Assert.False(Directory.Exists(Path.Combine(root, "Target", ".attachments")));
    }

    // AC-13: a clash in the target stores shot-2.png, rewrites the link and keeps the target file.
    [Fact]
    public void MoveWithNote_Clash_RewritesLink_KeepsTargetFile()
    {
        store.Create("Target");
        store.CreateChild("Target", "Resident");
        byte[] resident = [.. Png, 7, 7];
        attachments.Add("Target/Resident", "shot.png", resident);
        string residentTicks = FileSnapshot.Take(root)["Target/.attachments/shot.png"];
        WriteNote("Note", "# Note\n\n![shot.png](.attachments/shot.png) and [doc](.attachments/shot.png \"t\")\n"
            + "keep ](.attachments/shot.pngx) too\n");
        attachments.Add("Note", "shot.png", Png);

        attachments.MoveWithNote("Note", "Target");

        Assert.Equal(
            "# Note\n\n![shot.png](.attachments/shot-2.png) and [doc](.attachments/shot-2.png \"t\")\n"
            + "keep ](.attachments/shot.pngx) too\n",
            File.ReadAllText(Path.Combine(root, "Target", "Note.md")));
        Assert.Equal(residentTicks, FileSnapshot.Take(root)["Target/.attachments/shot.png"]);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, "Target", ".attachments", "shot-2.png")));
        Assert.NotNull(attachments.TryResolve("Target/Note", ".attachments/shot-2.png"));
        Assert.False(File.Exists(Path.Combine(root, ".attachments", "shot.png")));
        Assert.Empty(HistoryFiles());
    }

    // AC-12: the rewrite keeps CRLF and the BOM and changes only the renamed link.
    [Fact]
    public void MoveWithNote_Rewrite_KeepsCrlfAndBom()
    {
        store.Create("Target");
        store.CreateChild("Target", "Resident");
        attachments.Add("Target/Resident", "shot.png", Png);
        WriteNote("Note", "x");
        attachments.Add("Note", "shot.png", Png);
        attachments.Add("Note", "plan.pdf", Pdf);
        byte[] bom = [0xEF, 0xBB, 0xBF];
        File.WriteAllBytes(NotePath("Note"), [.. bom, .. Encoding.UTF8.GetBytes(
            "# Note\r\n\r\nGrüße ![shot.png](.attachments/shot.png)\r\n[plan.pdf](.attachments/plan.pdf)\r\n")]);

        attachments.MoveWithNote("Note", "Target");

        Assert.Equal(
            [.. bom, .. Encoding.UTF8.GetBytes(
                "# Note\r\n\r\nGrüße ![shot.png](.attachments/shot-2.png)\r\n[plan.pdf](.attachments/plan.pdf)\r\n")],
            File.ReadAllBytes(Path.Combine(root, "Target", "Note.md")));
    }

    // AC-14: a failure in copy, move or rewrite undoes everything.
    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    [InlineData("rewrite")]
    public void MoveWithNote_FailedStep_ChangesNothing(string step)
    {
        store.Create("Target");
        store.CreateChild("Target", "Resident");
        attachments.Add("Target/Resident", "shot.png", Png);
        WriteNoteWith("Note", "shot.png", Png);
        attachments.Add("Note", "plan.pdf", Pdf);
        File.AppendAllText(NotePath("Note"), "[plan.pdf](.attachments/plan.pdf)\n");
        store.Create("Empty");
        SortedDictionary<string, string> before = FileSnapshot.Take(parent);
        int copies = 0;
        attachments.BeforeMoveStep = s =>
        {
            if (s == "copy" && ++copies < 2)
            {
                return;
            }

            if (s == step)
            {
                throw new IOException("disk full");
            }
        };

        Assert.Throws<AttachmentMoveException>(() => attachments.MoveWithNote("Note", "Target"));
        Assert.Throws<AttachmentMoveException>(() => attachments.MoveWithNote("Note", "Empty"));

        Assert.Equal(before, FileSnapshot.Take(parent));
        Assert.True(store.TryFind("Note", out NoteInfo? note));
        Assert.Equal("Note", note?.Name);
        Assert.NotNull(attachments.TryResolve("Note", ".attachments/shot.png"));
        Assert.NotNull(attachments.TryResolve("Note", ".attachments/plan.pdf"));
        Assert.False(Directory.Exists(Path.Combine(root, "Empty")));
    }

    // AC-14: a failure in cleanup leaves the move done and the sources in place.
    [Fact]
    public void MoveWithNote_FailedCleanup_StillMoved()
    {
        store.Create("Target");
        WriteNoteWith("Note", "shot.png", Png);
        attachments.BeforeMoveStep = s =>
        {
            if (s == "cleanup")
            {
                throw new IOException("busy");
            }
        };

        NoteInfo? moved = attachments.MoveWithNote("Note", "Target");

        Assert.Equal("Target/Note", moved?.Name);
        Assert.True(File.Exists(Path.Combine(root, ".attachments", "shot.png")));
        Assert.NotNull(attachments.TryResolve("Target/Note", ".attachments/shot.png"));
    }

    // AC-14: a sibling with the title at the target rethrows NoteExistsException and leaves no copy.
    [Fact]
    public void MoveWithNote_Exists_RethrowsUnchanged_NoCopy()
    {
        store.Create("Target");
        store.CreateChild("Target", "Note");
        WriteNoteWith("Note", "shot.png", Png);
        SortedDictionary<string, string> before = FileSnapshot.Take(parent);

        Assert.Throws<NoteExistsException>(() => attachments.MoveWithNote("Note", "Target"));

        Assert.Equal(before, FileSnapshot.Take(parent));
        Assert.False(Directory.Exists(Path.Combine(root, "Target", ".attachments")));
    }

    // AC-14: a move under calendar throws ArgumentException unchanged and writes nothing.
    [Fact]
    public void MoveWithNote_UnderCalendar_RethrowsArgument_NoCopy()
    {
        store.Create("calendar");
        WriteNoteWith("Note", "shot.png", Png);
        SortedDictionary<string, string> before = FileSnapshot.Take(parent);

        Assert.Throws<ArgumentException>(() => attachments.MoveWithNote("Note", "calendar"));

        Assert.Equal(before, FileSnapshot.Take(parent));
        Assert.False(Directory.Exists(Path.Combine(root, "calendar", ".attachments")));
    }

    // AC-11/AC-12: a sensitive note in the level keeps every name, so the move copies.
    [Fact]
    public void MoveWithNote_SensitiveInLevel_KeepsSources()
    {
        store.Create("Target");
        WriteNoteWith("Note", "shot.png", Png);
        File.WriteAllBytes(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension), [1, 2, 3]);

        attachments.MoveWithNote("Note", "Target");

        Assert.True(File.Exists(Path.Combine(root, ".attachments", "shot.png")));
        Assert.True(File.Exists(Path.Combine(root, "Target", ".attachments", "shot.png")));
    }

    // AC-11: Shot.png and shot.png are two names; a sibling's text keeps a name ignoring case.
    [Fact]
    public void KeptBySiblings_IgnoresCase_ExcludesSelfAndOtherLevels()
    {
        WriteNote("Note", "![a](.attachments/Shot.png) ![b](.attachments/shot.png) ![c](.attachments/solo.png)");
        WriteNote("Other", "mentions SHOT.PNG in text");
        store.Create("Owner");
        store.CreateChild("Owner", "Deep");
        File.WriteAllText(NotePath("Owner/Deep"), "solo.png");
        store.TryFind("Note", out NoteInfo? note);

        HashSet<string> kept = attachments.KeptBySiblings(note!, ["Shot.png", "shot.png", "solo.png"]);

        Assert.Equal(["Shot.png", "shot.png"], kept.Order(StringComparer.Ordinal));
    }

    // AC-11: an unreadable (over MaxEditBytes) sibling keeps every name.
    [Fact]
    public void KeptBySiblings_OversizedSibling_KeepsAll()
    {
        WriteNote("Note", "x");
        WriteNote("Other", "x");
        using (FileStream stream = File.OpenWrite(NotePath("Other")))
        {
            stream.SetLength(NoteStore.MaxEditBytes + 1);
        }

        store.TryFind("Note", out NoteInfo? note);

        Assert.Equal(["a.png"], attachments.KeptBySiblings(note!, ["a.png"]));
    }

    // AC-15: delete removes only the attachments no sibling links, then the empty folder.
    [Fact]
    public void RemoveUnlinked_SoleGoes_SharedStays()
    {
        WriteNote("Note", "x");
        attachments.Add("Note", "sole.png", Png);
        attachments.Add("Note", "shared.pdf", Pdf);
        File.WriteAllText(NotePath("Note"), "![s](.attachments/sole.png) [p](.attachments/shared.pdf)\n");
        WriteNote("Other", "[p](.attachments/shared.pdf)\n");
        string sharedBefore = FileSnapshot.Take(root)[".attachments/shared.pdf"];
        store.TryFind("Note", out NoteInfo? note);
        IReadOnlyList<string> linked = attachments.LinkedNamesOf("Note");

        store.Delete("Note", removeHistory: true, removeAttachments: true);
        int removed = attachments.RemoveUnlinked(note!, linked);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(Path.Combine(root, ".attachments", "sole.png")));
        Assert.Equal(sharedBefore, FileSnapshot.Take(root)[".attachments/shared.pdf"]);

        store.TryFind("Other", out NoteInfo? other);
        IReadOnlyList<string> otherLinked = attachments.LinkedNamesOf("Other");
        store.Delete("Other", removeHistory: true, removeAttachments: true);
        Assert.Equal(1, attachments.RemoveUnlinked(other!, otherLinked));
        Assert.False(Directory.Exists(Path.Combine(root, ".attachments")));
    }

    // AC-15: Shot.png and shot.png: only the linked spelling goes, where the file system keeps both.
    [Fact]
    public void RemoveUnlinked_CaseVariants()
    {
        WriteNote("Note", "![a](.attachments/Shot.png)\n");
        Directory.CreateDirectory(Path.Combine(root, ".attachments"));
        File.WriteAllBytes(Path.Combine(root, ".attachments", "Shot.png"), Png);
        File.WriteAllBytes(Path.Combine(root, ".attachments", "shot.png"), Png);
        bool caseSensitive = Directory.GetFiles(Path.Combine(root, ".attachments")).Length == 2;
        store.TryFind("Note", out NoteInfo? note);
        IReadOnlyList<string> linked = attachments.LinkedNamesOf("Note");

        store.Delete("Note", removeHistory: true, removeAttachments: true);
        attachments.RemoveUnlinked(note!, linked);

        Assert.Equal(["Shot.png"], linked);
        if (caseSensitive)
        {
            Assert.Equal(["shot.png"], Directory.GetFiles(Path.Combine(root, ".attachments")).Select(Path.GetFileName));
        }
        else
        {
            Assert.False(Directory.Exists(Path.Combine(root, ".attachments")));
        }
    }

    // AC-15: an unreadable sibling or a sensitive note in the level keeps everything; unread names are never deleted.
    [Fact]
    public void RemoveUnlinked_KeepsWhenUnsure()
    {
        WriteNote("Note", "x");
        attachments.Add("Note", "a.png", Png);
        File.WriteAllBytes(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension), [1]);
        store.TryFind("Note", out NoteInfo? note);
        store.Delete("Note", removeHistory: true, removeAttachments: true);

        Assert.Equal(0, attachments.RemoveUnlinked(note!, ["a.png", "../x.png", "Note.md"]));
        Assert.True(File.Exists(Path.Combine(root, ".attachments", "a.png")));
    }

    // AC-15: a linked name that is a link is never deleted or followed (where links can be made).
    [Fact]
    public void RemoveUnlinked_DoesNotDeleteLinks()
    {
        WriteNote("Note", "![a](.attachments/a.png)\n");
        Directory.CreateDirectory(Path.Combine(root, ".attachments"));
        string outside = Path.Combine(parent, "outside.png");
        File.WriteAllBytes(outside, Png);
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, ".attachments", "a.png"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        store.TryFind("Note", out NoteInfo? note);
        store.Delete("Note", removeHistory: true, removeAttachments: true);

        Assert.Equal(0, attachments.RemoveUnlinked(note!, ["a.png"]));
        Assert.True(File.Exists(outside));
    }

    private static byte[] Bytes(string kind) => kind switch
    {
        "png" => Png,
        "jpeg" => Jpeg,
        "gif" => Gif,
        "webp" => WebP,
        "pdf" => Pdf,
        "svg" => Svg,
        _ => Html,
    };

    // Review round 1 (security): a crafted note can't make the link scanners run for hours. Each marker costs at most
    // MaxScannedName characters, so about 5 MB of markers with one ')' at the end is scanned in linear time.
    [Fact]
    public async Task LinkScans_AdversarialNote_FinishLinear()
    {
        StringBuilder text = new StringBuilder("![a](.attachments/real.png) [b](.attachments/doc.pdf)\n");
        while (text.Length < NoteStore.MaxEditBytes - 1)
        {
            text.Append(NoteAttachments.LinkMarker);
        }

        text.Append(')');
        string note = text.ToString();

        Task<(IReadOnlyList<string> Images, IReadOnlyList<string> Names)> scan =
            Task.Run(() => (NoteAttachments.ImageSources(note), NoteAttachments.LinkedNames(note)));

        Assert.Same(scan, await Task.WhenAny(scan, Task.Delay(ScanBound)));
        (IReadOnlyList<string> images, IReadOnlyList<string> names) = await scan;
        Assert.Equal([".attachments/real.png"], images);
        Assert.Equal(["real.png", "doc.pdf"], names);
    }

    // Review round 1 (security): about 5 MB of distinct valid names is scanned in linear time (no quadratic distinct check).
    [Fact]
    public async Task LinkedNames_ManyDistinctNames_FinishLinear()
    {
        StringBuilder text = new StringBuilder();
        int count = 0;
        while (text.Length < NoteStore.MaxEditBytes - 64)
        {
            text.Append("](.attachments/n").Append(count).Append(".png)");
            count++;
        }

        string note = text.ToString();
        Task<IReadOnlyList<string>> scan = Task.Run(() => NoteAttachments.LinkedNames(note));

        Assert.Same(scan, await Task.WhenAny(scan, Task.Delay(ScanBound)));
        IReadOnlyList<string> names = await scan;
        Assert.Equal(count, names.Count);
        Assert.Equal("n0.png", names[0]);
    }

    // Review round 1 (security): only MaxScannedName characters after a marker count; the longest stored name still does.
    [Fact]
    public void LinkedNames_LooksOnlyAsFarAsTheLongestStoredName()
    {
        string longest = Letters100 + ".jpeg";
        string tooLong = Letters100 + ".jpegx";
        string text = $"![a](.attachments/{longest}) ![b](.attachments/{tooLong}) ![c](.attachments/end.png)";

        Assert.Equal(NoteAttachments.MaxScannedName, longest.Length);
        Assert.Equal([longest, "end.png"], NoteAttachments.LinkedNames(text));
        Assert.Equal([".attachments/" + longest, ".attachments/end.png"], NoteAttachments.ImageSources(text));
    }

    // Review round 1 (security): up to MaxLinkedNames a delete removes the sole attachments; past it every one stays.
    [Theory]
    [InlineData(NoteAttachments.MaxLinkedNames, 0)]
    [InlineData(NoteAttachments.MaxLinkedNames + 1, 2)]
    public void RemoveUnlinked_PastTheCap_KeepsEvery(int linkCount, int expectedLeft)
    {
        WriteNote("Note", "x");
        attachments.Add("Note", "a.png", Png);
        attachments.Add("Note", "b.pdf", Pdf);
        StringBuilder text = new StringBuilder("![a](.attachments/a.png) [b](.attachments/b.pdf)\n");
        for (int i = 2; i < linkCount; i++)
        {
            text.Append("[x](.attachments/x").Append(i).Append(".png)\n");
        }

        File.WriteAllText(NotePath("Note"), text.ToString());
        store.TryFind("Note", out NoteInfo? note);
        IReadOnlyList<string> linked = attachments.LinkedNamesOf("Note");

        store.Delete("Note", removeHistory: true, removeAttachments: true);
        attachments.RemoveUnlinked(note!, linked);

        Assert.Equal(linkCount, linked.Count);
        string folder = Path.Combine(root, ".attachments");
        Assert.Equal(expectedLeft, Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0);
    }

    // Review round 2: a note linking more than MaxLinkedNames names never moves without its attachments; the move
    // changes nothing and throws AttachmentMoveException. At the cap it still moves with them.
    [Theory]
    [InlineData(NoteAttachments.MaxLinkedNames, true)]
    [InlineData(NoteAttachments.MaxLinkedNames + 1, false)]
    public void MoveWithNote_PastTheCap_RefusedAndChangesNothing(int linkCount, bool moves)
    {
        store.Create("Target");
        WriteNoteWith("Note", "shot.png", Png);
        StringBuilder text = new StringBuilder(File.ReadAllText(NotePath("Note")));
        for (int i = 1; i < linkCount; i++)
        {
            text.Append("[x](.attachments/x").Append(i).Append(".png)\n");
        }

        File.WriteAllText(NotePath("Note"), text.ToString());
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        if (moves)
        {
            Assert.Equal("Target/Note", attachments.MoveWithNote("Note", "Target")?.Name);
            Assert.Equal(Png, File.ReadAllBytes(Path.Combine(root, "Target", ".attachments", "shot.png")));
        }
        else
        {
            Assert.Throws<AttachmentMoveException>(() => attachments.MoveWithNote("Note", "Target"));
            Assert.Equal(before, FileSnapshot.Take(root));
        }
    }

    private string NotePath(string name) => Path.Combine([root, .. name.Split('/')]) + ".md";

    private void WriteNote(string title, string text)
    {
        store.Create(title);
        File.WriteAllText(NotePath(title), text);
    }

    // A top-level note linking one stored attachment.
    private void WriteNoteWith(string title, string file, byte[] bytes)
    {
        store.Create(title);
        AttachmentAdd added = attachments.Add(title, file, bytes);
        File.WriteAllText(NotePath(title), $"# {title}\n\n{added.Markdown}\n");
    }

    private IEnumerable<string> HistoryFiles() =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.Replace('\\', '/').Contains("/.history/", StringComparison.Ordinal));
}
