namespace Context;

/// <summary>
/// One occurrence of an appointment a <c>todos.json</c> todo is tied to (job 287): the event's <see cref="Uid"/>, the
/// occurrence's local <see cref="Start"/> to the minute and its one-line <see cref="Title"/> as it was when tied. The
/// Uid is untrusted outside text: stored only in <c>todos.json</c>, never shown.
/// </summary>
internal sealed record MeetingLink
{
    /// <summary>The longest <see cref="Uid"/> a link holds (and <see cref="TodoStore"/> reads).</summary>
    public const int MaxUidLength = 1000;

    /// <summary>The longest <see cref="Title"/> <see cref="For"/> keeps.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The start of the Uid of an appointment read from Outlook (<c>outlook:</c> and its GlobalAppointmentID).</summary>
    public const string OutlookUidPrefix = "outlook:";

    /// <summary>How many days after <see cref="Day"/> <see cref="NextOccurrence"/> looks at, at most.</summary>
    public const int MaxSearchDays = 100;

    /// <param name="uid">The appointment's <see cref="Appointment.Uid"/>.</param>
    /// <param name="start">The occurrence's local start; seconds and below are dropped.</param>
    /// <param name="title">The title shown with the todo.</param>
    public MeetingLink(string uid, DateTime start, string title)
    {
        Uid = uid;
        Start = ToMinute(start);
        Title = title;
    }

    /// <summary>The event's UID (or <c>outlook:</c> id); compared ordinally.</summary>
    public string Uid { get; }

    /// <summary>The local wall-clock start of the occurrence, to the minute (<see cref="DateTimeKind.Unspecified"/>).</summary>
    public DateTime Start { get; }

    /// <summary>The meeting's title when it was tied, one line, at most <see cref="MaxTitleLength"/> characters.</summary>
    public string Title { get; }

    /// <summary>The date of <see cref="Start"/>.</summary>
    public DateOnly Day => DateOnly.FromDateTime(Start);

    /// <summary>
    /// The link for <paramref name="appointment"/>, or <c>null</c> when it has no <see cref="Appointment.Uid"/> or one
    /// over <see cref="MaxUidLength"/> characters. The title goes through <see cref="SafeText.Clean"/>, line breaks
    /// become spaces, and it is cut to <see cref="MaxTitleLength"/> without splitting a surrogate pair.
    /// </summary>
    public static MeetingLink? For(Appointment appointment)
    {
        if (string.IsNullOrEmpty(appointment.Uid) || appointment.Uid.Length > MaxUidLength)
        {
            return null;
        }

        string title = SafeText.Clean(appointment.Title).Replace('\n', ' ');
        if (title.Length > MaxTitleLength)
        {
            int keep = char.IsHighSurrogate(title[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
            title = title[..keep];
        }

        return new MeetingLink(appointment.Uid, appointment.Start, title);
    }

    /// <summary>True when <paramref name="appointment"/> has this Uid (ordinal) and starts at this minute.</summary>
    public bool Matches(Appointment appointment) =>
        appointment.Uid is not null
        && string.Equals(appointment.Uid, Uid, StringComparison.Ordinal)
        && ToMinute(appointment.Start) == Start;

    /// <summary>
    /// The first appointment with the link's Uid whose start is after both <see cref="Start"/> and
    /// <paramref name="localNow"/>: the days from <see cref="Day"/> (or today, when that is later) through
    /// <see cref="MaxSearchDays"/> days after <see cref="Day"/> are read through <paramref name="day"/> in order, the
    /// earliest such start on the first day that has one wins. <c>null</c> when none is found.
    /// </summary>
    public static Appointment? NextOccurrence(
        MeetingLink link, Func<DateOnly, IReadOnlyList<Appointment>> day, DateTime localNow)
    {
        DateOnly from = link.Day;
        DateOnly today = DateOnly.FromDateTime(localNow);
        if (today > from)
        {
            from = today;
        }

        long last = Math.Min((long)link.Day.DayNumber + MaxSearchDays, DateOnly.MaxValue.DayNumber);
        for (long number = from.DayNumber; number <= last; number++)
        {
            Appointment? best = null;
            foreach (Appointment appointment in day(DateOnly.FromDayNumber((int)number)))
            {
                if (appointment.Uid is null
                    || !string.Equals(appointment.Uid, link.Uid, StringComparison.Ordinal)
                    || ToMinute(appointment.Start) <= link.Start
                    || appointment.Start <= localNow)
                {
                    continue;
                }

                if (best is null || appointment.Start < best.Start)
                {
                    best = appointment;
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>True when no appointment of <paramref name="appointmentsOfDay"/> <see cref="Matches"/> the link.</summary>
    public static bool Missing(MeetingLink link, IReadOnlyList<Appointment> appointmentsOfDay) =>
        !appointmentsOfDay.Any(link.Matches);

    /// <summary>Only the type name, so the Uid never leaks through formatting.</summary>
    public override string ToString() => nameof(MeetingLink);

    private static DateTime ToMinute(DateTime value) =>
        new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Unspecified);
}
