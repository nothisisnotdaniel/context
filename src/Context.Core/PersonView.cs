namespace Context;

/// <summary>What <see cref="PersonView.Build"/> found.</summary>
internal enum PersonViewKind
{
    /// <summary>No person name was given.</summary>
    MissingArgument,

    /// <summary>
    /// No note and no appointment mentions <see cref="PersonView.Person"/> (or the index can't be read and no
    /// appointment does: check <see cref="PersonView.IndexUnreadable"/>).
    /// </summary>
    NoMentions,

    /// <summary><see cref="PersonView.Notes"/> or <see cref="PersonView.Appointments"/> is non-empty.</summary>
    Mentions,
}

/// <summary>
/// The data of a person page (<c>context person &lt;name&gt;</c> and the desktop person panel), with no rendering:
/// the notes that mention the person with <c>@[...]</c> and the appointments that mention them or list them as an
/// attendee. Everything is read from disk: the calendar files and the cached copy of the published calendar, never
/// the network. All text in it (the name, note names, appointment titles and locations, file names) is raw outside
/// text: every front end must clean it (<see cref="SafeText.Clean"/>) and escape it for its own output.
/// </summary>
internal sealed record PersonView
{
    public PersonView(
        PersonViewKind kind,
        string person,
        IReadOnlyList<string> notes,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<string> unreadableCalendarFiles,
        bool indexUnreadable,
        TimeZoneInfo zone)
    {
        Kind = kind;
        Person = person;
        Notes = notes;
        Appointments = appointments;
        UnreadableCalendarFiles = unreadableCalendarFiles;
        IndexUnreadable = indexUnreadable;
        Zone = zone;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never
    /// print them. Front ends draw it through their <c>Render</c> step.
    /// </summary>
    public override string ToString() => nameof(PersonView);

    /// <summary>
    /// What was found: <c>MissingArgument</c> → no field (draw a usage hint); <c>NoMentions</c> →
    /// <see cref="Person"/>; <c>Mentions</c> → <see cref="Person"/>, <see cref="Notes"/> and
    /// <see cref="Appointments"/>. Check <see cref="IndexUnreadable"/> and <see cref="UnreadableCalendarFiles"/> for
    /// every kind but <c>MissingArgument</c>.
    /// </summary>
    public PersonViewKind Kind { get; }

    /// <summary>
    /// The name shown for the person (raw): the title of their page when it exists, else the ordinal-first spelling
    /// among the mentions in notes, else the name as typed, trimmed and with whitespace runs collapsed. Empty for
    /// <c>MissingArgument</c>.
    /// </summary>
    public string Person { get; }

    /// <summary>
    /// The full names of the notes that mention the person, sorted by name ignoring case, never the person's own page
    /// (raw). Empty when the index can't be read.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// The appointments (calendar files and the cached published calendar) whose title or location mentions the
    /// person with <c>@[...]</c> or whose attendee name matches them (by <see cref="PersonParser.Key"/>), sorted by
    /// start, latest first, then by title ignoring case. Their times are local times in <see cref="Zone"/>.
    /// </summary>
    public IReadOnlyList<Appointment> Appointments { get; }

    /// <summary>The names of the calendar files that couldn't be read (raw), as in <see cref="CurrentView"/>.</summary>
    public IReadOnlyList<string> UnreadableCalendarFiles { get; }

    /// <summary>True when <c>context.db</c> can't be read; then <see cref="Notes"/> is empty.</summary>
    public bool IndexUnreadable { get; }

    /// <summary>The calendar's zone; the appointment times are already local times in it.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// <c>all day</c>, or <c>HH:mm-HH:mm</c> on the appointment's start day, with <c>…</c> for an end on a later day.
    /// </summary>
    public static string TimeText(Appointment appointment)
    {
        DateTime from = appointment.Start.Date;
        return CurrentView.Time(appointment, from, from.AddDays(1));
    }

    /// <summary>
    /// Syncs the index and reads the person page of <paramref name="argument"/>: <c>Anna Schmidt</c> or
    /// <c>@[Anna Schmidt]</c>, matched ignoring case and whitespace runs. Reads <paramref name="feed"/>'s cached copy
    /// only (<see cref="CalendarFeed.ReadCache"/>); no request is made. Only <c>context.db</c> may be written.
    /// </summary>
    public static PersonView Build(TagIndex index, NoteStore store, CalendarStore calendar, CalendarFeed? feed, string argument)
    {
        string typed = TagParser.CollapseWhitespace(argument);
        if (typed.Length >= 3 && typed.StartsWith("@[", StringComparison.Ordinal) && typed.EndsWith(']'))
        {
            typed = TagParser.CollapseWhitespace(typed.AsSpan(2, typed.Length - 3));
        }

        if (typed.Length == 0)
        {
            return new PersonView(PersonViewKind.MissingArgument, string.Empty, [], [], [], false, calendar.Zone);
        }

        string key = PersonParser.Key(typed);
        string person = typed;
        IReadOnlyList<string> notes = [];
        bool indexUnreadable = false;
        try
        {
            index.Sync(store);
            notes = index.NotesMentioning(typed);
            person = index.PersonName(typed) ?? typed;
        }
        catch (TagIndexException)
        {
            indexUnreadable = true;
        }

        IReadOnlyList<Appointment> extra = [];
        string? cached = feed?.ReadCache();
        if (cached is not null)
        {
            try
            {
                extra = IcsParser.Parse(cached, calendar.Zone);
            }
            catch (FormatException)
            {
                // The feed only caches text that parsed, so this only happens if the cache was changed by hand.
            }
        }

        CalendarDay all = calendar.All(extra);
        List<Appointment> appointments = all.Appointments
            .Where(a => Mentions(a, key))
            .OrderByDescending(a => a.Start)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PersonViewKind kind = notes.Count == 0 && appointments.Count == 0 ? PersonViewKind.NoMentions : PersonViewKind.Mentions;
        return new PersonView(kind, person, notes, appointments, all.Unreadable, indexUnreadable, calendar.Zone);
    }

    private static bool Mentions(Appointment appointment, string key) =>
        appointment.Attendees.Any(a => string.Equals(PersonParser.Key(a), key, StringComparison.Ordinal))
        || Holds(appointment.Title, key)
        || (appointment.Location is not null && Holds(appointment.Location, key));

    private static bool Holds(string text, string key) =>
        PersonParser.Parse(text).Any(n => string.Equals(PersonParser.Key(n), key, StringComparison.Ordinal));
}
