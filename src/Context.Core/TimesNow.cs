using System.Globalization;

namespace Context;

/// <summary>What <see cref="TimesNow.Read"/> found for today.</summary>
internal enum TimesNowKind
{
    /// <summary>Nothing to show: no Times folder, no day file, an absence day or no arrival yet.</summary>
    None,

    /// <summary>Today's day file can't be read.</summary>
    Unreadable,

    /// <summary>Today has an arrival; the figures hold.</summary>
    Booked,
}

/// <summary>Where today stands in Times (job 383), read from <see cref="TimesDay"/> only.</summary>
internal enum TimesNowState
{
    /// <summary>No arrival today (also no Times folder, no day file, an absence day or an unreadable file).</summary>
    Out,

    /// <summary>Arrived (or back from a break) and not left.</summary>
    Working,

    /// <summary>Arrived, not left and not working: a break is open.</summary>
    OnBreak,

    /// <summary>Arrived and left.</summary>
    Left,
}

/// <summary>The open checkpoint of today: its work item (or <c>null</c>) and its raw name.</summary>
internal sealed record TimesCheckpoint
{
    public TimesCheckpoint(int? workItem, string name)
    {
        WorkItem = workItem;
        Name = name;
    }

    public int? WorkItem { get; }

    /// <summary>The checkpoint's name, raw (untrusted display text).</summary>
    public string Name { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TimesCheckpoint);
}

/// <summary>
/// Today's Times bookings as the current view shows them: arrival, leaving (once left), the time left against the
/// daily target and the open checkpoint. Reading only: nothing in the Times folder is created or changed.
/// </summary>
internal sealed record TimesNow
{
    public TimesNow(
        TimesNowKind kind,
        TimeOnly? arrival,
        TimeOnly? leaving,
        TimeSpan left,
        TimesCheckpoint? checkpoint,
        TimesNowState state = TimesNowState.Out)
    {
        Kind = kind;
        State = state;
        Arrival = arrival;
        Leaving = leaving;
        Left = left;
        Checkpoint = checkpoint;
    }

    public TimesNowKind Kind { get; }

    /// <summary>Where today stands (job 383): <see cref="TimesNowState.Out"/> unless <see cref="TimesNowKind.Booked"/>.</summary>
    public TimesNowState State { get; }

    /// <summary>The first arrival; set only for <see cref="TimesNowKind.Booked"/>.</summary>
    public TimeOnly? Arrival { get; }

    /// <summary>The last leaving; set only when the day <see cref="TimesDay.HasLeft"/>.</summary>
    public TimeOnly? Leaving { get; }

    /// <summary>The daily target minus the work time up to now; negative once the target is passed.</summary>
    public TimeSpan Left { get; }

    /// <summary>The last checkpoint with no later checkpoint, break start or leaving; else <c>null</c>.</summary>
    public TimesCheckpoint? Checkpoint { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(TimesNow);

    /// <summary>
    /// Today's bookings from <paramref name="folder"/> (<see cref="TimesDay.Read"/> for <paramref name="today"/>, the
    /// target from <see cref="TimesBalance.ReadTarget"/>) at <paramref name="now"/>. Never throws, never writes.
    /// </summary>
    public static TimesNow Read(string? folder, DateOnly today, TimeOnly now)
    {
        try
        {
            TimesDay day = TimesDay.Read(folder, today);
            if (day.Kind == TimesDayKind.Unreadable)
            {
                return new TimesNow(TimesNowKind.Unreadable, null, null, TimeSpan.Zero, null);
            }

            if (folder is null || day.Kind != TimesDayKind.Entries || day.Arrival is not { } arrival)
            {
                return None();
            }

            TimeSpan target = TimeSpan.FromMinutes(TimesBalance.ReadTarget(folder));
            TimeOnly? leaving = day.HasLeft ? day.Leaving : null;
            TimesNowState state = day.HasLeft ? TimesNowState.Left
                : day.IsWorking ? TimesNowState.Working
                : TimesNowState.OnBreak;
            return new TimesNow(
                TimesNowKind.Booked, arrival, leaving, target - day.WorkTimeAt(now), OpenCheckpoint(day), state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or InvalidOperationException)
        {
            return new TimesNow(TimesNowKind.Unreadable, null, null, TimeSpan.Zero, null);
        }
    }

    /// <summary>
    /// <paramref name="left"/> as Times shows it: <c>H:mm</c>, truncated to whole minutes, with <c>-</c> when
    /// negative; <c>0:00</c> for zero (and for less than a minute either way).
    /// </summary>
    public static string FormatLeft(TimeSpan left)
    {
        long minutes = left.Ticks / TimeSpan.TicksPerMinute;
        string sign = minutes < 0 ? "-" : string.Empty;
        long whole = Math.Abs(minutes);
        return sign + (whole / 60).ToString(CultureInfo.InvariantCulture) + ":"
            + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private static TimesNow None() => new(TimesNowKind.None, null, null, TimeSpan.Zero, null);

    private static TimesCheckpoint? OpenCheckpoint(TimesDay day)
    {
        TimesEntry? open = null;
        foreach (TimesEntry entry in day.Entries)
        {
            switch (entry.Kind)
            {
                case TimesEntryKind.Checkpoint:
                    open = entry;
                    break;
                case TimesEntryKind.BreakStart:
                case TimesEntryKind.Leaving:
                    open = null;
                    break;
                default:
                    break;
            }
        }

        return open is null ? null : new TimesCheckpoint(open.WorkItem, open.Name ?? string.Empty);
    }
}
