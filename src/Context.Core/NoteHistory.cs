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
    /// <paramref name="sealedTitle"/> that is not a stored file stem (nothing written) and
    /// <see cref="NoteHistoryException"/> when the version can't be written.
    /// </summary>
    public string? Keep(string notePath, byte[] bytes, string? sealedTitle = null, NoteFileEdit? edit = null)
    {
        if (sealedTitle is not null && !IsStoredTitle(sealedTitle))
        {
            throw new ArgumentException("Not a valid sealed title.", nameof(sealedTitle));
        }

        string extension = sealedTitle is null ? PlainExtension : NoteStore.SensitiveExtension;
        try
        {
            if (edit is null && !store.Contains(notePath))
            {
                throw new NoteHistoryException();
            }

            string folder = edit is null ? PrepareFolder(notePath) : FolderFor(notePath);
            edit?.EnsureFolder(folder);
            List<Entry> existing = ListFolder(folder, null, edit);
            Entry? newest = existing.Where(e => e.Extension == extension).OrderByDescending(e => e, ByAge).FirstOrDefault();
            if (newest is not null && newest.Version.Length == bytes.Length
                && (edit?.Read(newest.Path, MaxVersionBytes) ?? BoundedFile.Read(newest.Path, MaxVersionBytes)).AsSpan().SequenceEqual(bytes))
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
                    if (edit is null)
                    {
                        PrivateFile.Write(path, bytes);
                    }
                    else
                    {
                        edit.WriteNew(path, bytes);
                    }
                }
                catch (IOException) when (edit is null && File.Exists(path))
                {
                    continue;
                }

                Prune(folder, id, edit, extension);
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
    public string KeepSealedCopy(string notePath, string id, string sealedTitle, byte[] bytes, NoteFileEdit? edit = null)
    {
        if (!IsStoredTitle(sealedTitle) || ParseId(id) is null)
        {
            throw new ArgumentException("Not a valid version.", nameof(id));
        }

        try
        {
            if (edit is null && !store.Contains(notePath))
            {
                throw new NoteHistoryException();
            }

            string path = Path.Combine(edit is null ? PrepareFolder(notePath) : FolderFor(notePath), FileName(id, sealedTitle));
            if (edit is null)
            {
                PrivateFile.Write(path, bytes);
            }
            else
            {
                edit.WriteNew(path, bytes);
            }
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
    /// stored file stem). A missing history is empty; a <c>.history</c> or note
    /// folder that is a file, a link or a reparse point, or that can't be read, throws <see cref="NoteHistoryException"/>.
    /// </summary>
    public IReadOnlyList<NoteVersion> Versions(NoteInfo note, NoteFileEdit? edit = null) =>
        ListVersions(note.Path, edit, KindOf(note.Path));

    internal static IReadOnlyList<NoteVersion> BoundedPlainVersions(string path, NoteFileEdit edit)
    {
        IReadOnlyList<NoteVersion> versions = ListVersions(path, edit, PlainExtension);
        if (versions.Count > MaxVersionsPerNote || versions.Any(v => v.Length > MaxVersionBytes)
            || versions.Sum(v => v.Length) > MaxBytesPerNote)
        {
            throw new NoteHistoryException();
        }
        return versions;
    }

    internal static IReadOnlyList<NoteVersion> ListVersions(string notePath, NoteFileEdit? edit, string? extension)
    {
        try
        {
            return ListFolder(FolderFor(notePath), extension, edit)
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
    /// True when the newest version of the note at <paramref name="notePath"/> (of its own kind, found like
    /// <see cref="Versions"/>) was kept less than <paramref name="gap"/> ago on this history's clock. False with no
    /// version, and for a newest version from the future. Throws <see cref="NoteHistoryException"/> like
    /// <see cref="Versions"/>.
    /// </summary>
    public bool KeptWithin(string notePath, TimeSpan gap, NoteFileEdit? edit = null)
    {
        try
        {
            Entry? newest = ListFolder(FolderFor(notePath), KindOf(notePath), edit).OrderByDescending(e => e, ByAge).FirstOrDefault();
            if (newest is null)
            {
                return false;
            }

            TimeSpan age = utcNow() - newest.Version.SavedUtc;
            return age >= TimeSpan.Zero && age < gap;
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
    public bool TryRead(NoteInfo note, string id, out byte[]? bytes, NoteFileEdit? edit = null)
    {
        bytes = null;
        try
        {
            Entry? match = ListFolder(FolderFor(note.Path), KindOf(note.Path), edit)
                .FirstOrDefault(e => string.Equals(e.Version.Id, id, StringComparison.Ordinal));
            if (match is null)
            {
                return false;
            }

            bytes = edit?.Read(match.Path, MaxVersionBytes) ?? BoundedFile.Read(match.Path, MaxVersionBytes);
            edit?.Verify();
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
    internal static string FolderFor(string notePath)
    {
        string stem = Path.GetFileNameWithoutExtension(notePath);
        string folder = Path.Combine(Path.GetDirectoryName(notePath) ?? string.Empty, FolderName, stem);
        // Win32 normally strips a trailing space or dot on a directory component. Address the
        // exact stored stem so two different Markdown files can keep independent histories.
        return OperatingSystem.IsWindows() && (stem.EndsWith(' ') || stem.EndsWith('.')) ? ExtendedWindowsPath(folder) : folder;
    }

    internal static string ExtendedWindowsPath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

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

        NoteStore.RemoveIfEmpty(folder);
        NoteStore.RemoveIfEmpty(parent);
    }

    // Moves the history of the note at from to the note at to (their folders may differ). False, moving nothing, when
    // the target history already exists (a case-only rename skips that check); else true when it moved or there was
    // nothing to move. Throws IOException when the target's .history is a file or a link, or the move fails.
    internal static bool MoveHistory(string from, string to, bool caseOnly)
    {
        string source = FolderFor(from);
        string target = FolderFor(to);
        if (OperatingSystem.IsWindows() && (source.StartsWith(@"\\?\", StringComparison.Ordinal) || target.StartsWith(@"\\?\", StringComparison.Ordinal)))
        {
            // Directory.Move compares the two volume roots, including their namespace prefix.
            source = ExtendedWindowsPath(source);
            target = ExtendedWindowsPath(target);
        }
        string sourceParent = Path.GetDirectoryName(source) ?? string.Empty;
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

        string otherKind = KindOf(from) == PlainExtension ? NoteStore.SensitiveExtension : PlainExtension;
        if (ListFolder(source, otherKind).Count != 0)
        {
            return false;
        }
        EnsureFolder(targetParent);
        Directory.Move(source, target);
        if (!string.Equals(sourceParent, targetParent, StringComparison.Ordinal))
        {
            NoteStore.RemoveIfEmpty(sourceParent);
        }

        return true;
    }

    private static string KindOf(string notePath) =>
        string.Equals(Path.GetExtension(notePath), NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase)
            ? NoteStore.SensitiveExtension
            : PlainExtension;

    internal static string FileName(string id, string? sealedTitle) =>
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

        PrivateFile.CreateFolder(folder);
    }

    // The valid versions in a note's history folder (of one kind when extension is set). Missing: empty. A .history or
    // note folder that is a file, a link or a reparse point throws.
    private static List<Entry> ListFolder(string folder, string? extension, NoteFileEdit? edit = null)
    {
        if (edit is not null)
        {
            return edit.List(folder).Where(f => !f.Directory)
                .Select(f => ParseName(Path.Combine(folder, f.Name), f.Length, extension)).OfType<Entry>().ToList();
        }
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

        return ParseName(file.FullName, file.Length, extension);
    }

    private static Entry? ParseName(string path, long length, string? extension)
    {
        string name = Path.GetFileName(path);
        if (name.EndsWith(PlainExtension, StringComparison.Ordinal) && !name.Contains('~', StringComparison.Ordinal))
        {
            if (extension is not null && extension != PlainExtension)
            {
                return null;
            }

            string id = name[..^PlainExtension.Length];
            return ParseId(id) is { } parsed
                ? new Entry(path, new NoteVersion(id, parsed.Utc, length, null), parsed.Sequence, PlainExtension)
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
            return ParseId(id) is { } parsed && IsStoredTitle(title)
                ? new Entry(path, new NoteVersion(id, parsed.Utc, length, title), parsed.Sequence, NoteStore.SensitiveExtension)
                : null;
        }

        return null;
    }

    // A sealed title is embedded between a validated id and an extension. Existing top-level
    // file stems can include a final space or dot; the exact spelling authenticates the version.
    private static bool IsStoredTitle(string title) => title.Length > 0 && !title.StartsWith('.')
        && title.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(title) == title;

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
    private static void Prune(string folder, string kept, NoteFileEdit? edit, string extension)
    {
        List<Entry> oldestFirst = ListFolder(folder, extension, edit).OrderBy(e => e, ByAge).ToList();
        int count = oldestFirst.Count;
        long total = oldestFirst.Sum(e => e.Version.Length);
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

            long length = entry.Version.Length;
            if (edit is null)
            {
                File.Delete(entry.Path);
            }
            else
            {
                edit.Discard(entry.Path);
            }
            count--;
            total -= length;
        }
    }

    private sealed record Entry(string Path, NoteVersion Version, int Sequence, string Extension);
}
