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
        (NoteInfo note, string id) = Prepare(data, Body, "Earliest", 3);
        await data.RefreshAsync().WaitAsync(Wait);
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(data.Root));
        using JournalNoteLink link = new(note, directory);
        Assert.True(await link.Bind().WaitAsync(Wait));
        EditableNote opened = Assert.IsType<EditableNote>(await link.ReadForEdit().WaitAsync(Wait));
        ActionResult saved = await data.Actions!.SaveNote(opened, opened.Text.Replace("Example", "Edited", StringComparison.Ordinal)).WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, saved.Kind);
        Assert.Equal(AttachmentAddKind.Added, new NoteAttachments(data.Notes!).Add("Destination/Existing", "photo.png", Image(4)).Kind);
        ActionResult move = await data.Actions.MoveNote(link.Target, "Destination").WaitAsync(Wait);
        Assert.Equal(ActionKind.Done, move.Kind);
        NoteInfo moved = Assert.IsType<NoteInfo>(move.RelocatedNote);
        string expected = Body.Replace("Example", "Edited", StringComparison.Ordinal).Replace("photo.png)", "photo-2.png)", StringComparison.Ordinal);
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(expected)];
        Assert.Equal(bytes, File.ReadAllBytes(moved.Path));
        Assert.Equal("Earliest", File.ReadAllText(VersionPath(moved, id)));
        NoteVersion latest = data.State!.HistoryStore.Versions(moved).First();
        Assert.Equal(Body, File.ReadAllText(VersionPath(moved, latest.Id)));
        Assert.False(File.Exists(note.Path));
        string assets = Path.Combine(data.Root, "Destination", ".attachments");
        Assert.Equal(Image(4), File.ReadAllBytes(Path.Combine(assets, "photo.png")));
        Assert.Equal(Image(3), File.ReadAllBytes(Path.Combine(assets, "photo-2.png")));
        Assert.False(File.Exists(Path.Combine(data.Root, ".attachments", "photo.png")));
        using JournalNoteLink relocated = link.Relocated(moved);
        Assert.True(await relocated.Bind().WaitAsync(Wait));
        Assert.Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), (await relocated.ReadForEdit().WaitAsync(Wait))?.Text);
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
