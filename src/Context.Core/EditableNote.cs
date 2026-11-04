using System.Security.Cryptography;

namespace Context;

/// <summary>
/// A note opened for editing: its name and path, its text with <c>\n</c> line endings, and what is needed to save it
/// back safely: the SHA-256 of the file's bytes when it was read, and whether it used CRLF line endings or a BOM.
/// </summary>
internal sealed record EditableNote
{
    public EditableNote(string name, string path, string text, string version, bool hadCrlf, bool hadBom)
    {
        Name = name;
        Path = path;
        Text = text;
        Version = version;
        HadCrlf = hadCrlf;
        HadBom = hadBom;
    }

    /// <summary>The note's name (its file name without <c>.md</c>).</summary>
    public string Name { get; }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The text as read, with <c>\n</c> line endings and no BOM.</summary>
    public string Text { get; }

    /// <summary>The SHA-256 of the file's bytes when it was read, as lowercase hex.</summary>
    public string Version { get; }

    /// <summary>True when the file used <c>\r\n</c> line endings.</summary>
    public bool HadCrlf { get; }

    /// <summary>True when the file started with a UTF-8 byte order mark.</summary>
    public bool HadBom { get; }

    /// <summary>The type name only, so a note's text never ends up in a log by accident.</summary>
    public override string ToString() => nameof(EditableNote);

    /// <summary>The SHA-256 of <paramref name="bytes"/> as lowercase hex.</summary>
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>What <see cref="NoteStore.SaveIfUnchanged"/> did.</summary>
internal enum NoteSaveResult
{
    /// <summary>The new text was written.</summary>
    Saved,

    /// <summary>The text is the same as when the note was opened; nothing was written.</summary>
    Unchanged,

    /// <summary>The file changed on disk since it was opened; nothing was written.</summary>
    ChangedOnDisk,

    /// <summary>The file no longer exists; nothing was written.</summary>
    Missing,
}
