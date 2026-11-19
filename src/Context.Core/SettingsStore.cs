using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Context;

/// <summary><c>settings.json</c> can't be read, isn't valid JSON or holds a bad value; it is never overwritten then.</summary>
internal sealed class SettingsStoreException : IOException
{
    public SettingsStoreException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The new <c>settings.json</c> would be over <see cref="SettingsStore.MaxBytes"/>, so it would read as broken; nothing
/// was written.
/// </summary>
internal sealed class SettingsTooLargeException : IOException
{
    public SettingsTooLargeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// What the automatic day's end has already done: the last leaving it wrote into a <c>## Worked</c> section
/// (<c>yyyy-MM-ddTHH:mm</c>, raw) and the last day whose todos it moved. Both <c>null</c> when not recorded.
/// </summary>
internal sealed record DayEndSettings
{
    public DayEndSettings(string? worked, DateOnly? moved)
    {
        Worked = worked;
        Moved = moved;
    }

    /// <summary>The last leaving written by the automatic Worked refresh, as <c>yyyy-MM-ddTHH:mm</c>.</summary>
    public string? Worked { get; }

    /// <summary>The last day whose todos the automatic run moved.</summary>
    public DateOnly? Moved { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(DayEndSettings);
}

/// <summary>
/// A saved search: its <see cref="Name"/> as shown in the sidebar and its <see cref="Query"/> (full-text text or a
/// tag filter), both raw outside text.
/// </summary>
internal sealed record SavedSearch
{
    public SavedSearch(string name, string query)
    {
        Name = name;
        Query = query;
    }

    /// <summary>The name, normalized (<see cref="SavedSearches.NormalizeName"/>).</summary>
    public string Name { get; }

    /// <summary>The query as typed, trimmed.</summary>
    public string Query { get; }

    /// <summary>
    /// Both values, for the desktop sidebar: Context.Desktop never names a <c>.Query</c> member (a source rule that
    /// keeps the calendar link's text unread).
    /// </summary>
    public void Deconstruct(out string name, out string query)
    {
        name = Name;
        query = Query;
    }

    /// <summary>Only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(SavedSearch);
}

/// <summary>The rules for saved searches, shared by <see cref="SettingsStore"/> and the desktop actions.</summary>
internal static class SavedSearches
{
    /// <summary>The most characters a name may have after normalizing.</summary>
    public const int MaxNameLength = 50;

    /// <summary>The most saved searches.</summary>
    public const int MaxCount = 50;

    /// <summary>
    /// The name trimmed with whitespace runs collapsed to one space; <c>null</c> when that is empty, longer than
    /// <see cref="MaxNameLength"/> or holds a control or format character (bidi marks, zero-width characters) or a lone surrogate.
    /// </summary>
    public static string? NormalizeName(string name)
    {
        string normalized = TagParser.CollapseWhitespace(name);
        if (normalized.Length == 0 || normalized.Length > MaxNameLength || normalized.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            || HasLoneSurrogate(normalized))
        {
            return null;
        }

        return normalized;
    }

    // A lone surrogate isn't text and would be written as a six-byte escape.
    private static bool HasLoneSurrogate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the trimmed <paramref name="query"/> can be searched: a tag filter (<see cref="TagFilter.IsTagFilter"/>)
    /// of at most <see cref="TagFilterView.MaxQueryLength"/> characters that parses, or else a full-text text of
    /// <see cref="SearchView.MinQueryLength"/> to <see cref="SearchView.MaxQueryLength"/> characters.
    /// </summary>
    public static bool IsValidQuery(string query)
    {
        string trimmed = query.Trim();
        if (TagFilter.IsTagFilter(trimmed))
        {
            return trimmed.Length <= TagFilterView.MaxQueryLength && TagFilter.TryParse(trimmed, out _, out _);
        }

        return trimmed.Length >= SearchView.MinQueryLength && trimmed.Length <= SearchView.MaxQueryLength;
    }

    /// <summary>
    /// True when every entry has a normalized valid name and a valid query, no two names are equal ignoring case and
    /// there are at most <see cref="MaxCount"/>.
    /// </summary>
    public static bool IsValidList(IReadOnlyList<SavedSearch> searches)
    {
        if (searches.Count > MaxCount)
        {
            return false;
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (SavedSearch search in searches)
        {
            if (SavedSearches.NormalizeName(search.Name) != search.Name
                || !IsValidQuery(search.Query)
                || !names.Add(search.Name))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A board (job 242): the tag whose notes and note todos it shows and its columns in order, both normalized
/// (<see cref="TagIndex.NormalizeTag"/>, lower-cased columns). Set by hand in <c>settings.json</c>; never written.
/// </summary>
internal sealed record BoardSetting
{
    public BoardSetting(string tag, IReadOnlyList<string> columns)
    {
        Tag = tag;
        Columns = columns;
    }

    /// <summary>The tag, normalized, without <c>#</c>.</summary>
    public string Tag { get; }

    /// <summary>The columns in settings order, lower-cased, distinct (<see cref="BoardView.NormalizeColumn"/>).</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(BoardSetting);
}

/// <summary>
/// <c>&lt;root&gt;/settings.json</c>: <c>{"dayEnd":{"worked":"yyyy-MM-ddTHH:mm","moved":"yyyy-MM-dd"}}</c>, both values
/// optional, <c>"savedSearches":[{"name":"…","query":"…"}]</c>, <c>"outlook":{"enabled":true|false}</c>, <c>"paste":{"fetchTitles":true|false}</c> and
/// <c>"boards":[{"tag":"…","columns":["…"]}]</c> (read only, see <see cref="ReadBoards"/>). Read through <see cref="BoundedFile"/>; written as UTF-8 without BOM through a temp file and a move,
/// keeping every other property. The automatic day's end (<see cref="DayEnd.AutoClose"/>) and the saved searches use it.
/// </summary>
internal sealed class SettingsStore
{
    /// <summary>The settings file's name in the root.</summary>
    public const string FileName = "settings.json";

    /// <summary>The largest settings file that is read (64 KiB).</summary>
    public const long MaxBytes = 64 * 1024;

    private const string DayEndKey = "dayEnd";
    private const string WorkedKey = "worked";
    private const string MovedKey = "moved";
    private const string WorkedFormat = "yyyy-MM-dd'T'HH:mm";
    private const string MovedFormat = "yyyy-MM-dd";
    private const string SavedSearchesKey = "savedSearches";
    private const string NameKey = "name";
    private const string QueryKey = "query";
    private const string OutlookKey = "outlook";
    private const string EnabledKey = "enabled";
    private const string PasteKey = "paste";
    private const string FetchTitlesKey = "fetchTitles";
    private const string BoardsKey = "boards";
    private const string TagKey = "tag";
    private const string ColumnsKey = "columns";

    /// <summary>The most boards.</summary>
    public const int MaxBoards = 50;

    /// <summary>The most columns of one board.</summary>
    public const int MaxColumns = 12;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string root;

    public SettingsStore(string root)
    {
        this.root = root;
    }

    /// <summary>The settings file's path.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>The <c>yyyy-MM-ddTHH:mm</c> stamp for a leaving at <paramref name="time"/> on <paramref name="day"/>.</summary>
    public static string Stamp(DateOnly day, TimeOnly time) =>
        day.ToDateTime(time).ToString(WorkedFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The day's end values; a missing file gives two <c>null</c>s. Throws <see cref="SettingsStoreException"/> when
    /// the file can't be read, is over <see cref="MaxBytes"/>, isn't a JSON object, or <c>dayEnd</c>, <c>worked</c> or
    /// <c>moved</c> has a bad value.
    /// </summary>
    public DayEndSettings ReadDayEnd() => DayEndOf(ReadRoot());

    /// <summary>
    /// Writes <paramref name="settings"/> into <c>dayEnd</c> (a <c>null</c> value removes its property), keeping every
    /// other property, creating the root folder if needed. Reads the file again first and throws
    /// <see cref="SettingsStoreException"/> (writing nothing) when it is broken; throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it can't be written.
    /// </summary>
    public void SaveDayEnd(DayEndSettings settings)
    {
        JsonObject document = ReadRoot();
        DayEndOf(document);
        if (document[DayEndKey] is not JsonObject dayEnd)
        {
            dayEnd = new JsonObject();
            document[DayEndKey] = dayEnd;
        }

        SetOrRemove(dayEnd, WorkedKey, settings.Worked);
        SetOrRemove(
            dayEnd, MovedKey, settings.Moved?.ToString(MovedFormat, CultureInfo.InvariantCulture));

        Write(document);
    }

    /// <summary>
    /// The saved searches in saved order; a missing file or <c>savedSearches</c> property gives an empty list. Names
    /// come back normalized (<see cref="SavedSearches.NormalizeName"/>). Throws <see cref="SettingsStoreException"/>
    /// when the file can't be read, or <c>savedSearches</c> isn't an array of at most <see cref="SavedSearches.MaxCount"/>
    /// objects with exactly a string <c>name</c> and <c>query</c> that keep the rules (a valid name, a valid query,
    /// no two names equal ignoring case).
    /// </summary>
    public IReadOnlyList<SavedSearch> ReadSavedSearches() => SavedSearchesOf(ReadRoot());

    /// <summary>
    /// Writes <paramref name="searches"/> as <c>savedSearches</c>, in order, keeping every other property, creating
    /// the root folder if needed. Throws <see cref="ArgumentException"/> for a list that breaks the rules. Reads the
    /// file again first and throws <see cref="SettingsStoreException"/> (writing nothing) when it is broken, its
    /// <c>dayEnd</c> and <c>savedSearches</c> included; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when it can't be written, <see cref="SettingsTooLargeException"/> (writing nothing) when the file would be over
    /// <see cref="MaxBytes"/>.
    /// </summary>
    public void SaveSavedSearches(IReadOnlyList<SavedSearch> searches)
    {
        if (!SavedSearches.IsValidList(searches))
        {
            throw new ArgumentException("Bad saved searches.", nameof(searches));
        }

        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        JsonArray array = [];
        foreach (SavedSearch search in searches)
        {
            array.Add(new JsonObject { [NameKey] = search.Name, [QueryKey] = search.Query });
        }

        document[SavedSearchesKey] = array;
        Write(document);
    }

    /// <summary>
    /// True when <c>outlook</c> is exactly <c>{"enabled":true}</c>. A missing file or section, or any other value
    /// (another property, a non-boolean <c>enabled</c>, no object), counts as off. Throws
    /// <see cref="SettingsStoreException"/> only when the file itself can't be read or isn't a JSON object.
    /// </summary>
    public bool ReadOutlook() => OutlookOf(ReadRoot()) ?? false;

    /// <summary>
    /// Writes <c>"outlook":{"enabled":<paramref name="enabled"/>}</c>, replacing whatever <c>outlook</c> held and
    /// keeping every other property, creating the root folder if needed. Reads the file again first and throws
    /// <see cref="SettingsStoreException"/> (writing nothing) when it is broken, its <c>dayEnd</c> and
    /// <c>savedSearches</c> included; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when it can't be written, <see cref="SettingsTooLargeException"/> (writing nothing) when the file would be over
    /// <see cref="MaxBytes"/>.
    /// </summary>
    public void SaveOutlook(bool enabled)
    {
        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[OutlookKey] = new JsonObject { [EnabledKey] = enabled };
        Write(document);
    }

    /// <summary>
    /// True when <c>paste</c> is exactly <c>{"fetchTitles":true}</c>: a pasted link's page may be downloaded for its
    /// title. A missing file or section, or any other value, counts as off. Throws <see cref="SettingsStoreException"/>
    /// only when the file itself can't be read or isn't a JSON object.
    /// </summary>
    public bool ReadFetchTitles() => FlagOf(ReadRoot(), PasteKey, FetchTitlesKey) ?? false;

    /// <summary>
    /// Writes <c>"paste":{"fetchTitles":<paramref name="enabled"/>}</c>, replacing whatever <c>paste</c> held and
    /// keeping every other property, creating the root folder if needed. Throws like <see cref="SaveOutlook"/>.
    /// </summary>
    public void SaveFetchTitles(bool enabled)
    {
        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[PasteKey] = new JsonObject { [FetchTitlesKey] = enabled };
        Write(document);
    }

    /// <summary>
    /// The boards in saved order; a missing file or <c>boards</c> property gives an empty list. Each tag goes through
    /// <see cref="TagIndex.NormalizeTag"/> and each column is lower-cased. Throws <see cref="SettingsStoreException"/>
    /// when the file can't be read, or <c>boards</c> isn't an array of at most <see cref="MaxBoards"/> objects with
    /// exactly a string <c>tag</c> (a valid tag, unique ignoring case) and an array <c>columns</c> of 1 to
    /// <see cref="MaxColumns"/> strings that are columns (<see cref="BoardView.NormalizeColumn"/>), distinct once lower-cased. No write of this
    /// store changes <c>boards</c>: every other write keeps it as it is.
    /// </summary>
    public IReadOnlyList<BoardSetting> ReadBoards() => BoardsOf(ReadRoot());

    // Non-ASCII text is written as is rather than as six-byte \uXXXX escapes, so the rules' longest names and queries
    // stay well under MaxBytes. Only the file is written this way; it is never put into HTML.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes <paramref name="document"/> through a temp file and a move. Throws <see cref="SettingsTooLargeException"/>,
    /// writing nothing, when it would be over <see cref="MaxBytes"/>.
    /// </summary>
    private void Write(JsonObject document)
    {
        string json = document.ToJsonString(WriteOptions) + "\n";
        byte[] bytes = Utf8NoBom.GetBytes(json);
        if (bytes.Length > MaxBytes)
        {
            throw new SettingsTooLargeException($"{FileName} would be over {MaxBytes} bytes.");
        }

        Directory.CreateDirectory(root);
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

    private static void SetOrRemove(JsonObject target, string key, string? value)
    {
        if (value is null)
        {
            target.Remove(key);
        }
        else
        {
            target[key] = value;
        }
    }

    private JsonObject ReadRoot()
    {
        byte[] bytes;
        try
        {
            if (Directory.Exists(FilePath))
            {
                throw new SettingsStoreException("Not a file.");
            }

            bytes = BoundedFile.Read(FilePath, MaxBytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new JsonObject();
        }
        catch (Exception ex) when (ex is not SettingsStoreException
            && ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new SettingsStoreException("Can't read settings.json.", ex);
        }

        try
        {
            ReadOnlySpan<byte> json = bytes;
            if (json.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            {
                json = json[3..];
            }

            JsonNode? node = JsonNode.Parse(
                json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (node is not JsonObject document)
            {
                throw new SettingsStoreException("Not an object.");
            }

            return document;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new SettingsStoreException("Not valid JSON.", ex);
        }
    }

    private static DayEndSettings DayEndOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(DayEndKey, out JsonNode? node))
        {
            return new DayEndSettings(null, null);
        }

        if (node is not JsonObject dayEnd)
        {
            throw new SettingsStoreException("Bad dayEnd.");
        }

        string? worked = null;
        if (dayEnd.TryGetPropertyValue(WorkedKey, out JsonNode? workedNode))
        {
            worked = StringOf(workedNode);
            if (worked is null || !DateTime.TryParseExact(
                worked, WorkedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new SettingsStoreException("Bad worked.");
            }
        }

        DateOnly? moved = null;
        if (dayEnd.TryGetPropertyValue(MovedKey, out JsonNode? movedNode))
        {
            if (StringOf(movedNode) is not { } text || !DateOnly.TryParseExact(
                text, MovedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
            {
                throw new SettingsStoreException("Bad moved.");
            }

            moved = day;
        }

        return new DayEndSettings(worked, moved);
    }

    private static List<SavedSearch> SavedSearchesOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(SavedSearchesKey, out JsonNode? node))
        {
            return [];
        }

        if (node is not JsonArray array || array.Count > SavedSearches.MaxCount)
        {
            throw new SettingsStoreException("Bad savedSearches.");
        }

        List<SavedSearch> searches = [];
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject entry
                || entry.Count != 2
                || !entry.TryGetPropertyValue(NameKey, out JsonNode? nameNode)
                || !entry.TryGetPropertyValue(QueryKey, out JsonNode? queryNode)
                || StringOf(nameNode) is not { } rawName
                || StringOf(queryNode) is not { } query
                || SavedSearches.NormalizeName(rawName) is not { } name
                || !SavedSearches.IsValidQuery(query))
            {
                throw new SettingsStoreException("Bad saved search.");
            }

            searches.Add(new SavedSearch(name, query));
        }

        if (!SavedSearches.IsValidList(searches))
        {
            throw new SettingsStoreException("Bad savedSearches.");
        }

        return searches;
    }

    private static List<BoardSetting> BoardsOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(BoardsKey, out JsonNode? node))
        {
            return [];
        }

        if (node is not JsonArray array || array.Count > MaxBoards)
        {
            throw new SettingsStoreException("Bad boards.");
        }

        List<BoardSetting> boards = [];
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject entry
                || entry.Count != 2
                || !entry.TryGetPropertyValue(TagKey, out JsonNode? tagNode)
                || !entry.TryGetPropertyValue(ColumnsKey, out JsonNode? columnsNode)
                || StringOf(tagNode) is not { } rawTag
                || !BoardView.TryNormalizeTag(rawTag, out string tag)
                || !tags.Add(tag)
                || columnsNode is not JsonArray columnArray
                || columnArray.Count < 1
                || columnArray.Count > MaxColumns)
            {
                throw new SettingsStoreException("Bad board.");
            }

            List<string> columns = [];
            foreach (JsonNode? columnNode in columnArray)
            {
                if (StringOf(columnNode) is not { } rawColumn
                    || BoardView.NormalizeColumn(rawColumn) is not { } column
                    || columns.Contains(column, StringComparer.Ordinal))
                {
                    throw new SettingsStoreException("Bad board column.");
                }

                columns.Add(column);
            }

            boards.Add(new BoardSetting(tag, columns));
        }

        return boards;
    }

    // The stored value of "outlook", or null when it is missing or anything other than exactly {"enabled":<bool>}.
    private static bool? OutlookOf(JsonObject document) => FlagOf(document, OutlookKey, EnabledKey);

    // The stored value of a section that is exactly {"<flag>":<bool>}, or null when it is missing or anything else.
    private static bool? FlagOf(JsonObject document, string key, string flag)
    {
        if (!document.TryGetPropertyValue(key, out JsonNode? node)
            || node is not JsonObject section
            || section.Count != 1
            || !section.TryGetPropertyValue(flag, out JsonNode? enabledNode)
            || enabledNode is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
