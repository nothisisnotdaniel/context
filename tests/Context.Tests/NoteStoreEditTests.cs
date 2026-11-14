using System.Security.Cryptography;
using System.Text;

namespace Context.Tests;

// 094 AC-5 / AC-6: NoteStore.ReadForEdit and SaveIfUnchanged keep files byte for byte unless the text changed, keep
// CRLF and a BOM, and write nothing when the file changed on disk or is gone.
public sealed class NoteStoreEditTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string root = Path.Combine(Path.GetTempPath(), "context-edit-" + Guid.NewGuid().ToString("N"));
    private readonly NoteStore store;

    public NoteStoreEditTests()
    {
        Directory.CreateDirectory(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadForEdit_PlainLf_TextAndVersion()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# Plan\n\nText #tag\n");
        NoteInfo note = Write("Plan", bytes);

        EditableNote opened = store.ReadForEdit(note);

        Assert.Equal("Plan", opened.Name);
        Assert.Equal(note.Path, opened.Path);
        Assert.Equal("# Plan\n\nText #tag\n", opened.Text);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), opened.Version);
        Assert.False(opened.HadCrlf);
        Assert.False(opened.HadBom);
        Assert.Equal("EditableNote", opened.ToString());
    }

    [Fact]
    public void ReadForEdit_CrlfAndBom_TextHasLfOnly()
    {
        NoteInfo note = Write("Win", [.. Bom, .. Encoding.UTF8.GetBytes("# Win\r\n\r\nLine\r\n")]);

        EditableNote opened = store.ReadForEdit(note);

        Assert.Equal("# Win\n\nLine\n", opened.Text);
        Assert.True(opened.HadCrlf);
        Assert.True(opened.HadBom);
    }

    // AC-5: saving the text as opened writes nothing: same bytes, same last-write time.
    [Theory]
    [InlineData("# Plain\n\nText\n", false)]
    [InlineData("# Win\r\n\r\nLine\r\n", true)]
    [InlineData("* lossy list\n__b__\n", false)]
    public void SaveIfUnchanged_SameText_WritesNothing(string text, bool bom)
    {
        NoteInfo note = Write("Note", bom ? [.. Bom, .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text));
        File.SetLastWriteTimeUtc(note.Path, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        EditableNote opened = store.ReadForEdit(note);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        NoteSaveResult result = store.SaveIfUnchanged(opened, opened.Text, out EditableNote? saved);

        Assert.Equal(NoteSaveResult.Unchanged, result);
        Assert.Null(saved);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-5: an edit keeps CRLF line endings and the BOM.
    [Fact]
    public void SaveIfUnchanged_Edit_KeepsCrlfAndBom()
    {
        NoteInfo note = Write("Win", [.. Bom, .. Encoding.UTF8.GetBytes("# Win\r\n\r\nLine\r\n")]);
        EditableNote opened = store.ReadForEdit(note);

        NoteSaveResult result = store.SaveIfUnchanged(opened, "# Win\n\nLine\nMore\n", out EditableNote? saved);

        Assert.Equal(NoteSaveResult.Saved, result);
        byte[] expected = [.. Bom, .. Encoding.UTF8.GetBytes("# Win\r\n\r\nLine\r\nMore\r\n")];
        Assert.Equal(expected, File.ReadAllBytes(note.Path));
        Assert.NotNull(saved);
        Assert.Equal("# Win\n\nLine\nMore\n", saved.Text);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)), saved.Version);
        Assert.True(saved.HadCrlf);
        Assert.True(saved.HadBom);
        Assert.Equal(["Win.md"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)).ToArray());
    }

    // AC-5: a plain LF file gets exactly the new text, and the saved note can be saved again.
    [Fact]
    public void SaveIfUnchanged_Edit_PlainBytesAndSavesAgain()
    {
        NoteInfo note = Write("Plain", Encoding.UTF8.GetBytes("Old\n"));
        EditableNote opened = store.ReadForEdit(note);

        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(opened, "New #tag\n", out EditableNote? saved));
        Assert.Equal(Encoding.UTF8.GetBytes("New #tag\n"), File.ReadAllBytes(note.Path));
        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(saved!, "Newer\n", out _));
        Assert.Equal(Encoding.UTF8.GetBytes("Newer\n"), File.ReadAllBytes(note.Path));
    }

    // AC-6: a file changed on disk since it was opened is not overwritten.
    [Fact]
    public void SaveIfUnchanged_ChangedOnDisk_WritesNothing()
    {
        NoteInfo note = Write("Note", Encoding.UTF8.GetBytes("Mine\n"));
        EditableNote opened = store.ReadForEdit(note);
        File.WriteAllText(note.Path, "Theirs\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        NoteSaveResult result = store.SaveIfUnchanged(opened, "Mine, edited\n", out EditableNote? saved);

        Assert.Equal(NoteSaveResult.ChangedOnDisk, result);
        Assert.Null(saved);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-6: a deleted file is not created again.
    [Fact]
    public void SaveIfUnchanged_Missing_WritesNothing()
    {
        NoteInfo note = Write("Gone", Encoding.UTF8.GetBytes("Text\n"));
        EditableNote opened = store.ReadForEdit(note);
        File.Delete(note.Path);

        NoteSaveResult result = store.SaveIfUnchanged(opened, "Edited\n", out _);

        Assert.Equal(NoteSaveResult.Missing, result);
        Assert.Empty(Directory.GetFiles(root));
    }

    // An empty note reads as empty text with the hash of no bytes.
    [Fact]
    public void ReadForEdit_Empty_EmptyText()
    {
        NoteInfo note = Write("Empty", []);

        EditableNote opened = store.ReadForEdit(note);

        Assert.Equal(string.Empty, opened.Text);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), opened.Version);
        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(opened, "Now text\n", out _));
        Assert.Equal("Now text\n", File.ReadAllText(note.Path));
    }

    // A note over the limit is not opened; one exactly at the limit is.
    [Fact]
    public void ReadForEdit_OverLimit_Refused()
    {
        NoteInfo big = Write("Big", Encoding.ASCII.GetBytes(new string('a', (int)NoteStore.MaxEditBytes + 1)));
        NoteInfo edge = Write("Edge", Encoding.ASCII.GetBytes(new string('a', (int)NoteStore.MaxEditBytes)));

        Assert.ThrowsAny<IOException>(() => store.ReadForEdit(big));
        Assert.Equal((int)NoteStore.MaxEditBytes, store.ReadForEdit(edge).Text.Length);
    }

    // Text that would be over the limit on disk (here through CRLF) writes nothing.
    [Fact]
    public void SaveIfUnchanged_OverLimitOnDisk_WritesNothing()
    {
        NoteInfo note = Write("Win", Encoding.ASCII.GetBytes("a\r\n"));
        EditableNote opened = store.ReadForEdit(note);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        string text = new string('\n', (int)(NoteStore.MaxEditBytes / 2) + 1);

        NoteSaveResult result = store.SaveIfUnchanged(opened, text, out EditableNote? saved);

        Assert.Equal(NoteSaveResult.TooLarge, result);
        Assert.Null(saved);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // A file that grew over the limit since it was opened counts as changed and is not overwritten.
    [Fact]
    public void SaveIfUnchanged_GrewOverLimit_ChangedOnDisk()
    {
        NoteInfo note = Write("Plan", Encoding.UTF8.GetBytes("# Plan\n"));
        EditableNote opened = store.ReadForEdit(note);
        File.WriteAllBytes(note.Path, Encoding.ASCII.GetBytes(new string('b', (int)NoteStore.MaxEditBytes + 10)));
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Assert.Equal(NoteSaveResult.ChangedOnDisk, store.SaveIfUnchanged(opened, "# Plan\n\nMore\n", out _));
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // A FIFO named like a note is never opened (it reports length 0), so reading it returns at once as an empty note
    // instead of blocking the reader, and a save over it (opened with other text) writes nothing.
    [Fact]
    public async Task Fifo_ReadWithoutBlocking_SaveWritesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteInfo plan = Write("Plan", Encoding.UTF8.GetBytes("# Plan\n"));
        EditableNote opened = store.ReadForEdit(plan);
        File.Delete(plan.Path);
        using (System.Diagnostics.Process mkfifo = System.Diagnostics.Process.Start("mkfifo", plan.Path))
        {
            await mkfifo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, mkfifo.ExitCode);
        }

        EditableNote fifo = await Task.Run(() => store.ReadForEdit(plan)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(string.Empty, fifo.Text);
        NoteSaveResult result = await Task.Run(() => store.SaveIfUnchanged(opened, "# Plan\n\nMore\n", out _)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(NoteSaveResult.ChangedOnDisk, result);
        Assert.Equal(["Plan.md"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)).ToArray());
    }

    // The temp file of a save is private to the owner.
    [Fact]
    public void SaveIfUnchanged_TempFileIsPrivate()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NoteInfo note = Write("Plan", Encoding.UTF8.GetBytes("# Plan\n"));
        EditableNote opened = store.ReadForEdit(note);
        File.SetUnixFileMode(note.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(opened, "# Plan\n\nMore\n", out _));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(note.Path));
    }

    // 152 AC-8: saving a subpage lands on its own file; the temp file lives in its folder and is gone afterwards.
    [Fact]
    public void SaveIfUnchanged_Child_LandsOnItsFile_NoStrayFile()
    {
        Write("Meetings", Encoding.UTF8.GetBytes("# Meetings\n"));
        Directory.CreateDirectory(Path.Combine(root, "Meetings"));
        File.WriteAllText(Path.Combine(root, "Meetings", "Meeting 1.md"), "# Meeting 1\n");
        Assert.True(store.TryFind("Meetings/Meeting 1", out NoteInfo? child));
        EditableNote opened = store.ReadForEdit(child!);

        Assert.Equal("Meetings/Meeting 1", opened.Name);
        Assert.Equal(NoteSaveResult.Saved, store.SaveIfUnchanged(opened, "# Meeting 1\n\nAgenda\n", out EditableNote? saved));

        Assert.Equal("Meetings/Meeting 1", saved!.Name);
        Assert.Equal("# Meeting 1\n\nAgenda\n", File.ReadAllText(Path.Combine(root, "Meetings", "Meeting 1.md")));
        Assert.Equal("# Meetings\n", File.ReadAllText(Path.Combine(root, "Meetings.md")));
        Assert.Equal(["Meetings.md"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)).ToArray());
        Assert.Equal(["Meeting 1.md"], Directory.GetFiles(Path.Combine(root, "Meetings")).Select(f => Path.GetFileName(f)).ToArray());
        Assert.Equal([Path.Combine(root, "Meetings")], Directory.GetDirectories(root));
    }

    // 152 AC-8: Save (used by import) stays top-level only: a title with / is still refused.
    [Fact]
    public void Save_TitleWithSlash_StillRefused()
    {
        Write("Meetings", Encoding.UTF8.GetBytes("# Meetings\n"));

        Assert.Throws<ArgumentException>(() => store.Save("Meetings/Meeting 1", Encoding.UTF8.GetBytes("x")));

        Assert.Equal(["Meetings.md"], Directory.GetFileSystemEntries(root).Select(f => Path.GetFileName(f)).ToArray());
    }

    private NoteInfo Write(string name, byte[] bytes)
    {
        string path = Path.Combine(root, name + ".md");
        File.WriteAllBytes(path, bytes);
        return new NoteInfo(name, File.GetLastWriteTime(path), path);
    }
}
