namespace Context.Tests;

/// <summary>
/// An Outlook source in memory: the test sets the availability, the kind and the items each call answers, and can
/// make a call throw or block on a gate until <see cref="Release"/> (or <see cref="Dispose"/>). It counts the calls
/// and records their arguments; it never touches COM or Outlook. A blocked read waits at most its release bound
/// (30 s unless the ctor sets one, job 491), then throws a <see cref="TimeoutException"/> that says
/// <see cref="Release"/> was not called, so a test that fails before it releases never blocks a pool thread forever.
/// </summary>
internal sealed class FakeOutlookSource(TimeSpan? releaseBound = null) : IOutlookSource, IDisposable
{
    /// <summary>The longest a blocked read waits for <see cref="Release"/> unless the ctor sets a bound.</summary>
    public static readonly TimeSpan DefaultReleaseBound = TimeSpan.FromSeconds(30);

    private readonly TimeSpan releaseBound = releaseBound ?? DefaultReleaseBound;
    private readonly object gate = new();
    private readonly ManualResetEventSlim open = new(initialState: true);
    private readonly List<(DateOnly Day, TimeZoneInfo Zone)> appointmentCalls = [];
    private readonly List<(DateTime SinceUtc, int Max)> mailCalls = [];
    private int availabilityCalls;

    public OutlookAvailability Available { get; set; } = OutlookAvailability.Available;

    /// <summary>The kind every <see cref="Appointments"/> call answers.</summary>
    public OutlookReadKind AppointmentKind { get; set; } = OutlookReadKind.Ok;

    /// <summary>The items an <see cref="OutlookReadKind.Ok"/> <see cref="Appointments"/> call answers, by day.</summary>
    public Dictionary<DateOnly, IReadOnlyList<Appointment>> Days { get; } = [];

    /// <summary>The kind every <see cref="RecentMail"/> call answers.</summary>
    public OutlookReadKind MailKind { get; set; } = OutlookReadKind.Ok;

    /// <summary>The mails an <see cref="OutlookReadKind.Ok"/> <see cref="RecentMail"/> call answers.</summary>
    public List<MailMessage> Mails { get; } = [];

    /// <summary>When set, <see cref="Appointments"/> and <see cref="RecentMail"/> throw this after counting the call.</summary>
    public Exception? Throws { get; set; }

    public int AppointmentCalls
    {
        get
        {
            lock (gate)
            {
                return appointmentCalls.Count;
            }
        }
    }

    public int MailCalls
    {
        get
        {
            lock (gate)
            {
                return mailCalls.Count;
            }
        }
    }

    /// <summary>The arguments of every <see cref="RecentMail"/> call, in order.</summary>
    public IReadOnlyList<(DateTime SinceUtc, int Max)> MailArguments
    {
        get
        {
            lock (gate)
            {
                return [.. mailCalls];
            }
        }
    }

    public int AvailabilityCalls => Volatile.Read(ref availabilityCalls);

    /// <summary>The arguments of every <see cref="Appointments"/> call, in order.</summary>
    public IReadOnlyList<(DateOnly Day, TimeZoneInfo Zone)> AppointmentArguments
    {
        get
        {
            lock (gate)
            {
                return [.. appointmentCalls];
            }
        }
    }

    /// <summary>Makes every later read wait until <see cref="Release"/> or <see cref="Dispose"/>.</summary>
    public void Block() => open.Reset();

    /// <summary>Lets every waiting and later read through.</summary>
    public void Release() => open.Set();

    public OutlookAvailability Availability()
    {
        Interlocked.Increment(ref availabilityCalls);
        return Available;
    }

    public OutlookRead<Appointment> Appointments(DateOnly day, TimeZoneInfo zone)
    {
        lock (gate)
        {
            appointmentCalls.Add((day, zone));
        }

        WaitForRelease();
        if (Throws is not null)
        {
            throw Throws;
        }

        if (AppointmentKind != OutlookReadKind.Ok)
        {
            return new OutlookRead<Appointment>(AppointmentKind);
        }

        return new OutlookRead<Appointment>(OutlookReadKind.Ok, Days.TryGetValue(day, out IReadOnlyList<Appointment>? items) ? items : []);
    }

    public OutlookRead<MailMessage> RecentMail(DateTime sinceUtc, int max)
    {
        lock (gate)
        {
            mailCalls.Add((sinceUtc, max));
        }

        WaitForRelease();
        if (Throws is not null)
        {
            throw Throws;
        }

        if (MailKind != OutlookReadKind.Ok)
        {
            return new OutlookRead<MailMessage>(MailKind);
        }

        lock (gate)
        {
            return new OutlookRead<MailMessage>(OutlookReadKind.Ok, [.. Mails]);
        }
    }

    // Waits for the gate with the release bound; past it the read fails, naming what it waited for.
    private void WaitForRelease()
    {
        if (!open.Wait(releaseBound))
        {
            throw new TimeoutException($"FakeOutlookSource: Release() not called within {BoundedWait.Describe(releaseBound)}");
        }
    }

    /// <summary>Releases the gate, so no read stays blocked after the test; the gate itself is kept for late reads.</summary>
    public void Dispose() => open.Set();
}
