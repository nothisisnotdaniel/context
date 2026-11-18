using System.Globalization;

namespace Context;

/// <summary>
/// One day's appointments for the sidebar (<see cref="DesktopState.DayAppointments"/>): every appointment of the day
/// from the calendar files, the feed's cached copy and Outlook (when on), in <see cref="CalendarStore.Day(DateOnly)"/> order, the
/// calendar files that couldn't be read, and the state's clock as a local time in the calendar's zone. All text is
/// raw: clean it before showing it.
/// </summary>
internal sealed record DayAppointmentsView
{
    public DayAppointmentsView(
        DateOnly day,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<string> unreadable,
        DateTime localNow,
        bool failed = false,
        OutlookStatus outlook = OutlookStatus.Off)
    {
        Day = day;
        Appointments = appointments;
        Unreadable = unreadable;
        LocalNow = localNow;
        Failed = failed;
        Outlook = outlook;
    }

    public DateOnly Day { get; }

    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The names of the calendar files that couldn't be read (raw).</summary>
    public IReadOnlyList<string> Unreadable { get; }

    /// <summary>Now, in the calendar's zone (the appointments' times are in it too).</summary>
    public DateTime LocalNow { get; }

    /// <summary>True when the calendar couldn't be read at all; then the lists are empty.</summary>
    public bool Failed { get; }

    /// <summary>How reading Outlook went for this day; <see cref="OutlookStatus.Off"/> when Outlook is off.</summary>
    public OutlookStatus Outlook { get; }

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

/// <summary>How the last Outlook read for a panel went.</summary>
internal enum OutlookStatus
{
    /// <summary>Outlook is off (the setting, or it can't be read here): nothing was asked.</summary>
    Off,

    /// <summary>Read.</summary>
    Ok,

    /// <summary>Outlook isn't running.</summary>
    NotRunning,

    /// <summary>Outlook refused access.</summary>
    Denied,

    /// <summary>Any other error, or a source call that threw.</summary>
    Failed,

    /// <summary>The source call didn't return within <see cref="DesktopState.OutlookTimeout"/>.</summary>
    TimedOut,
}

/// <summary>The status line the appointments and mail panels show for an Outlook read.</summary>
internal static class OutlookStatusLine
{
    /// <summary>The fixed text of the line, or <c>null</c> when there is none (off or read).</summary>
    public static string? Text(OutlookStatus status) => status switch
    {
        OutlookStatus.NotRunning => "Outlook isn't running. Start it to see its appointments and mail.",
        OutlookStatus.Denied => "Can't read Outlook (access denied).",
        OutlookStatus.Failed => "Can't read Outlook (error).",
        OutlookStatus.TimedOut => "Can't read Outlook (timed out).",
        _ => null,
    };

    /// <summary>The line's colour class: grey for not running, red for the failures.</summary>
    public static string Css(OutlookStatus status) => status == OutlookStatus.NotRunning ? "grey" : "red";
}

/// <summary>
/// The last day's Inbox mail for the sidebar (<see cref="DesktopState.RecentMail"/>): at most
/// <see cref="DesktopState.MaxMails"/> mails received in the last 24 hours, newest first, how reading Outlook went, and
/// the state's clock in the calendar's zone. All text is raw: clean it before showing it.
/// </summary>
internal sealed record RecentMailView
{
    public RecentMailView(OutlookStatus status, IReadOnlyList<MailSummary> mails, DateTime localNow, TimeZoneInfo zone)
    {
        Status = status;
        Mails = mails;
        LocalNow = localNow;
        Zone = zone;
    }

    public OutlookStatus Status { get; }

    /// <summary>The mails, newest first; empty unless <see cref="Status"/> is <see cref="OutlookStatus.Ok"/>.</summary>
    public IReadOnlyList<MailSummary> Mails { get; }

    /// <summary>Now, in <see cref="Zone"/>.</summary>
    public DateTime LocalNow { get; }

    /// <summary>The calendar's zone, which the mail times are shown in.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// When <paramref name="mail"/> came in, in <see cref="Zone"/>: <c>HH:mm</c> when that is today, else
    /// <c>ddd HH:mm</c> (invariant).
    /// </summary>
    public string Time(MailSummary mail)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(mail.ReceivedUtc, DateTimeKind.Utc), Zone);
        return local.Date == LocalNow.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Returns only the type name, so formatting the view never prints mail text.</summary>
    public override string ToString() => nameof(RecentMailView);
}
