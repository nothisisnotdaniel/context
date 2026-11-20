namespace Context;

/// <summary>What <see cref="PeopleAttributes.Build"/> found.</summary>
internal enum PeopleAttributesKind
{
    /// <summary>The index was read; <see cref="PeopleAttributes.People"/> and the option lists may still be empty.</summary>
    Found,

    /// <summary><c>context.db</c> can't be read.</summary>
    IndexUnreadable,
}

/// <summary>One value of an attribute as a filter option: its <see cref="AttributeParser.ValueKey"/> and a label (raw).</summary>
internal sealed record AttributeOption(string Key, string Label)
{
    /// <summary>Returns only the type name: the label is raw outside text.</summary>
    public override string ToString() => nameof(AttributeOption);
}

/// <summary>A person page's affiliations and roles, shown values (raw) distinct by value key, in stored order.</summary>
internal sealed record PersonAttributes
{
    private readonly HashSet<string> affiliationKeys;
    private readonly HashSet<string> roleKeys;

    public PersonAttributes(IReadOnlyList<AttributeOption> affiliations, IReadOnlyList<AttributeOption> roles)
    {
        Affiliations = affiliations.Select(a => a.Label).ToList();
        Roles = roles.Select(r => r.Label).ToList();
        affiliationKeys = new HashSet<string>(affiliations.Select(a => a.Key), StringComparer.Ordinal);
        roleKeys = new HashSet<string>(roles.Select(r => r.Key), StringComparer.Ordinal);
    }

    /// <summary>Returns only the type name: the values are raw outside text.</summary>
    public override string ToString() => nameof(PersonAttributes);

    /// <summary>The shown affiliations (<see cref="PeopleAttributes.Shown"/>), raw.</summary>
    public IReadOnlyList<string> Affiliations { get; }

    /// <summary>The shown roles (<see cref="PeopleAttributes.Shown"/>), raw.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>True when one of the affiliations has the value key <paramref name="key"/>.</summary>
    public bool HasAffiliation(string key) => affiliationKeys.Contains(key);

    /// <summary>True when one of the roles has the value key <paramref name="key"/>.</summary>
    public bool HasRole(string key) => roleKeys.Contains(key);
}

/// <summary>
/// The <c>affiliation::</c> and <c>role::</c> values of the person pages (<see cref="People.PersonOf"/>) as stored in
/// the attribute index, for the People panel's filters. All text is raw: front ends clean it.
/// </summary>
internal sealed record PeopleAttributes
{
    /// <summary>The attribute key of an affiliation.</summary>
    public const string AffiliationKey = "affiliation";

    /// <summary>The attribute key of a role.</summary>
    public const string RoleKey = "role";

    public PeopleAttributes(
        PeopleAttributesKind kind,
        IReadOnlyDictionary<string, PersonAttributes> people,
        IReadOnlyList<AttributeOption> affiliations,
        IReadOnlyList<AttributeOption> roles)
    {
        Kind = kind;
        People = people;
        Affiliations = affiliations;
        Roles = roles;
    }

    /// <summary>The view for an index that can't be read: no people, no options.</summary>
    public static PeopleAttributes Unreadable { get; } = new(
        PeopleAttributesKind.IndexUnreadable,
        new Dictionary<string, PersonAttributes>(StringComparer.Ordinal),
        [],
        []);

    /// <summary>Returns only the type name: the contents are raw outside text.</summary>
    public override string ToString() => nameof(PeopleAttributes);

    /// <summary>What was found; read it first.</summary>
    public PeopleAttributesKind Kind { get; }

    /// <summary>Per person page note name (ordinal), its attributes; a page with neither attribute is not in it.</summary>
    public IReadOnlyDictionary<string, PersonAttributes> People { get; }

    /// <summary>The affiliation options, sorted by label ignoring case, then ordinal.</summary>
    public IReadOnlyList<AttributeOption> Affiliations { get; }

    /// <summary>The role options, sorted by label ignoring case, then ordinal.</summary>
    public IReadOnlyList<AttributeOption> Roles { get; }

    /// <summary>
    /// A value as shown: one that <see cref="AttributeParser.ValueKey"/> treats as exactly one <c>[[X]]</c> shows as the
    /// trimmed <c>X</c>; any other value as is.
    /// </summary>
    public static string Shown(string value)
    {
        string collapsed = TagParser.CollapseWhitespace(value);
        if (collapsed.Length > 4 && collapsed.StartsWith("[[", StringComparison.Ordinal)
            && collapsed.EndsWith("]]", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> inner = collapsed.AsSpan(2, collapsed.Length - 4);
            if (inner.IndexOfAny('[', ']') < 0)
            {
                string target = inner.Trim().ToString();
                if (target.Length > 0)
                {
                    return target;
                }
            }
        }

        return value;
    }

    /// <summary>
    /// Syncs the index, then reads the <c>affiliation::</c> and <c>role::</c> rows of the direct person pages. Never
    /// throws for an unreadable index (<see cref="PeopleAttributesKind.IndexUnreadable"/>).
    /// </summary>
    public static PeopleAttributes Build(TagIndex index, NoteStore store)
    {
        IReadOnlyList<(string Note, string Value)> affiliationRows;
        IReadOnlyList<(string Note, string Value)> roleRows;
        try
        {
            index.Sync(store);
            affiliationRows = index.AttributeValues(AffiliationKey);
            roleRows = index.AttributeValues(RoleKey);
        }
        catch (TagIndexException)
        {
            return Unreadable;
        }

        Dictionary<string, List<AttributeOption>> affiliations = PerPerson(affiliationRows);
        Dictionary<string, List<AttributeOption>> roles = PerPerson(roleRows);
        Dictionary<string, PersonAttributes> people = new(StringComparer.Ordinal);
        foreach (string note in affiliations.Keys.Concat(roles.Keys).Distinct(StringComparer.Ordinal))
        {
            people[note] = new PersonAttributes(
                affiliations.GetValueOrDefault(note) ?? [],
                roles.GetValueOrDefault(note) ?? []);
        }

        return new PeopleAttributes(PeopleAttributesKind.Found, people, Options(affiliations), Options(roles));
    }

    // The rows of direct person pages grouped by note, each value once by value key, in stored order.
    private static Dictionary<string, List<AttributeOption>> PerPerson(IReadOnlyList<(string Note, string Value)> rows)
    {
        Dictionary<string, List<AttributeOption>> found = new(StringComparer.Ordinal);
        foreach ((string note, string value) in rows)
        {
            if (Context.People.PersonOf(note) is null)
            {
                continue;
            }

            if (!found.TryGetValue(note, out List<AttributeOption>? list))
            {
                list = [];
                found[note] = list;
            }

            string key = AttributeParser.ValueKey(value);
            if (!list.Exists(o => string.Equals(o.Key, key, StringComparison.Ordinal)))
            {
                list.Add(new AttributeOption(key, Shown(value)));
            }
        }

        return found;
    }

    // Every value key once, labelled by the first person page in name order (ordinal), sorted by label.
    private static List<AttributeOption> Options(Dictionary<string, List<AttributeOption>> perPerson)
    {
        Dictionary<string, AttributeOption> options = new(StringComparer.Ordinal);
        foreach (string note in perPerson.Keys.Order(StringComparer.Ordinal))
        {
            foreach (AttributeOption option in perPerson[note])
            {
                options.TryAdd(option.Key, option);
            }
        }

        return options.Values
            .OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Label, StringComparer.Ordinal)
            .ToList();
    }
}
