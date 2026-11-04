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

    // With encryption on, the note is read decrypted and saved encrypted.
    [Fact]
    public void SaveIfUnchanged_Encrypted_StaysEncrypted()
    {
        Encryption encryption = new Encryption(root, minIterations: 1000);
        encryption.Use(Vault.Create(Path.Combine(root, "vault"), "a long enough passphrase", 1000));
        NoteStore encrypted = new NoteStore(root, encryption);
        NoteInfo note = Write("Secret", Encryption.Protect(encryption, Vault.KindNote, Encoding.UTF8.GetBytes("Hidden\n")));

        EditableNote opened = encrypted.ReadForEdit(note);
        Assert.Equal("Hidden\n", opened.Text);
        Assert.Equal(NoteSaveResult.Saved, encrypted.SaveIfUnchanged(opened, "Hidden too\n", out _));

        byte[] bytes = File.ReadAllBytes(note.Path);
        Assert.True(Vault.IsEncrypted(bytes));
        Assert.Equal("Hidden too\n", Encryption.DecodeText(Encryption.Unprotect(encryption, Vault.KindNote, bytes)));
    }

    private NoteInfo Write(string name, byte[] bytes)
    {
        string path = Path.Combine(root, name + ".md");
        File.WriteAllBytes(path, bytes);
        return new NoteInfo(name, File.GetLastWriteTime(path), path);
    }
}
