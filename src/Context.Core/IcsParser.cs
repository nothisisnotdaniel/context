using System.Globalization;
using System.Text;

namespace Context;

/// <summary>One calendar event, with its times in the zone it was parsed for.</summary>
internal sealed record Appointment
{
    /// <param name="title"><c>SUMMARY</c>, or an empty string when it is missing.</param>
    /// <param name="location"><c>LOCATION</c>, or <c>null</c> when it is missing.</param>
    /// <param name="start">Start in the local zone (all-day events: midnight of the first day).</param>
    /// <param name="end">End in the local zone (all-day events: midnight after the last day, exclusive).</param>
    /// <param name="allDay">True for a <c>VALUE=DATE</c> start.</param>
    /// <param name="attendees">The <c>CN</c> names of the <c>ATTENDEE</c>s; none when left out.</param>
    public Appointment(
        string title,
        string? location,
        DateTime start,
        DateTime end,
        bool allDay,
        IReadOnlyList<string>? attendees = null)
    {
        Title = title;
        Location = location;
        Start = start;
        End = end;
        AllDay = allDay;
        Attendees = attendees ?? [];
    }

    /// <summary><c>SUMMARY</c>, or an empty string when it is missing.</summary>
    public string Title { get; }

    /// <summary><c>LOCATION</c>, or <c>null</c> when it is missing.</summary>
    public string? Location { get; }

    /// <summary>Start in the local zone.</summary>
    public DateTime Start { get; }

    /// <summary>End in the local zone (exclusive for all-day events).</summary>
    public DateTime End { get; }

    /// <summary>True for an all-day event.</summary>
    public bool AllDay { get; }

    /// <summary>
    /// The names (<c>CN</c> parameter) of the event's <c>ATTENDEE</c>s, unescaped and normalized like a mention
    /// (<see cref="PersonParser.Normalize"/>), in file order. Attendees without a valid name are left out, and the
    /// attendee's address (the property value) is never read.
    /// </summary>
    public IReadOnlyList<string> Attendees { get; }

    /// <summary>Equal when every field is, with <see cref="Attendees"/> compared name by name (ordinal).</summary>
    public bool Equals(Appointment? other) =>
        other is not null
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(Location, other.Location, StringComparison.Ordinal)
        && Start == other.Start
        && End == other.End
        && AllDay == other.AllDay
        && Attendees.SequenceEqual(other.Attendees, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Title, Location, Start, End, AllDay, Attendees.Count);
}

/// <summary>
/// Reads the part of iCalendar (RFC 5545) that <c>/current</c> and the person pages need: the <c>SUMMARY</c>,
/// <c>LOCATION</c>, <c>DTSTART</c>, <c>DTEND</c>, <c>STATUS</c> and <c>ATTENDEE</c> names of each <c>VEVENT</c>. Everything else, including recurrence
/// (<c>RRULE</c>, <c>RDATE</c>, <c>EXDATE</c>) and nested components such as <c>VALARM</c>, is ignored.
/// The input is untrusted: it is scanned once, line by line, with no regexes.
/// </summary>
internal static class IcsParser
{
    private const string DateFormat = "yyyyMMdd";
    private const string DateTimeFormat = "yyyyMMdd'T'HHmmss";

    /// <summary>
    /// Returns every event in <paramref name="text"/> that has a valid <c>DTSTART</c>, isn't
    /// <c>STATUS:CANCELLED</c> and doesn't end before it starts, in file order. Times are converted to
    /// <paramref name="local"/>.
    /// </summary>
    /// <exception cref="FormatException">The text has no <c>BEGIN:VCALENDAR</c> line.</exception>
    public static IReadOnlyList<Appointment> Parse(string text, TimeZoneInfo local)
    {
        List<string> lines = Unfold(text);
        bool calendar = false;
        List<Appointment> result = [];
        EventBuilder? current = null;
        int nested = 0;
        foreach (string line in lines)
        {
            ContentLine? content = ContentLine.Read(line);
            if (content is null)
            {
                continue;
            }

            if (content.Is("BEGIN"))
            {
                string component = content.Value.Trim();
                if (component.Equals("VCALENDAR", StringComparison.OrdinalIgnoreCase))
                {
                    calendar = true;
                }
                else if (current is not null)
                {
                    nested++;
                }
                else if (component.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    current = new EventBuilder();
                    nested = 0;
                }

                continue;
            }

            if (content.Is("END"))
            {
                if (current is null)
                {
                    continue;
                }

                if (nested > 0)
                {
                    nested--;
                }
                else
                {
                    Appointment? appointment = current.Build(local);
                    if (appointment is not null)
                    {
                        result.Add(appointment);
                    }

                    current = null;
                }

                continue;
            }

            if (current is not null && nested == 0)
            {
                current.Add(content);
            }
        }

        if (!calendar)
        {
            throw new FormatException("Not an iCalendar file: no BEGIN:VCALENDAR line.");
        }

        return result;
    }

    /// <summary>Splits on CRLF, LF or CR and joins each line that starts with a space or tab to the one before.</summary>
    private static List<string> Unfold(string text)
    {
        List<string> lines = [];
        StringBuilder? current = null;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\r' && text[i] != '\n')
            {
                continue;
            }

            ReadOnlySpan<char> line = text.AsSpan(start, i - start);
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && current is not null)
            {
                current.Append(line[1..]);
            }
            else
            {
                if (current is not null)
                {
                    lines.Add(current.ToString());
                }

                current = new StringBuilder().Append(line);
            }

            if (i < text.Length - 1 && text[i] == '\r' && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (current is not null)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>Turns <c>\n \N \, \; \\</c> into the characters they stand for; other backslashes are kept.</summary>
    internal static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }

            char next = value[i + 1];
            switch (next)
            {
                case 'n':
                case 'N':
                    builder.Append('\n');
                    i++;
                    break;
                case ',':
                case ';':
                case '\\':
                    builder.Append(next);
                    i++;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>A parsed <c>DTSTART</c> or <c>DTEND</c>: the local time and whether it is a date only.</summary>
    private readonly record struct When
    {
        public When(DateTime time, bool date)
        {
            Time = time;
            Date = date;
        }

        public DateTime Time { get; }

        public bool Date { get; }
    }

    /// <summary>Reads a date or date-time in one of the four supported forms, or <c>null</c> if it isn't valid.</summary>
    private static When? ReadWhen(ContentLine content, TimeZoneInfo local)
    {
        string value = content.Value.Trim();
        string? valueType = content.Parameter("VALUE");
        bool dateOnly = string.Equals(valueType, "DATE", StringComparison.OrdinalIgnoreCase)
            || (value.Length == DateFormat.Length && !value.Contains('T'));
        try
        {
            if (dateOnly)
            {
                if (!DateTime.TryParseExact(
                    value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                {
                    return null;
                }

                return new When(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), true);
            }

            bool utc = value.EndsWith('Z') || value.EndsWith('z');
            string bare = utc ? value[..^1] : value;
            if (!DateTime.TryParseExact(
                bare, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
            {
                return null;
            }

            time = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            if (utc)
            {
                return new When(Unspecified(TimeZoneInfo.ConvertTimeFromUtc(time, local)), false);
            }

            string? zoneId = content.Parameter("TZID");
            TimeZoneInfo? source = zoneId is null ? null : FindZone(zoneId);
            if (source is null || source.Id == local.Id)
            {
                return new When(time, false);
            }

            return new When(Unspecified(Convert(time, source, local)), false);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime Convert(DateTime time, TimeZoneInfo source, TimeZoneInfo local)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(time, source, local);
        }
        catch (ArgumentException)
        {
            // A time that doesn't exist in the source zone (skipped by a clock change): use its standard offset.
            DateTime utc = DateTime.SpecifyKind(time - source.BaseUtcOffset, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, local);
        }
    }

    private static DateTime Unspecified(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Unspecified);

    /// <summary>The zone with <paramref name="id"/>, or <c>null</c> (treated as local) when there is none.</summary>
    private static TimeZoneInfo? FindZone(string id)
    {
        string trimmed = id.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(trimmed);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Collects the properties of one <c>VEVENT</c>; the first of each kind wins.</summary>
    private sealed class EventBuilder
    {
        private string? summary;
        private string? location;
        private ContentLine? start;
        private ContentLine? end;
        private bool cancelled;
        private readonly List<string> attendees = [];

        public void Add(ContentLine content)
        {
            if (content.Is("SUMMARY"))
            {
                summary ??= Unescape(content.Value);
            }
            else if (content.Is("LOCATION"))
            {
                location ??= Unescape(content.Value);
            }
            else if (content.Is("DTSTART"))
            {
                start ??= content;
            }
            else if (content.Is("DTEND"))
            {
                end ??= content;
            }
            else if (content.Is("ATTENDEE"))
            {
                // Only the CN name; the value (the mailto: address) is never read.
                string? common = content.Parameter("CN");
                string? name = common is null ? null : PersonParser.Normalize(Unescape(common));
                if (name is not null)
                {
                    attendees.Add(name);
                }
            }
            else if (content.Is("STATUS"))
            {
                cancelled = content.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
            }
        }

        public Appointment? Build(TimeZoneInfo local)
        {
            if (cancelled || start is null)
            {
                return null;
            }

            When? from = ReadWhen(start, local);
            if (from is null)
            {
                return null;
            }

            DateTime startTime = from.Value.Time;
            bool allDay = from.Value.Date;
            DateTime endTime;
            When? to = end is null ? null : ReadWhen(end, local);
            if (to is not null)
            {
                endTime = to.Value.Time;
            }
            else if (allDay)
            {
                if (startTime.Date == DateTime.MaxValue.Date)
                {
                    return null;
                }

                endTime = startTime.AddDays(1);
            }
            else
            {
                endTime = startTime;
            }

            if (endTime < startTime)
            {
                return null;
            }

            return new Appointment(summary ?? string.Empty, location, startTime, endTime, allDay, attendees);
        }
    }

    /// <summary>One unfolded line: <c>NAME;PARAM=value;…:value</c>.</summary>
    private sealed class ContentLine
    {
        private readonly string name;
        private readonly List<KeyValuePair<string, string>> parameters;

        private ContentLine(string name, List<KeyValuePair<string, string>> parameters, string value)
        {
            this.name = name;
            this.parameters = parameters;
            Value = value;
        }

        public string Value { get; }

        public bool Is(string property) => name.Equals(property, StringComparison.OrdinalIgnoreCase);

        public string? Parameter(string key)
        {
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                if (parameter.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Splits <paramref name="line"/> in one pass. <c>:</c> and <c>;</c> inside double-quoted parameter
        /// values don't count. Returns <c>null</c> when there is no value separator.
        /// </summary>
        public static ContentLine? Read(string line)
        {
            int nameEnd = 0;
            while (nameEnd < line.Length && line[nameEnd] != ';' && line[nameEnd] != ':')
            {
                nameEnd++;
            }

            if (nameEnd == line.Length)
            {
                return null;
            }

            string name = line[..nameEnd].Trim();
            List<KeyValuePair<string, string>> parameters = [];
            int i = nameEnd;
            while (i < line.Length && line[i] == ';')
            {
                int keyStart = i + 1;
                int j = keyStart;
                while (j < line.Length && line[j] != '=' && line[j] != ';' && line[j] != ':')
                {
                    j++;
                }

                string key = line[keyStart..j];
                StringBuilder value = new StringBuilder();
                if (j < line.Length && line[j] == '=')
                {
                    j++;
                    bool quoted = false;
                    while (j < line.Length && (quoted || (line[j] != ';' && line[j] != ':')))
                    {
                        if (line[j] == '"')
                        {
                            quoted = !quoted;
                        }
                        else
                        {
                            value.Append(line[j]);
                        }

                        j++;
                    }
                }

                parameters.Add(new KeyValuePair<string, string>(key.Trim(), value.ToString()));
                i = j;
            }

            if (i >= line.Length || line[i] != ':')
            {
                return null;
            }

            return new ContentLine(name, parameters, line[(i + 1)..]);
        }
    }
}
