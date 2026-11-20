namespace Context;

/// <summary>What <see cref="DesktopState.Triage"/> found.</summary>
internal enum TriageKind
{
    /// <summary>The queue is known (maybe empty, also when there is no inbox note).</summary>
    Shown,

    /// <summary>The inbox is a sensitive note; it was not opened.</summary>
    Sensitive,

    /// <summary>The inbox note can't be read.</summary>
    Unreadable,
}

/// <summary>The inbox lines to triage (<see cref="InboxTriage.Items"/>) for <see cref="Today"/>.</summary>
internal sealed record TriageView
{
    public TriageView(TriageKind kind, InboxQueue queue, DateOnly today)
    {
        Kind = kind;
        Queue = queue;
        Today = today;
    }

    public TriageKind Kind { get; }

    public InboxQueue Queue { get; }

    /// <summary>The day the queue was built for; snooze days and an empty todo date count from it.</summary>
    public DateOnly Today { get; }

    /// <summary>Returns only the type name, so formatting the view never prints the inbox's text.</summary>
    public override string ToString() => nameof(TriageView);
}
