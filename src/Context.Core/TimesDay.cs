using System.Globalization;
using System.Text.Json;

namespace Context;

/// <summary>
/// Where the Times app keeps its day files: <c>TIMES_DATA_DIR</c> when it is set, else
/// <c>&lt;ApplicationData&gt;/Times</c>. Like Times, the value is never trimmed or expanded; it is only checked.
/// </summary>
internal static class TimesFolder
{
    /// <summary>The environment variable that moves Times' data folder (read by the front ends' <c>Program.cs</c>).</summary>
    public const string VariableName = "TIMES_DATA_DIR";

    /// <summary>
    /// The Times folder for the value of <see cref="VariableName"/>: the value itself when it is an absolute path
    /// with no whitespace at either end, <c>&lt;ApplicationData&gt;/Times</c> when it is <c>null</c> or empty, and
    /// <c>null</c> (no Times folder) for any other value.
    /// </summary>
    public static string? Resolve(string? timesDataDir) =>
        Resolve(timesDataDir, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Like <see cref="Resolve(string?)"/> with the application data folder given (tests).</summary>
    internal static string? Resolve(string? timesDataDir, string? appData)
    {
        if (string.IsNullOrEmpty(timesDataDir))
        {
            return string.IsNullOrEmpty(appData) ? null : Path.Combine(appData, "Times");
        }

        if (timesDataDir.Trim().Length != timesDataDir.Length || !Path.IsPathFullyQualified(timesDataDir))
        {
            return null;
        }

        return timesDataDir;
    }
}

/// <summary>What a Times entry books.</summary>
internal enum TimesEntryKind
{
    Arrival,
    Leaving,
    BreakStart,
    BreakEnd,
    Checkpoint,
}

/// <summary>Why a Times day is off work.</summary>
internal enum TimesAbsence
{
    Vacation,
    Sick,
    PublicHoliday,
}

/// <summary>What <see cref="TimesDay.Read"/> found.</summary>
internal enum TimesDayKind
{
    /// <summary>There is no day file (or no Times folder).</summary>
    Missing,

    /// <summary>The day file can't be read: too big, not a regular file, bad JSON or an unknown value.</summary>
    Unreadable,

    /// <summary>The day is marked as an absence (<see cref="TimesDay.Absence"/>).</summary>
    Absence,

    /// <summary>The day has entries (possibly none).</summary>
    Entries,
}

/// <summary>One Times booking: kind, local time, and for a checkpoint its name and optional work item.</summary>
internal sealed record TimesEntry
{
    public TimesEntry(TimesEntryKind kind, TimeOnly time, string? name = null, int? workItem = null)
    {
        Kind = kind;
        Time = time;
        Name = name;
        WorkItem = workItem;
    }

    public TimesEntryKind Kind { get; }

    public TimeOnly Time { get; }

    /// <summary>The checkpoint's name, raw; <c>null</c> for every other kind.</summary>
    public string? Name { get; }

    /// <summary>The checkpoint's work item; <c>null</c> when it has none.</summary>
    public int? WorkItem { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TimesEntry);
}

/// <summary>
/// One row of Times' checkpoint summary: a work item (with every name it was booked under, in first-booked order)
/// or, for checkpoints without a work item, one name.
/// </summary>
internal sealed record TimesGroup
{
    public TimesGroup(int? workItem, IReadOnlyList<string> names, TimeSpan total, bool open)
    {
        WorkItem = workItem;
        Names = names;
        Total = total;
        Open = open;
    }

    public int? WorkItem { get; }

    /// <summary>The names, raw.</summary>
    public IReadOnlyList<string> Names { get; }

    public TimeSpan Total { get; }

    /// <summary>True when one of the group's checkpoints has no entry that ends it.</summary>
    public bool Open { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TimesGroup);
}

/// <summary>
/// A Times day file (<c>&lt;folder&gt;/yyyy-MM-dd.json</c>), read only, with the figures Times itself shows:
/// arrival, leaving, work time and the checkpoint groups. Never writes anything.
/// </summary>
internal sealed class TimesDay
{
    /// <summary>The largest day file that is read (64 KiB).</summary>
    public const long MaxBytes = 64 * 1024;

    private const string TimeFormat = "HH:mm";

    private TimesDay(TimesDayKind kind, TimesAbsence? absence, IReadOnlyList<TimesEntry> entries)
    {
        Kind = kind;
        Absence = absence;
        Entries = entries;
    }

    public TimesDayKind Kind { get; }

    /// <summary>The absence for <see cref="TimesDayKind.Absence"/>, else <c>null</c>.</summary>
    public TimesAbsence? Absence { get; }

    /// <summary>The entries, stable-sorted by time; empty unless <see cref="Kind"/> is <see cref="TimesDayKind.Entries"/>.</summary>
    public IReadOnlyList<TimesEntry> Entries { get; }

    /// <summary>The first arrival, or <c>null</c>.</summary>
    public TimeOnly? Arrival => Entries.FirstOrDefault(e => e.Kind == TimesEntryKind.Arrival)?.Time;

    /// <summary>The last leaving, or <c>null</c>.</summary>
    public TimeOnly? Leaving => Entries.LastOrDefault(e => e.Kind == TimesEntryKind.Leaving)?.Time;

    /// <summary>True when the day has a leaving and its walk ends off work.</summary>
    public bool HasLeft => Leaving is not null && FinalState() == State.Off;

    /// <summary>
    /// The work time, by Times' walk: off, then an arrival starts working, a break start pauses it, a break end
    /// resumes it and a leaving stops it (also an open break). Time counts only while working, up to the last entry.
    /// </summary>
    public TimeSpan WorkTime
    {
        get
        {
            TimeSpan total = TimeSpan.Zero;
            State state = State.Off;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (i > 0 && state == State.Working)
                {
                    total += Entries[i].Time - Entries[i - 1].Time;
                }

                state = Next(state, Entries[i].Kind);
            }

            return total;
        }
    }

    /// <summary>
    /// Times' checkpoint summary for a day that is not today: groups in the order of their first checkpoint. A
    /// checkpoint runs to the next checkpoint, break start or leaving; with none, it counts nothing and its group is open.
    /// </summary>
    public IReadOnlyList<TimesGroup> Groups
    {
        get
        {
            List<GroupBuilder> groups = [];
            Dictionary<int, GroupBuilder> byWorkItem = [];
            Dictionary<string, GroupBuilder> byName = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                TimesEntry entry = Entries[i];
                if (entry.Kind != TimesEntryKind.Checkpoint)
                {
                    continue;
                }

                string name = entry.Name ?? string.Empty;
                GroupBuilder? group;
                if (entry.WorkItem is { } workItem)
                {
                    if (!byWorkItem.TryGetValue(workItem, out group))
                    {
                        group = new GroupBuilder(workItem);
                        byWorkItem.Add(workItem, group);
                        groups.Add(group);
                    }
                }
                else if (!byName.TryGetValue(name, out group))
                {
                    group = new GroupBuilder(null);
                    byName.Add(name, group);
                    groups.Add(group);
                }

                if (!group.Names.Contains(name, StringComparer.Ordinal))
                {
                    group.Names.Add(name);
                }

                TimesEntry? end = EndOf(i);
                if (end is null)
                {
                    group.Open = true;
                }
                else
                {
                    group.Total += end.Time - entry.Time;
                }
            }

            return groups.Select(g => new TimesGroup(g.WorkItem, g.Names, g.Total, g.Open)).ToList();
        }
    }

    /// <summary>
    /// Reads <c>&lt;folder&gt;/yyyy-MM-dd.json</c> through <see cref="BoundedFile"/> (at most <see cref="MaxBytes"/>).
    /// A <c>null</c> folder or a missing file is <see cref="TimesDayKind.Missing"/>; a directory, an oversized or
    /// non-seekable file, bad JSON, an unknown kind or absence or a bad time is <see cref="TimesDayKind.Unreadable"/>.
    /// Reads both the <c>{"entries":[...]}</c> format and the legacy <c>{"arrival","leaving"}</c> one. Never throws,
    /// never writes.
    /// </summary>
    public static TimesDay Read(string? folder, DateOnly day)
    {
        if (folder is null)
        {
            return new TimesDay(TimesDayKind.Missing, null, []);
        }

        byte[] bytes;
        try
        {
            string path = Path.Combine(folder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
            if (Directory.Exists(path))
            {
                return Unreadable();
            }

            bytes = BoundedFile.Read(path, MaxBytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new TimesDay(TimesDayKind.Missing, null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return Unreadable();
        }

        return Parse(bytes);
    }

    /// <summary>The day in <paramref name="bytes"/> (UTF-8 JSON, a BOM allowed); never throws.</summary>
    internal static TimesDay Parse(byte[] bytes)
    {
        ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            json = json[3..];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return FromRoot(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException
            or FormatException)
        {
            return Unreadable();
        }
    }

    private static TimesDay Unreadable() => new(TimesDayKind.Unreadable, null, []);

    private static TimesDay FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        JsonElement? entriesElement = null;
        TimesAbsence? absence = null;
        TimeOnly? arrival = null;
        TimeOnly? leaving = null;
        bool hasLegacy = false;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException();
            }

            switch (property.Name)
            {
                case "entries":
                    entriesElement = property.Value;
                    break;
                case "absence":
                    absence = ReadAbsence(property.Value);
                    break;
                case "arrival":
                    hasLegacy = true;
                    arrival = ReadOptionalTime(property.Value);
                    break;
                case "leaving":
                    hasLegacy = true;
                    leaving = ReadOptionalTime(property.Value);
                    break;
                default:
                    // Unknown properties (corrections and later ones) are ignored, as Times does.
                    break;
            }
        }

        List<TimesEntry> entries = [];
        if (entriesElement is { } list)
        {
            if (hasLegacy)
            {
                throw new JsonException();
            }

            entries = ReadEntries(list);
        }
        else
        {
            if (arrival is { } a)
            {
                entries.Add(new TimesEntry(TimesEntryKind.Arrival, a));
            }

            if (leaving is { } l)
            {
                entries.Add(new TimesEntry(TimesEntryKind.Leaving, l));
            }
        }

        if (absence is { } kind)
        {
            if (hasLegacy || entries.Count > 0)
            {
                throw new JsonException();
            }

            return new TimesDay(TimesDayKind.Absence, kind, []);
        }

        // OrderBy is stable, so entries at the same time keep their file order.
        return new TimesDay(TimesDayKind.Entries, null, entries.OrderBy(e => e.Time).ToList());
    }

    private static List<TimesEntry> ReadEntries(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException();
        }

        List<TimesEntry> entries = [];
        foreach (JsonElement item in list.EnumerateArray())
        {
            entries.Add(ReadEntry(item));
        }

        return entries;
    }

    private static TimesEntry ReadEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException();
        }

        TimesEntryKind? kind = null;
        TimeOnly? time = null;
        JsonElement? name = null;
        JsonElement? workItem = null;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException();
            }

            switch (property.Name)
            {
                case "kind":
                    kind = ReadKind(property.Value);
                    break;
                case "time":
                    time = ReadTime(property.Value);
                    break;
                case "name":
                    name = property.Value;
                    break;
                case "workItem":
                    workItem = property.Value;
                    break;
                default:
                    break;
            }
        }

        if (kind is not { } k || time is not { } t)
        {
            throw new JsonException();
        }

        if (k != TimesEntryKind.Checkpoint)
        {
            // Like Times: a name or work item on another kind is ignored.
            return new TimesEntry(k, t);
        }

        if (name is not { ValueKind: JsonValueKind.String } n || n.GetString() is not { } text
            || text.Trim().Length == 0)
        {
            throw new JsonException();
        }

        int? number = null;
        if (workItem is { } w)
        {
            if (w.ValueKind != JsonValueKind.Number || !w.TryGetInt32(out int value) || value < 1)
            {
                throw new JsonException();
            }

            number = value;
        }

        return new TimesEntry(k, t, text, number);
    }

    private static TimesEntryKind ReadKind(JsonElement value) => ReadString(value) switch
    {
        "arrival" => TimesEntryKind.Arrival,
        "leaving" => TimesEntryKind.Leaving,
        "breakStart" => TimesEntryKind.BreakStart,
        "breakEnd" => TimesEntryKind.BreakEnd,
        "checkpoint" => TimesEntryKind.Checkpoint,
        _ => throw new JsonException(),
    };

    private static TimesAbsence ReadAbsence(JsonElement value) => ReadString(value) switch
    {
        "vacation" => TimesAbsence.Vacation,
        "sick" => TimesAbsence.Sick,
        "publicHoliday" => TimesAbsence.PublicHoliday,
        _ => throw new JsonException(),
    };

    private static TimeOnly? ReadOptionalTime(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : ReadTime(value);

    private static TimeOnly ReadTime(JsonElement value)
    {
        if (!TimeOnly.TryParseExact(
            ReadString(value), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            throw new JsonException();
        }

        return time;
    }

    private static string? ReadString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The entry that ends the checkpoint at index: the next checkpoint, break start or leaving, or null.
    private TimesEntry? EndOf(int index)
    {
        for (int j = index + 1; j < Entries.Count; j++)
        {
            if (Entries[j].Kind is TimesEntryKind.Checkpoint or TimesEntryKind.BreakStart or TimesEntryKind.Leaving)
            {
                return Entries[j];
            }
        }

        return null;
    }

    private State FinalState()
    {
        State state = State.Off;
        foreach (TimesEntry entry in Entries)
        {
            state = Next(state, entry.Kind);
        }

        return state;
    }

    private static State Next(State state, TimesEntryKind kind) => (state, kind) switch
    {
        (State.Off, TimesEntryKind.Arrival) => State.Working,
        (State.Working, TimesEntryKind.BreakStart) => State.OnBreak,
        (State.OnBreak, TimesEntryKind.BreakEnd) => State.Working,
        (State.Working or State.OnBreak, TimesEntryKind.Leaving) => State.Off,
        _ => state,
    };

    private enum State
    {
        Off,
        Working,
        OnBreak,
    }

    private sealed class GroupBuilder
    {
        public GroupBuilder(int? workItem)
        {
            WorkItem = workItem;
        }

        public int? WorkItem { get; }

        public List<string> Names { get; } = [];

        public TimeSpan Total { get; set; }

        public bool Open { get; set; }
    }
}
