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

    private readonly string root;

    public NoteStore(string root)
    {
        this.root = Path.GetFullPath(root);
    }

    /// <summary>The folder notes live in.</summary>
    public string Root => root;

    /// <summary>The <c>Context</c> folder in the user's app data folder.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Context");

    /// <summary>
    /// Checks a note title. Returns <c>null</c> if it is valid, otherwise a short plain-text reason
    /// (not markup). A valid title is non-blank, at most <see cref="MaxTitleLength"/> characters,
    /// does not start with <c>.</c>, and has no <c>/</c>, <c>\</c>, <c>:</c>, control character
    /// or <see cref="Path.GetInvalidFileNameChars"/> character, so it is always a single file name
    /// inside the root.
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

        return null;
    }

    /// <summary>
    /// Creates <c>&lt;root&gt;/&lt;title&gt;.md</c> containing <c># &lt;title&gt;\n</c> (UTF-8, no BOM),
    /// creating the root folder if needed. Never overwrites: throws <see cref="ArgumentException"/>
    /// for an invalid title and <see cref="NoteExistsException"/> if the note exists (ignoring case).
    /// </summary>
    public NoteInfo Create(string title)
    {
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
        byte[] content = Utf8NoBom.GetBytes($"# {title}\n");
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
    /// Opens the note called <paramref name="name"/> (ignoring case, with or without <c>.md</c>).
    /// The name is only matched against <see cref="List"/>, so it can never reach a file outside the
    /// root. An exact-case match wins over a case-insensitive one.
    /// </summary>
    public bool TryOpen(string name, out Note? note)
    {
        note = null;
        string wanted = name.Trim();
        if (wanted.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            wanted = wanted[..^Extension.Length];
        }

        if (wanted.Length == 0)
        {
            return false;
        }

        IReadOnlyList<NoteInfo> notes = List();
        NoteInfo? match = notes.FirstOrDefault(n => string.Equals(n.Name, wanted, StringComparison.Ordinal))
            ?? notes.FirstOrDefault(n => string.Equals(n.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        note = new Note(match.Name, File.ReadAllText(match.Path));
        return true;
    }
}
