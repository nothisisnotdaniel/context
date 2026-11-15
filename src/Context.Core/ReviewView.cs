namespace Context;

/// <summary>A note changed during the review week, as shown in the review.</summary>
internal sealed record ChangedNote
{
    public ChangedNote(string name, DateTime modified)
    {
        Name = name;
        Modified = modified;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(ChangedNote);

    /// <summary>The full note name, subpages included (raw: clean it before showing it).</summary>
    public string Name { get; }

    /// <summary>The note file's last-write time as a local time in the view's zone.</summary>
    public DateTime Modified { get; }
}

/// <summary>One day of the coming week with its appointments, as shown in the review.</summary>
internal sealed record ReviewDay
{
    public ReviewDay(DateOnly date, IReadOnlyList<Appointment> appointments)
    {
        Date = date;
        Appointments = appointments;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(ReviewDay);

    public DateOnly Date { get; }

    /// <summary>The day's appointments (files and feed merged), sorted like <see cref="CalendarDay"/>.</summary>
    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The time text of <paramref name="appointment"/> on this day, as <c>current</c> shows it.</summary>
    public string TimeText(Appointment appointment)
    {
        DateTime from = Date.ToDateTime(TimeOnly.MinValue);
        return CurrentView.Time(appointment, from, from.AddDays(1));
    }
}

/// <summary>
/// The data of the weekly review, with no rendering: the notes changed and the todos done in the last 7 days
/// (today included), the overdue todos and the appointments of the next 7 days. Any front end builds it with
/// <see cref="Build"/> and draws it its own way. All text in it is raw outside text: every front end must clean it
/// (<see cref="SafeText.Clean"/>) and escape it for its own output. It never opens <c>context.db</c>.
/// </summary>
internal sealed record ReviewView
{
    /// <summary>The number of days looked back (today included) and ahead (from tomorrow).</summary>
    public const int WindowDays = 7;

    public ReviewView(
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        IReadOnlyList<ChangedNote> changedNotes,
        bool todosUnreadable,
        IReadOnlyList<TodoItem> doneTodos,
        IReadOnlyList<TodoItem> overdue,
        IReadOnlyList<ReviewDay> days,
        string folder,
        IReadOnlyList<string> unreadableCalendarFiles,
        CalendarFeedStatus status,
        DateTime? cachedAtUtc,
        string? reason)
    {
        From = from;
        To = to;
        Zone = zone;
        ChangedNotes = changedNotes;
        TodosUnreadable = todosUnreadable;
        DoneTodos = doneTodos;
        Overdue = overdue;
        Days = days;
        Folder = folder;
        UnreadableCalendarFiles = unreadableCalendarFiles;
        Status = status;
        CachedAtUtc = cachedAtUtc;
        Reason = reason;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(ReviewView);

    /// <summary>The first day looked back (today-6).</summary>
    public DateOnly From { get; }

    /// <summary>The last day looked back (today).</summary>
    public DateOnly To { get; }

    /// <summary>The calendar's zone, in which every day and time of the view is counted.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>The notes (no sensitive ones) last written from <see cref="From"/> to <see cref="To"/>, newest first.</summary>
    public IReadOnlyList<ChangedNote> ChangedNotes { get; }

    /// <summary>True when <c>todos.json</c> can't be read; then <see cref="DoneTodos"/> and <see cref="Overdue"/> are empty.</summary>
    public bool TodosUnreadable { get; }

    /// <summary>The todos done from <see cref="From"/> to <see cref="To"/>, in <see cref="TodoStore.ListDone"/> order.</summary>
    public IReadOnlyList<TodoItem> DoneTodos { get; }

    /// <summary>The open todos due before <see cref="To"/>, in <see cref="TodoStore.List"/> order.</summary>
    public IReadOnlyList<TodoItem> Overdue { get; }

    /// <summary>Seven entries, today+1 to today+7, each with its appointments.</summary>
    public IReadOnlyList<ReviewDay> Days { get; }

    /// <summary>The full path of the calendar folder (raw).</summary>
    public string Folder { get; }

    /// <summary>The names of the calendar files that couldn't be read on any of the days, each once (raw).</summary>
    public IReadOnlyList<string> UnreadableCalendarFiles { get; }

    /// <summary>The Exchange feed's status, as in <see cref="CurrentView.Status"/>.</summary>
    public CalendarFeedStatus Status { get; }

    /// <summary>When the feed's text was downloaded (UTC), as in <see cref="CurrentView.CachedAtUtc"/>.</summary>
    public DateTime? CachedAtUtc { get; }

    /// <summary>Why the feed failed (a fixed text), as in <see cref="CurrentView.Reason"/>.</summary>
    public string? Reason { get; }

    /// <summary>The day <paramref name="todo"/> was done, in <see cref="Zone"/>.</summary>
    public DateOnly DoneDate(TodoItem todo) =>
        todo.Done is { } done
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(done, DateTimeKind.Utc), Zone))
            : throw new ArgumentException("The todo is not done.", nameof(todo));

    /// <summary>
    /// Reads everything the review needs. The feed (if any) is fetched once; <c>context.db</c> is never opened. An
    /// unreadable <c>todos.json</c> sets <see cref="TodosUnreadable"/>; the rest is still filled.
    /// </summary>
    public static ReviewView Build(
        NoteStore notes, TodoStore todos, CalendarStore calendar, CalendarFeed? feed, DateOnly today)
    {
        TimeZoneInfo zone = calendar.Zone;
        DateOnly from = today.AddDays(-(WindowDays - 1));

        List<ChangedNote> changed = notes.List()
            .Select(n => new ChangedNote(n.Name, InZone(n.Modified, zone)))
            .Where(n => InRange(DateOnly.FromDateTime(n.Modified), from, today))
            .OrderByDescending(n => n.Modified)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<TodoItem> done = [];
        IReadOnlyList<TodoItem> overdue = [];
        bool todosUnreadable = false;
        try
        {
            IReadOnlyList<TodoItem> open = todos.List();
            IReadOnlyList<TodoItem> finished = todos.ListDone();
            overdue = open.Where(t => t.Due < today).ToList();
            done = finished
                .Where(t => t.Done is { } d
                    && InRange(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), zone)), from, today))
                .ToList();
        }
        catch (TodoStoreException)
        {
            done = [];
            overdue = [];
            todosUnreadable = true;
        }

        CalendarFeedResult result = CurrentView.Fetch(feed);
        List<ReviewDay> days = [];
        List<string> unreadable = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 1; i <= WindowDays; i++)
        {
            DateOnly day = today.AddDays(i);
            CalendarDay read = calendar.Day(day, CurrentView.FeedEvents(result, zone, day));
            days.Add(new ReviewDay(day, read.Appointments));
            foreach (string file in read.Unreadable)
            {
                if (seen.Add(file))
                {
                    unreadable.Add(file);
                }
            }
        }

        return new ReviewView(
            from,
            today,
            zone,
            changed,
            todosUnreadable,
            done,
            overdue,
            days,
            calendar.Folder,
            unreadable,
            result.Status,
            result.CachedAtUtc,
            result.Reason);
    }

    // A file time (local to this machine, or UTC) as a local time in zone.
    private static DateTime InZone(DateTime time, TimeZoneInfo zone)
    {
        DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }

    private static bool InRange(DateOnly day, DateOnly from, DateOnly to) => day >= from && day <= to;
}
