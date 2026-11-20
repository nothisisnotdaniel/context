namespace Context;

/// <summary>
/// A tag filter such as <c>#1234 and not #done</c>: <c>term (and term)*</c>, where a term is an optional <c>not</c>
/// and one tag written as in a note (<c>#tag</c> or <c>#[tag with spaces]</c>). <c>and</c> and <c>not</c> ignore case;
/// at least one term has no <c>not</c>. There is no <c>or</c> and there are no parentheses.
/// </summary>
internal static class TagFilter
{
    private const string And = "and";
    private const string Not = "not";

    /// <summary>
    /// True when the trimmed <paramref name="text"/> is meant as a tag filter: it starts with <c>#</c>, or with
    /// <c>not</c> (ignoring case), whitespace and then <c>#</c>. Says nothing about whether it parses.
    /// </summary>
    public static bool IsTagFilter(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith('#'))
        {
            return true;
        }

        if (trimmed.Length <= Not.Length
            || !trimmed.StartsWith(Not, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(trimmed[Not.Length]))
        {
            return false;
        }

        return trimmed[Not.Length..].TrimStart().StartsWith('#');
    }

    /// <summary>
    /// Parses the trimmed <paramref name="text"/> as a tag filter. On success <paramref name="include"/> holds the
    /// tags every matching note must have and <paramref name="exclude"/> those it must not have, both normalized by
    /// <see cref="TagIndex.NormalizeTag"/>, distinct, in the order written. Anything else (<c>or</c>, parentheses, a
    /// bare word, an empty or invalid tag, a dangling <c>and</c> or <c>not</c>, only <c>not</c> terms) gives
    /// <c>false</c> and two empty lists. Checks no length; reads nothing.
    /// </summary>
    public static bool TryParse(string text, out IReadOnlyList<string> include, out IReadOnlyList<string> exclude)
    {
        include = [];
        exclude = [];
        List<Token>? tokens = Tokenize(text.Trim());
        if (tokens is null || tokens.Count == 0)
        {
            return false;
        }

        List<string> wanted = [];
        List<string> unwanted = [];
        int i = 0;
        while (true)
        {
            bool negated = false;
            if (i < tokens.Count && tokens[i].Word == Not)
            {
                negated = true;
                i++;
            }

            if (i >= tokens.Count || tokens[i].Tag is not { } tag)
            {
                return false;
            }

            List<string> target = negated ? unwanted : wanted;
            if (!target.Contains(tag, StringComparer.Ordinal))
            {
                target.Add(tag);
            }

            i++;
            if (i == tokens.Count)
            {
                break;
            }

            if (tokens[i].Word != And)
            {
                return false;
            }

            i++;
        }

        if (wanted.Count == 0)
        {
            return false;
        }

        include = wanted;
        exclude = unwanted;
        return true;
    }

    // The words and tags of the text, split at whitespace (a bracket tag may hold spaces); null for anything that is
    // neither and/not nor a valid tag.
    private static List<Token>? Tokenize(string text)
    {
        List<Token> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            int end;
            if (text[i] == '#' && i + 1 < text.Length && text[i + 1] == '[')
            {
                int close = text.IndexOf(']', i + 2);
                if (close < 0)
                {
                    return null;
                }

                end = close + 1;
                if (TagParser.NormalizeBracket(text.AsSpan(i + 2, close - i - 2)) is null)
                {
                    return null;
                }
            }
            else
            {
                end = i + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                if (text[i] == '#')
                {
                    // A plain tag holds no whitespace, so the bracket rules (letter first, tag characters, at most
                    // MaxTagLength) are exactly a note's #tag rules here.
                    if (TagParser.NormalizeBracket(text.AsSpan(i + 1, end - i - 1)) is null)
                    {
                        return null;
                    }
                }
            }

            if (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                return null;
            }

            string raw = text[i..end];
            if (raw.StartsWith('#'))
            {
                tokens.Add(new Token(TagIndex.NormalizeTag(raw), null));
            }
            else if (string.Equals(raw, And, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(null, And));
            }
            else if (string.Equals(raw, Not, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(null, Not));
            }
            else
            {
                return null;
            }

            i = end;
        }

        return tokens;
    }

    private readonly struct Token
    {
        public Token(string? tag, string? word)
        {
            Tag = tag;
            Word = word;
        }

        public string? Tag { get; }

        public string? Word { get; }
    }
}

/// <summary>What <see cref="TagFilterView.Build"/> found.</summary>
internal enum TagFilterViewKind
{
    /// <summary><see cref="TagFilterView.Notes"/> holds the matching notes (never empty then).</summary>
    Found,

    /// <summary>No note matches the filter.</summary>
    NoMatches,

    /// <summary>The text is not a valid tag filter (<see cref="TagFilter.TryParse"/>): nothing was read.</summary>
    Invalid,

    /// <summary>The text is longer than <see cref="TagFilterView.MaxQueryLength"/>: nothing was read.</summary>
    TooLong,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>
/// The plain notes that match a tag filter, with no rendering. The query and note names are raw text: every front end
/// must clean them (<see cref="SafeText.Clean"/>) and escape them for its own output.
/// </summary>
internal sealed record TagFilterView
{
    /// <summary>The most characters a filter may have after trimming (as <see cref="SearchView.MaxQueryLength"/>).</summary>
    public const int MaxQueryLength = SearchView.MaxQueryLength;

    public TagFilterView(TagFilterViewKind kind, string query, IReadOnlyList<string> notes)
    {
        Kind = kind;
        Query = query;
        Notes = notes;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw outside text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(TagFilterView);

    /// <summary>What was found; read it first.</summary>
    public TagFilterViewKind Kind { get; }

    /// <summary>The filter, trimmed (raw).</summary>
    public string Query { get; }

    /// <summary>The matching note names for <c>Found</c>, sorted ignoring case, then ordinal, each once; else empty.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// Trims <paramref name="text"/>; over <see cref="MaxQueryLength"/> is <c>TooLong</c>, then a text that doesn't
    /// parse (<see cref="TagFilter.TryParse"/>) is <c>Invalid</c>, both without opening the index. Else syncs the
    /// index and matches <see cref="TagIndex.NoteTags"/> in memory: a note matches when it has every included tag and
    /// none of the excluded ones. Sensitive notes are never indexed, so never listed. Never throws for an unreadable
    /// index.
    /// </summary>
    public static TagFilterView Build(TagIndex index, NoteStore store, string text)
    {
        string query = text.Trim();
        if (query.Length > MaxQueryLength)
        {
            return new TagFilterView(TagFilterViewKind.TooLong, query, []);
        }

        if (!TagFilter.TryParse(query, out IReadOnlyList<string> include, out IReadOnlyList<string> exclude))
        {
            return new TagFilterView(TagFilterViewKind.Invalid, query, []);
        }

        try
        {
            index.Sync(store);
            Dictionary<string, HashSet<string>> tagsByNote = new(StringComparer.Ordinal);
            foreach ((string note, string tag) in index.NoteTags())
            {
                if (!tagsByNote.TryGetValue(note, out HashSet<string>? tags))
                {
                    tags = new HashSet<string>(StringComparer.Ordinal);
                    tagsByNote[note] = tags;
                }

                tags.Add(tag);
            }

            List<string> notes = tagsByNote
                .Where(n => include.All(n.Value.Contains) && !exclude.Any(n.Value.Contains))
                .Select(n => n.Key)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();
            return new TagFilterView(notes.Count == 0 ? TagFilterViewKind.NoMatches : TagFilterViewKind.Found, query, notes);
        }
        catch (TagIndexException)
        {
            return new TagFilterView(TagFilterViewKind.IndexUnreadable, query, []);
        }
    }
}
