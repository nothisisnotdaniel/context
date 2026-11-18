using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Context;

/// <summary>
/// The list of the notes opened most recently in the desktop app (job 240): full note names, newest first, at most
/// <see cref="MaxCount"/>, distinct and compared ordinally. Every step is pure and returns a new list.
/// </summary>
internal static class RecentNotes
{
    /// <summary>The most names the list holds.</summary>
    public const int MaxCount = 10;

    /// <summary><paramref name="name"/> first, followed by the other entries in their order, cut to <see cref="MaxCount"/>.</summary>
    public static IReadOnlyList<string> Touch(IReadOnlyList<string> list, string name) =>
        new[] { name }
            .Concat(list.Where(n => !string.Equals(n, name, StringComparison.Ordinal)))
            .Take(MaxCount)
            .ToList();

    /// <summary>
    /// <paramref name="oldName"/> and every <c>oldName/…</c> entry renamed to the <paramref name="newName"/> prefix,
    /// each keeping its place; later duplicates are dropped so the first one stays.
    /// </summary>
    public static IReadOnlyList<string> Renamed(IReadOnlyList<string> list, string oldName, string newName) =>
        list.Select(n => string.Equals(n, oldName, StringComparison.Ordinal)
                ? newName
                : IsBelow(n, oldName) ? newName + n[oldName.Length..] : n)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxCount)
            .ToList();

    /// <summary>The list without <paramref name="name"/> and every <c>name/…</c> entry.</summary>
    public static IReadOnlyList<string> Removed(IReadOnlyList<string> list, string name) =>
        list.Where(n => !string.Equals(n, name, StringComparison.Ordinal) && !IsBelow(n, name)).ToList();

    /// <summary>
    /// True when <paramref name="name"/> could name a note: at most <see cref="NoteStore.MaxNameLength"/> characters and
    /// every <c>/</c>-separated part a valid title (<see cref="NoteStore.ValidateTitle"/>).
    /// </summary>
    public static bool IsValidName(string name) =>
        name.Length > 0
        && name.Length <= NoteStore.MaxNameLength
        && name.Split('/').All(part => NoteStore.ValidateTitle(part) is null);

    private static bool IsBelow(string name, string ancestor) =>
        name.Length > ancestor.Length + 1
        && name[ancestor.Length] == '/'
        && name.StartsWith(ancestor, StringComparison.Ordinal);
}

/// <summary>
/// <c>&lt;root&gt;/recent.json</c>: <c>{"recent":["&lt;full name&gt;", …]}</c>, newest first, at most
/// <see cref="RecentNotes.MaxCount"/> distinct names. App-owned state, not user content: a missing or broken file reads
/// as an empty list and the next write replaces it. Read through <see cref="BoundedFile"/>; written as UTF-8 without
/// BOM through a temp file and a move. The root folder is never created here.
/// </summary>
internal sealed class RecentNotesStore
{
    /// <summary>The file's name in the root.</summary>
    public const string FileName = "recent.json";

    /// <summary>The largest file that is read (64 KiB).</summary>
    public const long MaxBytes = 64 * 1024;

    private const string RecentKey = "recent";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Non-ASCII names are written as they are. Only the file is written this way; it is never put into HTML.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string root;

    public RecentNotesStore(string root)
    {
        this.root = root;
    }

    /// <summary>The file's full path.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// The stored names, newest first. Empty when the file is missing, unreadable, over <see cref="MaxBytes"/>, not
    /// valid JSON or not of the stated shape (more than <see cref="RecentNotes.MaxCount"/> names, a duplicate, or a name
    /// that fails <see cref="RecentNotes.IsValidName"/>). Never throws.
    /// </summary>
    public IReadOnlyList<string> Read()
    {
        try
        {
            byte[] bytes = BoundedFile.Read(FilePath, MaxBytes);
            if (JsonNode.Parse(bytes) is not JsonObject document
                || document.Count != 1
                || document[RecentKey] is not JsonArray array
                || array.Count > RecentNotes.MaxCount)
            {
                return [];
            }

            List<string> names = [];
            foreach (JsonNode? item in array)
            {
                if (item is not JsonValue value
                    || value.GetValueKind() != JsonValueKind.String
                    || value.GetValue<string>() is not { } name
                    || !RecentNotes.IsValidName(name)
                    || names.Contains(name, StringComparer.Ordinal))
                {
                    return [];
                }

                names.Add(name);
            }

            return names;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes <paramref name="names"/> (newest first) through a temp file and a move, replacing whatever the file held.
    /// Throws an <see cref="IOException"/> (or <see cref="UnauthorizedAccessException"/>) when it can't be written, for
    /// example when the root folder is missing or read-only.
    /// </summary>
    public void Write(IReadOnlyList<string> names)
    {
        JsonArray array = new();
        foreach (string name in names)
        {
            array.Add(name);
        }

        JsonObject document = new() { [RecentKey] = array };
        byte[] bytes = Utf8NoBom.GetBytes(document.ToJsonString(WriteOptions) + "\n");
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
