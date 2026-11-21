namespace Context;

/// <summary>One stored <c>note_glossary</c> row (job 380), read by <see cref="TagIndex.Glossary"/>.</summary>
internal sealed record IndexedGlossaryLine
{
    public IndexedGlossaryLine(string note, int line, string term, string termKey, string meaning)
    {
        Note = note;
        Line = line;
        Term = term;
        TermKey = termKey;
        Meaning = meaning;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(IndexedGlossaryLine);

    /// <summary>The note's full name (raw).</summary>
    public string Note { get; }

    /// <summary>The 0-based line number in the note.</summary>
    public int Line { get; }

    /// <summary>The term as written (raw).</summary>
    public string Term { get; }

    /// <summary>The upper-invariant term (<see cref="GlossaryParser.Key"/>), what terms are grouped by.</summary>
    public string TermKey { get; }

    /// <summary>The meaning (raw).</summary>
    public string Meaning { get; }
}

/// <summary>What <see cref="GlossaryView.Build"/> found.</summary>
internal enum GlossaryViewKind
{
    /// <summary><see cref="GlossaryView.Terms"/> holds the terms (never empty then).</summary>
    Found,

    /// <summary>No note has a glossary line.</summary>
    Empty,

    /// <summary>There are terms, but none matches the filter.</summary>
    NoMatches,

    /// <summary>The filter is over <see cref="GlossaryView.MaxFilterLength"/> chars: nothing was read.</summary>
    TooLong,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>One meaning of a glossary term: the note and line it was written on.</summary>
internal sealed record GlossaryMeaning
{
    public GlossaryMeaning(string note, int line, string meaning)
    {
        Note = note;
        Line = line;
        Meaning = meaning;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(GlossaryMeaning);

    /// <summary>The note's full name (raw).</summary>
    public string Note { get; }

    /// <summary>The note's title: the last segment of <see cref="Note"/> (raw).</summary>
    public string Title => Note.LastIndexOf('/') is int slash and >= 0 ? Note[(slash + 1)..] : Note;

    /// <summary>The 0-based line number in the note.</summary>
    public int Line { get; }

    /// <summary>The meaning (raw).</summary>
    public string Meaning { get; }
}

/// <summary>One glossary term with every meaning written for it, in any note.</summary>
internal sealed record GlossaryTerm
{
    public GlossaryTerm(string term, IReadOnlyList<GlossaryMeaning> meanings)
    {
        Term = term;
        Meanings = meanings;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(GlossaryTerm);

    /// <summary>The term: the spelling of its first meaning (raw).</summary>
    public string Term { get; }

    /// <summary>The meanings, by note name (ignoring case, then ordinal), then line; never empty.</summary>
    public IReadOnlyList<GlossaryMeaning> Meanings { get; }
}

/// <summary>
/// The glossary (job 380): every <c>Term:: meaning</c> line of the plain notes (<see cref="GlossaryParser"/>), one
/// <see cref="GlossaryTerm"/> per upper-invariant term, sorted by term (ignoring case, then ordinal).
/// </summary>
internal sealed record GlossaryView
{
    /// <summary>The longest filter, trimmed; a longer one is <see cref="GlossaryViewKind.TooLong"/>.</summary>
    public const int MaxFilterLength = 200;

    public GlossaryView(GlossaryViewKind kind, IReadOnlyList<GlossaryTerm> terms, IReadOnlyList<GlossaryTerm>? all = null)
    {
        Kind = kind;
        Terms = terms;
        All = all ?? terms;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(GlossaryView);

    /// <summary>What was found.</summary>
    public GlossaryViewKind Kind { get; }

    /// <summary>The terms that match the filter (all of them without one).</summary>
    public IReadOnlyList<GlossaryTerm> Terms { get; }

    /// <summary>Every term, before the filter; what <see cref="Narrow"/> filters again without a read.</summary>
    public IReadOnlyList<GlossaryTerm> All { get; }

    /// <summary>
    /// Syncs the index and builds the glossary, kept to the terms that match <paramref name="filter"/>. A filter over
    /// <see cref="MaxFilterLength"/> (trimmed) is <see cref="GlossaryViewKind.TooLong"/> without opening the index; a
    /// <see cref="TagIndexException"/> is <see cref="GlossaryViewKind.IndexUnreadable"/>.
    /// </summary>
    public static GlossaryView Build(TagIndex index, NoteStore store, string? filter)
    {
        if (IsTooLong(filter))
        {
            return new GlossaryView(GlossaryViewKind.TooLong, []);
        }

        try
        {
            index.Sync(store);
            return Narrow(FromRows(index.Glossary()), filter);
        }
        catch (TagIndexException)
        {
            return new GlossaryView(GlossaryViewKind.IndexUnreadable, []);
        }
    }

    /// <summary>The whole glossary of <paramref name="rows"/>, without a filter (<see cref="GlossaryViewKind.Found"/> or <see cref="GlossaryViewKind.Empty"/>).</summary>
    public static GlossaryView FromRows(IEnumerable<IndexedGlossaryLine> rows)
    {
        List<GlossaryTerm> terms = [];
        foreach (IGrouping<string, IndexedGlossaryLine> group in rows.GroupBy(r => r.TermKey, StringComparer.Ordinal))
        {
            List<IndexedGlossaryLine> ordered = group
                .OrderBy(r => r.Note, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Note, StringComparer.Ordinal)
                .ThenBy(r => r.Line)
                .ToList();
            terms.Add(new GlossaryTerm(
                ordered[0].Term,
                ordered.Select(r => new GlossaryMeaning(r.Note, r.Line, r.Meaning)).ToList()));
        }

        List<GlossaryTerm> sorted = terms
            .OrderBy(t => t.Term, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Term, StringComparer.Ordinal)
            .ToList();
        return new GlossaryView(sorted.Count == 0 ? GlossaryViewKind.Empty : GlossaryViewKind.Found, sorted);
    }

    /// <summary>
    /// <paramref name="view"/>'s terms kept to those whose term or any meaning contains the trimmed
    /// <paramref name="filter"/> (ordinal, ignoring case), all their meanings kept; pure, no read. A blank filter keeps
    /// every term; one over <see cref="MaxFilterLength"/> is <see cref="GlossaryViewKind.TooLong"/>; nothing left of a
    /// non-empty glossary is <see cref="GlossaryViewKind.NoMatches"/>. <see cref="GlossaryViewKind.IndexUnreadable"/>
    /// and <see cref="GlossaryViewKind.TooLong"/> views stay as they are.
    /// </summary>
    public static GlossaryView Narrow(GlossaryView view, string? filter)
    {
        if (view.Kind is GlossaryViewKind.IndexUnreadable or GlossaryViewKind.TooLong)
        {
            return view;
        }

        if (IsTooLong(filter))
        {
            return new GlossaryView(GlossaryViewKind.TooLong, [], view.All);
        }

        string trimmed = (filter ?? string.Empty).Trim();
        if (view.All.Count == 0)
        {
            return new GlossaryView(GlossaryViewKind.Empty, [], view.All);
        }

        if (trimmed.Length == 0)
        {
            return new GlossaryView(GlossaryViewKind.Found, view.All, view.All);
        }

        List<GlossaryTerm> kept = view.All
            .Where(t => t.Term.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || t.Meanings.Any(m => m.Meaning.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new GlossaryView(kept.Count == 0 ? GlossaryViewKind.NoMatches : GlossaryViewKind.Found, kept, view.All);
    }

    private static bool IsTooLong(string? filter) => (filter ?? string.Empty).Trim().Length > MaxFilterLength;
}
