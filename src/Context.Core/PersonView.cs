namespace Context;

/// <summary>What <see cref="PersonView.Build"/> found.</summary>
internal enum PersonViewKind
{
    /// <summary>No person name was given.</summary>
    MissingArgument,

    /// <summary>
    /// No note, appointment or todo mentions <see cref="PersonView.Person"/> (or the index can't be read and no
    /// appointment or todo does: check <see cref="PersonView.IndexUnreadable"/>).
    /// </summary>
    NoMentions,

    /// <summary>
    /// <see cref="PersonView.Notes"/>, <see cref="PersonView.Appointments"/>, <see cref="PersonView.OpenTodos"/> or
    /// <see cref="PersonView.DoneTodos"/> is non-empty.
    /// </summary>
    Mentions,
}

/// <summary>
/// The data of a person page (<c>context person &lt;name&gt;</c> and the desktop person panel), with no rendering:
/// the notes that mention the person with <c>@[...]</c>, the appointments that mention them or list them as an
/// attendee, and the open and done todos whose text mentions them. Everything is read from disk: the calendar files
/// and the cached copy of the published calendar, never the network. All text in it (the name, note names,
/// appointment titles and locations, file names, todo text) is raw outside text: every front end must clean it
/// (<see cref="SafeText.Clean"/>) and escape it for its own output.
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
        TimeZoneInfo zone,
        IReadOnlyList<AliasClash>? aliasClashes = null,
        IReadOnlyList<string>? ambiguousWith = null,
        IReadOnlyList<TodoItem>? openTodos = null,
        IReadOnlyList<TodoItem>? doneTodos = null,
        bool todosUnreadable = false,
        IReadOnlyList<TodoItem>? waiting = null,
        IReadOnlyList<NoteTopic>? topics = null,
        bool archiveUnreadable = false)
    {
        Kind = kind;
        Person = person;
        Notes = notes;
        Appointments = appointments;
        UnreadableCalendarFiles = unreadableCalendarFiles;
        IndexUnreadable = indexUnreadable;
        Zone = zone;
        AliasClashes = aliasClashes ?? [];
        AmbiguousWith = ambiguousWith ?? [];
        OpenTodos = openTodos ?? [];
        DoneTodos = doneTodos ?? [];
        TodosUnreadable = todosUnreadable;
        Waiting = waiting ?? [];
        Topics = topics ?? [];
        ArchiveUnreadable = archiveUnreadable;
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
    /// When the name resolves to a person page: the aliases on it that link to no one or to another person
    /// (<see cref="PersonAliases.Clashes"/>), in the page's order; else empty.
    /// </summary>
    public IReadOnlyList<AliasClash> AliasClashes { get; }

    /// <summary>
    /// When the name is an alias of more than one person page: their titles (raw), sorted ignoring case, then
    /// ordinal; the name then stays unresolved. Else empty.
    /// </summary>
    public IReadOnlyList<string> AmbiguousWith { get; }

    /// <summary>
    /// The open todos whose text mentions the person with <c>@[...]</c> (resolved through the aliases, like
    /// appointments), in <see cref="TodoStore.List"/> order. Empty when no todo store was given or it can't be read.
    /// </summary>
    public IReadOnlyList<TodoItem> OpenTodos { get; }

    /// <summary>
    /// The open waiting-for todos (<see cref="WaitingFor.TryParse"/>) whose person resolves to this person (through
    /// the aliases), in <see cref="TodoStore.List"/> order. They are left out of <see cref="OpenTodos"/>.
    /// </summary>
    public IReadOnlyList<TodoItem> Waiting { get; }

    /// <summary>
    /// The open topics for the next 1:1 with the person (job 253, <see cref="MeetingTopics.For"/> through the aliases).
    /// Empty when the index can't be read. They don't count for <see cref="Kind"/>: their note mentions the person.
    /// </summary>
    public IReadOnlyList<NoteTopic> Topics { get; }

    /// <summary>
    /// The matching done todos of <c>todos.json</c> and <c>todos-archive.json</c> (job 361), in
    /// <see cref="TodoStore.ListAllDone"/> order (latest done first).
    /// </summary>
    public IReadOnlyList<TodoItem> DoneTodos { get; }

    /// <summary>True when <c>todos-archive.json</c> can't be read; <see cref="DoneTodos"/> then holds only <c>todos.json</c> todos.</summary>
    public bool ArchiveUnreadable { get; }

    /// <summary>True when <c>todos.json</c> can't be read; then both todo lists are empty.</summary>
    public bool TodosUnreadable { get; }

    /// <summary>The day a done todo was done, in <see cref="Zone"/>.</summary>
    public DateOnly DoneDate(TodoItem todo) =>
        todo.Done is { } done
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(done, Zone))
            : throw new ArgumentException("The todo is not done.", nameof(todo));

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
    /// <c>@[Anna Schmidt]</c>, matched ignoring case and whitespace runs, resolved through the aliases
    /// (<see cref="PersonAliases"/>; from the index, or read from the person pages when it can't be read). Appointment
    /// mentions and attendee names are resolved the same way. Reads <paramref name="feed"/>'s cached copy
    /// only (<see cref="CalendarFeed.ReadCache"/>); no request is made. With <paramref name="todos"/>, the todos whose
    /// text mentions the person are matched the same way at query time (nothing is stored); an unreadable
    /// <c>todos.json</c> sets <see cref="TodosUnreadable"/>. Only <c>context.db</c> may be written.
    /// </summary>
    public static PersonView Build(
        TagIndex index, NoteStore store, CalendarStore calendar, CalendarFeed? feed, string argument, TodoStore? todos = null)
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

        PersonAliases? aliases = null;
        string? person = null;
        IReadOnlyList<string> notes = [];
        IReadOnlyList<NoteTopic> allTopics = [];
        bool indexUnreadable = false;
        try
        {
            index.Sync(store);
            aliases = index.Aliases();
            notes = index.NotesMentioning(typed);
            person = index.PersonName(typed);
            allTopics = index.Topics();
        }
        catch (TagIndexException)
        {
            indexUnreadable = true;
            notes = [];
            allTopics = [];
        }

        aliases ??= PersonAliases.Load(store);
        AliasResolution resolved = aliases.Resolve(typed);
        person ??= resolved.Person ?? typed;
        string key = aliases.PersonKey(typed);
        IReadOnlyList<AliasClash> clashes = resolved.Person is { } title ? aliases.Clashes(title) : [];

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
            .Where(a => Mentions(a, aliases, key))
            .OrderByDescending(a => a.Start)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<TodoItem> openTodos = [];
        List<TodoItem> doneTodos = [];
        List<TodoItem> waiting = [];
        bool todosUnreadable = false;
        bool archiveUnreadable = false;
        if (todos is not null)
        {
            try
            {
                IReadOnlyList<TodoItem> open = todos.List();
                IReadOnlyList<TodoItem> done = todos.ListAllDone(out archiveUnreadable);
                waiting = open.Where(t => WaitsOn(t.Text, aliases, key)).ToList();
                openTodos = open.Where(t => !waiting.Contains(t) && Holds(t.Text, aliases, key)).ToList();
                doneTodos = done.Where(t => Holds(t.Text, aliases, key)).ToList();
            }
            catch (TodoStoreException)
            {
                todosUnreadable = true;
                archiveUnreadable = false;
                openTodos = [];
                doneTodos = [];
                waiting = [];
            }
        }

        PersonViewKind kind = notes.Count == 0 && appointments.Count == 0 && openTodos.Count == 0 && doneTodos.Count == 0
            && waiting.Count == 0
            ? PersonViewKind.NoMentions
            : PersonViewKind.Mentions;
        return new PersonView(
            kind, person, notes, appointments, all.Unreadable, indexUnreadable, calendar.Zone, clashes, resolved.Titles,
            openTodos, doneTodos, todosUnreadable, waiting, MeetingTopics.For(allTopics, aliases, [typed]),
            archiveUnreadable);
    }

    private static bool Mentions(Appointment appointment, PersonAliases aliases, string key) =>
        appointment.Attendees.Any(a => string.Equals(aliases.PersonKey(a), key, StringComparison.Ordinal))
        || Holds(appointment.Title, aliases, key)
        || (appointment.Location is not null && Holds(appointment.Location, aliases, key));

    private static bool WaitsOn(string text, PersonAliases aliases, string key) =>
        WaitingFor.TryParse(text) is { } item
        && string.Equals(aliases.PersonKey(item.Person), key, StringComparison.Ordinal);

    private static bool Holds(string text, PersonAliases aliases, string key) =>
        PersonParser.Parse(text).Any(n => string.Equals(aliases.PersonKey(n), key, StringComparison.Ordinal));
}
