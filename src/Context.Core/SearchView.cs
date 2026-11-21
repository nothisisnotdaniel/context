namespace Context;

/// <summary>What <see cref="SearchView.Build"/> found.</summary>
internal enum SearchViewKind
{
    /// <summary><see cref="SearchView.Hits"/> holds the matching notes (never empty then).</summary>
    Found,

    /// <summary>No note contains the text.</summary>
    NoMatches,

    /// <summary>The text is blank or shorter than <see cref="SearchView.MinQueryLength"/>: nothing was read.</summary>
    TooShort,

    /// <summary>The text is longer than <see cref="SearchView.MaxQueryLength"/>: nothing was read.</summary>
    TooLong,

    /// <summary>
    /// <c>context.db</c> can't be read; <see cref="SearchView.Times"/> may still hold the matching Times bookings.
    /// </summary>
    IndexUnreadable,

    /// <summary>The start date is after the end date: nothing was read.</summary>
    InvalidRange,
}

/// <summary>A Times checkpoint group of one day that matches the query (job 379). Names are raw outside text.</summary>
internal sealed record TimesSearchHit
{
    public TimesSearchHit(DateOnly date, int? workItem, IReadOnlyList<string> names, TimeSpan total, bool open)
    {
        Date = date;
        WorkItem = workItem;
        Names = names;
        Total = total;
        Open = open;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(TimesSearchHit);

    /// <summary>The day of the day file.</summary>
    public DateOnly Date { get; }

    /// <summary>The group's work item, <c>null</c> for a group without one.</summary>
    public int? WorkItem { get; }

    /// <summary>The group's checkpoint names (raw), in first-booked order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>The group's booked time (<see cref="TimesGroup.Total"/>).</summary>
    public TimeSpan Total { get; }

    /// <summary>True when one of the group's checkpoints has no entry that ends it.</summary>
    public bool Open { get; }
}

/// <summary>A note that contains the searched text, with its first line that does (raw).</summary>
internal sealed record SearchHit
{
    public SearchHit(string note, int lineNumber, string line)
        : this(note, lineNumber, line, 0, [], null)
    {
    }

    public SearchHit(
        string note, int lineNumber, string line, int matchStart, IReadOnlyList<string> types, DateTime? modified)
    {
        Note = note;
        LineNumber = lineNumber;
        Line = line;
        MatchStart = matchStart;
        Types = types;
        Modified = modified;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(SearchHit);

    /// <summary>The note's full name (raw).</summary>
    public string Note { get; }

    /// <summary>The 1-based number of <see cref="Line"/> in the note.</summary>
    public int LineNumber { get; }

    /// <summary>The first line holding the text, without its line ending (raw).</summary>
    public string Line { get; }

    /// <summary>The index in <see cref="Line"/> of the first match of the query, ignoring case (ordinal).</summary>
    public int MatchStart { get; }

    /// <summary>
    /// The note's types (<see cref="PageTypes.TypesOf"/>: its <c>type::</c> values, or <c>person</c> for an untyped
    /// person page), read from the same text; empty when it has none.
    /// </summary>
    public IReadOnlyList<string> Types { get; }

    /// <summary>The note file's last-write time, UTC; <c>null</c> when the note is no longer listed.</summary>
    public DateTime? Modified { get; }
}

/// <summary>
/// The data of <c>search &lt;text&gt;</c>, with no rendering. The query, note names and lines are raw text: every front
/// end must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record SearchView
{
    /// <summary>The fewest characters a query needs after trimming (the trigram index matches nothing shorter).</summary>
    public const int MinQueryLength = 3;

    /// <summary>The most characters a query may have after trimming.</summary>
    public const int MaxQueryLength = 200;

    public SearchView(SearchViewKind kind, string query, IReadOnlyList<SearchHit> hits)
        : this(kind, query, hits, [], WorkItemTimesKind.NotSetUp, 0)
    {
    }

    public SearchView(
        SearchViewKind kind,
        string query,
        IReadOnlyList<SearchHit> hits,
        IReadOnlyList<TimesSearchHit> times,
        WorkItemTimesKind timesKind,
        int timesSkipped)
    {
        Kind = kind;
        Query = query;
        Hits = hits;
        Times = times;
        TimesKind = timesKind;
        TimesSkipped = timesSkipped;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(SearchView);

    /// <summary>What was found; read it first.</summary>
    public SearchViewKind Kind { get; }

    /// <summary>The query, trimmed (raw).</summary>
    public string Query { get; }

    /// <summary>One hit per matching note for <c>Found</c>, sorted by note ignoring case, then ordinal; else empty.</summary>
    public IReadOnlyList<SearchHit> Hits { get; }

    /// <summary>
    /// The matching Times checkpoint groups (job 379), newest day first, then in group order; empty unless
    /// <see cref="TimesKind"/> is <see cref="WorkItemTimesKind.Read"/>.
    /// </summary>
    public IReadOnlyList<TimesSearchHit> Times { get; }

    /// <summary>How Times' folder was read: not set up (or not searched), unlistable, or read.</summary>
    public WorkItemTimesKind TimesKind { get; }

    /// <summary>The day files in the range that couldn't be read.</summary>
    public int TimesSkipped { get; }

    /// <summary>
    /// Searches the notes only, with no Times folder and no date range (the older callers).
    /// </summary>
    public static SearchView Build(TagIndex index, NoteStore store, string text) =>
        Build(index, store, null, text, null, null, TimeZoneInfo.Utc);

    /// <summary>
    /// Trims <paramref name="text"/>. A start date after the end date is <c>InvalidRange</c> with nothing read. A query
    /// that is exactly <c>#&lt;n&gt;</c> (<see cref="WorkItemParser.TryParseNumber"/>) is a work item query: the notes
    /// come from <see cref="TagIndex.NotesWithItem"/>, each hit its first line holding the reference, with
    /// <see cref="SearchHit.MatchStart"/> at its <c>#</c>; no length rule applies. Any other text is checked for its
    /// length (<c>TooShort</c>, <c>TooLong</c>, without opening the index or Times' folder), then the index is synced
    /// and <c>note_text</c> searched (<see cref="TagIndex.Search"/>): each note's hit is its first line (split at
    /// <c>\n</c>, a trailing <c>\r</c> dropped) that contains the query ignoring case (ordinal); a note with no such
    /// line is dropped. Each hit also carries the note's types and its file's last-write time (UTC). With a range, a note
    /// whose last-write date in <paramref name="zone"/> is outside it, or that is no longer listed, is dropped. The day
    /// files of <paramref name="timesFolder"/> in the range (top level only, never written) give one Times hit per
    /// matching group: its work item for a work item query, else any name containing the query ignoring case. Never
    /// throws for an unreadable index or Times folder.
    /// </summary>
    public static SearchView Build(
        TagIndex index,
        NoteStore store,
        string? timesFolder,
        string text,
        DateOnly? from,
        DateOnly? to,
        TimeZoneInfo zone)
    {
        string query = text.Trim();
        if (from is { } start && to is { } stop && start > stop)
        {
            return new SearchView(SearchViewKind.InvalidRange, query, []);
        }

        int? item = IsWorkItemQuery(query, out int number) ? number : null;
        if (item is null)
        {
            if (query.Length < MinQueryLength)
            {
                return new SearchView(SearchViewKind.TooShort, query, []);
            }

            if (query.Length > MaxQueryLength)
            {
                return new SearchView(SearchViewKind.TooLong, query, []);
            }
        }

        List<SearchHit>? sorted = null;
        try
        {
            sorted = NoteHits(index, store, query, item, from, to, zone);
        }
        catch (TagIndexException)
        {
            sorted = null;
        }

        (WorkItemTimesKind timesKind, List<TimesSearchHit> times, int skipped) =
            TimesHits(timesFolder, query, item, from, to);
        SearchViewKind kind = sorted is null ? SearchViewKind.IndexUnreadable
            : sorted.Count > 0 || times.Count > 0 ? SearchViewKind.Found
            : SearchViewKind.NoMatches;
        return new SearchView(kind, query, sorted ?? [], times, timesKind, skipped);
    }

    /// <summary>
    /// True when the trimmed text is exactly <c>#&lt;n&gt;</c> (<see cref="WorkItemParser.TryParseNumber"/> after the
    /// <c>#</c>): a work item query, searched as text (notes and Times bookings) and never as a tag filter.
    /// </summary>
    public static bool IsWorkItemQuery(string text, out int item)
    {
        string trimmed = text.Trim();
        item = 0;
        return trimmed.StartsWith('#') && WorkItemParser.TryParseNumber(trimmed, out item);
    }

    private static List<SearchHit> NoteHits(
        TagIndex index, NoteStore store, string query, int? item, DateOnly? from, DateOnly? to, TimeZoneInfo zone)
    {
        index.Sync(store);
        List<SearchHit> hits = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        // Each body is only looked at for its row: the selector keeps the hit, never the text.
        IReadOnlyList<SearchHit> found = item is { } n
            ? index.TextsOf(index.NotesWithItem(n), (note, body) => ItemLine(note, body, n))
            : index.Search(query, (note, body) => FirstLine(note, body, query));
        foreach (SearchHit hit in found)
        {
            if (seen.Add(hit.Note))
            {
                hits.Add(hit);
            }
        }

        if (hits.Count > 0)
        {
            Dictionary<string, DateTime> modified = new(StringComparer.Ordinal);
            foreach (NoteInfo info in store.List())
            {
                modified[info.Name] = info.Modified.ToUniversalTime();
            }

            hits = hits
                .Select(h => new SearchHit(
                    h.Note,
                    h.LineNumber,
                    h.Line,
                    h.MatchStart,
                    h.Types,
                    modified.TryGetValue(h.Note, out DateTime time) ? time : null))
                .Where(h => InRange(h.Modified, from, to, zone))
                .ToList();
        }

        return hits
            .OrderBy(h => h.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Note, StringComparer.Ordinal)
            .ToList();
    }

    // With no range every note is kept; with one, a note whose last-write date (in zone) is inside it.
    private static bool InRange(DateTime? modifiedUtc, DateOnly? from, DateOnly? to, TimeZoneInfo zone)
    {
        if (from is null && to is null)
        {
            return true;
        }

        if (modifiedUtc is not { } utc)
        {
            return false;
        }

        DateOnly day = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
        return InRange(day, from, to);
    }

    private static bool InRange(DateOnly day, DateOnly? from, DateOnly? to) =>
        (from is not { } start || day >= start) && (to is not { } stop || day <= stop);

    // The matching groups of the day files in the range, newest first; the folder is only listed (top level) and read.
    private static (WorkItemTimesKind, List<TimesSearchHit>, int) TimesHits(
        string? folder, string query, int? item, DateOnly? from, DateOnly? to)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return (WorkItemTimesKind.NotSetUp, [], 0);
        }

        List<DateOnly> days = [];
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (TimesBalance.TryParseDayFile(Path.GetFileName(path), out DateOnly day) && InRange(day, from, to))
                {
                    days.Add(day);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return (WorkItemTimesKind.Unlistable, [], 0);
        }

        days.Sort();
        days.Reverse();
        List<TimesSearchHit> hits = [];
        int skipped = 0;
        foreach (DateOnly day in days)
        {
            TimesDay record = TimesDay.Read(folder, day);
            if (record.Kind == TimesDayKind.Unreadable)
            {
                skipped++;
                continue;
            }

            foreach (TimesGroup group in record.Groups)
            {
                bool matches = item is { } n
                    ? group.WorkItem == n
                    : group.Names.Any(name => name.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (matches)
                {
                    hits.Add(new TimesSearchHit(day, group.WorkItem, group.Names, group.Total, group.Open));
                }
            }
        }

        return (WorkItemTimesKind.Read, hits, skipped);
    }

    private static SearchHit? ItemLine(string note, string body, int item)
    {
        if (WorkItemParser.FirstLineWith(body, item) is not { } found)
        {
            return null;
        }

        IReadOnlyList<string> types = PageTypes.TypesOf(note, AttributeParser.Parse(body));
        return new SearchHit(note, found.Number, found.Line, found.Start, types, null);
    }

    private static SearchHit? FirstLine(string note, string body, string query)
    {
        int number = 0;
        int start = 0;
        while (start <= body.Length)
        {
            number++;
            int end = body.IndexOf('\n', start);
            int stop = end < 0 ? body.Length : end;
            string line = body[start..stop];
            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            int match = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (match >= 0)
            {
                IReadOnlyList<string> types = PageTypes.TypesOf(note, AttributeParser.Parse(body));
                return new SearchHit(note, number, line, match, types, null);
            }

            if (end < 0)
            {
                break;
            }

            start = end + 1;
        }

        return null;
    }
}
