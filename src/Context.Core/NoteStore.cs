using System.Text;

namespace Context;

/// <summary>A note file in the store: its name (the file name without <c>.md</c>) and last-write time.</summary>
internal sealed record NoteInfo
{
    public NoteInfo(string name, DateTime modified, string path)
    {
        Name = name;
        Modified = modified;
        Path = path;
    }

    /// <summary>The file name without the <c>.md</c> extension.</summary>
    public string Name { get; }

    /// <summary>The file's last-write time, local time.</summary>
    public DateTime Modified { get; }

    /// <summary>The full path of the file, always directly inside the store's root.</summary>
    public string Path { get; }
}

/// <summary>An opened note: its name and the file's text.</summary>
internal sealed record Note
{
    public Note(string name, string text)
    {
        Name = name;
        Text = text;
    }

    public string Name { get; }

    public string Text { get; }
}

/// <summary>Thrown by <see cref="NoteStore.Create"/> when a note with the same name (ignoring case) exists.</summary>
internal sealed class NoteExistsException : IOException
{
    public NoteExistsException(string name)
        : base($"Note {name} already exists.")
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Markdown notes kept as top-level <c>*.md</c> files in one root folder. A note's title is its
/// file name without <c>.md</c>. The files are the source of truth; nothing else is stored.
/// </summary>
internal sealed class NoteStore
{
    /// <summary>The longest title <see cref="Create"/> accepts.</summary>
    public const int MaxTitleLength = 100;

    private const string Extension = ".md";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Device names Windows reserves, with or without an extension: <c>CON</c>, <c>PRN</c>, <c>AUX</c>,
    /// <c>NUL</c>, <c>COM1</c>-<c>COM9</c>, <c>LPT1</c>-<c>LPT9</c> and the superscript digit forms.
    /// </summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM\u00B9", "COM\u00B2", "COM\u00B3",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
    };

    private readonly string root;
    private readonly Encryption? encryption;

    /// <param name="root">The folder notes live in.</param>
    /// <param name="encryption">The session's encryption state; <c>null</c> (or no vault) keeps files plain.</param>
    public NoteStore(string root, Encryption? encryption = null)
    {
        this.root = Path.GetFullPath(root);
        this.encryption = encryption;
    }

    /// <summary>The folder notes live in.</summary>
    public string Root => root;

    /// <summary>The <c>Context</c> folder in the user's app data folder.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Context");

    /// <summary>
    /// Trims <paramref name="title"/>, then keeps removing a trailing <c>.md</c> (ignoring case) and
    /// trimming again until it no longer ends in <c>.md</c>. Only ever removes characters, so the
    /// result must still go through <see cref="ValidateTitle"/>.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        string normalized = title.Trim();
        while (normalized.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^Extension.Length].Trim();
        }

        return normalized;
    }

    /// <summary>
    /// Checks a note title. Returns <c>null</c> if it is valid, otherwise a short plain-text reason
    /// (not markup). A valid title is non-blank, at most <see cref="MaxTitleLength"/> characters,
    /// does not start with <c>.</c>, and has no <c>/</c>, <c>\</c>, <c>:</c>, control character
    /// or <see cref="Path.GetInvalidFileNameChars"/> character, so it is always a single file name
    /// inside the root. On every OS, so notes stay usable on Windows too, it also must not end with
    /// <c>.</c> or a space, and the part before its first <c>.</c> (trailing spaces removed) must not
    /// be a name Windows reserves (<c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c>-<c>COM9</c>,
    /// <c>LPT1</c>-<c>LPT9</c>, also with superscript digits), ignoring case. The checks run in that
    /// order and the first failing one gives the message.
    /// </summary>
    public static string? ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "A note needs a title.";
        }

        if (title.Length > MaxTitleLength)
        {
            return $"A note title can be at most {MaxTitleLength} characters.";
        }

        if (title.StartsWith('.'))
        {
            return "A note title can't start with a dot.";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in title)
        {
            if (c is '/' or '\\' or ':' || char.IsControl(c) || Array.IndexOf(invalid, c) >= 0)
            {
                return "A note title can't contain / \\ : or control characters.";
            }
        }

        if (title.EndsWith('.') || title.EndsWith(' '))
        {
            return "A note title can't end with a dot or a space.";
        }

        int dot = title.IndexOf('.');
        string stem = (dot < 0 ? title : title[..dot]).TrimEnd(' ');
        if (WindowsReservedNames.Contains(stem))
        {
            return "A note title can't be a name Windows reserves (CON, PRN, AUX, NUL, COM1-9, LPT1-9).";
        }

        return null;
    }

    /// <summary>
    /// Creates <c>&lt;root&gt;/&lt;title&gt;.md</c> containing <c># &lt;title&gt;\n</c> (UTF-8, no BOM),
    /// creating the root folder if needed. Never overwrites: throws <see cref="ArgumentException"/>
    /// for an invalid title and <see cref="NoteExistsException"/> if the note exists (ignoring case).
    /// The title is normalized first (<see cref="NormalizeTitle"/>), so <c>x.md</c> creates <c>x.md</c>.
    /// </summary>
    public NoteInfo Create(string title)
    {
        title = NormalizeTitle(title);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(title));
        }

        if (List().Any(n => string.Equals(n.Name, title, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NoteExistsException(title);
        }

        Directory.CreateDirectory(root);
        string path = Path.Combine(root, title + Extension);
        byte[] content = Encryption.Protect(encryption, Vault.KindNote, Utf8NoBom.GetBytes($"# {title}\n"));
        try
        {
            using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Created by someone else between the check and CreateNew.
            throw new NoteExistsException(title);
        }

        return new NoteInfo(title, File.GetLastWriteTime(path), path);
    }

    /// <summary>
    /// Every top-level <c>*.md</c> file in the root, sorted by name ignoring case. Other files and
    /// subfolders are left out. A missing root lists as empty and is not created.
    /// </summary>
    public IReadOnlyList<NoteInfo> List()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*" + Extension, SearchOption.TopDirectoryOnly)
            .Where(p => string.Equals(Path.GetExtension(p), Extension, StringComparison.OrdinalIgnoreCase))
            .Select(p => new NoteInfo(Path.GetFileNameWithoutExtension(p), File.GetLastWriteTime(p), p))
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Opens the note called <paramref name="name"/>. The trimmed name is first matched as typed, then
    /// with one trailing <c>.md</c> removed, so a legacy <c>x.md.md</c> file opens as <c>x.md</c>.
    /// Each step prefers an exact-case match over a case-insensitive one. The name is only matched
    /// against <see cref="List"/>, so it can never reach a file outside the root.
    /// </summary>
    public bool TryOpen(string name, out Note? note)
    {
        note = null;
        if (!TryFind(name, out NoteInfo? match) || match is null)
        {
            return false;
        }

        note = new Note(match.Name, Read(match));
        return true;
    }

    /// <summary>Finds the note called <paramref name="name"/> like <see cref="TryOpen"/>, without reading it.</summary>
    public bool TryFind(string name, out NoteInfo? match)
    {
        match = null;
        string wanted = name.Trim();
        if (wanted.Length == 0)
        {
            return false;
        }

        IReadOnlyList<NoteInfo> notes = List();
        match = Find(notes, wanted);
        if (match is null && wanted.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = wanted[..^Extension.Length];
            if (stripped.Length > 0)
            {
                match = Find(notes, stripped);
            }
        }

        return match is not null;
    }

    /// <summary>
    /// The text of <paramref name="note"/> (decoded like <see cref="File.ReadAllText(string)"/>), decrypted when
    /// the file is encrypted. Throws <see cref="IOException"/> (also <see cref="VaultException"/>) or
    /// <see cref="UnauthorizedAccessException"/> when it can't be read.
    /// </summary>
    public string Read(NoteInfo note) => Encryption.ReadText(encryption, note.Path, Vault.KindNote);

    /// <summary>
    /// Reads <paramref name="note"/> for editing: the text (decrypted when the file is encrypted) with <c>\r\n</c>
    /// turned into <c>\n</c> and the BOM dropped, plus the SHA-256 of the file's bytes and whether it had CRLF
    /// line endings or a BOM, so <see cref="SaveIfUnchanged"/> can write it back the same way. Throws like
    /// <see cref="Read"/> when the file can't be read, and with an <see cref="IOException"/> when it is not a regular
    /// file or is larger than <see cref="MaxEditBytes"/> (plus the encryption overhead).
    /// </summary>
    public EditableNote ReadForEdit(NoteInfo note)
    {
        byte[] raw = BoundedFile.Read(note.Path, MaxEditFileBytes);
        byte[] plain = Encryption.Unprotect(encryption, Vault.KindNote, raw);
        if (plain.Length > MaxEditBytes)
        {
            throw new IOException("Too large.");
        }

        bool hadBom = plain.AsSpan().StartsWith(Utf8Bom);
        string text = Encryption.DecodeText(plain);
        bool hadCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        return new EditableNote(note.Name, note.Path, text.Replace("\r\n", "\n", StringComparison.Ordinal), EditableNote.Hash(raw), hadCrlf, hadBom);
    }

    /// <summary>
    /// Saves <paramref name="text"/> over the note <paramref name="opened"/> came from, only when the file has not
    /// changed since. Writes nothing and returns <see cref="NoteSaveResult.Unchanged"/> when the text equals the text
    /// as opened, <see cref="NoteSaveResult.Missing"/> when the file is gone and <see cref="NoteSaveResult.ChangedOnDisk"/>
    /// when its bytes no longer hash to <see cref="EditableNote.Version"/>. Otherwise writes the text through a temp
    /// file and a move, with CRLF line endings and a BOM when the file had them, encrypted while encryption is on, and
    /// returns the note as saved in <paramref name="saved"/>. The file is hashed again just before the move. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't write.
    /// </summary>
    public NoteSaveResult SaveIfUnchanged(EditableNote opened, string text, out EditableNote? saved)
    {
        saved = null;
        string plainText = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.Equals(plainText, opened.Text, StringComparison.Ordinal))
        {
            return NoteSaveResult.Unchanged;
        }

        NoteSaveResult check = CheckVersion(opened);
        if (check != NoteSaveResult.Saved)
        {
            return check;
        }

        string onDisk = opened.HadCrlf ? plainText.Replace("\n", "\r\n", StringComparison.Ordinal) : plainText;
        byte[] body = Utf8NoBom.GetBytes(onDisk);
        byte[] plain = opened.HadBom ? [.. Utf8Bom, .. body] : body;
        if (plain.Length > MaxEditBytes)
        {
            return NoteSaveResult.TooLarge;
        }

        byte[] content = Encryption.Protect(encryption, Vault.KindNote, plain);
        string folder = Path.GetDirectoryName(opened.Path) ?? root;
        string temp = Path.Combine(folder, $".{opened.Name}{Extension}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, PrivateTempOptions()))
            {
                stream.Write(content);
            }

            check = CheckVersion(opened);
            if (check != NoteSaveResult.Saved)
            {
                return check;
            }

            File.Move(temp, opened.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        saved = new EditableNote(opened.Name, opened.Path, plainText, EditableNote.Hash(content), opened.HadCrlf, opened.HadBom);
        return NoteSaveResult.Saved;
    }

    // Saved when the file still hashes to the version it was opened with. A file that is now too large or not a
    // regular file can't have that version, so it counts as changed (read with the same bound as ReadForEdit).
    private static NoteSaveResult CheckVersion(EditableNote opened)
    {
        byte[] current;
        try
        {
            current = BoundedFile.Read(opened.Path, MaxEditFileBytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return NoteSaveResult.Missing;
        }
        catch (IOException)
        {
            return NoteSaveResult.ChangedOnDisk;
        }

        return string.Equals(EditableNote.Hash(current), opened.Version, StringComparison.Ordinal)
            ? NoteSaveResult.Saved
            : NoteSaveResult.ChangedOnDisk;
    }

    /// <summary>
    /// Stores <paramref name="content"/> as the note <paramref name="title"/> (normalized and validated like
    /// <see cref="Create"/>), encrypted while encryption is on. An existing note with that name (ignoring case)
    /// is replaced through a temp file and a move, keeping its file name; otherwise a new file is created.
    /// Returns the note and whether it replaced one. Throws <see cref="ArgumentException"/> for an invalid title.
    /// </summary>
    public (NoteInfo Note, bool Replaced) Save(string title, byte[] content)
    {
        title = NormalizeTitle(title);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(title));
        }

        NoteInfo? existing = Find(List(), title);
        Directory.CreateDirectory(root);
        string name = existing?.Name ?? title;
        string path = existing?.Path ?? Path.Combine(root, title + Extension);
        string temp = Path.Combine(root, $".{name}{Extension}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encryption.Protect(encryption, Vault.KindNote, content));
            }

            File.Move(temp, path, overwrite: existing is not null);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return (new NoteInfo(name, File.GetLastWriteTime(path), path), existing is not null);
    }

    /// <summary>
    /// Creates the note <paramref name="title"/> (normalized and validated like <see cref="Create"/>) with
    /// <paramref name="text"/> (UTF-8, no BOM, encrypted while encryption is on) through a private temp file in the
    /// root and a move that never overwrites, so the note appears whole or not at all. Throws
    /// <see cref="NoteExistsException"/> when a note with that name exists (ignoring case), also when it appeared
    /// between the check and the move, and <see cref="ArgumentException"/> for an invalid title.
    /// </summary>
    public NoteInfo CreateWithText(string title, string text)
    {
        title = NormalizeTitle(title);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(title));
        }

        if (Find(List(), title) is not null)
        {
            throw new NoteExistsException(title);
        }

        Directory.CreateDirectory(root);
        string path = Path.Combine(root, title + Extension);
        string temp = Path.Combine(root, $".{title}{Extension}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, PrivateTempOptions()))
            {
                stream.Write(Encryption.Protect(encryption, Vault.KindNote, Utf8NoBom.GetBytes(text)));
            }

            try
            {
                File.Move(temp, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                throw new NoteExistsException(title);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return new NoteInfo(title, File.GetLastWriteTime(path), path);
    }

    /// <summary>The largest file <see cref="ReadImport"/> accepts (5 MB).</summary>
    public const long MaxImportBytes = 5L * 1024 * 1024;

    /// <summary>
    /// The largest note <see cref="ReadForEdit"/> opens and <see cref="SaveIfUnchanged"/> writes, counted in the file's
    /// plain bytes (BOM and line endings included): 5 MB, as for imports.
    /// </summary>
    public const long MaxEditBytes = MaxImportBytes;

    // The same limit on the file as stored, which is larger by the fixed overhead when it is encrypted.
    private const long MaxEditFileBytes = MaxEditBytes + Vault.Overhead;

    // A new temp file only the owner can read and write (mode 0600 off Windows), as in Encryption.Migrate.
    private static FileStreamOptions PrivateTempOptions()
    {
        FileStreamOptions options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    /// <summary>
    /// True when <paramref name="path"/> is the root itself or anywhere inside it, after resolving symbolic links
    /// in the existing part of both paths and ignoring case, so a plain copy can't land in the data folder
    /// through a link or another spelling.
    /// </summary>
    public bool Contains(string path)
    {
        string wanted = RealPath(Path.GetFullPath(path));
        string folder = RealPath(root);
        string withSeparator = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return string.Equals(wanted, folder, StringComparison.OrdinalIgnoreCase)
            || wanted.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes <paramref name="text"/> (UTF-8, no BOM, never encrypted) to a new file at <paramref name="path"/>.
    /// Never overwrites (<see cref="FileMode.CreateNew"/>): throws <see cref="IOException"/> if it exists. The
    /// caller checks <see cref="Contains"/> first.
    /// </summary>
    public static void WritePlainCopy(string path, string text)
    {
        using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>
    /// Reads the file to import, at most <see cref="MaxImportBytes"/>, without changing it, through
    /// <see cref="BoundedFile"/>: a file that reports length 0 is returned empty without being opened (so a FIFO
    /// never blocks) and a stream that can't seek is refused. Throws <see cref="FileNotFoundException"/> when it is
    /// missing, <see cref="InvalidDataException"/> when it is too large or encrypted, and <see cref="IOException"/>
    /// (also when it is not a regular file) or <see cref="UnauthorizedAccessException"/> when it can't be read.
    /// </summary>
    public static byte[] ReadImport(string path)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("No such file.");
        }

        if (info.Length > MaxImportBytes)
        {
            throw new InvalidDataException("Too large.");
        }

        byte[] bytes = BoundedFile.Read(path, MaxImportBytes);
        if (Vault.IsEncrypted(bytes))
        {
            throw new InvalidDataException("Encrypted.");
        }

        return bytes;
    }

    // Resolves symbolic links in the part of the path that exists, from the top down; the rest is kept as is.
    private static string RealPath(string fullPath, int depth = 0)
    {
        string? rootPart = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(rootPart))
        {
            return fullPath;
        }

        string current = rootPart;
        string[] parts = fullPath[rootPart.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        bool resolving = true;
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            if (!resolving)
            {
                continue;
            }

            try
            {
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (!info.Exists)
                {
                    resolving = false;
                    continue;
                }

                FileSystemInfo? target = info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    string resolved = Path.GetFullPath(target.FullName);
                    // The target's own folders may be links too (for example /var on macOS).
                    current = depth < 8 ? RealPath(resolved, depth + 1) : resolved;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                resolving = false;
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static NoteInfo? Find(IReadOnlyList<NoteInfo> notes, string name) =>
        notes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal))
            ?? notes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
}
