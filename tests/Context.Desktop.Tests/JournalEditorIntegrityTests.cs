using System.Text;

namespace Context.Tests;

public sealed class JournalEditorIntegrityTests : IDisposable
{
    private const string Name = "2026-10-29";
    private const string Body = "# 2026-10-29\r\nowner:: Example\r\n![photo](.attachments/photo.png)\n";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private readonly DesktopFixture data = new();
    private readonly DesktopFixture other = new();

    public void Dispose()
    {
        other.Dispose();
        data.Dispose();
    }

    private static byte[] Image(byte marker) => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, marker];

    private static (NoteInfo Note, string Version) Prepare(DesktopFixture fixture, string body, string version, byte image)
    {
        NoteStore store = new(fixture.Root);
        NoteInfo note = store.CreateWithText(Name, body);
        File.WriteAllBytes(note.Path, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(body)]);
        store.Create("Destination");
        store.CreateChild("Destination", "Existing");
        Assert.Equal(AttachmentAddKind.Added, new NoteAttachments(store).Add(note.Reference, "photo.png", Image(image)).Kind);
        NoteHistory history = new(store, () => fixture.UtcNow);
        return (note, Assert.IsType<string>(history.Keep(note.Path, Encoding.UTF8.GetBytes(version))));
    }

    private static string VersionPath(NoteInfo note, string id) => Path.Combine(NoteHistory.FolderFor(note.Path), id + ".md");

    [Theory]
    [InlineData("save")]
    [InlineData("rename")]
    [InlineData("move-source")]
    [InlineData("move-destination")]
    [InlineData("move-published")]
    public async Task ChangedBindingDuringPublication_PreservesBothNotesHistoriesAndImages(string operation)
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier selected", 1);
        (NoteInfo second, string otherId) = Prepare(other, "Independent note", "Independent history", 2);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string destination = Path.Combine(data.Root, "Destination");
        string activeStore = data.Root;
        directory.EntryAvailable = path => activeStore == data.Root
            || (operation is "move-destination" or "move-published" ? path != destination : path != note.Path);
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        EditableNote opened = Assert.IsType<EditableNote>(await link.ReadForEdit().WaitAsync(Wait));
        byte[] before = File.ReadAllBytes(note.Path);
        if (operation == "move-published")
        {
            data.Actions!.Attachments.BeforeMoveStep = step =>
            {
                if (step == "rewrite")
                {
                    activeStore = other.Root;
                }
            };
        }
        else
        {
            data.Notes!.BeforeEditPublish = () => activeStore = other.Root;
        }
        ActionResult result = operation switch
        {
            "save" => await data.Actions!.SaveNote(opened, "changed").WaitAsync(Wait),
            "rename" => await data.Actions!.RenameNote(link.Target, "Renamed", new Confirmation()).WaitAsync(Wait),
            _ => await data.Actions!.MoveNote(link.Target, "Destination").WaitAsync(Wait),
        };
        Assert.Equal(other.Root, activeStore);
        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.Equal(before, File.ReadAllBytes(note.Path));
        Assert.Equal("Earlier selected", File.ReadAllText(VersionPath(note, id)));
        Assert.Single(Directory.GetFiles(NoteHistory.FolderFor(note.Path)));
        Assert.Equal("Independent note", File.ReadAllText(second.Path));
        Assert.Equal("Independent history", File.ReadAllText(VersionPath(second, otherId)));
        Assert.Single(Directory.GetFiles(NoteHistory.FolderFor(second.Path)));
        foreach (DesktopFixture store in new[] { data, other })
        {
            Assert.False(File.Exists(Path.Combine(store.Root, "Renamed.md")));
            Assert.False(File.Exists(Path.Combine(store.Root, "Destination", Name + ".md")));
            Assert.False(Directory.Exists(Path.Combine(store.Root, ".history", "Renamed")));
            Assert.False(Directory.Exists(Path.Combine(store.Root, "Destination", ".history", Name)));
            Assert.False(Directory.Exists(Path.Combine(store.Root, "Destination", ".attachments")));
            Assert.Equal(Image(store == data ? (byte)1 : (byte)2), File.ReadAllBytes(Path.Combine(store.Root, ".attachments", "photo.png")));
        }
    }

    [Fact]
    public async Task SaveThenMove_PreservesFieldsLineEndingsVersionsAndAttachmentClash()
    {
        string earlierBody = Body.Replace("photo.png)\n", "earlier.png)\r\nstatus:: Ready\n", StringComparison.Ordinal);
        (NoteInfo note, string id) = Prepare(data, earlierBody, "Earliest", 3);
        await data.RefreshAsync().WaitAsync(Wait);
        Assert.Equal(AttachmentAddKind.Added, data.Actions!.Attachments.Add(note.Reference, "earlier.png", Image(9)).Kind);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        EditableNote opened = Assert.IsType<EditableNote>(await link.ReadForEdit().WaitAsync(Wait));
        string edited = opened.Text.Replace("Example", "Edited", StringComparison.Ordinal).Replace("earlier.png)", "photo.png)", StringComparison.Ordinal);
        ActionResult saved = await data.Actions.SaveNote(opened, edited).WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, saved.Kind);
        Assert.Equal(AttachmentAddKind.Added, new NoteAttachments(data.Notes!).Add("Destination/Existing", "photo.png", Image(4)).Kind);
        ActionResult move = await data.Actions.MoveNote(link.Target, "Destination").WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, move.Kind);
        NoteInfo moved = Assert.IsType<NoteInfo>(move.RelocatedNote);
        string expected = earlierBody.Replace("Example", "Edited", StringComparison.Ordinal).Replace("earlier.png)", "photo-2.png)", StringComparison.Ordinal);
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(expected)];
        Assert.Equal(bytes, File.ReadAllBytes(moved.Path));
        Assert.Equal("Earliest", File.ReadAllText(VersionPath(moved, id)));
        NoteVersion latest = data.State!.HistoryStore.Versions(moved).First();
        byte[] earlierBytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(earlierBody)];
        Assert.Equal(earlierBytes, File.ReadAllBytes(VersionPath(moved, latest.Id)));
        Assert.False(File.Exists(note.Path));
        string assets = Path.Combine(data.Root, "Destination", ".attachments");
        Assert.Equal(Image(4), File.ReadAllBytes(Path.Combine(assets, "photo.png")));
        Assert.Equal(Image(3), File.ReadAllBytes(Path.Combine(assets, "photo-2.png")));
        Assert.False(File.Exists(Path.Combine(data.Root, ".attachments", "photo.png")));
        using JournalNoteLink relocated = link.Relocated(moved);
        Assert.True(await relocated.Bind().WaitAsync(Wait));
        EditableNote current = Assert.IsType<EditableNote>(await relocated.ReadForEdit().WaitAsync(Wait));
        Assert.Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), current.Text);
        await AssertSharedImage(relocated, "moved.html", 3, 4);
        Assert.Equal(ActionKind.Done,
            (await data.Actions.RestoreVersion(moved.Name, latest.Id, current, new Confirmation()).WaitAsync(Wait)).Kind);
        await AssertSharedImage(relocated, "restored.html", 9, 4);
        Assert.Equal(Image(4), File.ReadAllBytes(Path.Combine(assets, "photo.png")));
    }

    private async Task AssertSharedImage(JournalNoteLink link, string output, byte expected, byte otherImage)
    {
        string path = data.Outside(output);
        Assert.Equal(ActionKind.Done, (await data.Actions!.ShareNote(link.Target, path, false).WaitAsync(Wait)).Kind);
        string html = File.ReadAllText(path);
        Assert.Contains(Convert.ToBase64String(Image(expected)), html);
        Assert.DoesNotContain(Convert.ToBase64String(Image(otherImage)), html);
    }

    [Theory]
    [InlineData("save history")]
    [InlineData("save note")]
    [InlineData("rename")]
    [InlineData("move")]
    public async Task FirstReadOfPublishedFileFails_RestoresOriginalFiles(string operation)
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier selected", 1);
        await data.RefreshAsync().WaitAsync(Wait);
        NoteInfo existing = Assert.Single(data.Notes!.List(), n => n.Name == "Destination/Existing");
        data.State!.HistoryStore.Keep(existing.Path, "Separate history"u8.ToArray());
        data.Actions!.Attachments.Add(existing.Reference, "photo.png", Image(2));
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string publication = operation switch
        {
            "save history" => VersionPath(note, id + "-1"),
            "save note" => note.Path,
            "rename" => Path.Combine(data.Root, "Renamed.md"),
            _ => Path.Combine(data.Root, "Destination", Name + ".md"),
        };
        bool checkSave = false, refused = false;
        directory.EntryAvailable = path =>
        {
            if (!refused && path == publication && File.Exists(publication)
                && (operation != "save note" || (checkSave && PublishedEdit())))
            {
                refused = true;
                return false;
            }
            return true;
        };
        bool PublishedEdit()
        {
            using FileStream file = new(publication, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader text = new(file);
            return text.ReadToEnd() == "Edited";
        }
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        EditableNote opened = Assert.IsType<EditableNote>(await link.ReadForEdit().WaitAsync(Wait));
        SortedDictionary<string, string> before = Files();
        checkSave = true;
        ActionResult result = operation switch
        {
            "rename" => await data.Actions.RenameNote(link.Target, "Renamed", new Confirmation()).WaitAsync(Wait),
            "move" => await data.Actions.MoveNote(link.Target, "Destination").WaitAsync(Wait),
            _ => await data.Actions.SaveNote(opened, "Edited").WaitAsync(Wait),
        };
        Assert.True(refused);
        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.Equal(before, Files());
        Assert.Equal("Earlier selected", await data.State.ReadVersion(link.Target, id).WaitAsync(Wait));
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);

    [Theory]
    [InlineData("rename")]
    [InlineData("move")]
    public async Task VersionKeptDuringRelocation_BothVersionsStayAtTheOriginalNote(string operation)
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier selected", 1);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        byte[] before = File.ReadAllBytes(note.Path);
        NoteHistory otherEditor = new(new NoteStore(data.Root), () => data.UtcNow);
        string? later = null;
        data.Notes!.BeforeEditPublish = () => later = otherEditor.Keep(note.Path, "Also kept"u8.ToArray());
        ActionResult result = operation == "rename"
            ? await data.Actions!.RenameNote(link.Target, "Renamed", new Confirmation()).WaitAsync(Wait)
            : await data.Actions!.MoveNote(link.Target, "Destination").WaitAsync(Wait);
        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.Equal(before, File.ReadAllBytes(note.Path));
        Assert.Equal("Earlier selected", await data.State!.ReadVersion(link.Target, id).WaitAsync(Wait));
        Assert.Equal("Also kept", await data.State.ReadVersion(link.Target, Assert.IsType<string>(later)).WaitAsync(Wait));
        Assert.Equal(2, otherEditor.Versions(note).Count);
        string target = operation == "rename" ? Path.Combine(data.Root, "Renamed.md") : Path.Combine(data.Root, "Destination", Name + ".md");
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(NoteHistory.FolderFor(target)));
        await AssertSharedImage(link, "original.html", 1, 2);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("move")]
    public async Task PlainEditorChangesOnlyItsOwnKindOfHistory(string operation)
    {
        data.Keys = FakeKeyStore.Ready();
        (NoteInfo note, string plainId) = Prepare(data, Body, "Plain version", 1);
        data.WriteSensitive(Name, "Independent sealed version");
        string sealedPath = Path.ChangeExtension(note.Path, NoteStore.SensitiveExtension);
        NoteHistory history = new(new NoteStore(data.Root), () => data.UtcNow);
        string sealedId = Assert.IsType<string>(history.Keep(sealedPath, File.ReadAllBytes(sealedPath), Name));
        data.WriteSensitive(Name, "Independent sealed note");
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        ActionResult result = operation switch
        {
            "delete" => await data.Actions!.DeleteNote(link.Target, new Confirmation()).WaitAsync(Wait),
            "rename" => await data.Actions!.RenameNote(link.Target, "Renamed", new Confirmation()).WaitAsync(Wait),
            _ => await data.Actions!.MoveNote(link.Target, "Destination").WaitAsync(Wait),
        };
        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.False(File.Exists(note.Path));
        Assert.False(File.Exists(VersionPath(note, plainId)));
        if (operation != "delete")
        {
            NoteInfo moved = Assert.IsType<NoteInfo>(result.RelocatedNote);
            Assert.Equal("Plain version", File.ReadAllText(VersionPath(moved, plainId)));
            Assert.Equal(operation == "rename" ? Body.Replace("# " + Name, "# Renamed", StringComparison.Ordinal) : Body,
                File.ReadAllText(moved.Path));
        }
        byte[] machine = data.Keys.MachinePublicKey()!;
        Assert.Equal("Independent sealed note", SealedNote.Open(Name, File.ReadAllBytes(sealedPath), machine, data.Keys.Agree));
        Assert.True(data.Notes!.TryFindSensitive(NoteReference.Stored(Name, true), out NoteInfo? sealedNote));
        Assert.True(history.TryRead(sealedNote!, sealedId, out byte[]? bytes));
        Assert.Equal("Independent sealed version", SealedNote.Open(Name, bytes!, machine, data.Keys.Agree));
    }

    [Theory]
    [InlineData("exact parent")]
    [InlineData("image unavailable")]
    [InlineData("version image clash")]
    [InlineData("rewritten size")]
    public async Task RefusedMove_PreservesSelectedAndDestinationData(string reason)
    {
        string body = reason == "rewritten size"
            ? Body + new string('x', (int)NoteStore.MaxEditBytes - 3 - Encoding.UTF8.GetByteCount(Body)) : Body;
        (NoteInfo note, string id) = Prepare(data, body, reason == "version image clash" ? Body : "Earlier", 1);
        data.WriteNote("Destination ", "Exact legacy parent");
        await data.RefreshAsync().WaitAsync(Wait);
        NoteAttachments attachments = data.Actions!.Attachments;
        Assert.Equal(AttachmentAddKind.Added, attachments.Add("Destination/Existing", "photo.png", Image(2)).Kind);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string image = Path.Combine(data.Root, ".attachments", "photo.png");
        bool refusedRead = false;
        directory.EntryAvailable = path =>
        {
            if (reason == "image unavailable" && path == image)
            {
                refusedRead = true;
                return false;
            }
            return true;
        };
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        SortedDictionary<string, string> before = Files();
        ActionResult result = await data.Actions.MoveNote(link.Target, reason == "exact parent" ? "Destination .md" : "Destination").WaitAsync(Wait);
        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.Equal(reason == "image unavailable", refusedRead);
        Assert.Equal(before, Files());
        Assert.Equal(reason == "version image clash" ? Body : "Earlier", File.ReadAllText(VersionPath(note, id)));
        if (reason == "exact parent")
        {
            Assert.Equal(ActionReason.Invalid, result.Reason);
        }
        if (reason == "rewritten size")
        {
            Assert.Equal(NoteStore.MaxEditBytes, new FileInfo(note.Path).Length);
            Assert.Equal(body.Replace("\r\n", "\n", StringComparison.Ordinal), (await link.ReadForEdit().WaitAsync(Wait))?.Text);
        }
    }

    [Fact]
    public async Task CollisionRewriteAtEditorLimit_ReopensWithTheSelectedImage()
    {
        // The BOM takes three bytes and "photo-2.png" takes two more than "photo.png".
        string body = Body + new string('x', (int)NoteStore.MaxEditBytes - 5 - Encoding.UTF8.GetByteCount(Body));
        (NoteInfo note, string id) = Prepare(data, body, "Earlier", 1);
        await data.RefreshAsync().WaitAsync(Wait);
        Assert.Equal(AttachmentAddKind.Added, data.Actions!.Attachments.Add("Destination/Existing", "photo.png", Image(2)).Kind);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        ActionResult result = await data.Actions.MoveNote(link.Target, "Destination").WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, result.Kind);
        NoteInfo moved = Assert.IsType<NoteInfo>(result.RelocatedNote);
        Assert.Equal(NoteStore.MaxEditBytes, new FileInfo(moved.Path).Length);
        Assert.Equal("Earlier", File.ReadAllText(VersionPath(moved, id)));
        using JournalNoteLink relocated = link.Relocated(moved);
        Assert.True(await relocated.Bind().WaitAsync(Wait));
        EditableNote opened = Assert.IsType<EditableNote>(await relocated.ReadForEdit().WaitAsync(Wait));
        InlineImage image = Assert.Single(await data.State!.InlineImages(opened.Target, opened.Text).WaitAsync(Wait));
        Assert.Equal(Convert.ToBase64String(Image(1)), image.Base64);
        Assert.Equal(Image(2), File.ReadAllBytes(Path.Combine(data.Root, "Destination", ".attachments", "photo.png")));
    }

    [Theory]
    [InlineData("move", "current")]
    [InlineData("delete", "current")]
    [InlineData("move", "unreadable")]
    [InlineData("delete", "version")]
    public async Task SiblingCreatedDuringCleanup_StillDisplaysTheOriginalImage(string operation, string reference)
    {
        (NoteInfo note, _) = Prepare(data, Body, "Earlier", 5);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        directory.EntryAvailable = path => reference != "unreadable" || path != Path.Combine(data.Root, "Sibling.md");
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        const string Shared = "![Shared](.attachments/photo.png)";
        NoteStore otherEditor = new(data.Root);
        string? kept = null;
        void CreateSibling()
        {
            NoteInfo created = otherEditor.CreateWithText("Sibling", reference == "version" ? "Current field" : Shared);
            if (reference == "version")
            {
                kept = data.State!.HistoryStore.Keep(created.Path, Encoding.UTF8.GetBytes(Shared));
            }
        }
        data.Notes!.BeforeDeleteFiles = CreateSibling;
        data.Actions!.Attachments.BeforeMoveStep = step =>
        {
            if (step == "cleanup")
            {
                CreateSibling();
            }
        };
        ActionResult result = operation == "move"
            ? await data.Actions.MoveNote(link.Target, "Destination").WaitAsync(Wait)
            : await data.Actions.DeleteNote(link.Target, new Confirmation()).WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.False(File.Exists(note.Path));
        EditableNote sibling = Assert.IsType<EditableNote>(await data.State!.ReadForEdit("Sibling").WaitAsync(Wait));
        if (reference == "version")
        {
            ActionResult restored = await data.Actions.RestoreVersion(sibling.Name, Assert.IsType<string>(kept), sibling, new Confirmation()).WaitAsync(Wait);
            Assert.Equal(ActionKind.Done, restored.Kind);
            sibling = Assert.IsType<EditableNote>(restored.Note);
        }
        Assert.Equal(Shared, sibling.Text);
        Assert.Equal(Convert.ToBase64String(Image(5)), Assert.Single(await data.State.InlineImages(sibling.Target, sibling.Text).WaitAsync(Wait)).Base64);
    }

    [Fact]
    public async Task NewSubpageDuringConfirmedDelete_KeepsBothPagesAndTheirImage()
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier", 5);
        NoteStore otherEditor = new(data.Root);
        const string Shared = "![Shared](.attachments/shared.png)";
        NoteInfo first = otherEditor.CreateChild(note.Reference, "First", Shared);
        Assert.Equal(AttachmentAddKind.Added, new NoteAttachments(otherEditor).Add(first.Reference, "shared.png", Image(6)).Kind);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        data.Notes!.BeforeDeleteFiles = () => otherEditor.CreateChild(note.Reference, "Second", Shared);
        Assert.NotEqual(ActionKind.Done, (await data.Actions!.DeleteNote(link.Target, new Confirmation()).WaitAsync(Wait)).Kind);
        Assert.Equal("Earlier", await data.State!.ReadVersion(link.Target, id).WaitAsync(Wait));
        foreach (string page in new[] { Name + "/First", Name + "/Second" })
        {
            EditableNote opened = Assert.IsType<EditableNote>(await data.State.ReadForEdit(page).WaitAsync(Wait));
            Assert.Equal(Convert.ToBase64String(Image(6)), Assert.Single(await data.State.InlineImages(opened.Target, opened.Text).WaitAsync(Wait)).Base64);
        }
    }

    [Fact]
    public async Task ConfirmedDelete_PreservesAnotherEditorsSaveAndItsHistory()
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier", 5);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        NoteStore secondEditor = new(data.Root);
        NoteHistory history = new(secondEditor, () => data.UtcNow);
        NoteSaveResult? concurrentSave = null;
        data.Notes!.BeforeDeleteFiles = () => concurrentSave = secondEditor.SaveIfUnchanged(
            secondEditor.ReadForEdit(note), "Saved by second editor", out _, history);
        ActionResult result = await data.Actions!.DeleteNote(link.Target, new Confirmation()).WaitAsync(Wait);
        Assert.Equal(NoteSaveResult.Saved, concurrentSave);
        Assert.NotEqual(ActionKind.Done, result.Kind);
        Assert.Equal("Saved by second editor", File.ReadAllText(note.Path));
        Assert.Equal("Earlier", File.ReadAllText(VersionPath(note, id)));
        Assert.Equal(Body, File.ReadAllText(VersionPath(note, history.Versions(note).First().Id)));
        Assert.Equal(2, history.Versions(note).Count);
        Assert.Equal(Image(5), File.ReadAllBytes(Path.Combine(data.Root, ".attachments", "photo.png")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedDelete_VerifiesTheVersionSetAndPreservesSharedImages(bool newerVersion)
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Earlier", 5);
        data.WriteNote("Sibling", "![Shared](.attachments/photo.png)");
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        string? later = null;
        if (newerVersion)
        {
            data.Notes!.BeforeDeleteFiles = () => later = data.State!.HistoryStore.Keep(note.Path, "Also kept"u8.ToArray());
        }
        ActionResult result = await data.Actions!.DeleteNote(link.Target, new Confirmation()).WaitAsync(Wait);
        Assert.Equal(!newerVersion, result.Kind == ActionKind.Done);
        if (newerVersion)
        {
            Assert.Equal(Body, File.ReadAllText(note.Path));
            Assert.Equal("Earlier", File.ReadAllText(VersionPath(note, id)));
            Assert.Equal("Also kept", File.ReadAllText(VersionPath(note, Assert.IsType<string>(later))));
        }
        else
        {
            Assert.False(File.Exists(note.Path));
            Assert.False(Directory.Exists(NoteHistory.FolderFor(note.Path)));
        }
        Assert.Equal("![Shared](.attachments/photo.png)", File.ReadAllText(Path.Combine(data.Root, "Sibling.md")));
        Assert.Equal(Image(5), File.ReadAllBytes(Path.Combine(data.Root, ".attachments", "photo.png")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShareAndHistory_ReadOnlyTheSelectedSource(bool stale)
    {
        (NoteInfo note, string id) = Prepare(data, Body, "Selected version", 6);
        Prepare(other, "Other body", "Other version", 7);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string owner = data.Root, secondary = VersionPath(note, id);
        directory.EntryAvailable = path =>
        {
            if (stale && path == secondary)
            {
                owner = other.Root;
            }
            return owner == data.Root;
        };
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        string? version = await data.State!.ReadVersion(link.Target, id).WaitAsync(Wait);
        Assert.Equal(stale ? null : "Selected version", version);
        Assert.Equal(stale ? other.Root : data.Root, owner);
        owner = data.Root;
        secondary = Path.Combine(data.Root, ".attachments", "photo.png");
        string shared = data.Outside("shared.html");
        ActionResult result = await data.Actions!.ShareNote(link.Target, shared, false).WaitAsync(Wait);
        Assert.Equal(stale ? other.Root : data.Root, owner);
        if (stale)
        {
            Assert.NotEqual(ActionKind.Done, result.Kind);
            Assert.False(File.Exists(shared));
        }
        else
        {
            Assert.Equal(ActionKind.Done, result.Kind);
            string html = File.ReadAllText(shared);
            Assert.Contains(Convert.ToBase64String(Image(6)), html);
            Assert.DoesNotContain(Convert.ToBase64String(Image(7)), html);
        }
    }

    [Theory]
    [InlineData("sealed note")]
    [InlineData("sealed version")]
    [InlineData("plain history")]
    [InlineData("new version")]
    public async Task Conversion_VerifiesPublicationAndPlainHistoryCleanup(string change)
    {
        data.Keys = FakeKeyStore.Ready();
        (NoteInfo note, string id) = Prepare(data, Body, "Readable version", 8);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string plain = VersionPath(note, id);
        string sealedNote = Path.ChangeExtension(note.Path, NoteStore.SensitiveExtension);
        string sealedVersion = Path.Combine(NoteHistory.FolderFor(note.Path), NoteHistory.FileName(id, Name));
        string? unavailable = null, later = null;
        directory.EntryAvailable = path => path != unavailable;
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        bool cleanupEntered = false;
        data.Actions!.BeforePlainHistoryCleanup = () =>
        {
            cleanupEntered = true;
            Assert.False(File.Exists(note.Path));
            unavailable = change switch { "sealed note" => sealedNote, "sealed version" => sealedVersion, "plain history" => plain, _ => null };
            if (change == "new version")
            {
                later = data.State!.HistoryStore.Keep(note.Path, "Later version"u8.ToArray());
            }
        };
        ActionResult result = await data.Actions.MarkSensitive(link.Target, new Confirmation()).WaitAsync(Wait);
        Assert.True(cleanupEntered);
        if (change.StartsWith("sealed", StringComparison.Ordinal))
        {
            Assert.NotEqual(ActionKind.Done, result.Kind);
            Assert.Equal(Body, File.ReadAllText(note.Path));
            Assert.Equal("Readable version", File.ReadAllText(plain));
            Assert.False(File.Exists(sealedNote));
            Assert.False(File.Exists(sealedVersion));
        }
        else
        {
            Assert.Equal((ActionKind.Done, ActionReason.PlainHistoryLeft), (result.Kind, result.Reason));
            Assert.False(File.Exists(note.Path));
            Assert.Equal(change == "plain history", File.Exists(plain));
            Assert.Equal(change == "new version" ? "Later version" : "Readable version", File.ReadAllText(VersionPath(note, later ?? id)));
            byte[] machine = data.Keys.MachinePublicKey()!;
            Assert.Equal("\uFEFF" + Body, SealedNote.Open(Name, File.ReadAllBytes(sealedNote), machine, data.Keys.Agree));
            Assert.Equal("Readable version", SealedNote.Open(Name, File.ReadAllBytes(sealedVersion), machine, data.Keys.Agree));
        }
    }

    [Fact]
    public async Task Conversion_CleansTheOriginalHistoryWhenTheConfiguredStoreChanges()
    {
        data.Keys = FakeKeyStore.Ready();
        (NoteInfo note, string id) = Prepare(data, Body, "Selected history", 1);
        (NoteInfo second, string otherId) = Prepare(other, "Independent body", "Independent history", 2);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        string configured = data.Root;
        directory.EntryAvailable = path => path != data.Root || configured == data.Root;
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        data.Actions!.BeforePlainHistoryCleanup = () => configured = other.Root;
        ActionResult result = await data.Actions.MarkSensitive(link.Target, new Confirmation()).WaitAsync(Wait);
        Assert.Equal(other.Root, configured);
        Assert.Equal((ActionKind.Done, ActionReason.None), (result.Kind, result.Reason));
        Assert.False(File.Exists(note.Path));
        Assert.False(File.Exists(VersionPath(note, id)));
        byte[] machine = data.Keys.MachinePublicKey()!;
        string sealedVersion = Path.Combine(NoteHistory.FolderFor(note.Path), NoteHistory.FileName(id, Name));
        Assert.Equal("Selected history", SealedNote.Open(Name, File.ReadAllBytes(sealedVersion), machine, data.Keys.Agree));
        Assert.Equal("\uFEFF" + Body, SealedNote.Open(Name, File.ReadAllBytes(Path.ChangeExtension(note.Path, ".ctxs")), machine, data.Keys.Agree));
        Assert.Equal("Independent body", File.ReadAllText(second.Path));
        Assert.Equal("Independent history", File.ReadAllText(VersionPath(second, otherId)));
    }
}
