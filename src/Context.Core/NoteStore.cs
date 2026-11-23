using System.Text;

namespace Context;

/// <summary>
/// A note file in the store: its name, last-write time and path. A top-level note's name is its file name without
/// <c>.md</c>; a subpage's name is its parent's name, <c>/</c> and its own title (<c>Meetings/Meeting 1</c>).
/// </summary>
internal sealed record NoteInfo
{
    public NoteInfo(string name, DateTime modified, string path)
    {
        Name = name;
        Modified = modified;
        Path = path;
        int slash = name.LastIndexOf('/');
        Title = slash < 0 ? name : name[(slash + 1)..];
        Parent = slash < 0 ? null : name[..slash];
    }

    /// <summary>The full name: the titles from the top level down, joined with <c>/</c> on every OS.</summary>
    public string Name { get; }

    /// <summary>The last segment of <see cref="Name"/>: the file name without <c>.md</c>.</summary>
    public string Title { get; }

    /// <summary>The parent's full name, or <c>null</c> for a top-level note.</summary>
    public string? Parent { get; }

    /// <summary>The file's last-write time, local time.</summary>
    public DateTime Modified { get; }

    /// <summary>The full path of the file: directly inside the store's root, or inside its parent's child folder.</summary>
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
/// Thrown by <see cref="NoteStore.Save"/> in create-only mode when a plain note with that name (ignoring case)
/// exists, also one that appeared between the check and the move. <see cref="Name"/> is that note's name.
/// </summary>
internal sealed class NoteTakenException : IOException
{
    public NoteTakenException(string name)
        : base($"Note {name} already exists.")
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Thrown by <see cref="NoteStore.ReadImport"/> when the source is empty or not a regular file (a FIFO, a device).
/// Not an <see cref="IOException"/>, so it is never taken for an ordinary read error. The message is fixed.
/// </summary>
internal sealed class SpecialImportFileException : Exception
{
    public SpecialImportFileException()
        : base("Empty or special file.")
    {
    }
}

/// <summary>
/// Markdown notes kept as <c>*.md</c> files in one root folder. A note's title is its file name without
/// <c>.md</c>. The subpages of <c>X.md</c> are the <c>*.md</c> files in the folder <c>X/</c> next to it, to any
/// depth. The files are the source of truth; nothing else is stored.
/// </summary>
internal sealed class NoteStore
{
    /// <summary>The longest title <see cref="Create"/> accepts.</summary>
    public const int MaxTitleLength = 100;

    /// <summary>The longest full name (<c>Parent/Title</c>) a subpage can have; a longer one is not listed.</summary>
    public const int MaxNameLength = 400;

    private const string Extension = ".md";

    /// <summary>The extension of a sensitive note's sealed file (<c>&lt;Title&gt;.ctxs</c>).</summary>
    public const string SensitiveExtension = ".ctxs";

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

    /// <param name="root">The folder notes live in.</param>
    public NoteStore(string root)
    {
        this.root = Path.GetFullPath(root);
        IgnoresCase = ProbeIgnoresCase;
        SameEntry = DefaultSameEntry;
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

        if (List().Any(n => string.Equals(n.Name, title, StringComparison.OrdinalIgnoreCase)) || IsSensitiveName(title))
        {
            throw new NoteExistsException(title);
        }

        PrivateFile.CreateFolder(root);
        string path = Path.Combine(root, title + Extension);
        byte[] content = Utf8NoBom.GetBytes($"# {title}\n");
        try
        {
            using FileStream stream = PrivateFile.OpenNew(path);
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
    /// Creates the subpage <paramref name="title"/> of the note <paramref name="parent"/> (found like
    /// <see cref="TryFind"/>, never used as a path): <c>&lt;parent folder&gt;/&lt;title&gt;.md</c> containing
    /// <c># &lt;title&gt;\n</c> (UTF-8, no BOM, <see cref="FileMode.CreateNew"/>), creating the parent's child folder
    /// if needed. The title is normalized and validated like <see cref="Create"/>, so a <c>/</c> is refused. Throws
    /// <see cref="NoteExistsException"/> when a sibling has that title (ignoring case) and
    /// <see cref="ArgumentException"/> for an invalid title, a full name over <see cref="MaxNameLength"/>, a parent
    /// below the top-level <c>calendar</c> note, or a missing parent.
    /// </summary>
    public NoteInfo CreateChild(string parent, string title) => CreateChild(parent, title, string.Empty);

    /// <summary>
    /// Like <see cref="CreateChild(string, string)"/>, with <paramref name="body"/> written right after the
    /// <c># &lt;title&gt;\n</c> line (an empty body gives the plain heading).
    /// </summary>
    public NoteInfo CreateChild(string parent, string title, string body)
    {
        title = NormalizeTitle(title);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(title));
        }

        List<Entry> entries = ListEntries();
        Entry owner = FindEntry(entries, parent) ?? throw new ArgumentException($"No note called {parent.Trim()}.", nameof(parent));
        CheckCanHoldSubpages(owner);
        string name = owner.Note.Name + "/" + title;
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException(NameTooLongText, nameof(title));
        }

        if (HasSibling(entries, owner.Note.Name, title, except: null))
        {
            throw new NoteExistsException(name);
        }

        string folder = ChildFolderFor(owner);
        PrivateFile.CreateFolder(folder);
        string path = Path.Combine(folder, title + Extension);
        byte[] content = Utf8NoBom.GetBytes($"# {title}\n" + body);
        try
        {
            using FileStream stream = PrivateFile.OpenNew(path);
            stream.Write(content);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Created by someone else between the check and CreateNew.
            throw new NoteExistsException(name);
        }

        return new NoteInfo(name, File.GetLastWriteTime(path), path);
    }

    /// <summary>
    /// Renames the note called <paramref name="name"/> (found like <see cref="TryFind"/>, never used as a path): only
    /// its last segment changes to <paramref name="newTitle"/>, normalized and validated like <see cref="Create"/>
    /// (throws <see cref="ArgumentException"/> with the <see cref="ValidateTitle"/> text); the parent stays. Throws
    /// <see cref="NoteExistsException"/> when a sibling has that title, ignoring case (at the top level also a
    /// sensitive note); a case-only rename of the same note is allowed. The file is moved with
    /// <c>File.Move(..., overwrite: false)</c>, so its bytes (including the <c># Title</c> line) stay as they were, and
    /// its child folder moves with it, never over another folder (when that fails the file is moved back). Throws
    /// <see cref="ArgumentException"/> when a subpage's new full name would be over <see cref="MaxNameLength"/>.
    /// Returns the renamed note, the note itself when the title is exactly its name (nothing is written), or
    /// <c>null</c> when <paramref name="name"/> names no note.
    /// </summary>
    public NoteInfo? Rename(string name, string newTitle)
    {
        string title = NormalizeTitle(newTitle);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(newTitle));
        }

        List<Entry> entries = ListEntries();
        Entry? source = FindEntry(entries, name);
        if (source is null)
        {
            return null;
        }

        if (string.Equals(source.Note.Title, title, StringComparison.Ordinal))
        {
            return source.Note;
        }

        string? parent = source.Note.Parent;
        string newName = parent is null ? title : parent + "/" + title;
        CheckSubtreeFits(entries, source, newName, parent);
        if (HasSibling(entries, parent, title, except: source) || (parent is null && IsSensitiveName(title)))
        {
            throw new NoteExistsException(newName);
        }

        string folder = Path.GetDirectoryName(source.Note.Path) ?? root;
        string target = Path.Combine(folder, title + Extension);
        bool caseOnly = string.Equals(source.Note.Title, title, StringComparison.OrdinalIgnoreCase);
        MoveTree(source, target, Path.Combine(folder, title), newName, caseOnly);
        return new NoteInfo(newName, File.GetLastWriteTime(target), target);
    }

    /// <summary>
    /// Moves the note called <paramref name="name"/> (found like <see cref="TryFind"/>) and its subpages under the note
    /// <paramref name="newParent"/> (also found like <see cref="TryFind"/>, never used as a path), or to the top level
    /// when that is null or blank. Neither the file nor the child folder overwrites anything; when moving the child
    /// folder fails, the file is moved back and the exception propagates. Throws <see cref="ArgumentException"/> for a
    /// missing parent, a move under the note itself or one of its subpages, a move below the top-level
    /// <c>calendar</c> note and a full name over <see cref="MaxNameLength"/>, and <see cref="NoteExistsException"/>
    /// when a note at the new place has the same title (ignoring case; at the top level also a sensitive note).
    /// Returns the moved note, the note itself when it is already there (nothing is written), or <c>null</c> when
    /// <paramref name="name"/> names no note.
    /// </summary>
    public NoteInfo? Move(string name, string? newParent)
    {
        List<Entry> entries = ListEntries();
        Entry? source = FindEntry(entries, name);
        if (source is null)
        {
            return null;
        }

        Entry? owner = null;
        if (!string.IsNullOrWhiteSpace(newParent))
        {
            owner = FindEntry(entries, newParent)
                ?? throw new ArgumentException($"No note called {newParent.Trim()}.", nameof(newParent));
            if (ReferenceEquals(owner, source) || IsBelow(owner.Note.Name, source.Note.Name))
            {
                throw new ArgumentException("A note can't move under itself or one of its subpages.", nameof(newParent));
            }

            CheckCanHoldSubpages(owner);
            string? error = ValidateTitle(source.Note.Title);
            if (error is not null)
            {
                throw new ArgumentException(error, nameof(name));
            }
        }

        string? parent = owner?.Note.Name;
        if (string.Equals(parent, source.Note.Parent, StringComparison.Ordinal))
        {
            return source.Note;
        }

        string title = source.Note.Title;
        string newName = parent is null ? title : parent + "/" + title;
        CheckSubtreeFits(entries, source, newName, parent);
        if (HasSibling(entries, parent, title, except: source) || (parent is null && IsSensitiveName(title)))
        {
            throw new NoteExistsException(newName);
        }

        string folder = owner is null ? root : ChildFolderFor(owner);
        bool created = !Directory.Exists(folder);
        PrivateFile.CreateFolder(folder);
        string target = Path.Combine(folder, title + Extension);
        try
        {
            MoveTree(source, target, Path.Combine(folder, title), newName, caseOnly: false);
        }
        catch (Exception) when (created)
        {
            RemoveIfEmpty(folder);
            throw;
        }

        return new NoteInfo(newName, File.GetLastWriteTime(target), target);
    }

    /// <summary>The number of notes below the note called <paramref name="name"/> (0 when it names no note).</summary>
    public int Descendants(string name)
    {
        List<Entry> entries = ListEntries();
        Entry? match = FindEntry(entries, name);
        return match is null ? 0 : entries.Count(e => IsBelow(e.Note.Name, match.Note.Name));
    }

    /// <summary>
    /// Deletes the note called <paramref name="name"/> (found like <see cref="TryFind"/>, so only a file
    /// <see cref="List"/> returns) and every subpage below it that <see cref="List"/> reports, then removes the child
    /// folders that are now empty (any other file keeps its folder), and returns true; or returns false and deletes
    /// nothing when no note matches. With <paramref name="removeHistory"/> the kept versions of every deleted note go
    /// too (<see cref="NoteHistory.RemoveHistory"/>, with a <c>.history</c> folder that is then empty); otherwise they
    /// stay, and so does a child folder that still holds a <c>.history</c>. With <paramref name="removeAttachments"/>
    /// the <c>.attachments</c> folder inside every deleted child folder goes too (only a real folder, never through a
    /// link), so an emptied child folder is removed; the attachments of the note's own level are left to
    /// <see cref="NoteAttachments.RemoveUnlinked"/>.
    /// </summary>
    public bool Delete(string name, bool removeHistory = false, bool removeAttachments = false)
    {
        List<Entry> entries = ListEntries();
        Entry? match = FindEntry(entries, name);
        if (match is null)
        {
            return false;
        }

        List<Entry> subtree = entries.Where(e => ReferenceEquals(e, match) || IsBelow(e.Note.Name, match.Note.Name)).ToList();
        foreach (Entry entry in subtree.OrderByDescending(e => e.Note.Name.Length))
        {
            if (removeHistory)
            {
                NoteHistory.Remove(entry.Note.Path, null);
            }

            File.Delete(entry.Note.Path);
        }

        foreach (string folder in subtree.Select(e => e.ChildFolder).OfType<string>().OrderByDescending(f => f.Length))
        {
            if (removeAttachments)
            {
                RemoveAttachmentsFolder(Path.Combine(folder, NoteAttachments.FolderName));
            }

            RemoveIfEmpty(folder);
        }

        return true;
    }

    /// <summary>
    /// The folder that holds the subpages of the note <paramref name="parent"/> (found like <see cref="TryFind"/>), or
    /// the root for null or blank: where a note moved there is stored. Throws <see cref="ArgumentException"/> when
    /// <paramref name="parent"/> names no note or can't have subpages (like <see cref="Move"/>).
    /// </summary>
    internal string LevelFolder(string? parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return root;
        }

        Entry owner = FindEntry(ListEntries(), parent)
            ?? throw new ArgumentException($"No note called {parent.Trim()}.", nameof(parent));
        CheckCanHoldSubpages(owner);
        return ChildFolderFor(owner);
    }

    /// <summary>
    /// Every note: the top-level <c>*.md</c> files in the root and, after each, its subpages, depth first. Siblings are
    /// sorted by title ignoring case, then exactly. The subpages of a note are the <c>*.md</c> files in the folder next
    /// to it whose name equals its title (exact case first, then ignoring case). A folder that is a link or reparse
    /// point, starts with <c>.</c>, is the top-level <c>calendar</c> folder or has no matching note is never read; a
    /// subpage whose title fails <see cref="ValidateTitle"/> or whose full name is over <see cref="MaxNameLength"/> is
    /// left out; at the top level a file whose name starts with <c>.</c> is left out too. A missing root lists as empty
    /// and is not created.
    /// </summary>
    public IReadOnlyList<NoteInfo> List() => ListEntries().Select(e => e.Note).ToList();

    /// <summary>
    /// Every sensitive note: the top-level <c>*.ctxs</c> files in the root, sorted like <see cref="List"/>. The name is
    /// the file name without <c>.ctxs</c>. The files are sealed by the desktop app; this class never reads their
    /// content. A missing root lists as empty and is not created.
    /// </summary>
    public IReadOnlyList<NoteInfo> ListSensitive()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Files(root, SensitiveExtension)
            .Where(p => string.Equals(Path.GetExtension(p), SensitiveExtension, StringComparison.OrdinalIgnoreCase))
            .Select(p => new NoteInfo(Path.GetFileNameWithoutExtension(p), File.GetLastWriteTime(p), p))
            .Where(n => n.Name.Length > 0)
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Finds the sensitive note called <paramref name="name"/> (trimmed): an exact match in
    /// <see cref="ListSensitive"/> first, then one ignoring case. The name is never turned into a path.
    /// </summary>
    public bool TryFindSensitive(string name, out NoteInfo? match)
    {
        match = null;
        string wanted = name.Trim();
        if (wanted.Length == 0)
        {
            return false;
        }

        match = Find(ListSensitive(), wanted);
        return match is not null;
    }

    /// <summary>
    /// Deletes the sensitive note <see cref="TryFindSensitive"/> finds for <paramref name="name"/> and returns true,
    /// or returns false and deletes nothing when there is none. Never deletes a <c>.md</c> file. With
    /// <paramref name="removeHistory"/> its kept versions (<c>.history/&lt;Title&gt;/</c>) go too; otherwise they stay.
    /// </summary>
    public bool DeleteSensitive(string name, bool removeHistory = false)
    {
        if (!TryFindSensitive(name, out NoteInfo? match) || match is null)
        {
            return false;
        }

        if (removeHistory)
        {
            NoteHistory.Remove(match.Path, null);
        }

        File.Delete(match.Path);
        return true;
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
        match = FindEntry(ListEntries(), name)?.Note;
        return match is not null;
    }

    /// <summary>
    /// The text of <paramref name="note"/> (decoded like <see cref="File.ReadAllText(string)"/>), read through
    /// <see cref="BoundedFile"/>. Throws <see cref="FileTooLargeException"/> when it is larger than
    /// <see cref="MaxNoteBytes"/>, and <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it
    /// can't be read. A FIFO or empty file reads as <c>""</c>.
    /// </summary>
    public string Read(NoteInfo note) => DecodeText(BoundedFile.Read(note.Path, MaxNoteBytes));

    /// <summary>Reads a previously listed note, checking its current file and location when opening it.</summary>
    public string ReadPlainFile(NoteInfo note) => DecodeText(BoundedFile.Read(note.Path, MaxNoteBytes,
        file => IsPlainFile(file) && Contains(file.FullName)));

    /// <summary>
    /// Reads <paramref name="note"/> for editing: the text with <c>\r\n</c>
    /// turned into <c>\n</c> and the BOM dropped, plus the SHA-256 of the file's bytes and whether it had CRLF
    /// line endings or a BOM, so <see cref="SaveIfUnchanged"/> can write it back the same way. Throws like
    /// <see cref="Read"/> when the file can't be read, and with an <see cref="IOException"/> when it is not a regular
    /// file or is larger than <see cref="MaxEditBytes"/>.
    /// </summary>
    public EditableNote ReadForEdit(NoteInfo note)
    {
        byte[] raw = BoundedFile.Read(note.Path, MaxEditBytes);
        bool hadBom = raw.AsSpan().StartsWith(Utf8Bom);
        string text = DecodeText(raw);
        (string plain, bool hadCrlf, IReadOnlyList<bool>? lineCrlf) = LineEndings.Read(text);
        return new EditableNote(note.Name, note.Path, plain, EditableNote.Hash(raw), hadCrlf, hadBom, lineCrlf);
    }

    /// <summary>
    /// Saves <paramref name="text"/> over the note <paramref name="opened"/> came from, only when the file has not
    /// changed since. Writes nothing and returns <see cref="NoteSaveResult.Unchanged"/> when the text equals the text
    /// as opened, <see cref="NoteSaveResult.Missing"/> when the file is gone and <see cref="NoteSaveResult.ChangedOnDisk"/>
    /// when its bytes no longer hash to <see cref="EditableNote.Version"/>. Otherwise writes the text through a temp
    /// file and a move, with the line endings of <see cref="LineEndings.Apply"/> and a BOM when the file had one, and
    /// returns the note as saved in <paramref name="saved"/>. The file is hashed again just before the move. Throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't write.
    /// </summary>
    public NoteSaveResult SaveIfUnchanged(EditableNote opened, string text, out EditableNote? saved) =>
        SaveIfUnchanged(opened, text, out saved, null);

    /// <summary>
    /// Like <see cref="SaveIfUnchanged(EditableNote, string, out EditableNote?)"/>; with <paramref name="history"/>, the
    /// file's previous bytes are kept as a version (<see cref="NoteHistory.Keep"/>) after the last version check and
    /// before the move, so a save that writes nothing keeps nothing. When that throws
    /// (<see cref="NoteHistoryException"/>) the note is not changed.
    /// </summary>
    public NoteSaveResult SaveIfUnchanged(EditableNote opened, string text, out EditableNote? saved, NoteHistory? history)
    {
        saved = null;
        string plainText = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.Equals(plainText, opened.Text, StringComparison.Ordinal))
        {
            return NoteSaveResult.Unchanged;
        }

        NoteSaveResult check = CheckVersion(opened, out _);
        if (check != NoteSaveResult.Saved)
        {
            return check;
        }

        string onDisk = LineEndings.Apply(opened, plainText);
        byte[] body = Utf8NoBom.GetBytes(onDisk);
        byte[] plain = opened.HadBom ? [.. Utf8Bom, .. body] : body;
        if (plain.Length > MaxEditBytes)
        {
            return NoteSaveResult.TooLarge;
        }

        string folder = Path.GetDirectoryName(opened.Path) ?? root;
        // Named after the last segment: a subpage's name holds a '/', its file name doesn't.
        string title = opened.Name[(opened.Name.LastIndexOf('/') + 1)..];
        string temp = TempPath(folder, title);
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(plain);
            }

            check = CheckVersion(opened, out byte[]? previous);
            if (check != NoteSaveResult.Saved)
            {
                return check;
            }

            if (history is not null && previous is not null)
            {
                history.Keep(opened.Path, previous);
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

        (_, bool savedCrlf, IReadOnlyList<bool>? savedLines) = LineEndings.Read(onDisk);
        saved = new EditableNote(opened.Name, opened.Path, plainText, EditableNote.Hash(plain), savedCrlf, opened.HadBom, savedLines);
        return NoteSaveResult.Saved;
    }

    // Saved when the file still hashes to the version it was opened with. A file that is now too large or not a
    // regular file can't have that version, so it counts as changed (read with the same bound as ReadForEdit).
    private static NoteSaveResult CheckVersion(EditableNote opened, out byte[]? current)
    {
        current = null;
        try
        {
            current = BoundedFile.Read(opened.Path, MaxEditBytes);
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
    /// <see cref="Create"/>). An existing note with that name (ignoring case)
    /// is replaced through a temp file and a move, keeping its file name; otherwise a new file is created.
    /// Returns the note and whether it replaced one. Throws <see cref="ArgumentException"/> for an invalid title and
    /// <see cref="NoteExistsException"/> when a sensitive note has that name, also one that appeared just before the
    /// move. With <paramref name="createOnly"/> an existing plain note is never replaced: the move never overwrites
    /// and <see cref="NoteTakenException"/> (with that note's name) is thrown, also when the note appeared between
    /// the check and the move; nothing changes then.
    /// </summary>
    public (NoteInfo Note, bool Replaced) Save(string title, byte[] content, bool createOnly = false)
    {
        title = NormalizeTitle(title);
        string? error = ValidateTitle(title);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(title));
        }

        if (IsSensitiveName(title))
        {
            throw new NoteExistsException(title);
        }

        NoteInfo? existing = Find(List(), title);
        if (createOnly && existing is not null)
        {
            throw new NoteTakenException(existing.Name);
        }

        PrivateFile.CreateFolder(root);
        string name = existing?.Name ?? title;
        string path = existing?.Path ?? Path.Combine(root, title + Extension);
        string temp = TempPath(root, name);
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(content);
            }

            BeforeSaveMove?.Invoke();
            if (IsSensitiveName(title))
            {
                // A sensitive note with that name appeared in between: the name is taken for plain notes.
                throw new NoteExistsException(title);
            }

            try
            {
                File.Move(temp, path, overwrite: existing is not null);
            }
            // File.Move(overwrite: false) fails when the target exists: a note that appeared after the check. In
            // create-only mode that is a taken name; otherwise the exception is rethrown as before.
            catch (IOException) when (existing is null && File.Exists(path))
            {
                if (!createOnly)
                {
                    throw;
                }

                throw new NoteTakenException(Find(List(), title)?.Name ?? title);
            }
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
    /// <paramref name="text"/> (UTF-8, no BOM) through a private temp file in the
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

        if (Find(List(), title) is not null || IsSensitiveName(title))
        {
            throw new NoteExistsException(title);
        }

        PrivateFile.CreateFolder(root);
        string path = Path.Combine(root, title + Extension);
        string temp = TempPath(root, title);
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(Utf8NoBom.GetBytes(text));
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

    /// <summary>The one limit for reading a note in full (<see cref="Read"/>, the index, the CLI): 5 MB.</summary>
    public const long MaxNoteBytes = MaxEditBytes;

    // Decodes text the way File.ReadAllText does: UTF-8 unless a byte order mark says otherwise; the mark is dropped.
    internal static string DecodeText(byte[] bytes)
    {
        using MemoryStream stream = new MemoryStream(bytes, writable: false);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// True when <paramref name="path"/> is the root itself or anywhere inside it, after resolving symbolic links
    /// in the existing part of both paths, so a plain copy can't land in the data folder through a link or another
    /// spelling. Paths are compared ordinally; a match that only holds when case is ignored counts when the root's
    /// file system ignores case (<see cref="IgnoresCase"/>).
    /// </summary>
    public bool Contains(string path)
    {
        string wanted = RealPath(Path.GetFullPath(path));
        string folder = RealPath(root);
        if (IsAtOrInside(wanted, folder, StringComparison.Ordinal))
        {
            return true;
        }

        return IsAtOrInside(wanted, folder, StringComparison.OrdinalIgnoreCase) && IgnoresCase(folder);
    }

    private static bool IsAtOrInside(string wanted, string folder, StringComparison comparison)
    {
        string withSeparator = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return string.Equals(wanted, folder, comparison) || wanted.StartsWith(withSeparator, comparison);
    }

    /// <summary>
    /// Test seam: true when the file system holding the given path ignores case. By default it probes the deepest
    /// existing folder of the path (<see cref="ProbeIgnoresCase"/>); any doubt answers true.
    /// <see cref="Contains"/> guards in both directions: for export and plain copies (and import) true refuses, but in
    /// <c>NoteAttachments</c> and <c>NoteHistory</c> true allows a write or delete. The fail-safe true is the
    /// refusing answer only for the first kind, so changing it for one caller changes it for the other.
    /// </summary>
    internal Func<string, bool> IgnoresCase { get; set; }

    /// <summary>
    /// Test seam: true when the two spellings (the second with one letter's case flipped) name the same entry.
    /// Used by the default <see cref="IgnoresCase"/>.
    /// </summary>
    internal Func<string, string, bool> SameEntry { get; set; }

    // Flips the case of one ASCII letter in the deepest existing folder of the path (or the nearest folder above it
    // with a letter) and asks SameEntry whether both spellings name one entry. When it can't tell (no letter to flip,
    // nothing exists, an IO or access error) it answers true, the more refusing answer for Contains.
    private bool ProbeIgnoresCase(string path)
    {
        try
        {
            string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            while (current is not null && !Directory.Exists(current))
            {
                current = Path.GetDirectoryName(current);
            }

            while (current is not null)
            {
                string name = Path.GetFileName(current);
                int at = name.AsSpan().IndexOfAny(AsciiLetters);
                string? folderAbove = Path.GetDirectoryName(current);
                if (at >= 0 && folderAbove is not null)
                {
                    char letter = name[at];
                    char flippedLetter = char.IsAsciiLetterUpper(letter) ? char.ToLowerInvariant(letter) : char.ToUpperInvariant(letter);
                    string flipped = Path.Combine(folderAbove, string.Concat(name.AsSpan(0, at), [flippedLetter], name.AsSpan(at + 1)));
                    return SameEntry(current, flipped);
                }

                current = folderAbove;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static readonly System.Buffers.SearchValues<char> AsciiLetters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    // The flipped spelling names the same folder when it exists, resolves to the same real path (ignoring case) and
    // the folder above does not list both spellings as separate entries.
    private static bool DefaultSameEntry(string original, string flipped)
    {
        if (!Directory.Exists(flipped))
        {
            return false;
        }

        if (!string.Equals(RealPath(original), RealPath(flipped), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? folderAbove = Path.GetDirectoryName(original);
        if (folderAbove is null)
        {
            return true;
        }

        string originalName = Path.GetFileName(original);
        string flippedName = Path.GetFileName(flipped);
        bool sawOriginal = false;
        bool sawFlipped = false;
        foreach (string entry in Directory.EnumerateFileSystemEntries(folderAbove))
        {
            string entryName = Path.GetFileName(entry);
            sawOriginal |= string.Equals(entryName, originalName, StringComparison.Ordinal);
            sawFlipped |= string.Equals(entryName, flippedName, StringComparison.Ordinal);
        }

        return !(sawOriginal && sawFlipped);
    }

    /// <summary>The longest UTF-8 prefix of a title a temp file name keeps.</summary>
    internal const int MaxTempPrefixBytes = 64;

    /// <summary>
    /// A new temp file path in <paramref name="folder"/>: <c>.{prefix}.md.{guid}.tmp</c>, where the prefix is
    /// <paramref name="title"/> cut to at most <see cref="MaxTempPrefixBytes"/> UTF-8 bytes on a whole-rune
    /// boundary, so the name stays far below the 255-byte file name limit.
    /// </summary>
    internal static string TempPath(string folder, string title)
    {
        StringBuilder prefix = new StringBuilder(title.Length);
        int bytes = 0;
        foreach (Rune rune in title.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > MaxTempPrefixBytes)
            {
                break;
            }

            prefix.Append(rune.ToString());
        }

        return Path.Combine(folder, $".{prefix}{Extension}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>
    /// Writes <paramref name="text"/> (UTF-8, no BOM) to a new file at <paramref name="path"/>.
    /// Never overwrites (<see cref="FileMode.CreateNew"/>): throws <see cref="IOException"/> if it exists. The
    /// caller checks <see cref="Contains"/> first.
    /// </summary>
    public static void WritePlainCopy(string path, string text)
    {
        using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>
    /// True when <paramref name="path"/> may be imported by name: its file name ends in <c>.md</c>, ignoring case
    /// (<c>a.MD</c> is accepted, <c>notes.md.txt</c> is not). Both front ends check it before reading the file.
    /// </summary>
    public static bool IsImportSource(string path) =>
        Path.GetFileName(path).EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the file to import, at most <see cref="MaxImportBytes"/>, without changing it, through
    /// <see cref="BoundedFile"/>. A file that reports length 0 (an empty file, a FIFO, a device such as
    /// <c>/dev/null</c>) is never opened, so a FIFO never blocks: it throws <see cref="SpecialImportFileException"/>,
    /// as does a file that turns out empty when read. Throws <see cref="FileNotFoundException"/> when it is missing or
    /// a directory, <see cref="InvalidDataException"/> when it is too large, and <see cref="IOException"/> (also when
    /// it can't seek) or <see cref="UnauthorizedAccessException"/> when it can't be read.
    /// </summary>
    public static byte[] ReadImport(string path)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("No such file.");
        }

        if (info.Length == 0)
        {
            throw new SpecialImportFileException();
        }

        if (info.Length > MaxImportBytes)
        {
            throw new InvalidDataException("Too large.");
        }

        byte[] content = BoundedFile.Read(path, MaxImportBytes);
        if (content.Length == 0)
        {
            throw new SpecialImportFileException();
        }

        return content;
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

    // True when a sensitive note has this name, ignoring case: the name is taken for plain notes.
    private bool IsSensitiveName(string title) =>
        ListSensitive().Any(n => string.Equals(n.Name, title, StringComparison.OrdinalIgnoreCase));

    private const string NameTooLongText = "A note's full name can be at most 400 characters.";

    /// <summary>Test hook: runs just before a child folder is moved, so a test can make that move fail.</summary>
    internal Action? BeforeFolderMove { get; set; }

    /// <summary>Test hook: runs in <see cref="Save"/> just before the temp file is moved, so a test can make a note appear.</summary>
    internal Action? BeforeSaveMove { get; set; }

    // Every listed note with the folder holding its subpages (null when there is none).
    private List<Entry> ListEntries()
    {
        List<Entry> entries = [];
        if (Directory.Exists(root))
        {
            AddLevel(root, null, entries);
        }

        return entries;
    }

    // Adds the notes in one folder, each followed by its subpages. Only the top level throws when it can't be read;
    // a subfolder that can't be read lists as empty.
    private static void AddLevel(string folder, string? parent, List<Entry> entries)
    {
        List<string> files;
        if (parent is null)
        {
            files = Files(folder, Extension);
        }
        else
        {
            try
            {
                files = Files(folder, Extension);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }

        List<(string Title, string Path)> notes = files
            .Where(p => string.Equals(Path.GetExtension(p), Extension, StringComparison.OrdinalIgnoreCase))
            .Select(p => (Title: Path.GetFileNameWithoutExtension(p), Path: p))
            .Where(n => parent is null
                ? !n.Title.StartsWith('.')
                : (ValidateTitle(n.Title) is null && parent.Length + 1 + n.Title.Length <= MaxNameLength))
            .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Title, StringComparer.Ordinal)
            .ToList();
        if (notes.Count == 0)
        {
            return;
        }

        List<DirectoryInfo> folders = SubFolders(folder, top: parent is null);
        DirectoryInfo?[] childFolders = new DirectoryInfo?[notes.Count];
        HashSet<string> claimed = new(StringComparer.Ordinal);
        for (int i = 0; i < notes.Count; i++)
        {
            DirectoryInfo? exact = folders.FirstOrDefault(d => string.Equals(d.Name, notes[i].Title, StringComparison.Ordinal));
            if (exact is not null)
            {
                childFolders[i] = exact;
                claimed.Add(exact.Name);
            }
        }

        for (int i = 0; i < notes.Count; i++)
        {
            if (childFolders[i] is null)
            {
                DirectoryInfo? other = folders.FirstOrDefault(d => !claimed.Contains(d.Name)
                    && string.Equals(d.Name, notes[i].Title, StringComparison.OrdinalIgnoreCase));
                if (other is not null)
                {
                    childFolders[i] = other;
                    claimed.Add(other.Name);
                }
            }
        }

        for (int i = 0; i < notes.Count; i++)
        {
            string name = parent is null ? notes[i].Title : parent + "/" + notes[i].Title;
            NoteInfo note = new NoteInfo(name, File.GetLastWriteTime(notes[i].Path), notes[i].Path);
            entries.Add(new Entry(note, childFolders[i]?.FullName));
            if (childFolders[i] is DirectoryInfo child)
            {
                AddLevel(child.FullName, name, entries);
            }
        }
    }

    private static readonly EnumerationOptions NoteFileOptions = new EnumerationOptions
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    // The paths of the files in a folder whose name ends in the extension in any case, on every OS. Links (symbolic
    // links, junctions, other reparse points), folders and devices are left out, so a note never reads a file from
    // outside the root through a link. Throws when the folder can't be read. The "*" + extension pattern matches
    // ignoring case through NoteFileOptions; callers still check the exact extension (Path.GetExtension, ignoring case).
    private static List<string> Files(string folder, string extension) =>
        new DirectoryInfo(folder).EnumerateFiles("*" + extension, NoteFileOptions)
            .Where(IsPlainFile)
            .Select(f => f.FullName)
            .ToList();

    private static bool IsPlainFile(FileInfo file) => file.Exists && !IsLink(file)
        && (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) == 0;

    // The folders in a folder that can hold subpages: no link or reparse point, no name starting with '.', and at the
    // top level not the calendar folder. Sorted exactly by name so the case-insensitive match is the same every time.
    private static List<DirectoryInfo> SubFolders(string folder, bool top)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.') && !IsLink(d)
                    && !(top && string.Equals(d.Name, CalendarStore.FolderName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // True for a symbolic link, a junction or any other reparse point; one that can't be checked counts as a link.
    internal static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Finds a typed name like TryFind: as typed, then without one trailing .md; exact case first, then ignoring case.
    private static Entry? FindEntry(List<Entry> entries, string name)
    {
        string wanted = name.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        Entry? match = FindExact(entries, wanted);
        if (match is null && wanted.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = wanted[..^Extension.Length];
            if (stripped.Length > 0)
            {
                match = FindExact(entries, stripped);
            }
        }

        return match;
    }

    private static Entry? FindExact(List<Entry> entries, string name) =>
        entries.FirstOrDefault(e => string.Equals(e.Note.Name, name, StringComparison.Ordinal))
            ?? entries.FirstOrDefault(e => string.Equals(e.Note.Name, name, StringComparison.OrdinalIgnoreCase));

    // True when name is a subpage of ancestor at any depth.
    private static bool IsBelow(string name, string ancestor) =>
        name.Length > ancestor.Length + 1 && name.StartsWith(ancestor + "/", StringComparison.Ordinal);

    // True when another note with the same parent has this title, ignoring case.
    private static bool HasSibling(List<Entry> entries, string? parent, string title, Entry? except) =>
        entries.Any(e => !ReferenceEquals(e, except)
            && string.Equals(e.Note.Parent, parent, StringComparison.Ordinal)
            && string.Equals(e.Note.Title, title, StringComparison.OrdinalIgnoreCase));

    // A note below the top-level calendar note, or whose title would make a hidden or invalid folder, can't hold subpages.
    private static void CheckCanHoldSubpages(Entry owner)
    {
        string top = owner.Note.Parent is null ? owner.Note.Name : owner.Note.Name[..owner.Note.Name.IndexOf('/')];
        if (string.Equals(top, CalendarStore.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Notes in calendar can't have subpages.");
        }

        if (ValidateTitle(owner.Note.Title) is not null)
        {
            throw new ArgumentException($"{owner.Note.Name} can't have subpages.");
        }
    }

    // Refuses a new full name over MaxNameLength for the note or any of its subpages (they would no longer be listed),
    // and a top-level calendar note with subpages (its folder would be the calendar folder).
    private static void CheckSubtreeFits(List<Entry> entries, Entry source, string newName, string? parent)
    {
        int longest = entries.Where(e => IsBelow(e.Note.Name, source.Note.Name))
            .Select(e => e.Note.Name.Length - source.Note.Name.Length)
            .DefaultIfEmpty(0)
            .Max();
        if ((parent is not null || longest > 0) && newName.Length + longest > MaxNameLength)
        {
            throw new ArgumentException(NameTooLongText);
        }

        if (parent is null && source.ChildFolder is not null
            && string.Equals(newName, CalendarStore.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A note called calendar can't have subpages.");
        }
    }

    // The folder that holds (or will hold) a note's subpages. Refuses a place that exists but can't be one (a file,
    // a link), so nothing is ever written through a link.
    private static string ChildFolderFor(Entry owner)
    {
        if (owner.ChildFolder is not null)
        {
            return owner.ChildFolder;
        }

        string folder = Path.Combine(Path.GetDirectoryName(owner.Note.Path) ?? string.Empty, owner.Note.Title);
        if (File.Exists(folder) || Directory.Exists(folder) || new DirectoryInfo(folder).LinkTarget is not null)
        {
            throw new IOException($"{owner.Note.Name} can't have subpages.");
        }

        return folder;
    }

    // Moves a note's history, then its file, then its child folder, never over anything. When a later step fails the
    // earlier ones are moved back.
    private void MoveTree(Entry source, string target, string folderTarget, string newName, bool caseOnly)
    {
        if (!NoteHistory.MoveHistory(source.Note.Path, target, caseOnly))
        {
            throw new IOException($"{newName} already has a history.");
        }

        try
        {
            MoveFileAndFolder(source, target, folderTarget, newName, caseOnly);
        }
        catch (Exception)
        {
            try
            {
                NoteHistory.MoveHistory(target, source.Note.Path, caseOnly);
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
                // The history stays under the new name; the original error is the one to report.
            }

            throw;
        }
    }

    // Moves a note's file, then its child folder, never over anything. When the folder move fails the file goes back.
    private void MoveFileAndFolder(Entry source, string target, string folderTarget, string newName, bool caseOnly)
    {
        try
        {
            File.Move(source.Note.Path, target, overwrite: false);
        }
        catch (IOException) when (!caseOnly && File.Exists(target))
        {
            // Created by someone else between the check and the move.
            throw new NoteExistsException(newName);
        }

        if (source.ChildFolder is null)
        {
            return;
        }

        try
        {
            BeforeFolderMove?.Invoke();
            if (!caseOnly && (Directory.Exists(folderTarget) || File.Exists(folderTarget)))
            {
                throw new IOException($"{newName} already has a folder.");
            }

            Directory.Move(source.ChildFolder, folderTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(target, source.Note.Path, overwrite: false);
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
                // The note stays under its new name; the original error is the one to report.
            }

            throw;
        }
    }

    // Removes a child folder's .attachments with everything in it, only when it is a real folder (not a link or
    // reparse point); a file inside is deleted, never followed. Never throws: what can't be removed stays.
    private static void RemoveAttachmentsFolder(string folder)
    {
        try
        {
            DirectoryInfo info = new DirectoryInfo(folder);
            if (info.Exists && !IsLink(info))
            {
                info.Delete(recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place.
        }
    }

    // Removes a folder only when it is empty; never follows a link and never throws.
    internal static void RemoveIfEmpty(string folder)
    {
        try
        {
            DirectoryInfo info = new DirectoryInfo(folder);
            if (info.Exists && !IsLink(info) && !info.EnumerateFileSystemInfos().Any())
            {
                info.Delete(recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place.
        }
    }

    // A listed note and the folder holding its subpages.
    private sealed class Entry
    {
        public Entry(NoteInfo note, string? childFolder)
        {
            Note = note;
            ChildFolder = childFolder;
        }

        public NoteInfo Note { get; }

        public string? ChildFolder { get; }
    }

    private static NoteInfo? Find(IReadOnlyList<NoteInfo> notes, string name) =>
        notes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal))
            ?? notes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
}
