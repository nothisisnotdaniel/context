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

    /// <summary>
    /// The meeting note's name: <c>Meetings/&lt;title&gt;/yyyy-MM-dd</c> in the existing spelling, or a legacy
    /// top-level <c>yyyy-MM-dd &lt;title&gt;</c>.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// The meeting note: the subpage <c>Meetings/&lt;title&gt;/yyyy-MM-dd</c> for one appointment, tagged <c>#meeting</c>
/// and filled with the appointment's title, time, location and attendees. An existing one, or a legacy top-level
/// <c>yyyy-MM-dd &lt;title&gt;</c> note (plain or sensitive), is never read, changed or replaced, so each meeting keeps
/// one note.
/// </summary>
internal static class MeetingNote
{
    /// <summary>The tag every new meeting note carries.</summary>
    public const string Tag = "meeting";

    /// <summary>The heading of a new meeting note's topics section (job 253).</summary>
    public const string TopicsHeading = "Topics";

    /// <summary>The title used when nothing of the appointment's title is left.</summary>
    public const string FallbackTitle = "Meeting";

    /// <summary>The top-level note that holds one page per appointment title.</summary>
    public const string FolderName = "Meetings";

    /// <summary>The longest title part: it is a segment of its own, so <see cref="NoteStore.MaxTitleLength"/>.</summary>
    public const int MaxTitlePart = NoteStore.MaxTitleLength;

    /// <summary>
    /// The longest title part of a legacy name: <c>yyyy-MM-dd</c> and a space take 11 of
    /// <see cref="NoteStore.MaxTitleLength"/>.
    /// </summary>
    public const int LegacyMaxTitlePart = NoteStore.MaxTitleLength - 11;

    /// <summary>The error when there is no appointment today.</summary>
    public const string NoAppointmentsText = "There are no appointments today.";

    /// <summary>The empty sections every new meeting note ends its <see cref="Format"/> text with (job 381).</summary>
    public const string SectionsText = "\n## Notes\n\n## Decisions\n\n## Actions\n";

    /// <summary>The error when nothing is running now and nothing timed starts later today (job 381).</summary>
    public const string NoCurrentText = "There is no current or next appointment today.";

    /// <summary>The longest name a Times booking gets (job 381).</summary>
    public const int MaxBookingName = TimesBooking.MaxName;

    /// <summary>The longest Times output line shown after a refused booking (job 381).</summary>
    public const int MaxRefusalText = TimesBooking.MaxRefusalText;

    /// <summary>The <c>--book</c> error for an all-day appointment (job 381); no run is started.</summary>
    public const string AllDayBookingText = "All-day appointments can't be booked in Times.";

    /// <summary>The booking error when no <c>tm</c> was found or started (job 381).</summary>
    public const string TimesNotFoundText = TimesBooking.NotFoundText;

    /// <summary>The booking error when <c>tm</c> was stopped after its timeout (job 381).</summary>
    public const string TimesTimedOutText = TimesBooking.TimedOutText;

    private const string DateFormat = "yyyy-MM-dd";

    private const string ClockFormat = "H:mm";

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

    /// <summary>The error when no appointment runs at <paramref name="time"/> today (job 381).</summary>
    public static string NoAppointmentAtText(TimeOnly time) =>
        $"There is no appointment at {time.ToString(ClockFormat, CultureInfo.InvariantCulture)} today.";

    /// <summary>The error when no appointment today is called <paramref name="title"/> (job 381); one line.</summary>
    public static string NoAppointmentCalledText(string title) => $"There is no appointment called {OneLine(title)} today.";

    /// <summary>
    /// Picks the appointment for <c>meeting</c> and <c>/meeting</c> (job 381) from today's <paramref name="appointments"/>
    /// (<see cref="Today"/> order) by <paramref name="argument"/>. No appointments at all is <see cref="NoAppointmentsText"/>.
    /// A blank argument picks the first timed (not all-day) appointment with start &lt;= <paramref name="localNow"/> &lt;
    /// end, else the timed one with the earliest start after now (ties: list order), else
    /// <see cref="NoCurrentText"/>. An argument of ASCII digits only (as given, no spaces) is the number of
    /// <see cref="Pick"/>; one that isn't a number of at least 1 is <see cref="NoAppointmentText"/>-like
    /// (<c>There is no appointment &lt;digits&gt; today.</c>). A trimmed <c>H:mm</c>/<c>HH:mm</c> (hour 0-23, minute
    /// 0-59) picks the first timed appointment with start &lt;= that time today &lt; end, else
    /// <see cref="NoAppointmentAtText"/>. Anything else, whitespace runs collapsed, is a title: the first appointment
    /// whose one-line title equals it ignoring case, else the first whose title contains it ignoring case, else
    /// <see cref="NoAppointmentCalledText"/>.
    /// </summary>
    public static Appointment? Select(
        IReadOnlyList<Appointment> appointments, string? argument, DateTime localNow, out string? error)
    {
        if (appointments.Count == 0)
        {
            error = NoAppointmentsText;
            return null;
        }

        string raw = argument ?? string.Empty;
        if (raw.Length > 0 && raw.All(char.IsAsciiDigit))
        {
            if (TryParseNumber(raw, out int number))
            {
                return Pick(appointments, number, out error);
            }

            error = $"There is no appointment {raw} today.";
            return null;
        }

        string text = TagParser.CollapseWhitespace(SafeText.Clean(raw).Replace('\n', ' '));
        if (text.Length == 0)
        {
            return Current(appointments, localNow, out error);
        }

        if (TryParseTime(text, out TimeOnly time))
        {
            DateTime at = localNow.Date + time.ToTimeSpan();
            Appointment? running = appointments.FirstOrDefault(a => !a.AllDay && a.Start <= at && at < a.End);
            error = running is null ? NoAppointmentAtText(time) : null;
            return running;
        }

        Appointment? exact = appointments.FirstOrDefault(
            a => string.Equals(TitleText(a), text, StringComparison.OrdinalIgnoreCase));
        Appointment? match = exact ?? appointments.FirstOrDefault(
            a => TitleText(a).Contains(text, StringComparison.OrdinalIgnoreCase));
        error = match is null ? NoAppointmentCalledText(text) : null;
        return match;
    }

    /// <summary>
    /// Reads <c>H:mm</c> or <c>HH:mm</c> (ASCII digits, hour 0-23, minute 0-59); anything else (<c>25:00</c>,
    /// <c>9:5</c>, <c>9.30</c>) is <c>false</c>.
    /// </summary>
    public static bool TryParseTime(string text, out TimeOnly time)
    {
        time = default;
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon is < 1 or > 2 || text.Length != colon + 3 || !text.Remove(colon, 1).All(char.IsAsciiDigit))
        {
            return false;
        }

        int hour = int.Parse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture);
        int minute = int.Parse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }

    /// <summary>The start time as Times reads it: <c>H:mm</c> (invariant).</summary>
    public static string BookingTime(Appointment appointment) =>
        appointment.Start.ToString(ClockFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The name a Times booking gets (job 381): the one-line title with every work item token
    /// <see cref="WorkItemParser"/> finds (<c>#&lt;n&gt;</c>, <c>AB#&lt;n&gt;</c>) removed, whitespace runs collapsed to
    /// one space, trimmed, every leading <c>#</c> and <c>-</c> dropped (Times joins its arguments again and would read
    /// a first word like <c>#05</c> as its project or <c>--version</c> as an option), cut to
    /// <see cref="MaxBookingName"/> characters (never inside a surrogate pair, trimmed again); <see cref="FallbackTitle"/>
    /// when nothing is left.
    /// </summary>
    public static string BookingName(Appointment appointment)
    {
        string name = TimesBooking.CleanName(WorkItemParser.RemoveReferences(OneLine(appointment.Title)));
        return name.Length == 0 ? FallbackTitle : name;
    }

    /// <summary>
    /// The <c>tm</c> arguments that book <paramref name="appointment"/> as a checkpoint (job 381): <c>checkpoint</c>,
    /// <see cref="BookingTime"/>, then <c>#&lt;position&gt;</c> when the first work item of
    /// <paramref name="noteText"/> (<see cref="WorkItemParser.Parse"/>) is on the quicklist <paramref name="projects"/>
    /// (<see cref="TimesProjects.Read"/>, 1-based, first row with that item), then <see cref="BookingName"/>. Each is
    /// one argument, never a shell word.
    /// </summary>
    public static IReadOnlyList<string> BookingArguments(
        Appointment appointment, string noteText, IReadOnlyList<(int WorkItem, string Title)> projects)
    {
        List<string> arguments = ["checkpoint", BookingTime(appointment)];
        IReadOnlyList<int> items = WorkItemParser.Parse(noteText);
        if (items.Count > 0)
        {
            for (int i = 0; i < projects.Count; i++)
            {
                if (projects[i].WorkItem == items[0])
                {
                    arguments.Add("#" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }
        }

        arguments.Add(BookingName(appointment));
        return arguments;
    }

    /// <summary>The line after a booking Times accepted (job 381).</summary>
    public static string BookedText(Appointment appointment) =>
        $"Booked {BookingTime(appointment)} {BookingName(appointment)} in Times.";

    /// <summary>The question the desktop asks before it books (job 381).</summary>
    public static string BookingQuestion(Appointment appointment) =>
        $"Book {BookingName(appointment)} at {BookingTime(appointment)} in Times?";

    /// <summary>
    /// What a booking run gave (job 381): <c>true</c> with <see cref="BookedText"/> for exit code 0, else <c>false</c>
    /// with <see cref="TimesNotFoundText"/>, <see cref="TimesTimedOutText"/> or <c>Times refused the booking: &lt;line&gt;</c>,
    /// the line being the first non-empty line of stderr, else of stdout (cleaned, trimmed, cut to
    /// <see cref="MaxRefusalText"/>), or <c>exit code &lt;n&gt;</c> when both are empty.
    /// </summary>
    public static bool BookingOutcome(Appointment appointment, TimesRunResult result, out string text)
    {
        if (TimesBooking.Outcome(result, out text))
        {
            text = BookedText(appointment);
            return true;
        }

        return false;
    }

    // The running timed appointment, else the next timed one today, else null with NoCurrentText.
    private static Appointment? Current(IReadOnlyList<Appointment> appointments, DateTime localNow, out string? error)
    {
        Appointment? picked = appointments.FirstOrDefault(a => !a.AllDay && a.Start <= localNow && localNow < a.End);
        if (picked is null)
        {
            foreach (Appointment a in appointments)
            {
                if (!a.AllDay && a.Start > localNow && (picked is null || a.Start < picked.Start))
                {
                    picked = a;
                }
            }
        }

        error = picked is null ? NoCurrentText : null;
        return picked;
    }

    // The title as one line with whitespace runs collapsed, for matching.
    private static string TitleText(Appointment appointment) => TagParser.CollapseWhitespace(OneLine(appointment.Title));

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
    /// The title part of the name, one segment: the one-line cleaned title with each <c>/ \ : * ? " &lt; &gt; |</c> and
    /// control character turned into a space, whitespace runs collapsed, trimmed, cut to <see cref="MaxTitlePart"/>
    /// characters (never inside a surrogate pair), trailing <c>.md</c>, dots and spaces removed until none is left,
    /// then leading dots and spaces removed. A part that still fails <see cref="NoteStore.ValidateTitle"/> (a
    /// Windows-reserved stem) becomes <c>Meeting &lt;part&gt;</c>, cut and trimmed again; an empty one is
    /// <see cref="FallbackTitle"/>. The result always passes <see cref="NoteStore.ValidateTitle"/> and equals its own
    /// <see cref="NoteStore.NormalizeTitle"/>.
    /// </summary>
    public static string TitlePartFor(Appointment appointment)
    {
        string part = Clean(appointment.Title, MaxTitlePart).TrimStart('.', ' ');
        if (part.Length > 0 && NoteStore.ValidateTitle(part) is not null)
        {
            part = TrimEnd(Cut(FallbackTitle + " " + part, MaxTitlePart));
        }

        return part.Length == 0 ? FallbackTitle : part;
    }

    /// <summary>
    /// The meeting note's name: <c>Meetings/&lt;title part&gt;/yyyy-MM-dd</c> (<see cref="TitlePartFor"/>; the date
    /// is the local start date, invariant).
    /// </summary>
    public static string NameFor(Appointment appointment) =>
        $"{FolderName}/{TitlePartFor(appointment)}/{Date(appointment.Start)}";

    /// <summary>
    /// The name meeting notes had before job 220: <c>yyyy-MM-dd &lt;title&gt;</c> (invariant), the date being the local
    /// start date. The title is cleaned like <see cref="TitlePartFor"/> but cut to <see cref="LegacyMaxTitlePart"/>
    /// characters, leading dots stay and an empty one is <see cref="FallbackTitle"/>. The result always passes
    /// <see cref="NoteStore.ValidateTitle"/> and equals its own <see cref="NoteStore.NormalizeTitle"/>.
    /// </summary>
    public static string LegacyNameFor(Appointment appointment)
    {
        string part = Clean(appointment.Title, LegacyMaxTitlePart);
        return $"{Date(appointment.Start)} {(part.Length == 0 ? FallbackTitle : part)}";
    }

    /// <summary>
    /// Whether <paramref name="name"/> is the meeting note of <paramref name="appointment"/>: its
    /// <see cref="NameFor"/> or <see cref="LegacyNameFor"/>, ignoring case.
    /// </summary>
    public static bool IsNoteOf(Appointment appointment, string name) =>
        string.Equals(name, NameFor(appointment), StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, LegacyNameFor(appointment), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The text of a new meeting note, with <c>\n</c> line ends and ending in <c>\n</c>: <c># yyyy-MM-dd</c>, a blank
    /// line, <c>#meeting</c>, a blank line, then <c>- Title:</c>, <c>- Time:</c>, <c>- Location:</c> (only when set)
    /// and <c>- Attendees:</c> lines, then (job 381) <see cref="SectionsText"/>: the empty <c>## Notes</c>,
    /// <c>## Decisions</c> and <c>## Actions</c> sections. Every inserted value is one line.
    /// </summary>
    public static string Format(Appointment appointment)
    {
        StringBuilder text = new StringBuilder();
        text.Append("# ").Append(Date(appointment.Start)).Append('\n');
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
        text.Append(SectionsText);
        return text.ToString();
    }

    /// <summary>
    /// Makes sure the meeting note for <paramref name="appointment"/> exists. An existing
    /// <c>Meetings/&lt;part&gt;/yyyy-MM-dd</c> (ignoring case) gives <see cref="MeetingNoteKind.Existing"/>; otherwise a
    /// top-level <see cref="LegacyNameFor"/> note gives <see cref="MeetingNoteKind.Existing"/> (plain) or
    /// <see cref="MeetingNoteKind.Sensitive"/> (sealed); all without writing anything. Otherwise the missing parents
    /// <c>Meetings</c> (<c># Meetings\n</c>) and <c>Meetings/&lt;part&gt;</c> (<c># &lt;part&gt;\n</c>) are created
    /// through <see cref="NoteStore.Create"/> and <see cref="NoteStore.CreateChild(string, string)"/>, existing ones (in
    /// any case) are reused unchanged, then the note through
    /// <see cref="NoteStore.CreateChild(string, string, string)"/> with <see cref="Format"/>'s text; a note or parent
    /// that appeared in between counts as existing. Throws <see cref="IOException"/> when a sensitive top-level
    /// <c>Meetings</c> is in the way, and <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when
    /// the folder can't be read or written. <paramref name="beforeCreate"/> (tests only) runs after the checks, just
    /// before the first create. A created note also gets <see cref="MeetingCarryOver"/>'s <c>## From last time</c>
    /// section with the open points of the previous occurrence's note (see <see cref="CarryOver"/>);
    /// <paramref name="beforeReadPrevious"/> (tests only) runs just before that note is read. With
    /// <paramref name="topics"/> (job 253) a created note also gets a <c>## Topics</c> section (<see cref="TopicsSection"/>)
    /// before that one; an existing note is never written.
    /// </summary>
    public static MeetingNoteResult Ensure(
        NoteStore notes,
        Appointment appointment,
        Action? beforeCreate = null,
        Action<NoteInfo>? beforeReadPrevious = null,
        IReadOnlyList<NoteTopic>? topics = null)
    {
        string part = TitlePartFor(appointment);
        string date = Date(appointment.Start);
        string name = $"{FolderName}/{part}/{date}";
        if (notes.TryFind(name, out NoteInfo? current) && current is not null)
        {
            return new MeetingNoteResult(MeetingNoteKind.Existing, current.Name);
        }

        if (Existing(notes, LegacyNameFor(appointment)) is { } legacy)
        {
            return legacy;
        }

        CheckFolderNotSensitive(notes);
        beforeCreate?.Invoke();
        // CreateChild writes "# <title>\n" itself and the title is the date, which is Format's first line, so only the
        // rest goes in as the body. The whole-text fallback only guards a Format that stops starting with that heading.
        string heading = "# " + date + "\n";
        string text = Format(appointment);
        string body = text.StartsWith(heading, StringComparison.Ordinal) ? text[heading.Length..] : text;
        try
        {
            NoteInfo folder = Parent(notes, null, FolderName);
            NoteInfo meeting = Parent(notes, folder.Name, part);
            try
            {
                body += TopicsSection(topics);
                body += CarryOver(notes, meeting.Name, DateOnly.FromDateTime(appointment.Start), beforeReadPrevious);
                NoteInfo created = notes.CreateChild(meeting.Name, date, body);
                return new MeetingNoteResult(MeetingNoteKind.Created, created.Name);
            }
            catch (NoteExistsException)
            {
                string wanted = meeting.Name + "/" + date;
                return new MeetingNoteResult(
                    MeetingNoteKind.Existing,
                    notes.TryFind(wanted, out NoteInfo? found) && found is not null ? found.Name : wanted);
            }
        }
        catch (ArgumentException ex)
        {
            // A parent that went away in between or can't hold subpages: the note can't be written.
            throw new IOException("The meeting note can't be created.", ex);
        }
    }

    /// <summary>
    /// The topics section of a new meeting note: <c>\n## Topics\n\n</c>, then one
    /// <c>- @[&lt;Person&gt;]: &lt;Text&gt; (from [[&lt;note&gt;]])</c> line per topic (plain <c>from &lt;note&gt;</c> for a
    /// name with <c>[</c>, <c>]</c>, <c>#</c> or a backtick), at most <see cref="MeetingCarryOver.MaxLines"/> lines, then
    /// <c>- … and &lt;k&gt; more.</c>. The empty text for no topics.
    /// </summary>
    internal static string TopicsSection(IReadOnlyList<NoteTopic>? topics)
    {
        if (topics is null || topics.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder text = new StringBuilder();
        text.Append("\n## ").Append(TopicsHeading).Append("\n\n");
        int count = Math.Min(topics.Count, MeetingCarryOver.MaxLines);
        for (int i = 0; i < count; i++)
        {
            NoteTopic topic = topics[i];
            bool plain = topic.Note.IndexOfAny(['[', ']', '#', '`']) >= 0;
            text.Append("- @[").Append(topic.Person).Append("]: ").Append(OneLine(topic.Text))
                .Append(plain ? $" (from {OneLine(topic.Note)})" : $" (from [[{topic.Note}]])")
                .Append('\n');
        }

        if (topics.Count > MeetingCarryOver.MaxLines)
        {
            text.Append("- … and ")
                .Append((topics.Count - MeetingCarryOver.MaxLines).ToString(CultureInfo.InvariantCulture))
                .Append(" more.\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// The carry-over section for a new note of <paramref name="date"/> under <paramref name="meetingPage"/>, or the
    /// empty text when there is no previous note, it is over <see cref="MeetingCarryOver.MaxPreviousBytes"/>, it can't
    /// be read or it has no open points. The previous note is only read.
    /// </summary>
    internal static string CarryOver(NoteStore notes, string meetingPage, DateOnly date, Action<NoteInfo>? beforeRead = null)
    {
        NoteInfo? previous = MeetingCarryOver.Previous(notes, meetingPage, date);
        if (previous is null)
        {
            return string.Empty;
        }

        string text;
        try
        {
            beforeRead?.Invoke(previous);
            text = NoteStore.DecodeText(BoundedFile.Read(previous.Path, MeetingCarryOver.MaxPreviousBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }

        IReadOnlyList<string> lines = MeetingCarryOver.Extract(text);
        return lines.Count == 0 ? string.Empty : MeetingCarryOver.Section(previous.Name, lines);
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

    // The title cleaned to one line of at most max chars with no path or control character and no trailing ".md",
    // dot or space; may be empty.
    private static string Clean(string title, int max)
    {
        string cleaned = SafeText.Clean(title);
        StringBuilder spaced = new StringBuilder(cleaned.Length);
        foreach (char c in cleaned)
        {
            spaced.Append(char.IsControl(c) || PathChars.Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        return TrimEnd(Cut(TagParser.CollapseWhitespace(spaced.ToString()), max));
    }

    // At most max chars, never ending in a lone high surrogate.
    private static string Cut(string part, int max)
    {
        if (part.Length <= max)
        {
            return part;
        }

        int cut = max;
        if (char.IsHighSurrogate(part[cut - 1]))
        {
            cut--;
        }

        return part[..cut];
    }

    // Removes trailing ".md", dots and spaces until none is left.
    private static string TrimEnd(string part)
    {
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

        return part;
    }

    // A sensitive top-level Meetings can't hold subpages: a plain IOException, not NoteExistsException.
    private static void CheckFolderNotSensitive(NoteStore notes)
    {
        if (notes.TryFindSensitive(FolderName, out _))
        {
            throw new IOException("The meetings note is sensitive.");
        }
    }

    // The page parent/title (top level when parent is null), reused in any case or created as "# title\n".
    private static NoteInfo Parent(NoteStore notes, string? parent, string title)
    {
        string wanted = parent is null ? title : parent + "/" + title;
        if (notes.TryFind(wanted, out NoteInfo? found) && found is not null)
        {
            return found;
        }

        try
        {
            return parent is null ? notes.Create(title) : notes.CreateChild(parent, title);
        }
        catch (NoteExistsException)
        {
            // Created by someone else between the check and the write.
            if (parent is null)
            {
                CheckFolderNotSensitive(notes);
            }

            if (notes.TryFind(wanted, out found) && found is not null)
            {
                return found;
            }

            throw new IOException("The meetings page can't be found.");
        }
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
