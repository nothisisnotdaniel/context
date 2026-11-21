using System.Text;

namespace Context;

/// <summary>The file types an attachment can have, decided from its bytes (and matched against its extension).</summary>
internal enum AttachmentType
{
    /// <summary>Anything else, including SVG, HTML, a renamed file and bytes whose extension doesn't match.</summary>
    Unsupported,
    Png,
    Jpeg,
    Gif,
    WebP,
    Pdf,

    /// <summary>An Excalidraw drawing (job 384): a <c>.excalidraw</c> JSON object whose top-level <c>type</c> is <c>"excalidraw"</c>.</summary>
    Excalidraw,
}

/// <summary>How <see cref="NoteAttachments.Add"/> ended.</summary>
internal enum AttachmentAddKind
{
    /// <summary>The file was stored; nothing was overwritten.</summary>
    Added,

    /// <summary>Over <see cref="NoteAttachments.MaxBytes"/>; nothing was written.</summary>
    TooLarge,

    /// <summary>Not a PNG, JPEG, GIF, WebP or PDF file with a matching extension; nothing was written.</summary>
    Unsupported,

    /// <summary>The note is not in <see cref="NoteStore.List"/>; nothing was written.</summary>
    NoNote,

    /// <summary>The note's <c>.attachments</c> is a link, a reparse point or not a folder; nothing was written.</summary>
    Unsafe,
}

/// <summary>The answer of <see cref="NoteAttachments.Add"/>: the stored name and its Markdown link when added.</summary>
internal sealed record AttachmentAdd
{
    public AttachmentAdd(AttachmentAddKind kind, AttachmentType type, string? name, string? markdown)
    {
        Kind = kind;
        Type = type;
        Name = name;
        Markdown = markdown;
    }

    public AttachmentAddKind Kind { get; }

    public AttachmentType Type { get; }

    /// <summary>The stored file name (<c>&lt;stem&gt;.&lt;ext&gt;</c>), only when added.</summary>
    public string? Name { get; }

    /// <summary><c>.attachments/&lt;name&gt;</c>, only when added.</summary>
    public string? Src => Name is null ? null : NoteAttachments.FolderName + "/" + Name;

    /// <summary><c>![name](.attachments/name)</c> for an image or drawing, <c>[name](.attachments/name)</c> for a PDF; only when added.</summary>
    public string? Markdown { get; }
}

/// <summary>A move of a note with its attachments failed and was undone (the cause is the inner exception).</summary>
internal sealed class AttachmentMoveException : IOException
{
    public AttachmentMoveException(Exception inner)
        : base("The attachments couldn't be moved.", inner)
    {
    }
}

/// <summary>A checked attachment file: its full path, type, MIME type and bytes.</summary>
internal sealed record ResolvedAttachment
{
    public ResolvedAttachment(string path, AttachmentType type, byte[] bytes)
    {
        Path = path;
        Type = type;
        Bytes = bytes;
    }

    public string Path { get; }

    public AttachmentType Type { get; }

    public string Mime => NoteAttachments.MimeOf(Type);

    public bool IsImage => NoteAttachments.IsImage(Type);

    public byte[] Bytes { get; }
}

/// <summary>
/// The files dropped onto a plain note, kept next to it in the <c>.attachments</c> folder of the note's own level
/// (<c>Work/Projects/.attachments/</c> for <c>Work/Projects/Plan</c>, <c>&lt;root&gt;/.attachments/</c> for a top-level
/// note). The type always comes from the bytes and must match the extension; only PNG, JPEG, GIF, WebP and PDF are kept.
/// A stored name is made only by <see cref="CleanName"/> and the clash rule of <see cref="Add"/>, and a link is opened
/// only through <see cref="TryResolve"/>, which accepts exactly the names those can make. Never touches a note file or
/// <c>context.db</c>; <see cref="NoteStore.List"/> never lists a folder starting with <c>.</c>.
/// </summary>
internal sealed class NoteAttachments
{
    /// <summary>The folder name, inside a note's level.</summary>
    public const string FolderName = ".attachments";

    /// <summary>The start of every attachment link source.</summary>
    public const string SrcPrefix = FolderName + "/";

    /// <summary>What precedes an attachment source in Markdown.</summary>
    public const string LinkMarker = "](" + SrcPrefix;

    /// <summary>The largest attachment (10 MB).</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>The most files one drop attaches.</summary>
    public const int MaxFilesPerDrop = 10;

    /// <summary>The most images a note shows inline.</summary>
    public const int MaxInlineImages = 20;

    /// <summary>The longest stem of a stored name (the part before its one <c>.</c>).</summary>
    public const int MaxStemLength = 100;

    /// <summary>The longest extension a stored file can have (<c>excalidraw</c>, job 384).</summary>
    public const int MaxExtensionLength = 10;

    /// <summary>The most drawings a note shows inline (job 384), on top of <see cref="MaxInlineImages"/>.</summary>
    public const int MaxInlineDrawings = 20;

    /// <summary>The extension of an Excalidraw drawing.</summary>
    public const string DrawingExtension = "excalidraw";

    /// <summary>The longest name a link scan looks at: stem, <c>.</c> and extension.</summary>
    public const int MaxScannedName = MaxStemLength + 1 + MaxExtensionLength;

    /// <summary>
    /// The most distinct linked names a move or delete acts on; a note that links more keeps every attachment (a move
    /// carries none, a delete removes none), so a crafted note can't make the sibling check run for hours.
    /// </summary>
    public const int MaxLinkedNames = 100;

    /// <summary>The highest clash suffix tried (<c>-2</c> to <c>-99</c>).</summary>
    public const int MaxClash = 99;

    /// <summary>The stem used when the cleaned stem is empty or a name <see cref="NoteStore.ValidateTitle"/> refuses.</summary>
    public const string FallbackStem = "attachment";

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif87Magic = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89Magic = "GIF89a"u8.ToArray();
    private static readonly byte[] RiffMagic = "RIFF"u8.ToArray();
    private static readonly byte[] WebPMagic = "WEBP"u8.ToArray();
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly NoteStore notes;

    public NoteAttachments(NoteStore notes)
    {
        this.notes = notes;
    }

    /// <summary>
    /// Test hook of <see cref="MoveWithNote"/>: runs before each step (<c>copy</c> for each file, <c>move</c>,
    /// <c>rewrite</c>, <c>cleanup</c>) and may throw.
    /// </summary>
    internal Action<string>? BeforeMoveStep { get; set; }

    /// <summary>The <c>.attachments</c> folder of <paramref name="note"/>'s level: next to the note's file.</summary>
    public static string Folder(NoteInfo note) =>
        Path.Combine(Path.GetDirectoryName(note.Path) ?? string.Empty, FolderName);

    /// <summary>
    /// The stored name for a dropped file's <paramref name="fileName"/>: the last path segment (split at <c>/</c> and
    /// <c>\</c>); the extension is the text after its last <c>.</c>, lower-cased, letters and digits only; in the stem
    /// (the text before that <c>.</c>) every character outside <c>[A-Za-z0-9_-]</c> becomes <c>-</c>, runs of <c>-</c>
    /// collapse, a leading <c>-</c> goes, the stem is cut to <see cref="MaxStemLength"/> and a trailing <c>-</c> goes
    /// after the cut. An empty stem or one <see cref="NoteStore.ValidateTitle"/> refuses becomes
    /// <see cref="FallbackStem"/>. A name without a usable extension becomes its cleaned whole segment, with no <c>.</c>.
    /// </summary>
    public static string CleanName(string fileName)
    {
        int slash = fileName.LastIndexOfAny(['/', '\\']);
        string segment = fileName[(slash + 1)..];
        int dot = segment.LastIndexOf('.');
        string extension = dot < 0 ? string.Empty : AsciiLettersAndDigits(segment[(dot + 1)..]);
        if (extension.Length == 0)
        {
            return CleanStem(segment);
        }

        return CleanStem(segment[..dot]) + "." + extension;
    }

    /// <summary>
    /// The type of <paramref name="bytes"/> when its magic bytes are PNG, JPEG, GIF, WebP or PDF and the extension of
    /// <paramref name="name"/> (after its last <c>.</c>, ignoring case) is that type's; for the extension
    /// <c>excalidraw</c>, <see cref="AttachmentType.Excalidraw"/> when the bytes are one UTF-8 JSON object (a BOM
    /// allowed) whose top-level <c>type</c> is the string <c>"excalidraw"</c>; else
    /// <see cref="AttachmentType.Unsupported"/>.
    /// </summary>
    public static AttachmentType Sniff(string name, ReadOnlySpan<byte> bytes)
    {
        int dot = name.LastIndexOf('.');
        string extension = dot < 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
        if (extension == DrawingExtension)
        {
            return IsExcalidrawJson(bytes) ? AttachmentType.Excalidraw : AttachmentType.Unsupported;
        }

        AttachmentType type = FromBytes(bytes);
        bool matches = type switch
        {
            AttachmentType.Png => extension == "png",
            AttachmentType.Jpeg => extension is "jpg" or "jpeg",
            AttachmentType.Gif => extension == "gif",
            AttachmentType.WebP => extension == "webp",
            AttachmentType.Pdf => extension == "pdf",
            _ => false,
        };
        return matches ? type : AttachmentType.Unsupported;
    }

    /// <summary>True for PNG, JPEG, GIF and WebP.</summary>
    public static bool IsImage(AttachmentType type) =>
        type is AttachmentType.Png or AttachmentType.Jpeg or AttachmentType.Gif or AttachmentType.WebP;

    /// <summary>The MIME type of <paramref name="type"/>; <c>application/octet-stream</c> for an unsupported one.</summary>
    public static string MimeOf(AttachmentType type) => type switch
    {
        AttachmentType.Png => "image/png",
        AttachmentType.Jpeg => "image/jpeg",
        AttachmentType.Gif => "image/gif",
        AttachmentType.WebP => "image/webp",
        AttachmentType.Pdf => "application/pdf",
        AttachmentType.Excalidraw => "application/json",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// True when <paramref name="name"/> has the shape <see cref="CleanName"/> and the clash rule make:
    /// <c>&lt;stem&gt;.&lt;ext&gt;</c>, the stem 1 to <see cref="MaxStemLength"/> characters of <c>[A-Za-z0-9_-]</c>
    /// with no leading or trailing <c>-</c> and no <c>--</c>, accepted by <see cref="NoteStore.ValidateTitle"/>, and
    /// the extension lower-case <c>[a-z0-9]+</c>.
    /// </summary>
    public static bool IsStoredName(string name)
    {
        int dot = name.IndexOf('.');
        if (dot <= 0 || dot != name.LastIndexOf('.') || dot == name.Length - 1)
        {
            return false;
        }

        string stem = name[..dot];
        string extension = name[(dot + 1)..];
        if (stem.Length > MaxStemLength || stem[0] == '-' || stem[^1] == '-'
            || stem.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in stem)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        foreach (char c in extension)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                return false;
            }
        }

        return NoteStore.ValidateTitle(stem) is null;
    }

    /// <summary>
    /// Stores <paramref name="bytes"/> as <c>&lt;note folder&gt;/.attachments/&lt;clean name&gt;</c>, creating the folder:
    /// a temp file in that folder, then a move that never overwrites. On a clash it tries <c>&lt;stem&gt;-2</c> to
    /// <c>-99</c>, each time cutting the stem to <c>100 - len("-N")</c> and trimming a trailing <c>-</c> before adding
    /// the suffix. Refuses, writing nothing, a file over <see cref="MaxBytes"/>, an unsupported type, a
    /// <paramref name="note"/> name that is not in <see cref="NoteStore.List"/> and an <c>.attachments</c> that is a link
    /// or reparse point. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't
    /// write, or when every clash name is taken.
    /// </summary>
    public AttachmentAdd Add(string note, string fileName, byte[] bytes)
    {
        if (bytes.LongLength > MaxBytes)
        {
            return new AttachmentAdd(AttachmentAddKind.TooLarge, AttachmentType.Unsupported, null, null);
        }

        string clean = CleanName(fileName);
        AttachmentType type = Sniff(clean, bytes);
        if (type == AttachmentType.Unsupported || !IsStoredName(clean))
        {
            return new AttachmentAdd(AttachmentAddKind.Unsupported, AttachmentType.Unsupported, null, null);
        }

        if (!notes.TryFind(note, out NoteInfo? found) || found is null)
        {
            return new AttachmentAdd(AttachmentAddKind.NoNote, type, null, null);
        }

        string folder = Folder(found);
        if (!IsSafeFolder(folder))
        {
            return new AttachmentAdd(AttachmentAddKind.Unsafe, type, null, null);
        }

        PrivateFile.CreateFolder(folder);
        if (!IsSafeFolder(folder) || !notes.Contains(folder))
        {
            return new AttachmentAdd(AttachmentAddKind.Unsafe, type, null, null);
        }

        string name = Store(folder, clean, bytes);
        string link = IsImage(type) || type == AttachmentType.Excalidraw ? $"![{name}]({FolderName}/{name})" : $"[{name}]({FolderName}/{name})";
        return new AttachmentAdd(AttachmentAddKind.Added, type, name, link);
    }

    // Writes bytes as <folder>/<clean name> (or the first free -N clash name): a 0600 temp file in that folder, then a
    // move that never overwrites. Returns the stored name; throws when it can't write or every clash name is taken.
    private static string Store(string folder, string clean, byte[] bytes)
    {
        int dot = clean.IndexOf('.');
        string stem = clean[..dot];
        string extension = clean[(dot + 1)..];
        string temp = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(bytes);
            }

            for (int n = 1; n <= MaxClash; n++)
            {
                string name = ClashName(stem, n) + "." + extension;
                string target = Path.Combine(folder, name);
                if (File.Exists(target) || Directory.Exists(target) || new FileInfo(target).LinkTarget is not null)
                {
                    continue;
                }

                try
                {
                    File.Move(temp, target, overwrite: false);
                }
                catch (IOException) when (File.Exists(target) || Directory.Exists(target))
                {
                    // Taken between the check and the move: try the next name.
                    continue;
                }

                return name;
            }

            throw new IOException("Every clash name is taken.");
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// The stem for try <paramref name="n"/>: the stem itself for 1, else the stem cut to <c>100 - len("-N")</c>, a
    /// trailing <c>-</c> trimmed, then <c>-N</c>.
    /// </summary>
    public static string ClashName(string stem, int n)
    {
        if (n <= 1)
        {
            return stem;
        }

        string suffix = "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string cut = stem.Length > MaxStemLength - suffix.Length ? stem[..(MaxStemLength - suffix.Length)] : stem;
        return cut.TrimEnd('-') + suffix;
    }

    /// <summary>
    /// The attachment <paramref name="src"/> links to from the note <paramref name="note"/> (a name in
    /// <see cref="NoteStore.List"/>): only <c>.attachments/&lt;name&gt;</c> with a name of the stored shape
    /// (<see cref="IsStoredName"/>), a regular file inside the root (no link or reparse point, in a folder that is no
    /// link) of at most <see cref="MaxBytes"/> whose bytes pass <see cref="Sniff"/>. <c>null</c> for anything else,
    /// and when it can't be read. Never writes.
    /// </summary>
    public ResolvedAttachment? TryResolve(string note, string src)
    {
        if (!src.StartsWith(SrcPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = src[SrcPrefix.Length..];
        if (!IsStoredName(name))
        {
            return null;
        }

        try
        {
            if (!notes.TryFind(note, out NoteInfo? found) || found is null)
            {
                return null;
            }

            string folder = Folder(found);
            DirectoryInfo directory = new DirectoryInfo(folder);
            if (!directory.Exists || NoteStore.IsLink(directory))
            {
                return null;
            }

            string path = Path.Combine(folder, name);
            FileInfo file = new FileInfo(path);
            if (!file.Exists || NoteStore.IsLink(file)
                || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0
                || file.Length == 0 || file.Length > MaxBytes || !notes.Contains(path))
            {
                return null;
            }

            byte[] bytes = BoundedFile.Read(path, MaxBytes);
            AttachmentType type = Sniff(name, bytes);
            return type == AttachmentType.Unsupported ? null : new ResolvedAttachment(path, type, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The distinct image sources in <paramref name="text"/> that follow <c>](</c> and are <c>.attachments/&lt;name&gt;)</c>
    /// with a name of the stored shape (<see cref="IsStoredName"/>) and an image extension, in text order, at most
    /// <see cref="MaxInlineImages"/>. Nothing is read; <see cref="TryResolve"/> still checks each.
    /// </summary>
    public static IReadOnlyList<string> ImageSources(string text)
    {
        List<string> sources = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int at = 0;
        while (sources.Count < MaxInlineImages)
        {
            string? name = NextLinkedName(text, ref at, ')');
            if (name is null)
            {
                break;
            }

            string src = SrcPrefix + name;
            if (IsImageExtension(name) && seen.Add(src))
            {
                sources.Add(src);
            }
        }

        return sources;
    }

    /// <summary>
    /// The distinct drawing sources (job 384) in <paramref name="text"/>: the <see cref="ImageSources"/> rules, but only
    /// names with the <c>.excalidraw</c> extension, at most <see cref="MaxInlineDrawings"/>. Nothing is read;
    /// <see cref="TryResolve"/> still checks each.
    /// </summary>
    public static IReadOnlyList<string> DrawingSources(string text)
    {
        List<string> sources = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int at = 0;
        while (sources.Count < MaxInlineDrawings)
        {
            string? name = NextLinkedName(text, ref at, ')');
            if (name is null)
            {
                break;
            }

            string src = SrcPrefix + name;
            if (IsDrawingName(name) && seen.Add(src))
            {
                sources.Add(src);
            }
        }

        return sources;
    }

    /// <summary>True when <paramref name="src"/> is <c>.attachments/&lt;stored name&gt;.excalidraw</c>.</summary>
    public static bool IsDrawingSrc(string src) =>
        src.StartsWith(SrcPrefix, StringComparison.Ordinal) && IsStoredName(src[SrcPrefix.Length..])
        && IsDrawingName(src[SrcPrefix.Length..]);

    /// <summary>
    /// The distinct names (ordinal) of every <c>](.attachments/&lt;name&gt;</c> in <paramref name="text"/> that is
    /// followed by <c>)</c> or a space and whose name has the stored shape (<see cref="IsStoredName"/>), in text order.
    /// Image and plain links count, and so do links in code (keeping errs on the safe side). Only the first
    /// <see cref="MaxScannedName"/> characters after each marker are looked at, so the scan is linear in the text;
    /// a longer name can't be a stored one.
    /// </summary>
    public static IReadOnlyList<string> LinkedNames(string text)
    {
        List<string> names = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int at = 0;
        while (true)
        {
            string? name = NextLinkedName(text, ref at, ')', ' ');
            if (name is null)
            {
                break;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// The next name of the stored shape after a <see cref="LinkMarker"/> at or after <paramref name="at"/> that ends at
    /// one of <paramref name="ends"/> within <see cref="MaxScannedName"/> characters, or <c>null</c> when there is none.
    /// Each marker costs at most <see cref="MaxScannedName"/> characters and allocates only a name that short, so a
    /// whole scan is linear in the text. <paramref name="at"/> moves past the marker.
    /// </summary>
    private static string? NextLinkedName(string text, ref int at, params char[] ends)
    {
        while (at < text.Length)
        {
            int start = text.IndexOf(LinkMarker, at, StringComparison.Ordinal);
            if (start < 0)
            {
                at = text.Length;
                return null;
            }

            int nameStart = start + LinkMarker.Length;
            at = nameStart;
            int window = Math.Min(MaxScannedName + 1, text.Length - nameStart);
            int end = text.AsSpan(nameStart, window).IndexOfAny(ends);
            if (end <= 0)
            {
                continue;
            }

            string name = text.Substring(nameStart, end);
            if (IsStoredName(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="LinkedNames"/> of the saved file of <paramref name="note"/> (a name in <see cref="NoteStore.List"/>);
    /// empty when it names no note or can't be read (then no attachment is touched).
    /// </summary>
    public IReadOnlyList<string> LinkedNamesOf(string note)
    {
        try
        {
            if (!notes.TryFind(note, out NoteInfo? found) || found is null)
            {
                return [];
            }

            return LinkedNames(DecodeText(BoundedFile.Read(found.Path, NoteStore.MaxEditBytes)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Moves the note <paramref name="name"/> like <see cref="NoteStore.Move"/> (same arguments, answers and
    /// <see cref="NoteExistsException"/> / <see cref="ArgumentException"/>), together with the attachments it links.
    /// For a move to another level each name in <see cref="LinkedNames"/> of the note's saved text that
    /// <see cref="TryResolve"/> accepts is first stored in the target level's <c>.attachments</c> (the <see cref="Add"/>
    /// rules, so a clash gives <c>-N</c>); then the note moves; then only the links whose name changed are rewritten
    /// byte for byte (no history version); last the source of each name no remaining sibling in the old level links is
    /// deleted. Any failure before the cleanup removes the copies (and a folder this move created) and moves the note
    /// back: the two refusals of <see cref="NoteStore.Move"/> are rethrown unchanged, anything else is an
    /// <see cref="AttachmentMoveException"/>. A failed cleanup only leaves sources behind. A move within the level
    /// touches no attachment. A move to another level of a note that links more than <see cref="MaxLinkedNames"/> names
    /// changes nothing and throws <see cref="AttachmentMoveException"/>, so a note never moves without its attachments.
    /// </summary>
    public NoteInfo? MoveWithNote(string name, string? newParent)
    {
        if (!notes.TryFind(name, out NoteInfo? source) || source is null)
        {
            return notes.Move(name, newParent);
        }

        NoteInfo? owner = null;
        if (!string.IsNullOrWhiteSpace(newParent) && (!notes.TryFind(newParent.Trim(), out owner) || owner is null))
        {
            return notes.Move(name, newParent);
        }

        if (string.Equals(owner?.Name, source.Parent, StringComparison.Ordinal))
        {
            return notes.Move(name, newParent);
        }

        byte[] original;
        List<ResolvedAttachment> linked = [];
        bool tooMany = false;
        try
        {
            original = BoundedFile.Read(source.Path, NoteStore.MaxEditBytes);
            IReadOnlyList<string> names = LinkedNames(DecodeText(original));
            tooMany = names.Count > MaxLinkedNames;
            foreach (string linkedName in names)
            {
                if (tooMany)
                {
                    break;
                }


                ResolvedAttachment? resolved = TryResolve(source.Name, FolderName + "/" + linkedName);
                if (resolved is not null)
                {
                    linked.Add(resolved);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable note has no attachments to carry; its move is the plain one.
            return notes.Move(name, newParent);
        }

        if (tooMany)
        {
            // Too many to carry and check against the siblings: the note stays where it is, with every file.
            throw new AttachmentMoveException(new IOException($"The note links more than {MaxLinkedNames} attachments."));
        }

        if (linked.Count == 0)
        {
            return notes.Move(name, newParent);
        }

        MoveUndo undo = new MoveUndo();
        Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        NoteInfo? moved = null;
        try
        {
            string level;
            try
            {
                level = notes.LevelFolder(owner?.Name);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // Let NoteStore.Move give its own answer for this target.
                return notes.Move(name, newParent);
            }

            string target = Path.Combine(level, FolderName);
            if (!IsSafeFolder(target))
            {
                throw new IOException("The target .attachments is not a real folder.");
            }

            undo.LevelCreated = !Directory.Exists(level) ? level : null;
            undo.FolderCreated = !Directory.Exists(target) ? target : null;
            PrivateFile.CreateFolder(target);
            if (!IsSafeFolder(target) || !notes.Contains(target))
            {
                throw new IOException("The target .attachments is not a real folder.");
            }

            foreach (ResolvedAttachment attachment in linked)
            {
                BeforeMoveStep?.Invoke("copy");
                string oldName = Path.GetFileName(attachment.Path);
                string stored = Store(target, oldName, attachment.Bytes);
                undo.Copies.Add(Path.Combine(target, stored));
                if (!string.Equals(stored, oldName, StringComparison.Ordinal))
                {
                    renamed[oldName] = stored;
                }
            }

            BeforeMoveStep?.Invoke("move");
            try
            {
                moved = notes.Move(source.Name, owner?.Name);
            }
            catch (Exception ex) when (ex is NoteExistsException or ArgumentException)
            {
                Undo(undo, null, source);
                throw;
            }

            if (moved is null)
            {
                Undo(undo, null, source);
                return null;
            }

            BeforeMoveStep?.Invoke("rewrite");
            if (renamed.Count > 0)
            {
                Rewrite(moved.Path, renamed);
            }
        }
        catch (Exception ex) when (ex is not NoteExistsException and not ArgumentException and not AttachmentMoveException)
        {
            Undo(undo, moved, source);
            throw new AttachmentMoveException(ex);
        }

        try
        {
            BeforeMoveStep?.Invoke("cleanup");
            string oldFolder = Path.GetDirectoryName(linked[0].Path) ?? string.Empty;
            HashSet<string> kept = KeptBySiblings(source, linked.Select(a => Path.GetFileName(a.Path)).ToList());
            foreach (ResolvedAttachment attachment in linked)
            {
                if (!kept.Contains(Path.GetFileName(attachment.Path)))
                {
                    DeleteStored(oldFolder, Path.GetFileName(attachment.Path));
                }
            }

            NoteStore.RemoveIfEmpty(oldFolder);
        }
        catch (Exception)
        {
            // Nothing is lost: the sources that couldn't be removed stay.
        }

        return moved;
    }

    /// <summary>
    /// After the note <paramref name="deleted"/> is gone: deletes from its level's <c>.attachments</c> each of
    /// <paramref name="names"/> (taken from its text before the delete) that no remaining sibling links (see
    /// <see cref="KeptBySiblings"/>), only regular files of the stored shape, never through a link, then removes that
    /// folder when it is empty. Returns how many files went. Never throws: a file it can't remove stays. More than
    /// <see cref="MaxLinkedNames"/> names remove nothing.
    /// </summary>
    public int RemoveUnlinked(NoteInfo deleted, IReadOnlyList<string> names)
    {
        try
        {
            if (names.Count == 0 || names.Count > MaxLinkedNames)
            {
                // More than MaxLinkedNames: too many to check against the siblings, so every attachment stays.
                return 0;
            }

            string folder = Folder(deleted);
            DirectoryInfo directory = new DirectoryInfo(folder);
            if (!directory.Exists || NoteStore.IsLink(directory) || !notes.Contains(folder))
            {
                return 0;
            }

            HashSet<string> kept = KeptBySiblings(deleted, names);
            int removed = 0;
            foreach (string name in names.Where(n => !kept.Contains(n)))
            {
                if (DeleteStored(folder, name))
                {
                    removed++;
                }
            }

            NoteStore.RemoveIfEmpty(folder);
            return removed;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The ones of <paramref name="names"/> that another note in <paramref name="note"/>'s level (a sibling in
    /// <see cref="NoteStore.List"/>: same <see cref="NoteInfo.Parent"/>) links: its file text on disk contains the name,
    /// ignoring case. A sibling that can't be read or is over <see cref="NoteStore.MaxEditBytes"/> links every name,
    /// and so does any sensitive note in the level, which is never read (no key-store call).
    /// </summary>
    public HashSet<string> KeptBySiblings(NoteInfo note, IReadOnlyList<string> names)
    {
        HashSet<string> all = new HashSet<string>(names, StringComparer.Ordinal);
        if (note.Parent is null && notes.ListSensitive().Count > 0)
        {
            return all;
        }

        HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (NoteInfo sibling in notes.List())
        {
            if (!string.Equals(sibling.Parent, note.Parent, StringComparison.Ordinal)
                || string.Equals(sibling.Name, note.Name, StringComparison.Ordinal))
            {
                continue;
            }

            string text;
            try
            {
                text = DecodeText(BoundedFile.Read(sibling.Path, NoteStore.MaxEditBytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return all;
            }

            foreach (string name in names)
            {
                if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(name);
                }
            }
        }

        return kept;
    }

    // Puts everything back after a failed move: the note to its old place (with its old bytes; the rewrite is the
    // last step that can fail, and its move is atomic), the copies, then the folders this move created.
    private void Undo(MoveUndo undo, NoteInfo? moved, NoteInfo source)
    {
        if (moved is not null)
        {
            try
            {
                notes.Move(moved.Name, source.Parent);
            }
            catch (Exception)
            {
                // Best effort: the copies stay too, so every link of the note still finds its file.
                return;
            }
        }

        foreach (string copy in undo.Copies)
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in place.
            }
        }

        if (undo.FolderCreated is not null)
        {
            NoteStore.RemoveIfEmpty(undo.FolderCreated);
        }

        if (undo.LevelCreated is not null)
        {
            NoteStore.RemoveIfEmpty(undo.LevelCreated);
        }
    }

    // Replaces the bytes "](.attachments/<old>" followed by ')' or ' ' with the new name; every other byte stays.
    // The names are ASCII, so this works on the UTF-8 bytes. Temp file in the note's folder, then a move over it.
    private static void Rewrite(string path, IReadOnlyDictionary<string, string> renamed)
    {
        byte[] bytes = BoundedFile.Read(path, NoteStore.MaxEditBytes);
        byte[] marker = Encoding.ASCII.GetBytes(LinkMarker);
        List<KeyValuePair<byte[], byte[]>> pairs = renamed
            .Select(p => new KeyValuePair<byte[], byte[]>(Encoding.ASCII.GetBytes(p.Key), Encoding.ASCII.GetBytes(p.Value)))
            .ToList();
        List<byte> output = new List<byte>(bytes.Length + 64);
        int at = 0;
        while (at < bytes.Length)
        {
            int found = bytes.AsSpan(at).IndexOf(marker);
            if (found < 0)
            {
                break;
            }

            int nameStart = at + found + marker.Length;
            output.AddRange(bytes.AsSpan(at, nameStart - at));
            at = nameStart;
            foreach (KeyValuePair<byte[], byte[]> pair in pairs)
            {
                byte[] old = pair.Key;
                int end = nameStart + old.Length;
                if (end < bytes.Length && bytes.AsSpan(nameStart, old.Length).SequenceEqual(old)
                    && (bytes[end] == (byte)')' || bytes[end] == (byte)' '))
                {
                    output.AddRange(pair.Value);
                    at = end;
                    break;
                }
            }
        }

        output.AddRange(bytes.AsSpan(at));
        string temp = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
            {
                stream.Write(output.ToArray());
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // Deletes <folder>/<name> only when the name has the stored shape and it is a regular file (no link, reparse
    // point or folder). Never throws.
    private static bool DeleteStored(string folder, string name)
    {
        try
        {
            if (!IsStoredName(name))
            {
                return false;
            }

            FileInfo file = new FileInfo(Path.Combine(folder, name));
            if (!file.Exists || NoteStore.IsLink(file) || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            {
                return false;
            }

            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DecodeText(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        return Encoding.UTF8.GetString(span);
    }

    // What a failed move has to undo.
    private sealed class MoveUndo
    {
        public List<string> Copies { get; } = [];

        public string? LevelCreated { get; set; }

        public string? FolderCreated { get; set; }
    }

    private static bool IsImageExtension(string name)
    {
        string extension = name[(name.LastIndexOf('.') + 1)..];
        return extension is "png" or "jpg" or "jpeg" or "gif" or "webp";
    }

    private static bool IsDrawingName(string name) =>
        name.EndsWith("." + DrawingExtension, StringComparison.Ordinal);

    // One complete JSON object (UTF-8, an optional BOM, at most 64 deep) whose last top-level "type" is "excalidraw".
    private static bool IsExcalidrawJson(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
        }

        try
        {
            System.Text.Json.Utf8JsonReader reader = new System.Text.Json.Utf8JsonReader(
                bytes, new System.Text.Json.JsonReaderOptions { MaxDepth = ExcalidrawSvg.MaxDepth });
            if (!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            bool found = false;
            while (reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                bool isType = reader.ValueTextEquals("type"u8);
                reader.Read();
                if (isType)
                {
                    found = reader.TokenType == System.Text.Json.JsonTokenType.String
                        && reader.ValueTextEquals("excalidraw"u8);
                }

                reader.Skip();
            }

            return reader.TokenType == System.Text.Json.JsonTokenType.EndObject && reader.CurrentDepth == 0
                && !reader.Read() && found;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static AttachmentType FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngMagic))
        {
            return AttachmentType.Png;
        }

        if (bytes.StartsWith(JpegMagic))
        {
            return AttachmentType.Jpeg;
        }

        if (bytes.StartsWith(Gif87Magic) || bytes.StartsWith(Gif89Magic))
        {
            return AttachmentType.Gif;
        }

        if (bytes.Length >= 12 && bytes.StartsWith(RiffMagic) && bytes[8..12].SequenceEqual(WebPMagic))
        {
            return AttachmentType.WebP;
        }

        if (bytes.StartsWith(PdfMagic))
        {
            return AttachmentType.Pdf;
        }

        return AttachmentType.Unsupported;
    }

    // A missing folder is fine (it is created); an existing one must be a real folder, not a link or a file.
    private static bool IsSafeFolder(string folder)
    {
        DirectoryInfo info = new DirectoryInfo(folder);
        if (info.LinkTarget is not null || File.Exists(folder))
        {
            return false;
        }

        return !info.Exists || !NoteStore.IsLink(info);
    }

    private static string AsciiLettersAndDigits(string text)
    {
        StringBuilder kept = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                kept.Append(char.ToLowerInvariant(c));
            }
        }

        return kept.ToString();
    }

    // The one stored-stem rule: outside [A-Za-z0-9_-] becomes '-', runs of '-' collapse, a leading '-' goes, the cut
    // to MaxStemLength, then a trailing '-' goes; empty or refused by ValidateTitle becomes the fallback.
    private static string CleanStem(string stem)
    {
        StringBuilder clean = new StringBuilder(stem.Length);
        foreach (char c in stem)
        {
            char kept = char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '-';
            if (kept == '-' && (clean.Length == 0 || clean[^1] == '-'))
            {
                continue;
            }

            clean.Append(kept);
        }

        string cut = clean.Length > MaxStemLength ? clean.ToString(0, MaxStemLength) : clean.ToString();
        cut = cut.TrimEnd('-');
        return cut.Length == 0 || NoteStore.ValidateTitle(cut) is not null ? FallbackStem : cut;
    }
}
