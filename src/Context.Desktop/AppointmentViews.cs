namespace Context;

/// <summary>
/// One day's appointments for the sidebar (<see cref="DesktopState.DayAppointments"/>): every appointment of the day
/// from the calendar files and the feed's cached copy, in <see cref="CalendarStore.Day(DateOnly)"/> order, the
/// calendar files that couldn't be read, and the state's clock as a local time in the calendar's zone. All text is
/// raw: clean it before showing it.
/// </summary>
internal sealed record DayAppointmentsView
{
    public DayAppointmentsView(
        DateOnly day, IReadOnlyList<Appointment> appointments, IReadOnlyList<string> unreadable, DateTime localNow, bool failed = false)
    {
        Day = day;
        Appointments = appointments;
        Unreadable = unreadable;
        LocalNow = localNow;
        Failed = failed;
    }

    public DateOnly Day { get; }

    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The names of the calendar files that couldn't be read (raw).</summary>
    public IReadOnlyList<string> Unreadable { get; }

    /// <summary>Now, in the calendar's zone (the appointments' times are in it too).</summary>
    public DateTime LocalNow { get; }

    /// <summary>True when the calendar couldn't be read at all; then the lists are empty.</summary>
    public bool Failed { get; }

    /// <summary>Returns only the type name, so formatting the view never prints calendar text.</summary>
    public override string ToString() => nameof(DayAppointmentsView);
}

/// <summary>
/// The appointment page (<see cref="DesktopState.AppointmentPage"/>): every note related to the appointments of one
/// day with one title (<see cref="MeetingPrep.Related(Appointment, IReadOnlyList{NoteInfo}, IReadOnlyList{ValueTuple{string, string}}, int?)"/>
/// with no cap), and the first of those appointments (for its meeting note). All text is raw.
/// </summary>
internal sealed record AppointmentPageView
{
    public AppointmentPageView(
        string title, DateOnly day, IReadOnlyList<RelatedNote> related, Appointment? first, bool indexUnreadable)
    {
        Title = title;
        Day = day;
        Related = related;
        First = first;
        IndexUnreadable = indexUnreadable;
    }

    public string Title { get; }

    public DateOnly Day { get; }

    /// <summary>The related notes, ranked; empty when none match or when <see cref="IndexUnreadable"/> is set.</summary>
    public IReadOnlyList<RelatedNote> Related { get; }

    /// <summary>The first appointment of <see cref="Day"/> with <see cref="Title"/>; <c>null</c> when there is none.</summary>
    public Appointment? First { get; }

    /// <summary>True when <c>context.db</c> (or the note list) can't be read.</summary>
    public bool IndexUnreadable { get; }

    /// <summary>Returns only the type name, so formatting the view never prints note or calendar text.</summary>
    public override string ToString() => nameof(AppointmentPageView);
}
