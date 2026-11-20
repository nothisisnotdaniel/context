namespace Context;

/// <summary>What <see cref="FindView.Build"/> found.</summary>
internal enum FindViewKind
{
    /// <summary><see cref="FindView.Notes"/> holds the notes with the attribute (never empty then).</summary>
    Found,

    /// <summary>No note has the attribute.</summary>
    NoMatches,

    /// <summary>The key is no valid key or the value is empty: nothing was read.</summary>
    InvalidQuery,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>
/// The data of <c>find &lt;key&gt; &lt;value&gt;</c>, with no rendering. The value and the note names are raw text:
/// every front end must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record FindView
{
    public FindView(FindViewKind kind, string key, string value, IReadOnlyList<string> notes)
    {
        Kind = kind;
        Key = key;
        Value = value;
        Notes = notes;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(FindView);

    /// <summary>What was found; read it first.</summary>
    public FindViewKind Kind { get; }

    /// <summary>The key, normalized (<see cref="AttributeParser.NormalizeKey"/>); empty for <c>InvalidQuery</c> with a bad key.</summary>
    public string Key { get; }

    /// <summary>The value as asked for, trimmed with whitespace runs collapsed (raw).</summary>
    public string Value { get; }

    /// <summary>The matching note names for <c>Found</c>, sorted ignoring case, then ordinal, each once; else empty.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// Checks the query, syncs the index, then lists the notes with <paramref name="key"/><c>::</c>
    /// <paramref name="value"/> (<see cref="TagIndex.NotesWithAttribute"/>). <c>type person</c> also lists the person
    /// pages without a <c>type::</c> line (<see cref="PageTypes.TypesOf"/>). A bad key or an empty value gives
    /// <c>InvalidQuery</c> without reading anything. Never throws for an unreadable index.
    /// </summary>
    public static FindView Build(TagIndex index, NoteStore store, string key, string value)
    {
        string? wanted = AttributeParser.NormalizeKey(key);
        string collapsed = TagParser.CollapseWhitespace(value);
        if (wanted is null || collapsed.Length == 0)
        {
            return new FindView(FindViewKind.InvalidQuery, wanted ?? string.Empty, collapsed, []);
        }

        try
        {
            index.Sync(store);
            IEnumerable<string> notes = index.NotesWithAttribute(wanted, collapsed);
            if (wanted == PageTypes.TypeKey && AttributeParser.ValueKey(collapsed) == PageTypes.Person)
            {
                notes = notes.Concat(index.NotesWithoutAttribute(PageTypes.TypeKey).Where(n => People.PersonOf(n) is not null));
            }

            List<string> sorted = notes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
            return new FindView(sorted.Count == 0 ? FindViewKind.NoMatches : FindViewKind.Found, wanted, collapsed, sorted);
        }
        catch (TagIndexException)
        {
            return new FindView(FindViewKind.IndexUnreadable, wanted, collapsed, []);
        }
    }
}
