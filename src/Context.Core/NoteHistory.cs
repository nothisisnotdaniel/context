using System.Globalization;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>
/// One kept version of a note: its id (<c>yyyyMMdd'T'HHmmssfff'Z'[-n]</c>), the UTC time in the id, the file's length
/// and, for a sealed version, the title it is sealed under.
/// </summary>
internal sealed record NoteVersion
{
    public NoteVersion(string id, DateTime savedUtc, long length, string? sealedTitle)
    {
        Id = id;
        SavedUtc = savedUtc;
        Length = length;
        SealedTitle = sealedTitle;
    }

    public string Id { get; }

    public DateTime SavedUtc { get; }

    public long Length { get; }

    /// <summary>The exact title a sealed version is sealed under; <c>null</c> for a plain version.</summary>
    public string? SealedTitle { get; }

    public override string ToString() => nameof(NoteVersion);
}

/// <summary>Thrown when a version can't be kept or the history can't be read. The message is a fixed text.</summary>
internal sealed class NoteHistoryException : IOException
{
    public NoteHistoryException()
        : base("Can't use the note history.")
    {
    }
}

/// <summary>
/// The kept versions of notes. A note <c>&lt;folder&gt;/&lt;Title&gt;.md</c> keeps its versions as
/// <c>&lt;folder&gt;/.history/&lt;Title&gt;/&lt;id&gt;.md</c>, a sensitive <c>&lt;root&gt;/&lt;Title&gt;.ctxs</c> as
/// <c>&lt;root&gt;/.history/&lt;Title&gt;/&lt;id&gt;~&lt;sealed title&gt;.ctxs</c>. A version is the note file's
/// previous bytes, unchanged. Each note keeps at most <see cref="MaxVersionsPerNote"/> versions and
/// <see cref="MaxBytesPerNote"/> bytes. Links and reparse points are never followed or written through.
/// </summary>
internal sealed partial class NoteHistory
{
    /// <summary>The folder next to the notes of one level that holds their versions.</summary>
    public const string FolderName = ".history";

    /// <summary>The most versions one note keeps.</summary>
    public const int MaxVersionsPerNote = 50;

    /// <summary>The most bytes the versions of one note take together (25 MB).</summary>
    public const long MaxBytesPerNote = 25L * 1024 * 1024;

    /// <summary>The largest version file <see cref="TryRead"/> reads (8 MB, the largest sealed note).</summary>
    public const long MaxVersionBytes = 8L * 1024 * 1024;

    private const string IdFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    private const string PlainExtension = ".md";

    private const int MaxSameMillisecond = 1000;

    // Orders versions by their time, then their -n counter (ascending: oldest first).
    private static readonly Comparer<Entry> ByAge = Comparer<Entry>.Create((a, b) =>
    {
        int byTime = a.Version.SavedUtc.CompareTo(b.Version.SavedUtc);
        return byTime != 0 ? byTime : a.Sequence.CompareTo(b.Sequence);
    });

    private readonly NoteStore store;
    private readonly Func<DateTime> utcNow;

    public NoteHistory(NoteStore store, Func<DateTime> utcNow)
    {
        this.store = store;
        this.utcNow = utcNow;
    }

    /// <summary>
    /// Keeps <paramref name="bytes"/> (the note's previous file content) as a new version of the note at
    /// <paramref name="notePath"/>: plain as <c>&lt;id&gt;.md</c>, or with <paramref name="sealedTitle"/> as
    /// <c>&lt;id&gt;~&lt;sealedTitle&gt;.ctxs</c>, with <see cref="FileMode.CreateNew"/> and mode <c>0600</c> off
    /// Windows. The id comes from the clock; a taken id gets <c>-1</c>, <c>-2</c>, ... Writes nothing and returns
    /// <c>null</c> when the newest version of the same kind holds the same bytes. Then removes the oldest versions until
    /// the caps hold, never the one just written. Throws <see cref="ArgumentException"/> for a
    /// <paramref name="sealedTitle"/> that fails <see cref="NoteStore.ValidateTitle"/> (nothing written) and
    /// <see cref="NoteHistoryException"/> when the version can't be written.
    /// </summary>
    public string? Keep(string notePath, byte[] bytes, string? sealedTitle = null)
    {
        if (sealedTitle is not null && NoteStore.ValidateTitle(sealedTitle) is not null)
        {
            throw new ArgumentException("Not a valid sealed title.", nameof(sealedTitle));
        }

        string extension = sealedTitle is null ? PlainExtension : NoteStore.SensitiveExtension;
        try
        {
            if (!store.Contains(notePath))
            {
                throw new NoteHistoryException();
            }

            string folder = PrepareFolder(notePath);
            List<Entry> existing = ListFolder(folder, null);
            Entry? newest = existing.Where(e => e.Extension == extension).OrderByDescending(e => e, ByAge).FirstOrDefault();
            if (newest is not null && newest.File.Length == bytes.Length
                && BoundedFile.Read(newest.File.FullName, MaxVersionBytes).AsSpan().SequenceEqual(bytes))
            {
                return null;
            }

            string baseId = utcNow().ToString(IdFormat, CultureInfo.InvariantCulture);
            HashSet<string> taken = new(existing.Select(e => e.Version.Id), StringComparer.Ordinal);
            for (int n = 0; n < MaxSameMillisecond; n++)
            {
                string id = n == 0 ? baseId : baseId + "-" + n.ToString(CultureInfo.InvariantCulture);
                if (taken.Contains(id))
                {
                    continue;
                }

                string path = Path.Combine(folder, FileName(id, sealedTitle));
                try
                {
                    WritePrivate(path, bytes);
                }
                catch (IOException) when (File.Exists(path))
                {
                    continue;
                }

                Prune(folder, id);
                return id;
            }

            throw new NoteHistoryException();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not NoteHistoryException)
        {
            throw new NoteHistoryException();
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> as the sealed version <c>&lt;id&gt;~&lt;sealedTitle&gt;.ctxs</c> of the note at
    /// <paramref name="notePath"/> with the given id (<see cref="FileMode.CreateNew"/>, <c>0600</c> off Windows) and
    /// returns its path; no pruning. Used when a note is marked sensitive and its plain versions are sealed. Throws
    /// <see cref="ArgumentException"/> for an invalid id or title and <see cref="NoteHistoryException"/> when it can't write.
    /// </summary>
    public string KeepSealedCopy(string notePath, string id, string sealedTitle, byte[] bytes)
    {
        if (NoteStore.ValidateTitle(sealedTitle) is not null || ParseId(id) is null)
        {
            throw new ArgumentException("Not a valid version.", nameof(id));
        }

        try
        {
            if (!store.Contains(notePath))
            {
                throw new NoteHistoryException();
            }

            string path = Path.Combine(PrepareFolder(notePath), FileName(id, sealedTitle));
            WritePrivate(path, bytes);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not NoteHistoryException)
        {
            throw new NoteHistoryException();
        }
    }

    /// <summary>
    /// The versions of <paramref name="note"/> of its own kind (<c>.md</c> versions for a plain note, sealed ones for a
    /// <c>.ctxs</c>), newest first. Only regular files with a valid version name count (a sealed one also needs a
    /// title that passes <see cref="NoteStore.ValidateTitle"/>). A missing history is empty; a <c>.history</c> or note
    /// folder that is a file, a link or a reparse point, or that can't be read, throws <see cref="NoteHistoryException"/>.
    /// </summary>
    public IReadOnlyList<NoteVersion> Versions(NoteInfo note)
    {
        string extension = KindOf(note.Path);
        try
        {
            return ListFolder(FolderFor(note.Path), extension)
                .OrderByDescending(e => e, ByAge)
                .Select(e => e.Version)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not NoteHistoryException)
        {
            throw new NoteHistoryException();
        }
    }

    /// <summary>
    /// Reads the version <paramref name="id"/> of <paramref name="note"/> (found like <see cref="Versions"/>, never
    /// through a link, at most <see cref="MaxVersionBytes"/>). False when there is no such version or it can't be read.
    /// </summary>
    public bool TryRead(NoteInfo note, string id, out byte[]? bytes)
    {
        bytes = null;
        try
        {
            Entry? match = ListFolder(FolderFor(note.Path), KindOf(note.Path))
                .FirstOrDefault(e => string.Equals(e.Version.Id, id, StringComparison.Ordinal));
            if (match is null)
            {
                return false;
            }

            bytes = BoundedFile.Read(match.File.FullName, MaxVersionBytes);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            bytes = null;
            return false;
        }
    }

    /// <summary>
    /// Removes the versions of the note at <paramref name="notePath"/> (only those with <paramref name="extension"/>,
    /// <c>.md</c> or <c>.ctxs</c>, when given), then its history folder and the <c>.history</c> folder when they are
    /// empty. Never follows a link. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when a
    /// version can't be removed.
    /// </summary>
    public void RemoveHistory(string notePath, string? extension = null) => Remove(notePath, extension);

    // The history folder of the note at notePath: <its folder>/.history/<its file stem>.
    internal static string FolderFor(string notePath) =>
        Path.Combine(Path.GetDirectoryName(notePath) ?? string.Empty, FolderName, Path.GetFileNameWithoutExtension(notePath));

    // See RemoveHistory.
    internal static void Remove(string notePath, string? extension)
    {
        string parent = Path.Combine(Path.GetDirectoryName(notePath) ?? string.Empty, FolderName);
        string folder = FolderFor(notePath);
        DirectoryInfo parentInfo = new DirectoryInfo(parent);
        DirectoryInfo info = new DirectoryInfo(folder);
        if (!parentInfo.Exists || NoteStore.IsLink(parentInfo) || !info.Exists || NoteStore.IsLink(info))
        {
            return;
        }

        foreach (FileInfo file in info.EnumerateFiles())
        {
            if (Parse(file, extension) is not null)
            {
                file.Delete();
            }
        }

        RemoveIfEmpty(folder);
        RemoveIfEmpty(parent);
    }

    // Moves the history of the note at from to the note at to (their folders may differ). False, moving nothing, when
    // the target history already exists (a case-only rename skips that check); else true when it moved or there was
    // nothing to move. Throws IOException when the target's .history is a file or a link, or the move fails.
    internal static bool MoveHistory(string from, string to, bool caseOnly)
    {
        string source = FolderFor(from);
        string sourceParent = Path.GetDirectoryName(source) ?? string.Empty;
        string target = FolderFor(to);
        string targetParent = Path.GetDirectoryName(target) ?? string.Empty;
        if (!caseOnly && (Directory.Exists(target) || File.Exists(target) || new FileInfo(target).LinkTarget is not null))
        {
            return false;
        }

        DirectoryInfo sourceParentInfo = new DirectoryInfo(sourceParent);
        if (!sourceParentInfo.Exists || NoteStore.IsLink(sourceParentInfo) || !Directory.Exists(source))
        {
            return true;
        }

        EnsureFolder(targetParent);
        Directory.Move(source, target);
        if (!string.Equals(sourceParent, targetParent, StringComparison.Ordinal))
        {
            RemoveIfEmpty(sourceParent);
        }

        return true;
    }

    // Removes a folder only when it is empty; never follows a link and never throws.
    internal static void RemoveIfEmpty(string folder)
    {
        try
        {
            DirectoryInfo info = new DirectoryInfo(folder);
            if (info.Exists && !NoteStore.IsLink(info) && !info.EnumerateFileSystemInfos().Any())
            {
                info.Delete(recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place.
        }
    }

    private static string KindOf(string notePath) =>
        string.Equals(Path.GetExtension(notePath), NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase)
            ? NoteStore.SensitiveExtension
            : PlainExtension;

    private static string FileName(string id, string? sealedTitle) =>
        sealedTitle is null ? id + PlainExtension : id + "~" + sealedTitle + NoteStore.SensitiveExtension;

    // Creates .history and the note's folder in it when missing; refuses a place that is a file, a link or a reparse point.
    private static string PrepareFolder(string notePath)
    {
        string folder = FolderFor(notePath);
        EnsureFolder(Path.GetDirectoryName(folder) ?? string.Empty);
        EnsureFolder(folder);
        return folder;
    }

    private static void EnsureFolder(string folder)
    {
        DirectoryInfo info = new DirectoryInfo(folder);
        if (File.Exists(folder) || new FileInfo(folder).LinkTarget is not null || (info.Exists && NoteStore.IsLink(info)))
        {
            throw new NoteHistoryException();
        }

        if (info.Exists)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // A new file only the owner can read and write (mode 0600 off Windows); never an existing one.
    private static void WritePrivate(string path, byte[] bytes)
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

        using FileStream stream = new FileStream(path, options);
        stream.Write(bytes);
    }

    // The valid versions in a note's history folder (of one kind when extension is set). Missing: empty. A .history or
    // note folder that is a file, a link or a reparse point throws.
    private static List<Entry> ListFolder(string folder, string? extension)
    {
        string parent = Path.GetDirectoryName(folder) ?? string.Empty;
        foreach (string place in new[] { parent, folder })
        {
            DirectoryInfo placeInfo = new DirectoryInfo(place);
            if (File.Exists(place) || new FileInfo(place).LinkTarget is not null || (placeInfo.Exists && NoteStore.IsLink(placeInfo)))
            {
                throw new NoteHistoryException();
            }

            if (!placeInfo.Exists)
            {
                return [];
            }
        }

        return new DirectoryInfo(folder).EnumerateFiles()
            .Select(f => Parse(f, extension))
            .OfType<Entry>()
            .ToList();
    }

    // A regular file with a valid version name (of one kind when extension is set), else null.
    private static Entry? Parse(FileInfo file, string? extension)
    {
        if (NoteStore.IsLink(file) || (file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
        {
            return null;
        }

        string name = file.Name;
        if (name.EndsWith(PlainExtension, StringComparison.Ordinal) && !name.Contains('~', StringComparison.Ordinal))
        {
            if (extension is not null && extension != PlainExtension)
            {
                return null;
            }

            string id = name[..^PlainExtension.Length];
            return ParseId(id) is { } parsed
                ? new Entry(file, new NoteVersion(id, parsed.Utc, file.Length, null), parsed.Sequence, PlainExtension)
                : null;
        }

        int tilde = name.IndexOf('~', StringComparison.Ordinal);
        if (tilde > 0 && name.EndsWith(NoteStore.SensitiveExtension, StringComparison.Ordinal))
        {
            if (extension is not null && extension != NoteStore.SensitiveExtension)
            {
                return null;
            }

            string id = name[..tilde];
            string rest = name[(tilde + 1)..];
            string title = rest[..^NoteStore.SensitiveExtension.Length];
            return ParseId(id) is { } parsed && NoteStore.ValidateTitle(title) is null
                ? new Entry(file, new NoteVersion(id, parsed.Utc, file.Length, title), parsed.Sequence, NoteStore.SensitiveExtension)
                : null;
        }

        return null;
    }

    // The time and the -n counter of a valid id, else null.
    private static (DateTime Utc, int Sequence)? ParseId(string id)
    {
        Match match = IdPattern().Match(id);
        if (!match.Success
            || !DateTime.TryParseExact(
                match.Groups[1].Value,
                IdFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime utc))
        {
            return null;
        }

        int sequence = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        return (utc, sequence);
    }

    [GeneratedRegex(@"^([0-9]{8}T[0-9]{9}Z)(?:-([1-9][0-9]{0,5}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    // Removes the oldest versions until at most MaxVersionsPerNote are left and they take at most MaxBytesPerNote;
    // the version kept is never removed.
    private static void Prune(string folder, string kept)
    {
        List<Entry> oldestFirst = ListFolder(folder, null).OrderBy(e => e, ByAge).ToList();
        int count = oldestFirst.Count;
        long total = oldestFirst.Sum(e => e.File.Length);
        foreach (Entry entry in oldestFirst)
        {
            if (count <= MaxVersionsPerNote && total <= MaxBytesPerNote)
            {
                break;
            }

            if (string.Equals(entry.Version.Id, kept, StringComparison.Ordinal))
            {
                continue;
            }

            long length = entry.File.Length;
            entry.File.Delete();
            count--;
            total -= length;
        }
    }

    private sealed class Entry
    {
        public Entry(FileInfo file, NoteVersion version, int sequence, string extension)
        {
            File = file;
            Version = version;
            Sequence = sequence;
            Extension = extension;
        }

        public FileInfo File { get; }

        public NoteVersion Version { get; }

        public int Sequence { get; }

        public string Extension { get; }
    }
}
