using System.Globalization;

namespace Context;

/// <summary>How the Times hours of a work item page were read.</summary>
internal enum WorkItemTimesKind
{
    /// <summary>No Times folder: <c>null</c> or missing.</summary>
    NotSetUp,

    /// <summary>The Times folder exists but can't be listed.</summary>
    Unlistable,

    /// <summary>The day files were read (<see cref="WorkItemView.Hours"/>).</summary>
    Read,
}

/// <summary>How the commits of a work item page were read.</summary>
internal enum WorkItemCommitsKind
{
    /// <summary>No repository folders are set.</summary>
    NotSetUp,

    /// <summary><c>git</c> could not be started.</summary>
    GitMissing,

    /// <summary>The repositories were read (some may be in <see cref="WorkItemView.FailedRepos"/>).</summary>
    Read,
}

/// <summary>A person mentioned in the notes and todos of a work item, with the number of those notes and todos.</summary>
internal sealed record WorkItemPerson
{
    public WorkItemPerson(string name, int count)
    {
        Name = name;
        Count = count;
    }

    /// <summary>The name shown (raw): the person page's title, else the ordinal-first spelling.</summary>
    public string Name { get; }

    /// <summary>The number of notes plus todos that mention the person.</summary>
    public int Count { get; }

    public override string ToString() => nameof(WorkItemPerson);
}

/// <summary>The hours booked on a work item in one ISO week.</summary>
internal sealed record WorkItemWeek
{
    public WorkItemWeek(string week, TimeSpan total)
    {
        Week = week;
        Total = total;
    }

    /// <summary>The ISO week, <c>yyyy-Www</c> (<see cref="ISOWeek"/>).</summary>
    public string Week { get; }

    public TimeSpan Total { get; }

    public override string ToString() => nameof(WorkItemWeek);
}

/// <summary>The hours booked on a work item in Times.</summary>
internal sealed record WorkItemHours
{
    public WorkItemHours(IReadOnlyList<WorkItemWeek> weeks, TimeSpan total, int skippedFiles)
    {
        Weeks = weeks;
        Total = total;
        SkippedFiles = skippedFiles;
    }

    /// <summary>The weeks with time above zero, newest first.</summary>
    public IReadOnlyList<WorkItemWeek> Weeks { get; }

    /// <summary>The sum of every week.</summary>
    public TimeSpan Total { get; }

    /// <summary>The day files that couldn't be read.</summary>
    public int SkippedFiles { get; }

    public override string ToString() => nameof(WorkItemHours);
}

/// <summary>A commit that mentions a work item, with the folder name of its repository (raw).</summary>
internal sealed record WorkItemCommit
{
    public WorkItemCommit(string repo, string hash, DateOnly date, string subject)
    {
        Repo = repo;
        Hash = hash;
        Date = date;
        Subject = subject;
    }

    /// <summary>The repository folder's name (its last segment).</summary>
    public string Repo { get; }

    public string Hash { get; }

    public DateOnly Date { get; }

    public string Subject { get; }

    public override string ToString() => nameof(WorkItemCommit);
}

/// <summary>
/// The data of a work item page (job 280: <c>items/&lt;n&gt;</c>, <c>context item &lt;n&gt;</c>), with no rendering:
/// the notes, todos, people and meetings that mention <c>#&lt;n&gt;</c>, the hours booked on it in Times and the
/// commits that mention it. All text is raw outside text: every front end cleans it. Only <c>context.db</c> may be
/// written; the Times folder and the repositories are only read.
/// </summary>
internal sealed record WorkItemView
{
    public WorkItemView(
        int item,
        IReadOnlyList<string> notes,
        IReadOnlyList<TodoItem> openTodos,
        IReadOnlyList<TodoItem> doneTodos,
        IReadOnlyList<WorkItemPerson> people,
        IReadOnlyList<Appointment> meetings,
        WorkItemHours hours,
        IReadOnlyList<WorkItemCommit> commits,
        bool indexUnreadable,
        bool todosUnreadable,
        WorkItemTimesKind timesKind,
        WorkItemCommitsKind commitsKind,
        IReadOnlyList<string> failedRepos,
        TimeZoneInfo zone,
        bool archiveUnreadable = false)
    {
        Item = item;
        Notes = notes;
        OpenTodos = openTodos;
        DoneTodos = doneTodos;
        People = people;
        Meetings = meetings;
        Hours = hours;
        Commits = commits;
        IndexUnreadable = indexUnreadable;
        TodosUnreadable = todosUnreadable;
        TimesKind = timesKind;
        CommitsKind = commitsKind;
        FailedRepos = failedRepos;
        Zone = zone;
        ArchiveUnreadable = archiveUnreadable;
    }

    public override string ToString() => nameof(WorkItemView);

    /// <summary>The work item number.</summary>
    public int Item { get; }

    /// <summary>The notes that mention the item, never its own page, sorted ignoring case, then ordinal. Empty when the index can't be read.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>The open <c>todos.json</c> todos whose text mentions the item, in <see cref="TodoStore.List"/> order.</summary>
    public IReadOnlyList<TodoItem> OpenTodos { get; }

    /// <summary>
    /// The done todos of <c>todos.json</c> and <c>todos-archive.json</c> (job 361) whose text mentions the item, in
    /// <see cref="TodoStore.ListAllDone"/> order.
    /// </summary>
    public IReadOnlyList<TodoItem> DoneTodos { get; }

    /// <summary>True when <c>todos-archive.json</c> can't be read; <see cref="DoneTodos"/> then holds only <c>todos.json</c> todos.</summary>
    public bool ArchiveUnreadable { get; }

    /// <summary>
    /// The <c>@[...]</c> people of <see cref="Notes"/> and of both todo lists, resolved through the aliases, sorted by
    /// count descending, then by name ignoring case, then ordinal.
    /// </summary>
    public IReadOnlyList<WorkItemPerson> People { get; }

    /// <summary>
    /// The events (calendar files and the cached feed, one entry per <c>VEVENT</c>) whose title or location mentions
    /// the item, latest first, then by title ignoring case. Times are local in <see cref="Zone"/>.
    /// </summary>
    public IReadOnlyList<Appointment> Meetings { get; }

    /// <summary>The booked hours; empty unless <see cref="TimesKind"/> is <see cref="WorkItemTimesKind.Read"/>.</summary>
    public WorkItemHours Hours { get; }

    /// <summary>The commits that mention the item, newest first, then in repository order.</summary>
    public IReadOnlyList<WorkItemCommit> Commits { get; }

    /// <summary>True when <c>context.db</c> can't be read; then <see cref="Notes"/> is empty and people come from todos only.</summary>
    public bool IndexUnreadable { get; }

    /// <summary>True when <c>todos.json</c> can't be read; then both todo lists are empty.</summary>
    public bool TodosUnreadable { get; }

    public WorkItemTimesKind TimesKind { get; }

    public WorkItemCommitsKind CommitsKind { get; }

    /// <summary>The folder names of the repositories whose commits couldn't be read, in settings order.</summary>
    public IReadOnlyList<string> FailedRepos { get; }

    /// <summary>The calendar's zone.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>The day a done todo was done, in <see cref="Zone"/>.</summary>
    public DateOnly DoneDate(TodoItem todo) =>
        todo.Done is { } done
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(done, Zone))
            : throw new ArgumentException("The todo is not done.", nameof(todo));

    /// <summary>The ISO week name of <paramref name="day"/>: <c>yyyy-Www</c>.</summary>
    public static string WeekOf(DateOnly day)
    {
        DateTime date = day.ToDateTime(TimeOnly.MinValue);
        return ISOWeek.GetYear(date).ToString("0000", CultureInfo.InvariantCulture)
            + "-W" + ISOWeek.GetWeekOfYear(date).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Syncs the index and builds the page of work item <paramref name="item"/>. Never throws. Reads the feed's cache
    /// only (no request), Times' day files read-only and each repository through <paramref name="commitLog"/>, which is
    /// not called without repositories.
    /// </summary>
    public static WorkItemView Build(
        TagIndex index,
        NoteStore store,
        TodoStore? todos,
        CalendarStore calendar,
        CalendarFeed? feed,
        string? timesFolder,
        IReadOnlyList<string> repositories,
        ICommitLog commitLog,
        int item)
    {
        IReadOnlyList<string> notes = [];
        IReadOnlyList<(string Note, string Spelling)> noteMentions = [];
        PersonAliases? aliases = null;
        bool indexUnreadable = false;
        try
        {
            index.Sync(store);
            notes = index.NotesWithItem(item);
            noteMentions = index.MentionsIn(notes);
            aliases = index.Aliases();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            indexUnreadable = true;
            notes = [];
            noteMentions = [];
        }

        aliases ??= PersonAliases.Load(store);

        List<TodoItem> openTodos = [];
        List<TodoItem> doneTodos = [];
        bool todosUnreadable = false;
        bool archiveUnreadable = false;
        if (todos is not null)
        {
            try
            {
                openTodos = todos.List().Where(t => WorkItemParser.Mentions(t.Text, item)).ToList();
                doneTodos = todos.ListAllDone(out archiveUnreadable)
                    .Where(t => WorkItemParser.Mentions(t.Text, item)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                todosUnreadable = true;
                archiveUnreadable = false;
                openTodos = [];
                doneTodos = [];
            }
        }

        IReadOnlyList<WorkItemPerson> people = PeopleOf(noteMentions, openTodos.Concat(doneTodos), aliases);
        IReadOnlyList<Appointment> meetings = MeetingsOf(calendar, feed, item);
        (WorkItemTimesKind timesKind, WorkItemHours hours) = HoursOf(timesFolder, item);
        (WorkItemCommitsKind commitsKind, IReadOnlyList<WorkItemCommit> commits, IReadOnlyList<string> failed) =
            CommitsOf(repositories, commitLog, item);
        return new WorkItemView(
            item, notes, openTodos, doneTodos, people, meetings, hours, commits, indexUnreadable, todosUnreadable,
            timesKind, commitsKind, failed, calendar.Zone, archiveUnreadable);
    }

    private static List<WorkItemPerson> PeopleOf(
        IReadOnlyList<(string Note, string Spelling)> noteMentions, IEnumerable<TodoItem> todos, PersonAliases aliases)
    {
        // key -> (shown name, the sources that mention them)
        Dictionary<string, (string Name, HashSet<string> Sources)> found = new(StringComparer.Ordinal);
        void Add(string source, string spelling)
        {
            AliasResolution resolved = aliases.Resolve(spelling);
            string key = aliases.PersonKey(spelling);
            string name = resolved.Person ?? spelling;
            if (found.TryGetValue(key, out (string Name, HashSet<string> Sources) entry))
            {
                // Spellings of one key resolve alike: a person page's title, or the ordinal-first spelling.
                if (resolved.Person is null && string.CompareOrdinal(name, entry.Name) < 0)
                {
                    entry = (name, entry.Sources);
                }

                entry.Sources.Add(source);
                found[key] = entry;
            }
            else
            {
                found[key] = (name, new HashSet<string>(StringComparer.Ordinal) { source });
            }
        }

        foreach ((string note, string spelling) in noteMentions)
        {
            Add("note:" + note, spelling);
        }

        int number = 0;
        foreach (TodoItem todo in todos)
        {
            number++;
            foreach (string spelling in PersonParser.Parse(todo.Text))
            {
                Add("todo:" + number.ToString(CultureInfo.InvariantCulture), spelling);
            }
        }

        return found.Values
            .Select(v => new WorkItemPerson(v.Name, v.Sources.Count))
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<Appointment> MeetingsOf(CalendarStore calendar, CalendarFeed? feed, int item)
    {
        try
        {
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

            return calendar.All(extra).Appointments
                .Where(a => WorkItemParser.Mentions(a.Title, item)
                    || (a.Location is not null && WorkItemParser.Mentions(a.Location, item)))
                .OrderByDescending(a => a.Start)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static (WorkItemTimesKind, WorkItemHours) HoursOf(string? folder, int item)
    {
        WorkItemHours empty = new WorkItemHours([], TimeSpan.Zero, 0);
        if (folder is null || !Directory.Exists(folder))
        {
            return (WorkItemTimesKind.NotSetUp, empty);
        }

        List<DateOnly> days = [];
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (TimesBalance.TryParseDayFile(Path.GetFileName(path), out DateOnly day))
                {
                    days.Add(day);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return (WorkItemTimesKind.Unlistable, empty);
        }

        Dictionary<string, TimeSpan> weeks = new(StringComparer.Ordinal);
        int skipped = 0;
        foreach (DateOnly day in days)
        {
            TimesDay record = TimesDay.Read(folder, day);
            if (record.Kind == TimesDayKind.Unreadable)
            {
                skipped++;
                continue;
            }

            TimeSpan booked = TimeSpan.Zero;
            foreach (TimesGroup group in record.Groups)
            {
                if (group.WorkItem == item)
                {
                    booked += group.Total;
                }
            }

            if (booked > TimeSpan.Zero)
            {
                string week = WeekOf(day);
                weeks[week] = weeks.GetValueOrDefault(week) + booked;
            }
        }

        List<WorkItemWeek> rows = weeks
            .Where(w => w.Value > TimeSpan.Zero)
            .OrderByDescending(w => w.Key, StringComparer.Ordinal)
            .Select(w => new WorkItemWeek(w.Key, w.Value))
            .ToList();
        TimeSpan total = rows.Aggregate(TimeSpan.Zero, (sum, w) => sum + w.Total);
        return (WorkItemTimesKind.Read, new WorkItemHours(rows, total, skipped));
    }

    private static (WorkItemCommitsKind, IReadOnlyList<WorkItemCommit>, IReadOnlyList<string>) CommitsOf(
        IReadOnlyList<string> repositories, ICommitLog commitLog, int item)
    {
        if (repositories.Count == 0)
        {
            return (WorkItemCommitsKind.NotSetUp, [], []);
        }

        List<WorkItemCommit> commits = [];
        List<string> failed = [];
        foreach (string folder in repositories)
        {
            string repo = RepoName(folder);
            try
            {
                foreach (LoggedCommit commit in commitLog.Read(folder, item))
                {
                    commits.Add(new WorkItemCommit(repo, commit.Hash, commit.Date, commit.Subject));
                }
            }
            catch (GitMissingException)
            {
                return (WorkItemCommitsKind.GitMissing, [], []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException)
            {
                failed.Add(repo);
            }
        }

        // OrderByDescending is stable, so one day keeps git's order within a repository and the settings order across.
        return (WorkItemCommitsKind.Read, commits.OrderByDescending(c => c.Date).ToList(), failed);
    }

    /// <summary>The last segment of a repository folder (trailing separators ignored).</summary>
    public static string RepoName(string folder)
    {
        string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : folder;
    }
}
