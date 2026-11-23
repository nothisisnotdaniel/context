namespace Context;

/// <summary>Tracks Outlook history and day reads for one open journal. Other calendars can continue after a failure.</summary>
internal sealed class JournalOutlook
{
    private readonly Func<DateOnly, (OutlookStatus Status, IReadOnlyList<Appointment> Items)> readDay;

    public JournalOutlook(OutlookStatus status, Func<DateOnly, (OutlookStatus Status, IReadOnlyList<Appointment> Items)> readDay)
    {
        Status = status;
        this.readDay = readDay;
    }

    public OutlookStatus Status { get; private set; }

    public IReadOnlyList<Appointment> Day(DateOnly date)
    {
        if (Status != OutlookStatus.Ok)
        {
            return [];
        }
        (OutlookStatus status, IReadOnlyList<Appointment> items) = readDay(date);
        Status = status;
        return items;
    }
}
