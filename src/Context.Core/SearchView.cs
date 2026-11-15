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

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>A note that contains the searched text, with its first line that does (raw).</summary>
internal sealed record SearchHit
{
    public SearchHit(string note, int lineNumber, string line)
    {
        Note = note;
        LineNumber = lineNumber;
        Line = line;
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
    {
        Kind = kind;
        Query = query;
        Hits = hits;
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
    /// Trims <paramref name="text"/> and checks its length (<c>TooShort</c>, <c>TooLong</c>, without opening the
    /// index), then syncs the index and searches <c>note_text</c> (<see cref="TagIndex.Search"/>). Each note's hit is
    /// its first line (split at <c>\n</c>, a trailing <c>\r</c> dropped) that contains the query ignoring case
    /// (ordinal); a note with no such line is dropped. Never throws for an unreadable index.
    /// </summary>
    public static SearchView Build(TagIndex index, NoteStore store, string text)
    {
        string query = text.Trim();
        if (query.Length < MinQueryLength)
        {
            return new SearchView(SearchViewKind.TooShort, query, []);
        }

        if (query.Length > MaxQueryLength)
        {
            return new SearchView(SearchViewKind.TooLong, query, []);
        }

        try
        {
            index.Sync(store);
            List<SearchHit> hits = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach ((string note, string body) in index.Search(query))
            {
                if (seen.Contains(note))
                {
                    continue;
                }

                SearchHit? hit = FirstLine(note, body, query);
                if (hit is not null)
                {
                    seen.Add(note);
                    hits.Add(hit);
                }
            }

            List<SearchHit> sorted = hits
                .OrderBy(h => h.Note, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.Note, StringComparer.Ordinal)
                .ToList();
            return new SearchView(sorted.Count == 0 ? SearchViewKind.NoMatches : SearchViewKind.Found, query, sorted);
        }
        catch (TagIndexException)
        {
            return new SearchView(SearchViewKind.IndexUnreadable, query, []);
        }
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

            if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return new SearchHit(note, number, line);
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
