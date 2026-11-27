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

/// <summary>The desktop colour theme (job 341): follow the OS, or always light or dark.</summary>
internal enum AppTheme
{
    /// <summary>Follow the OS colour scheme.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
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

/// <summary>Why a repository folder can't be added (<see cref="Repositories.CheckAdd"/>).</summary>
internal enum RepositoryCheck
{
    /// <summary>The folder can be added.</summary>
    Ok,

    /// <summary>Nothing was typed.</summary>
    Blank,

    /// <summary>The path is not absolute (fully qualified).</summary>
    Relative,

    /// <summary>The path is over <see cref="Repositories.MaxPathLength"/> characters or holds a control character.</summary>
    TooLong,

    /// <summary>No directory exists at the path.</summary>
    Missing,

    /// <summary>The folder is already in the list (ignoring case).</summary>
    Duplicate,

    /// <summary>The list already holds <see cref="Repositories.MaxCount"/> folders.</summary>
    Full,
}

/// <summary>
/// The rules for the repository folders in <c>settings.json</c> (job 280): each entry trimmed, fully qualified, at most
/// <see cref="MaxPathLength"/> characters without control characters, distinct ignoring case, at most
/// <see cref="MaxCount"/> entries. Only <see cref="CheckAdd"/> checks that the folder exists.
/// </summary>
internal static class Repositories
{
    /// <summary>The most repository folders.</summary>
    public const int MaxCount = 20;

    /// <summary>The longest repository folder path.</summary>
    public const int MaxPathLength = 400;

    /// <summary>True for a stored entry's shape: trimmed, fully qualified, 1 to 400 characters, no control characters.</summary>
    public static bool IsValidStored(string folder) =>
        folder.Length > 0
        && folder.Length <= MaxPathLength
        && string.Equals(folder, folder.Trim(), StringComparison.Ordinal)
        && !folder.Any(char.IsControl)
        && Path.IsPathFullyQualified(folder);

    /// <summary>True when every entry has the stored shape, no two are equal ignoring case and there are at most 20.</summary>
    public static bool IsValidList(IReadOnlyList<string> folders) =>
        folders.Count <= MaxCount
        && folders.All(IsValidStored)
        && folders.Distinct(StringComparer.OrdinalIgnoreCase).Count() == folders.Count;

    /// <summary>
    /// Checks the typed <paramref name="typed"/> (trimmed into <paramref name="folder"/>) against the rules and the
    /// list <paramref name="current"/>, in this order: blank, too long, relative, missing, duplicate, full.
    /// </summary>
    public static RepositoryCheck CheckAdd(IReadOnlyList<string> current, string? typed, out string folder)
    {
        folder = (typed ?? string.Empty).Trim();
        if (folder.Length == 0)
        {
            return RepositoryCheck.Blank;
        }

        if (folder.Length > MaxPathLength || folder.Any(char.IsControl))
        {
            return RepositoryCheck.TooLong;
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            return RepositoryCheck.Relative;
        }

        if (!Directory.Exists(folder))
        {
            return RepositoryCheck.Missing;
        }

        string candidate = folder;
        if (current.Any(f => string.Equals(f, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return RepositoryCheck.Duplicate;
        }

        return current.Count >= MaxCount ? RepositoryCheck.Full : RepositoryCheck.Ok;
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
/// optional, <c>"savedSearches":[{"name":"…","query":"…"}]</c>, <c>"outlook":{"enabled":true|false}</c>, <c>"mailDigest":{"enabled":true|false}</c>, <c>"paste":{"fetchTitles":true|false}</c> and
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
    private const string MailDigestKey = "mailDigest";
    private const string PasteKey = "paste";
    private const string FetchTitlesKey = "fetchTitles";
    private const string BoardsKey = "boards";
    private const string TagKey = "tag";
    private const string ColumnsKey = "columns";
    private const string ScreenshotsKey = "screenshots";
    private const string FolderKey = "folder";
    private const string RepositoriesKey = "repositories";
    private const string AppearanceKey = "appearance";
    private const string ThemeKey = "theme";
    private const string SidebarsKey = "sidebars";
    private const string LeftKey = "left";
    private const string RightKey = "right";
    private const string WidthKey = "width";
    private const string CollapsedKey = "collapsed";

    /// <summary>The longest screenshot folder path.</summary>
    public const int MaxScreenshotFolderLength = 1024;

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
    /// True when <c>mailDigest</c> is exactly <c>{"enabled":true}</c> (job 286: the Mail digest may read the Inbox when
    /// the user clicks Read mail). A missing file or section, or any other value, counts as off. Throws
    /// <see cref="SettingsStoreException"/> only when the file itself can't be read or isn't a JSON object.
    /// </summary>
    public bool ReadMailDigest() => FlagOf(ReadRoot(), MailDigestKey, EnabledKey) ?? false;

    /// <summary>
    /// Writes <c>"mailDigest":{"enabled":<paramref name="enabled"/>}</c>, replacing whatever <c>mailDigest</c> held and
    /// keeping every other property, creating the root folder if needed. Throws like <see cref="SaveOutlook"/>.
    /// </summary>
    public void SaveMailDigest(bool enabled)
    {
        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[MailDigestKey] = new JsonObject { [EnabledKey] = enabled };
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
    /// The saved theme (job 341): <c>appearance</c> exactly <c>{"theme":"system"|"light"|"dark"}</c> (lower-case
    /// strings). A missing file or section, or any other value, gives <see cref="AppTheme.System"/>. Throws
    /// <see cref="SettingsStoreException"/> only when the file itself can't be read or isn't a JSON object.
    /// </summary>
    public AppTheme ReadTheme() => ThemeOf(ReadRoot());

    /// <summary>
    /// Writes <c>"appearance":{"theme":"<lower-case name>"}</c>, replacing whatever <c>appearance</c> held and keeping
    /// every other property, creating the root folder if needed. Throws like <see cref="SaveOutlook"/>.
    /// </summary>
    public void SaveTheme(AppTheme theme)
    {
        string name = ThemeName(theme);
        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[AppearanceKey] = new JsonObject { [ThemeKey] = name };
        Write(document);
    }

    /// <summary>
    /// The saved sidebar layout (job 490): <c>"sidebars":{"left":{"width":N,"collapsed":B},"right":{…}}</c>. A missing
    /// file or section gives <see cref="SidebarLayout.Default"/>; a side that isn't an object with an integer
    /// <c>width</c> in its bounds and a boolean <c>collapsed</c> gives that side's default. Throws
    /// <see cref="SettingsStoreException"/> only when the file itself can't be read or isn't a JSON object.
    /// </summary>
    public SidebarLayout ReadSidebars() => SidebarsOf(ReadRoot());

    /// <summary>
    /// Writes <c>"sidebars":{"left":{"width":N,"collapsed":B},"right":{…}}</c>, replacing whatever <c>sidebars</c>
    /// held and keeping every other property. Throws <see cref="ArgumentOutOfRangeException"/> (nothing read or
    /// written) for a width outside its bounds, otherwise like <see cref="SaveOutlook"/>.
    /// </summary>
    public void SaveSidebars(SidebarLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!SidebarLayout.IsWidth(SidebarSide.Left, layout.LeftWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), "The left width is out of range.");
        }

        if (!SidebarLayout.IsWidth(SidebarSide.Right, layout.RightWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), "The right width is out of range.");
        }

        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[SidebarsKey] = new JsonObject
        {
            [LeftKey] = new JsonObject { [WidthKey] = layout.LeftWidth, [CollapsedKey] = layout.LeftCollapsed },
            [RightKey] = new JsonObject { [WidthKey] = layout.RightWidth, [CollapsedKey] = layout.RightCollapsed },
        };
        Write(document);
    }

    /// <summary>The stored and JS name of <paramref name="theme"/>: <c>system</c>, <c>light</c> or <c>dark</c>.</summary>
    public static string ThemeName(AppTheme theme) => theme switch
    {
        AppTheme.System => "system",
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme)),
    };

    /// <summary>
    /// The watched screenshot folder (job 260), or null when the file or <c>screenshots</c> is missing. Throws
    /// <see cref="SettingsStoreException"/> when the file can't be read, or <c>screenshots</c> isn't an object with
    /// exactly a string <c>folder</c> that is a fully qualified path of at most
    /// <see cref="MaxScreenshotFolderLength"/> characters without control characters.
    /// </summary>
    public string? ReadScreenshotFolder() => ScreenshotFolderOf(ReadRoot());

    /// <summary>
    /// Writes <c>"screenshots":{"folder":<paramref name="folder"/>}</c>, replacing whatever <c>screenshots</c> held and
    /// keeping every other property. Throws <see cref="ArgumentException"/> for a folder
    /// <see cref="IsValidScreenshotFolder"/> refuses, otherwise like <see cref="SaveOutlook"/>.
    /// </summary>
    public void SaveScreenshotFolder(string folder)
    {
        if (!IsValidScreenshotFolder(folder))
        {
            throw new ArgumentException("Not a valid screenshot folder.", nameof(folder));
        }

        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        document[ScreenshotsKey] = new JsonObject { [FolderKey] = folder };
        Write(document);
    }

    /// <summary>
    /// Removes <c>screenshots</c>, keeping every other property; writes nothing when it is not there. Returns whether
    /// it was there. Throws like <see cref="SaveOutlook"/>.
    /// </summary>
    public bool RemoveScreenshotFolder()
    {
        JsonObject document = ReadRoot();
        if (!document.ContainsKey(ScreenshotsKey))
        {
            return false;
        }

        DayEndOf(document);
        SavedSearchesOf(document);
        document.Remove(ScreenshotsKey);
        Write(document);
        return true;
    }

    /// <summary>
    /// True for a stored screenshot folder's shape: fully qualified, 1 to <see cref="MaxScreenshotFolderLength"/>
    /// characters, no control characters. Whether it exists is not checked.
    /// </summary>
    public static bool IsValidScreenshotFolder(string folder) =>
        folder.Length > 0
        && folder.Length <= MaxScreenshotFolderLength
        && !folder.Any(char.IsControl)
        && Path.IsPathFullyQualified(folder);

    /// <summary>
    /// The repository folders (job 280) in saved order; a missing file or <c>repositories</c> property gives an empty
    /// list. Throws <see cref="SettingsStoreException"/> when the file can't be read, or <c>repositories</c> isn't an
    /// array of at most <see cref="Repositories.MaxCount"/> strings of the stored shape
    /// (<see cref="Repositories.IsValidStored"/>), distinct ignoring case. Whether each folder exists is not checked.
    /// </summary>
    public IReadOnlyList<string> ReadRepositories() => RepositoriesOf(ReadRoot());

    /// <summary>
    /// Writes <paramref name="folders"/> as <c>repositories</c>, in order (an empty list removes the property),
    /// keeping every other property. Throws <see cref="ArgumentException"/> for a list that breaks the stored rules,
    /// otherwise like <see cref="SaveOutlook"/>; the file's <c>repositories</c> is re-checked too.
    /// </summary>
    public void SaveRepositories(IReadOnlyList<string> folders)
    {
        if (!Repositories.IsValidList(folders))
        {
            throw new ArgumentException("Bad repository folders.", nameof(folders));
        }

        JsonObject document = ReadRoot();
        DayEndOf(document);
        SavedSearchesOf(document);
        RepositoriesOf(document);
        if (folders.Count == 0)
        {
            document.Remove(RepositoriesKey);
        }
        else
        {
            JsonArray array = [];
            foreach (string folder in folders)
            {
                array.Add(folder);
            }

            document[RepositoriesKey] = array;
        }

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

        PrivateFile.CreateFolder(root);
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = PrivateFile.OpenNew(temp))
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

    private static string? ScreenshotFolderOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(ScreenshotsKey, out JsonNode? node))
        {
            return null;
        }

        if (node is not JsonObject section
            || section.Count != 1
            || !section.TryGetPropertyValue(FolderKey, out JsonNode? folderNode)
            || StringOf(folderNode) is not string folder
            || !IsValidScreenshotFolder(folder))
        {
            throw new SettingsStoreException("Not a valid screenshots value.");
        }

        return folder;
    }

    private static List<string> RepositoriesOf(JsonObject document)
    {
        List<string> folders = [];
        if (!document.TryGetPropertyValue(RepositoriesKey, out JsonNode? node))
        {
            return folders;
        }

        if (node is not JsonArray array || array.Count > Repositories.MaxCount)
        {
            throw new SettingsStoreException("Not a valid repositories value.");
        }

        foreach (JsonNode? item in array)
        {
            if (StringOf(item) is not string folder || !Repositories.IsValidStored(folder))
            {
                throw new SettingsStoreException("Not a valid repositories value.");
            }

            folders.Add(folder);
        }

        if (!Repositories.IsValidList(folders))
        {
            throw new SettingsStoreException("Not a valid repositories value.");
        }

        return folders;
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

    // The stored theme, or System when "appearance" is missing or anything other than exactly {"theme":<known name>}.
    private static AppTheme ThemeOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(AppearanceKey, out JsonNode? node)
            || node is not JsonObject section
            || section.Count != 1
            || !section.TryGetPropertyValue(ThemeKey, out JsonNode? themeNode))
        {
            return AppTheme.System;
        }

        return StringOf(themeNode) switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.System,
        };
    }

    // The stored sidebars; each side that isn't {"width":<integer in bounds>,"collapsed":<bool>} gets its default.
    private static SidebarLayout SidebarsOf(JsonObject document)
    {
        SidebarLayout layout = SidebarLayout.Default;
        if (!document.TryGetPropertyValue(SidebarsKey, out JsonNode? node) || node is not JsonObject section)
        {
            return layout;
        }

        foreach ((SidebarSide side, string key) in new[] { (SidebarSide.Left, LeftKey), (SidebarSide.Right, RightKey) })
        {
            if (section.TryGetPropertyValue(key, out JsonNode? sideNode)
                && sideNode is JsonObject stored
                && IntOf(stored, WidthKey) is { } width
                && SidebarLayout.IsWidth(side, width)
                && BoolOf(stored, CollapsedKey) is { } collapsed)
            {
                layout = layout.WithWidth(side, width).WithCollapsed(side, collapsed);
            }
        }

        return layout;
    }

    private static int? IntOf(JsonObject section, string key) =>
        section.TryGetPropertyValue(key, out JsonNode? node)
            && node is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && value.TryGetValue(out JsonElement element)
            && element.TryGetInt32(out int number)
            ? number
            : null;

    private static bool? BoolOf(JsonObject section, string key) =>
        section.TryGetPropertyValue(key, out JsonNode? node) && node is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
