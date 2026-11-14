namespace Context;

/// <summary>What a mention's name links to (<see cref="PersonAliases.Resolve"/>).</summary>
internal enum AliasResolutionKind
{
    /// <summary>No person page has that title or alias: the name is its own person entry.</summary>
    None,

    /// <summary>The name is a person page's title or the alias of exactly one person page.</summary>
    Person,

    /// <summary>The name is an alias of more than one person page, so it links to no one.</summary>
    Ambiguous,
}

/// <summary>The result of <see cref="PersonAliases.Resolve"/>.</summary>
internal sealed record AliasResolution
{
    public AliasResolution(AliasResolutionKind kind, string? person, IReadOnlyList<string> titles)
    {
        Kind = kind;
        Person = person;
        Titles = titles;
    }

    /// <summary>
    /// Returns only the type name: the titles are raw note text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(AliasResolution);

    /// <summary>What the name links to.</summary>
    public AliasResolutionKind Kind { get; }

    /// <summary>The title of the person page for <c>Person</c> (raw), else <c>null</c>.</summary>
    public string? Person { get; }

    /// <summary>
    /// For <c>Ambiguous</c>, the titles of the person pages that share the alias (raw), sorted ignoring case, then
    /// ordinal; else empty.
    /// </summary>
    public IReadOnlyList<string> Titles { get; }
}

/// <summary>How an alias on a person page clashes with another person page.</summary>
internal enum AliasClashKind
{
    /// <summary>Another person page has the same alias, so a mention of it links to no one.</summary>
    Shared,

    /// <summary>The alias is another person page's title, so a mention of it links to that person.</summary>
    OtherName,
}

/// <summary>An alias of a person page that doesn't link to that person (<see cref="PersonAliases.Clashes"/>).</summary>
internal sealed record AliasClash
{
    public AliasClash(string alias, AliasClashKind kind, IReadOnlyList<string> others)
    {
        Alias = alias;
        Kind = kind;
        Others = others;
    }

    /// <summary>
    /// Returns only the type name: the alias and titles are raw note text, so formatting this record must never print
    /// them.
    /// </summary>
    public override string ToString() => nameof(AliasClash);

    /// <summary>The alias as written on the page (raw).</summary>
    public string Alias { get; }

    /// <summary>Why it doesn't link to the page's person.</summary>
    public AliasClashKind Kind { get; }

    /// <summary>
    /// The other person pages' titles (raw): those that share the alias, sorted ignoring case, then ordinal, or the
    /// one whose title it is.
    /// </summary>
    public IReadOnlyList<string> Others { get; }

    /// <summary>The warning line (raw: a front end cleans it).</summary>
    public string Text => Kind == AliasClashKind.Shared
        ? $"Alias {Alias} is also used by {string.Join(", ", Others)}, so @[{Alias}] links to no one."
        : $"Alias {Alias} is another person's name, so @[{Alias}] links to them.";
}

/// <summary>
/// The aliases of the person pages (<c>aliases:: Anna, AS</c> on <c>people/Anna Schmidt</c>) and the one rule that
/// resolves a mention's name to a person: a person page's title (ignoring case) is that person; otherwise the alias of
/// exactly one person page is that person; otherwise the name stays its own person entry. Only direct person pages
/// (<see cref="People.PersonOf"/>) supply aliases. Aliases are raw note text: they are never turned into a path.
/// </summary>
internal sealed class PersonAliases
{
    /// <summary>The attribute key of the alias lines.</summary>
    public const string AttributeKey = "aliases";

    // Person key -> page title (the first given per key).
    private readonly Dictionary<string, string> titles = new(StringComparer.Ordinal);

    // Person key -> the page's aliases (as written, distinct by key, never the person's own key).
    private readonly Dictionary<string, List<string>> aliases = new(StringComparer.Ordinal);

    // Alias key -> the keys of the persons that have it, in order of first appearance.
    private readonly Dictionary<string, List<string>> owners = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the rule from (page title, aliases) pairs, one per person page. With two pages of one title key the
    /// first title given wins and their aliases are merged.
    /// </summary>
    public PersonAliases(IEnumerable<(string Title, IReadOnlyList<string> Aliases)> pages)
    {
        List<(string Key, IReadOnlyList<string> Aliases)> all = [];
        foreach ((string title, IReadOnlyList<string> pageAliases) in pages)
        {
            string key = PersonParser.Key(title);
            titles.TryAdd(key, title);
            all.Add((key, pageAliases));
        }

        // The alias keys already kept per person, so merging stays linear in the number of aliases.
        Dictionary<string, HashSet<string>> kept = new(StringComparer.Ordinal);
        foreach ((string key, IReadOnlyList<string> pageAliases) in all)
        {
            if (!aliases.TryGetValue(key, out List<string>? own))
            {
                own = [];
                aliases[key] = own;
                kept[key] = new HashSet<string>(StringComparer.Ordinal);
            }

            HashSet<string> ownKeys = kept[key];
            foreach (string alias in pageAliases)
            {
                string aliasKey = PersonParser.Key(alias);
                if (aliasKey == key || !ownKeys.Add(aliasKey))
                {
                    continue;
                }

                own.Add(alias);
                if (!owners.TryGetValue(aliasKey, out List<string>? people))
                {
                    people = [];
                    owners[aliasKey] = people;
                }

                if (!people.Contains(key))
                {
                    people.Add(key);
                }
            }
        }
    }

    /// <summary>No person pages, so no name resolves.</summary>
    public static PersonAliases Empty { get; } = new([]);

    /// <summary>
    /// The aliases in a page's <paramref name="attributes"/>: every <c>aliases::</c> value split at <c>,</c>, each part
    /// normalized with <see cref="PersonParser.Normalize"/>; empty or invalid parts are skipped, parts are distinct by
    /// <see cref="PersonParser.Key"/> (first spelling kept), and the page's own <paramref name="title"/> is dropped.
    /// </summary>
    public static IReadOnlyList<string> Parse(IReadOnlyList<PageAttribute> attributes, string? title = null)
    {
        string? own = title is null ? null : PersonParser.Key(TagParser.CollapseWhitespace(title));
        List<string> found = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (PageAttribute attribute in attributes)
        {
            if (attribute.Key != AttributeKey)
            {
                continue;
            }

            foreach (string part in attribute.Value.Split(','))
            {
                if (PersonParser.Normalize(part) is { } alias && PersonParser.Key(alias) is var key && key != own && seen.Add(key))
                {
                    found.Add(alias);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Reads the person pages straight from <paramref name="store"/> (no index): every direct subpage of the top-level
    /// <c>people</c> note, in full-name order (ordinal). An unreadable page has no aliases; a store that can't be
    /// listed gives the pages read so far. Sealed pages are never listed, so they have no aliases. Never throws.
    /// </summary>
    public static PersonAliases Load(NoteStore store)
    {
        List<(string Title, IReadOnlyList<string> Aliases)> pages = [];
        try
        {
            foreach (NoteInfo note in store.List().OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                if (People.PersonOf(note.Name) is not { } title)
                {
                    continue;
                }

                IReadOnlyList<string> found;
                try
                {
                    found = Parse(AttributeParser.Parse(store.Read(note)), title);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    found = [];
                }

                pages.Add((title, found));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The pages can't be listed: the ones read so far stay.
        }

        return new PersonAliases(pages);
    }

    /// <summary>
    /// What the mention name <paramref name="name"/> links to, matched by <see cref="PersonParser.Key"/> of the name
    /// trimmed with whitespace runs collapsed.
    /// </summary>
    public AliasResolution Resolve(string name)
    {
        string key = PersonParser.Key(TagParser.CollapseWhitespace(name));
        if (titles.TryGetValue(key, out string? title))
        {
            return new AliasResolution(AliasResolutionKind.Person, title, []);
        }

        if (owners.TryGetValue(key, out List<string>? people))
        {
            return people.Count == 1
                ? new AliasResolution(AliasResolutionKind.Person, titles[people[0]], [])
                : new AliasResolution(AliasResolutionKind.Ambiguous, null, SortTitles(people.Select(p => titles[p])));
        }

        return new AliasResolution(AliasResolutionKind.None, null, []);
    }

    /// <summary>
    /// The key mentions are grouped by for <paramref name="name"/>: the resolved person's key, else the name's own
    /// key.
    /// </summary>
    public string PersonKey(string name)
    {
        AliasResolution resolved = Resolve(name);
        return resolved.Person is { } title ? PersonParser.Key(title) : PersonParser.Key(TagParser.CollapseWhitespace(name));
    }

    /// <summary>
    /// The aliases of the person page <paramref name="title"/> that don't link to that person, in the page's order:
    /// one that is another person page's title, or one that another person page also has. Empty for no such page.
    /// </summary>
    public IReadOnlyList<AliasClash> Clashes(string title)
    {
        string key = PersonParser.Key(TagParser.CollapseWhitespace(title));
        if (!aliases.TryGetValue(key, out List<string>? own))
        {
            return [];
        }

        List<AliasClash> clashes = [];
        foreach (string alias in own)
        {
            string aliasKey = PersonParser.Key(alias);
            if (titles.TryGetValue(aliasKey, out string? other))
            {
                clashes.Add(new AliasClash(alias, AliasClashKind.OtherName, [other]));
            }
            else if (owners.TryGetValue(aliasKey, out List<string>? people) && people.Count > 1)
            {
                clashes.Add(new AliasClash(alias, AliasClashKind.Shared, SortTitles(people.Where(p => p != key).Select(p => titles[p]))));
            }
        }

        return clashes;
    }

    /// <summary>
    /// The hint for a mention <paramref name="name"/> that is an alias of more than one person (raw: a front end
    /// cleans it): <c>@[Anna] is an alias of more than one person: Anna Berg, Anna Schmidt.</c>
    /// </summary>
    public static string AmbiguousText(string name, IReadOnlyList<string> titles) =>
        $"@[{name}] is an alias of more than one person: {string.Join(", ", titles)}.";

    private static List<string> SortTitles(IEnumerable<string> names) =>
        names.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ThenBy(t => t, StringComparer.Ordinal).ToList();
}
