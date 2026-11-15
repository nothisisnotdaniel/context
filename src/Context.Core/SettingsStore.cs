using System.Globalization;
using System.Text;
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
/// <c>&lt;root&gt;/settings.json</c>: <c>{"dayEnd":{"worked":"yyyy-MM-ddTHH:mm","moved":"yyyy-MM-dd"}}</c>, both values
/// optional. Read through <see cref="BoundedFile"/>; written as UTF-8 without BOM through a temp file and a move,
/// keeping every other property. Only the automatic day's end (<see cref="DayEnd.AutoClose"/>) uses it.
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

        string json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        Directory.CreateDirectory(root);
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Utf8NoBom.GetBytes(json));
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

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
