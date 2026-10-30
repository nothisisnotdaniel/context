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
    public CalendarDay Day(DateOnly day, IReadOnlyList<Appointment> extra)
    {
        DateTime from = day.ToDateTime(TimeOnly.MinValue);
        DateTime to = from.AddDays(1);
        List<Appointment> appointments = extra.Where(a => Overlaps(a, from, to)).ToList();
        List<string> unreadable = [];
        foreach (string path in Files(unreadable))
        {
            string name = Path.GetFileName(path);
            string? text = ReadText(path);
            if (text is null)
            {
                unreadable.Add(name);
                continue;
            }

            try
            {
                foreach (Appointment appointment in IcsParser.Parse(text, local))
                {
                    if (Overlaps(appointment, from, to))
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

    private static bool Overlaps(Appointment appointment, DateTime from, DateTime to)
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
    private List<string> Files(List<string> unreadable)
    {
        if (!Directory.Exists(Folder))
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
            return Directory.EnumerateFiles(Folder, "*" + Extension, options)
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

    /// <summary>The file's text (UTF-8, BOM dropped), or <c>null</c> if it can't be read or is too large.</summary>
    internal static string? ReadText(string path)
    {
        try
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > MaxFileBytes)
            {
                return null;
            }

            // Read at most one byte past the cap, in case the file grows while it is read.
            byte[] buffer = new byte[(int)stream.Length + 1];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total == buffer.Length && buffer.Length <= MaxFileBytes)
                {
                    Array.Resize(ref buffer, (int)Math.Min(buffer.Length * 2L, MaxFileBytes + 1));
                }
            }

            if (total > MaxFileBytes)
            {
                return null;
            }

            string text = Utf8.GetString(buffer, 0, total);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
