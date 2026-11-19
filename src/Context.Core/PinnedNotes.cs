namespace Context;

/// <summary>
/// The notes pinned in the desktop app's sidebar (job 278): full note names in the order they were pinned, at most
/// <see cref="MaxCount"/>, distinct and compared ordinally. Every step is pure and returns a new list.
/// </summary>
internal static class PinnedNotes
{
    /// <summary>The most names the list holds.</summary>
    public const int MaxCount = 20;

    /// <summary>
    /// <paramref name="name"/> appended at the end; the same list when the name is already there or the list holds
    /// <see cref="MaxCount"/> names.
    /// </summary>
    public static IReadOnlyList<string> Pin(IReadOnlyList<string> list, string name) =>
        list.Contains(name, StringComparer.Ordinal) || list.Count >= MaxCount ? list : [.. list, name];

    /// <summary>The list without <paramref name="name"/> (only that name; its subpages stay pinned).</summary>
    public static IReadOnlyList<string> Unpin(IReadOnlyList<string> list, string name) =>
        list.Where(n => !string.Equals(n, name, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// <paramref name="oldName"/> and every <c>oldName/…</c> entry renamed to the <paramref name="newName"/> prefix,
    /// each keeping its place; later duplicates are dropped so the first one stays.
    /// </summary>
    public static IReadOnlyList<string> Renamed(IReadOnlyList<string> list, string oldName, string newName) =>
        list.Select(n => string.Equals(n, oldName, StringComparison.Ordinal)
                ? newName
                : RecentNotes.IsBelow(n, oldName) ? newName + n[oldName.Length..] : n)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCount)
            .ToList();

    /// <summary>The list without <paramref name="name"/> and every <c>name/…</c> entry.</summary>
    public static IReadOnlyList<string> Removed(IReadOnlyList<string> list, string name) =>
        list.Where(n => !string.Equals(n, name, StringComparison.Ordinal) && !RecentNotes.IsBelow(n, name)).ToList();

    /// <summary>True when <paramref name="name"/> could name a note (<see cref="RecentNotes.IsValidName"/>).</summary>
    public static bool IsValidName(string name) => RecentNotes.IsValidName(name);
}

/// <summary>
/// <c>&lt;root&gt;/pinned.json</c>: <c>{"pinned":["&lt;full name&gt;", …]}</c>, in pinned order, at most
/// <see cref="PinnedNotes.MaxCount"/> distinct names. App-owned state, not user content: a missing or broken file reads
/// as an empty list and the next write replaces it. Same read and write rules as <see cref="RecentNotesStore"/>
/// (<see cref="NameListFile"/>). The root folder is never created here.
/// </summary>
internal sealed class PinnedNotesStore
{
    /// <summary>The file's name in the root.</summary>
    public const string FileName = "pinned.json";

    /// <summary>The largest file that is read (64 KiB).</summary>
    public const long MaxBytes = 64 * 1024;

    private const string PinnedKey = "pinned";

    private readonly string root;

    public PinnedNotesStore(string root)
    {
        this.root = root;
    }

    /// <summary>The file's full path.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// The stored names in pinned order. Empty when the file is missing, unreadable, over <see cref="MaxBytes"/>, not
    /// valid JSON or not of the stated shape (more than <see cref="PinnedNotes.MaxCount"/> names, a duplicate, or a name
    /// that fails <see cref="PinnedNotes.IsValidName"/>). Never throws.
    /// </summary>
    public IReadOnlyList<string> Read() => NameListFile.Read(FilePath, PinnedKey, PinnedNotes.MaxCount, MaxBytes);

    /// <summary>
    /// Writes <paramref name="names"/> through a temp file and a move, replacing whatever the file held. Throws an
    /// <see cref="IOException"/> (or <see cref="UnauthorizedAccessException"/>) when it can't be written, for example
    /// when the root folder is missing or <c>pinned.json</c> is a folder.
    /// </summary>
    public void Write(IReadOnlyList<string> names) => NameListFile.Write(root, FileName, PinnedKey, names);
}
