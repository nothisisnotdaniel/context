using System.Globalization;
using System.Text;

namespace Context;

/// <summary>What <see cref="MeetingNote.Ensure"/> found or did.</summary>
internal enum MeetingNoteKind
{
    /// <summary>There was no meeting note; it was created.</summary>
    Created,

    /// <summary>The plain meeting note exists; nothing was written.</summary>
    Existing,

    /// <summary>The meeting note is a sensitive (sealed) note; nothing was read or written.</summary>
    Sensitive,
}

/// <summary>The answer of <see cref="MeetingNote.Ensure"/>: what happened and the note's name.</summary>
internal sealed record MeetingNoteResult
{
    public MeetingNoteResult(MeetingNoteKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(MeetingNoteResult);

    public MeetingNoteKind Kind { get; }

    /// <summary>The meeting note's name (<c>yyyy-MM-dd &lt;title&gt;</c>).</summary>
    public string Name { get; }
}

/// <summary>
/// The meeting note: the top-level note <c>yyyy-MM-dd &lt;title&gt;</c> for one appointment, tagged <c>#meeting</c>
/// and filled with the appointment's title, time, location and attendees. An existing one (plain or sensitive) is
/// never read, changed or replaced, so each meeting keeps one note.
/// </summary>
internal static class MeetingNote
{
    /// <summary>The tag every new meeting note carries.</summary>
    public const string Tag = "meeting";

    /// <summary>The title used when nothing of the appointment's title is left.</summary>
    public const string FallbackTitle = "Meeting";

    /// <summary>
    /// The longest title part of the name: <c>yyyy-MM-dd</c> and a space take 11 of
    /// <see cref="NoteStore.MaxTitleLength"/>.
    /// </summary>
    public const int MaxTitlePart = NoteStore.MaxTitleLength - 11;

    /// <summary>The error when there is no appointment today.</summary>
    public const string NoAppointmentsText = "There are no appointments today.";

    private const string DateFormat = "yyyy-MM-dd";

    private const string TimeFormat = "HH:mm";

    // Refused in file names on some OS; each becomes a space. Control characters too.
    private const string PathChars = "/\\:*?\"<>|";

    /// <summary>The error when there is no appointment number <paramref name="number"/> today.</summary>
    public static string NoAppointmentText(int number) =>
        $"There is no appointment {number.ToString(CultureInfo.InvariantCulture)} today.";

    /// <summary>The error when the meeting note can't be created.</summary>
    public static string WriteFailedText(string name) => $"Can't write {name}.";

    /// <summary>
    /// Reads an appointment number: one or more ASCII digits giving an <see cref="int"/> of at least 1. Anything else
    /// (blank, signs, spaces, other digits, zero, overflow) is <c>false</c>.
    /// </summary>
    public static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    /// <summary>
    /// The appointment numbered <paramref name="number"/> (1-based) in <paramref name="appointments"/>, or
    /// <c>null</c> with <see cref="NoAppointmentsText"/> or <see cref="NoAppointmentText"/> in
    /// <paramref name="error"/>.
    /// </summary>
    public static Appointment? Pick(IReadOnlyList<Appointment> appointments, int number, out string? error)
    {
        if (appointments.Count == 0)
        {
            error = NoAppointmentsText;
            return null;
        }

        if (number < 1 || number > appointments.Count)
        {
            error = NoAppointmentText(number);
            return null;
        }

        error = null;
        return appointments[number - 1];
    }

    /// <summary>
    /// Today's appointments in <see cref="CurrentView"/> order: the calendar files plus the feed (fetched once), the
    /// list <c>/current</c> numbers. Never touches <c>context.db</c>.
    /// </summary>
    public static IReadOnlyList<Appointment> Today(CalendarStore calendar, CalendarFeed? feed, DateOnly today)
    {
        CalendarFeedResult result = CurrentView.Fetch(feed);
        return calendar.Day(today, CurrentView.FeedEvents(result, calendar.Zone, today)).Appointments;
    }

    /// <summary>
    /// The meeting note's name: <c>yyyy-MM-dd &lt;title&gt;</c> (invariant), the date being the local start date. The
    /// title is the one-line cleaned title with each <c>/ \ : * ? " &lt; &gt; |</c> and control character turned into
    /// a space, whitespace runs collapsed, trimmed, cut to <see cref="MaxTitlePart"/> characters (never inside a
    /// surrogate pair), then trailing <c>.md</c>, dots and spaces removed until none is left; an empty one is
    /// <see cref="FallbackTitle"/>. The result always passes <see cref="NoteStore.ValidateTitle"/> and equals its own
    /// <see cref="NoteStore.NormalizeTitle"/>.
    /// </summary>
    public static string NameFor(Appointment appointment)
    {
        string date = Date(appointment.Start);
        return $"{date} {TitlePart(appointment.Title)}";
    }

    /// <summary>
    /// The text of a new meeting note, with <c>\n</c> line ends and ending in <c>\n</c>: <c># &lt;name&gt;</c>, a blank
    /// line, <c>#meeting</c>, a blank line, then <c>- Title:</c>, <c>- Time:</c>, <c>- Location:</c> (only when set)
    /// and <c>- Attendees:</c> lines. Every inserted value is one line.
    /// </summary>
    public static string Format(Appointment appointment)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(NameFor(appointment)).Append('\n');
        text.Append('\n');
        text.Append('#').Append(Tag).Append('\n');
        text.Append('\n');
        string title = OneLine(appointment.Title);
        text.Append("- Title: ").Append(title.Length == 0 ? "(no title)" : title).Append('\n');
        text.Append("- Time: ").Append(Time(appointment)).Append('\n');
        if (!string.IsNullOrEmpty(appointment.Location))
        {
            text.Append("- Location: ").Append(OneLine(appointment.Location)).Append('\n');
        }

        text.Append("- Attendees: ");
        text.Append(appointment.Attendees.Count == 0 ? "none" : string.Join(", ", appointment.Attendees.Select(Attendee)));
        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Makes sure the meeting note for <paramref name="appointment"/> exists. An existing plain note (ignoring case)
    /// gives <see cref="MeetingNoteKind.Existing"/> and a sensitive one <see cref="MeetingNoteKind.Sensitive"/>, both
    /// without writing anything. Otherwise the top-level note is created with <see cref="Format"/>'s text through
    /// <see cref="NoteStore.CreateWithText"/> (temp file and a move that never overwrites); a note that appeared in
    /// between counts as existing. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when
    /// the folder can't be read or written. <paramref name="beforeCreate"/> (tests only) runs after the check, just
    /// before the create.
    /// </summary>
    public static MeetingNoteResult Ensure(NoteStore notes, Appointment appointment, Action? beforeCreate = null)
    {
        string name = NameFor(appointment);
        if (Existing(notes, name) is { } found)
        {
            return found;
        }

        try
        {
            beforeCreate?.Invoke();
            NoteInfo created = notes.CreateWithText(name, Format(appointment));
            return new MeetingNoteResult(MeetingNoteKind.Created, created.Name);
        }
        catch (NoteExistsException)
        {
            return Existing(notes, name) ?? new MeetingNoteResult(MeetingNoteKind.Existing, name);
        }
    }

    /// <summary>
    /// <c>yyyy-MM-dd HH:mm-HH:mm</c>, <c>yyyy-MM-dd HH:mm</c> (zero length), <c>yyyy-MM-dd HH:mm to yyyy-MM-dd HH:mm</c>
    /// (dates differ), <c>yyyy-MM-dd all day</c> or <c>yyyy-MM-dd to yyyy-MM-dd all day</c> (the last day is the
    /// exclusive end minus one day).
    /// </summary>
    internal static string Time(Appointment appointment)
    {
        DateTime start = appointment.Start;
        DateTime end = appointment.End;
        if (appointment.AllDay)
        {
            DateOnly first = DateOnly.FromDateTime(start);
            DateOnly last = DateOnly.FromDateTime(end).AddDays(-1);
            return last > first
                ? $"{Date(start)} to {last.ToString(DateFormat, CultureInfo.InvariantCulture)} all day"
                : $"{Date(start)} all day";
        }

        string from = start.ToString(TimeFormat, CultureInfo.InvariantCulture);
        string to = end.ToString(TimeFormat, CultureInfo.InvariantCulture);
        if (end == start)
        {
            return $"{Date(start)} {from}";
        }

        return start.Date == end.Date
            ? $"{Date(start)} {from}-{to}"
            : $"{Date(start)} {from} to {Date(end)} {to}";
    }

    // The title part of the name (see NameFor).
    private static string TitlePart(string title)
    {
        string cleaned = SafeText.Clean(title);
        StringBuilder spaced = new StringBuilder(cleaned.Length);
        foreach (char c in cleaned)
        {
            spaced.Append(char.IsControl(c) || PathChars.Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        string part = TagParser.CollapseWhitespace(spaced.ToString());
        if (part.Length > MaxTitlePart)
        {
            int cut = MaxTitlePart;
            if (char.IsHighSurrogate(part[cut - 1]))
            {
                cut--;
            }

            part = part[..cut];
        }

        string before;
        do
        {
            before = part;
            part = part.TrimEnd('.', ' ');
            if (part.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                part = part[..^3];
            }
        }
        while (part.Length != before.Length);

        return part.Length == 0 ? FallbackTitle : part;
    }

    // An attendee as a mention when the mention parser reads exactly that name back, else as one-line plain text.
    private static string Attendee(string name)
    {
        string mention = $"@[{name}]";
        IReadOnlyList<string> parsed = PersonParser.Parse(mention);
        return parsed.Count == 1 && string.Equals(parsed[0], name, StringComparison.Ordinal) ? mention : OneLine(name);
    }

    // The existing meeting note called name, plain first, or null when there is none.
    private static MeetingNoteResult? Existing(NoteStore notes, string name)
    {
        if (notes.TryFind(name, out NoteInfo? plain) && plain is not null)
        {
            return new MeetingNoteResult(MeetingNoteKind.Existing, plain.Name);
        }

        if (notes.TryFindSensitive(name, out NoteInfo? sealedNote) && sealedNote is not null)
        {
            return new MeetingNoteResult(MeetingNoteKind.Sensitive, sealedNote.Name);
        }

        return null;
    }

    private static string Date(DateTime time) => time.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string OneLine(string text) => SafeText.Clean(text).Replace('\n', ' ');
}
