namespace Context;

/// <summary>Whether Outlook can be read on this machine at all.</summary>
internal enum OutlookAvailability
{
    /// <summary>Not Windows: there is no Outlook COM interface.</summary>
    Unsupported,

    /// <summary>Windows without classic Outlook (the new Outlook has no COM interface).</summary>
    NotInstalled,

    /// <summary>Classic Outlook is installed; it can be read while it runs.</summary>
    Available,
}

/// <summary>How one read from Outlook ended.</summary>
internal enum OutlookReadKind
{
    /// <summary>Read; the items are filled.</summary>
    Ok,

    /// <summary>Outlook isn't running; it is never started.</summary>
    NotRunning,

    /// <summary>Outlook (or its object-model guard) refused access.</summary>
    Denied,

    /// <summary>Any other error.</summary>
    Failed,
}

/// <summary>
/// One read from Outlook: its <see cref="Kind"/> and, for <see cref="OutlookReadKind.Ok"/>, the items (raw outside
/// text). Never an exception message.
/// </summary>
internal sealed record OutlookRead<T>
{
    public OutlookRead(OutlookReadKind kind, IReadOnlyList<T>? items = null)
    {
        Kind = kind;
        Items = items ?? [];
    }

    public OutlookReadKind Kind { get; }

    /// <summary>The items read; empty unless <see cref="Kind"/> is <see cref="OutlookReadKind.Ok"/>.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(OutlookRead<T>);
}

/// <summary>One Inbox mail: when it came in (UTC), the sender's display name and the subject, both raw outside text.</summary>
internal sealed record MailSummary
{
    public MailSummary(DateTime receivedUtc, string sender, string subject)
    {
        ReceivedUtc = receivedUtc;
        Sender = sender;
        Subject = subject;
    }

    /// <summary>When the mail came in, in UTC.</summary>
    public DateTime ReceivedUtc { get; }

    /// <summary>The sender's display name (raw; may be empty).</summary>
    public string Sender { get; }

    /// <summary>The subject (raw; may be empty).</summary>
    public string Subject { get; }

    /// <summary>Only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(MailSummary);
}

/// <summary>
/// Reads the default Calendar and Inbox of the Outlook that is already running, read-only. Calls may block (Outlook
/// busy, a security prompt); <see cref="DesktopState"/> bounds them with its <see cref="DesktopState.OutlookTimeout"/>.
/// </summary>
internal interface IOutlookSource
{
    /// <summary>Whether classic Outlook can be read here; reads no mail or calendar data.</summary>
    public OutlookAvailability Availability();

    /// <summary>The appointments touching <paramref name="day"/>, with times in <paramref name="zone"/>.</summary>
    public OutlookRead<Appointment> Appointments(DateOnly day, TimeZoneInfo zone);

    /// <summary>At most <paramref name="max"/> Inbox mails received at or after <paramref name="sinceUtc"/>.</summary>
    public OutlookRead<MailSummary> RecentMail(DateTime sinceUtc, int max);
}

/// <summary>The Outlook source on an OS other than Windows: nothing can be read.</summary>
internal sealed class UnsupportedOutlookSource : IOutlookSource
{
    public OutlookAvailability Availability() => OutlookAvailability.Unsupported;

    public OutlookRead<Appointment> Appointments(DateOnly day, TimeZoneInfo zone) => new(OutlookReadKind.Failed);

    public OutlookRead<MailSummary> RecentMail(DateTime sinceUtc, int max) => new(OutlookReadKind.Failed);
}

/// <summary>
/// The settings page's Outlook section (<see cref="DesktopState.OutlookSetting"/>): whether Outlook can be read here,
/// whether the setting is on, and whether <c>settings.json</c> couldn't be read (then it shows as off).
/// </summary>
internal sealed record OutlookSettingView
{
    public OutlookSettingView(OutlookAvailability availability, bool enabled, bool unreadable)
    {
        Availability = availability;
        Enabled = enabled;
        Unreadable = unreadable;
    }

    public OutlookAvailability Availability { get; }

    /// <summary>The stored value; false when there is none or it can't be read.</summary>
    public bool Enabled { get; }

    /// <summary>True when <c>settings.json</c> couldn't be read.</summary>
    public bool Unreadable { get; }

    /// <summary>Only the type name, like the other view records.</summary>
    public override string ToString() => nameof(OutlookSettingView);
}
