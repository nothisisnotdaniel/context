using System.Globalization;

namespace Context;

/// <summary>A todo due today or earlier, as shown in the <c>/current</c> view.</summary>
internal sealed record DueTodo
{
    public DueTodo(int number, DateOnly due, string text, bool overdue)
    {
        Number = number;
        Due = due;
        Text = text;
        Overdue = overdue;
    }

    /// <summary>The todo's number as shown in the sidebar (1-based, in <see cref="TodoStore.List"/> order).</summary>
    public int Number { get; }

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

    /// <summary>The note name (raw: clean it before showing it).</summary>
    public string Name { get; }

    /// <summary>The matching tags, normalised (lower-case, no <c>#</c>), distinct and sorted ordinally.</summary>
    public IReadOnlyList<string> Tags { get; }
}

/// <summary>
/// The data of the <c>/current</c> view, with no rendering: today's appointments, every todo due today or
/// earlier and the notes linked to them. Any front end builds it with <see cref="Build"/> and draws it its own
/// way. All text in it (appointment titles and locations, file names, todo text, note names, tags) is raw
/// outside text: every front end must clean it (<see cref="SafeText.Clean"/>) and escape it for its own output.
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
        bool indexUnreadable)
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
    }

    public DateOnly Today { get; }

    /// <summary>The calendar's zone: appointment times are in it, and the feed's cache time is shown in it.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Every appointment of the day (files and feed merged), sorted, not truncated.</summary>
    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The full path of the calendar folder (raw).</summary>
    public string Folder { get; }

    /// <summary>The names of the calendar files that couldn't be read (raw).</summary>
    public IReadOnlyList<string> UnreadableCalendarFiles { get; }

    /// <summary>The Exchange feed's status; <c>NotConfigured</c> without a feed.</summary>
    public CalendarFeedStatus Status { get; }

    /// <summary>When the feed's text was downloaded (UTC), if there is any.</summary>
    public DateTime? CachedAtUtc { get; }

    /// <summary>
    /// Why the feed failed (<c>Stale</c>, <c>Failed</c>): one of the fixed texts in <see cref="CalendarFeed"/>.
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

    /// <summary>True when <c>context.db</c> can't be read; then <see cref="LinkedNotes"/> is empty.</summary>
    public bool IndexUnreadable { get; }

    /// <summary>
    /// Reads everything the view needs. The feed (if any) is fetched once; when <c>todos.json</c> can't be read
    /// there are no due todos and the index isn't touched, and when nothing is due the index isn't touched either.
    /// </summary>
    public static CurrentView Build(
        TodoStore todos, TagIndex index, NoteStore notes, CalendarStore calendar, CalendarFeed? feed, DateOnly today)
    {
        CalendarFeedResult result = feed?.Fetch() ?? new CalendarFeedResult(CalendarFeedStatus.NotConfigured);
        IReadOnlyList<Appointment> extra = [];
        if (result.Text is not null)
        {
            try
            {
                extra = IcsParser.Parse(result.Text, calendar.Zone);
            }
            catch (FormatException)
            {
                // The feed only caches text that parsed, so this only happens if the cache was changed by hand.
            }
        }

        CalendarDay day = calendar.Day(today, extra);

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

        List<DueTodo> due = [];
        for (int i = 0; i < items.Count; i++)
        {
            TodoItem todo = items[i];
            if (todo.Due <= today)
            {
                due.Add(new DueTodo(i + 1, todo.Due, todo.Text, todo.Due < today));
            }
        }

        IReadOnlyList<LinkedNote> linked = [];
        bool indexUnreadable = false;
        if (due.Count > 0)
        {
            try
            {
                linked = Linked(index, notes, due);
            }
            catch (TagIndexException)
            {
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
            indexUnreadable);
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

    private static List<LinkedNote> Linked(TagIndex index, NoteStore notes, IReadOnlyList<DueTodo> due)
    {
        index.Sync(notes);
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
