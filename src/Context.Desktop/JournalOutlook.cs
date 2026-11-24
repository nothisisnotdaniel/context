namespace Context;

/// <summary>Tracks Outlook history and day reads for one open journal. Other calendars can continue after a failure.</summary>
internal sealed class JournalOutlook
{
    private readonly Func<DateOnly, (OutlookStatus Status, IReadOnlyList<Appointment> Items)> readDay;
    private readonly DateOnly? oldest;

    public JournalOutlook(OutlookStatus status, DateOnly? oldest, Func<DateOnly, (OutlookStatus Status, IReadOnlyList<Appointment> Items)> readDay)
    {
        Status = status;
        this.oldest = oldest;
        this.readDay = readDay;
    }

    public OutlookStatus Status { get; private set; }
    public DateOnly? Oldest => Status == OutlookStatus.Ok ? oldest : null;

    public IReadOnlyList<Appointment> Day(DateOnly date)
    {
        if (Oldest is not { } start || date < start)
        {
            return [];
        }
        (OutlookStatus status, IReadOnlyList<Appointment> items) = readDay(date);
        Status = status;
        return items;
    }
}
