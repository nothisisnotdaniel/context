using System.Globalization;

namespace Context;

/// <summary>A todo due today or earlier, as shown in the <c>/current</c> view.</summary>
internal sealed record DueTodo
{
    public DueTodo(DateOnly due, string text, bool overdue)
    {
        Due = due;
        Text = text;
        Overdue = overdue;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(DueTodo);

    public DateOnly Due { get; }

    /// <summary>The todo text as stored (raw: clean it before showing it).</summary>
    public string Text { get; }

    /// <summary>True when <see cref="Due"/> is before today.</summary>
    public bool Overdue { get; }
}

/// <summary>A note linked to the due todos through at least one shared tag.</summary>
internal sealed record LinkedNote
{
    public LinkedNote(string name, IReadOnlyList<string> tags)
    {
        Name = name;
        Tags = tags;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(LinkedNote);

    /// <summary>The note name (raw: clean it before showing it).</summary>
    public string Name { get; }

    /// <summary>The matching tags, normalised (lower-case, no <c>#</c>), distinct and sorted ordinally.</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// The data of the <c>/current</c> view, with no rendering: today's appointments, every todo due today or
/// earlier, the notes linked to them and the notes related to each appointment. Any front end builds it with
/// <see cref="Build"/> and draws it its own way. All text in it (appointment titles and locations, file names,
/// todo text, note names, tags) is raw outside text: every front end must clean it (<see cref="SafeText.Clean"/>)
/// and escape it for its own output.
/// </summary>
internal sealed record CurrentView
{
    /// <summary>Shown for a side of an appointment that lies outside today.</summary>
    public const string Outside = "…";

    private const string TimeFormat = "HH:mm";

    public CurrentView(
        DateOnly today,
        TimeZoneInfo zone,
        IReadOnlyList<Appointment> appointments,
        string folder,
        IReadOnlyList<string> unreadableCalendarFiles,
        CalendarFeedStatus status,
        DateTime? cachedAtUtc,
        string? reason,
        bool todosUnreadable,
        IReadOnlyList<DueTodo> due,
        IReadOnlyList<LinkedNote> linkedNotes,
        bool indexUnreadable,
        IReadOnlyList<IReadOnlyList<RelatedNote>>? related = null)
    {
        Today = today;
        Zone = zone;
        Appointments = appointments;
        Folder = folder;
        UnreadableCalendarFiles = unreadableCalendarFiles;
        Status = status;
        CachedAtUtc = cachedAtUtc;
        Reason = reason;
        TodosUnreadable = todosUnreadable;
        Due = due;
        LinkedNotes = linkedNotes;
        IndexUnreadable = indexUnreadable;
        Related = related ?? appointments.Select(_ => (IReadOnlyList<RelatedNote>)[]).ToList();
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(CurrentView);

    /// <summary>The day the view is for (the title date); due todos are those due on or before it.</summary>
    public DateOnly Today { get; }

    /// <summary>
    /// The calendar's zone: <see cref="Build"/> sets it to the <see cref="CalendarStore"/>'s zone, and it is never
    /// <c>null</c>. The <c>Start</c>/<c>End</c> of <see cref="Appointments"/> are already local times in it, so don't
    /// convert them again; convert <see cref="CachedAtUtc"/> with it before showing it. Renderers use it instead of
    /// <c>TimeZoneInfo.Local</c>, so the view alone decides which zone times are shown in.
    /// </summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Every appointment of the day (files and feed merged), sorted, not truncated.</summary>
    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The full path of the calendar folder (raw).</summary>
    public string Folder { get; }

    /// <summary>The names of the calendar files that couldn't be read (raw).</summary>
    public IReadOnlyList<string> UnreadableCalendarFiles { get; }

    /// <summary>
    /// The Exchange feed's status, and the feed line a renderer draws for it (the feed's events are already merged
    /// into <see cref="Appointments"/>):
    /// <c>NotConfigured</c> (no link set, or no feed given) and <c>Fresh</c>: no feed line.
    /// <c>Stale</c>: the download failed and the events are the older cached copy; draw an offline line with the
    /// cache time from <see cref="CachedAtUtc"/>, and only when it is non-null (<see cref="Build"/> always sets it
    /// for <c>Stale</c>, but a hand-built view may not). <see cref="Reason"/> is set too, but showing it is optional.
    /// <c>Failed</c>: the download failed and there is no cached copy; draw an error line with <see cref="Reason"/>.
    /// Never show the calendar link itself in any of these lines.
    /// </summary>
    public CalendarFeedStatus Status { get; }

    /// <summary>
    /// When the feed's text was downloaded (UTC; convert it with <see cref="Zone"/> before showing it). Non-null for
    /// <c>Stale</c> (the time of the cached copy), also set for <c>Fresh</c>; <c>null</c> for <c>NotConfigured</c>
    /// and <c>Failed</c>. Check it for <c>null</c> before using it.
    /// </summary>
    public DateTime? CachedAtUtc { get; }

    /// <summary>
    /// Why the feed failed: non-null for <c>Stale</c> and <c>Failed</c>, <c>null</c> otherwise. It is always one of
    /// the fixed texts in <see cref="CalendarFeed"/>, never an exception message; still clean and escape it like
    /// any other text before showing it.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// True when <c>todos.json</c> can't be read; then <see cref="Due"/> and <see cref="LinkedNotes"/> are empty.
    /// </summary>
    public bool TodosUnreadable { get; }

    /// <summary>Every todo due today or earlier, in sidebar order.</summary>
    public IReadOnlyList<DueTodo> Due { get; }

    /// <summary>The linked notes, sorted by name ignoring case (then ordinally).</summary>
    public IReadOnlyList<LinkedNote> LinkedNotes { get; }

    /// <summary>
    /// The notes related to each appointment (meeting prep, <see cref="MeetingPrep.Related"/>): runs parallel to
    /// <see cref="Appointments"/>, same count and order, so <c>Related[i]</c> belongs to <c>Appointments[i]</c>. Each
    /// list holds at most <see cref="MeetingPrep.MaxRelated"/> notes, ranked; empty when none match or when
    /// <see cref="IndexUnreadable"/> is set. Names and tags are raw: clean them before showing them.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RelatedNote>> Related { get; }

    /// <summary>
    /// True when <c>context.db</c> can't be read; then <see cref="LinkedNotes"/> and every list in
    /// <see cref="Related"/> are empty.
    /// </summary>
    public bool IndexUnreadable { get; }

    /// <summary>
    /// Reads everything the view needs. The feed (if any) is fetched once. The index is synced once and read through
    /// one <see cref="TagIndex.NoteTags"/> call when there is at least one appointment, and synced when something is
    /// due; <paramref name="dueOnDayOnly"/> keeps only the todos due exactly on <paramref name="today"/> (a chosen day's
    /// view: none is overdue); with no appointments and nothing due (also when <c>todos.json</c> can't be read) it isn't touched.
    /// </summary>
    public static CurrentView Build(
        TodoStore todos, TagIndex index, NoteStore notes, CalendarStore calendar, CalendarFeed? feed, DateOnly today,
        bool dueOnDayOnly = false)
    {
        CalendarFeedResult result = Fetch(feed);
        CalendarDay day = calendar.Day(today, FeedEvents(result, calendar.Zone, today));

        IReadOnlyList<TodoItem> items;
        bool todosUnreadable = false;
        try
        {
            items = todos.List();
        }
        catch (TodoStoreException)
        {
            items = [];
            todosUnreadable = true;
        }

        List<DueTodo> due = DueOn(items, today);
        if (dueOnDayOnly)
        {
            due = due.Where(t => t.Due == today).ToList();
        }

        IReadOnlyList<LinkedNote> linked = [];
        IReadOnlyList<IReadOnlyList<RelatedNote>>? related = null;
        bool indexUnreadable = false;
        if (due.Count > 0 || day.Appointments.Count > 0)
        {
            try
            {
                index.Sync(notes);
                if (due.Count > 0)
                {
                    linked = Linked(index, due);
                }

                if (day.Appointments.Count > 0)
                {
                    IReadOnlyList<(string Note, string Tag)> noteTags = index.NoteTags();
                    IReadOnlyList<NoteInfo> listed = notes.List();
                    related = day.Appointments.Select(a => MeetingPrep.Related(a, listed, noteTags)).ToList();
                }
            }
            catch (TagIndexException)
            {
                linked = [];
                related = null;
                indexUnreadable = true;
            }
        }

        return new CurrentView(
            today,
            calendar.Zone,
            day.Appointments,
            calendar.Folder,
            day.Unreadable,
            result.Status,
            result.CachedAtUtc,
            result.Reason,
            todosUnreadable,
            due,
            linked,
            indexUnreadable,
            related);
    }

    /// <summary>Fetches the feed once; no feed is <see cref="CalendarFeedStatus.NotConfigured"/>.</summary>
    internal static CalendarFeedResult Fetch(CalendarFeed? feed) =>
        feed?.Fetch() ?? new CalendarFeedResult(CalendarFeedStatus.NotConfigured);

    /// <summary>The feed's events touching <paramref name="day"/>, or none when it has no text or doesn't parse.</summary>
    internal static IReadOnlyList<Appointment> FeedEvents(CalendarFeedResult result, TimeZoneInfo zone, DateOnly day)
    {
        if (result.Text is not null)
        {
            try
            {
                return IcsParser.Parse(result.Text, zone, day);
            }
            catch (FormatException)
            {
                // The feed only caches text that parsed, so this only happens if the cache was changed by hand.
            }
        }

        return [];
    }

    /// <summary>Every todo of <paramref name="items"/> due on or before <paramref name="today"/>, in their order.</summary>
    internal static List<DueTodo> DueOn(IReadOnlyList<TodoItem> items, DateOnly today)
    {
        List<DueTodo> due = [];
        foreach (TodoItem todo in items)
        {
            if (todo.Due <= today)
            {
                due.Add(new DueTodo(todo.Due, todo.Text, todo.Due < today));
            }
        }

        return due;
    }

    /// <summary>The time text of <paramref name="appointment"/> on <see cref="Today"/>.</summary>
    public string TimeText(Appointment appointment)
    {
        DateTime from = Today.ToDateTime(TimeOnly.MinValue);
        return Time(appointment, from, from.AddDays(1));
    }

    /// <summary><c>all day</c>, or <c>HH:mm-HH:mm</c> with <c>…</c> for a side outside <c>[from, to]</c>.</summary>
    internal static string Time(Appointment appointment, DateTime from, DateTime to)
    {
        if (appointment.AllDay)
        {
            return "all day";
        }

        string start = appointment.Start < from
            ? Outside
            : appointment.Start.ToString(TimeFormat, CultureInfo.InvariantCulture);
        string end = appointment.End > to
            ? Outside
            : appointment.End.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return $"{start}-{end}";
    }

    private static List<LinkedNote> Linked(TagIndex index, IReadOnlyList<DueTodo> due)
    {
        Dictionary<string, SortedSet<string>> linked = new(StringComparer.Ordinal);
        IEnumerable<string> tags = due
            .SelectMany(t => TagParser.Parse(t.Text))
            .Distinct(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            foreach (string note in index.NotesWithTag(tag))
            {
                if (!linked.TryGetValue(note, out SortedSet<string>? noteTags))
                {
                    noteTags = new SortedSet<string>(StringComparer.Ordinal);
                    linked[note] = noteTags;
                }

                noteTags.Add(tag);
            }
        }

        return linked.Keys
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n, StringComparer.Ordinal)
            .Select(n => new LinkedNote(n, linked[n].ToList()))
            .ToList();
    }
}
