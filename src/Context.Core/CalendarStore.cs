using System.Text;

namespace Context;

/// <summary>The appointments of one day, and the calendar files that couldn't be read.</summary>
internal sealed record CalendarDay
{
    public CalendarDay(IReadOnlyList<Appointment> appointments, IReadOnlyList<string> unreadable)
    {
        Appointments = appointments;
        Unreadable = unreadable;
    }

    /// <summary>All-day events first, then by start, then by title (ordinal, ignoring case).</summary>
    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>
    /// File names (without folder) that couldn't be read, sorted. An empty name means the
    /// <c>calendar</c> folder itself couldn't be listed.
    /// </summary>
    public IReadOnlyList<string> Unreadable { get; }
}

/// <summary>
/// Reads the <c>*.ics</c> files directly in <c>&lt;root&gt;/calendar/</c> (no subfolders) with
/// <see cref="IcsParser"/>. Read-only: it never creates, writes or deletes anything.
/// </summary>
internal sealed class CalendarStore
{
    /// <summary>The name of the folder in the root that holds the <c>.ics</c> files.</summary>
    public const string FolderName = "calendar";

    /// <summary>Files larger than this (5 MB) are not read and count as unreadable.</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;

    private const string Extension = ".ics";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeZoneInfo local;

    /// <param name="root">The folder that holds <c>calendar/</c>.</param>
    /// <param name="local">The zone events are converted to.</param>
    public CalendarStore(string root, TimeZoneInfo local)
    {
        Folder = Path.Combine(root, FolderName);
        this.local = local;
    }

    /// <summary>The full path of the <c>calendar</c> folder.</summary>
    public string Folder { get; }

    /// <summary>The zone events are converted to and days are counted in.</summary>
    public TimeZoneInfo Zone => local;

    /// <summary>
    /// Returns the events whose local span overlaps <paramref name="day"/> (<c>[day 00:00, day+1 00:00)</c>),
    /// plus zero-length events that start on it, and the names of the files that couldn't be read
    /// (I/O or access errors, not iCalendar, or over <see cref="MaxFileBytes"/>).
    /// </summary>
    public CalendarDay Day(DateOnly day) => Day(day, []);

    /// <summary>
    /// Like <see cref="Day(DateOnly)"/>, but <paramref name="extra"/> events (for example from the published
    /// calendar link) are filtered and sorted together with the events from the files.
    /// </summary>
    public CalendarDay Day(DateOnly day, IReadOnlyList<Appointment> extra) => Day(day, extra, null);

    internal CalendarDay Day(DateOnly day, IReadOnlyList<Appointment> extra, IEnumerable<(string Name, string? Text)>? sources)
    {
        DateTime from = day.ToDateTime(TimeOnly.MinValue);
        DateTime to = from.AddDays(1);
        return Read(extra, a => Overlaps(a, from, to), text => IcsParser.Parse(text, local, day), sources);
    }

    /// <summary>
    /// Every event in the files plus <paramref name="extra"/>, sorted like <see cref="Day(DateOnly, IReadOnlyList{Appointment})"/>
    /// (all-day first, then by start, then by title), and the names of the files that couldn't be read.
    /// </summary>
    public CalendarDay All(IReadOnlyList<Appointment> extra) =>
        Read(extra, _ => true, text => IcsParser.Parse(text, local));

    public DateOnly? Oldest(DateOnly through, string? cached, IEnumerable<(string Name, string? Text)> sources)
    {
        DateOnly? unsearched = null;
        void IncludeUnsearched(DateOnly lower)
        {
            if (lower <= through && (unsearched is null || lower < unsearched))
            {
                unsearched = lower;
            }
        }

        IReadOnlyList<Appointment> feed = [];
        if (cached is not null)
        {
            try
            {
                feed = IcsParser.HistoryStarts(cached, local, through, IncludeUnsearched);
            }
            catch (FormatException)
            {
                // Readable calendar files still contribute when the cached copy is malformed.
            }
        }
        return Read(feed, a => DateOnly.FromDateTime(a.Start) <= through,
            text => IcsParser.HistoryStarts(text, local, through, IncludeUnsearched), sources)
            .Appointments.Select(a => (DateOnly?)DateOnly.FromDateTime(a.Start)).Append(unsearched).Min();
    }

    private CalendarDay Read(
        IReadOnlyList<Appointment> extra,
        Func<Appointment, bool> keep,
        Func<string, IReadOnlyList<Appointment>> parse,
        IEnumerable<(string Name, string? Text)>? sources = null)
    {
        List<Appointment> appointments = extra.Where(keep).ToList();
        List<string> unreadable = [];
        foreach ((string name, string? text) in sources ?? Files(Folder, unreadable).Select(p => (Path.GetFileName(p), ReadText(p))))
        {
            if (text is null)
            {
                unreadable.Add(name);
                continue;
            }

            try
            {
                foreach (Appointment appointment in parse(text))
                {
                    if (keep(appointment))
                    {
                        appointments.Add(appointment);
                    }
                }
            }
            catch (FormatException)
            {
                unreadable.Add(name);
            }
        }

        List<Appointment> sorted = appointments
            .OrderBy(a => a.AllDay ? 0 : 1)
            .ThenBy(a => a.Start)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        unreadable.Sort(StringComparer.OrdinalIgnoreCase);
        return new CalendarDay(sorted, unreadable);
    }

    /// <summary>
    /// True when the appointment's span overlaps <c>[from, to)</c>, or it has no length and starts in it.
    /// </summary>
    internal static bool Overlaps(Appointment appointment, DateTime from, DateTime to)
    {
        if (appointment.End < appointment.Start)
        {
            return false;
        }

        if (appointment.Start == appointment.End)
        {
            return appointment.Start >= from && appointment.Start < to;
        }

        return appointment.Start < to && appointment.End > from;
    }

    /// <summary>The top-level <c>*.ics</c> files, sorted by name; none when the folder doesn't exist.</summary>
    internal static List<string> Files(string folder, List<string> unreadable)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        EnumerationOptions options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
        };
        try
        {
            return Directory.EnumerateFiles(folder, "*" + Extension, options)
                .Where(p => p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(string.Empty);
            return [];
        }
    }

    /// <summary>
    /// The file's text (UTF-8, BOM dropped), or <c>null</c> if it can't be read or is larger than
    /// <see cref="MaxFileBytes"/>.
    /// </summary>
    internal static string? ReadText(string path)
    {
        try
        {
            return Decode(BoundedFile.Read(path, MaxFileBytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string ReadText(FileStream file) => Decode(BoundedFile.Read(file, MaxFileBytes));

    private static string Decode(byte[] bytes)
    {
        string text = Utf8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
}
