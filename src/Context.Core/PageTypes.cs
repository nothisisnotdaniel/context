namespace Context;

/// <summary>A page type: its name (lower case) and the attributes its pages come with, in order.</summary>
internal sealed record PageType
{
    public PageType(string name, IReadOnlyList<string> attributes)
    {
        Name = name;
        Attributes = attributes;
    }

    /// <summary>
    /// Returns only the type name: the contents are raw note text, so formatting this record must never print them.
    /// </summary>
    public override string ToString() => nameof(PageType);

    /// <summary>The type's name, lower-cased (invariant); raw text.</summary>
    public string Name { get; }

    /// <summary>The attribute keys a page of this type comes with (each passes <see cref="AttributeParser.NormalizeKey"/>).</summary>
    public IReadOnlyList<string> Attributes { get; }
}

/// <summary>
/// The page types: the built-in <c>person</c> (<c>affiliation</c>, <c>role</c>) and <c>organization</c> (none), and
/// one type per direct subpage of the top-level note <c>types</c>, whose <c>attribute::</c> lines list its attributes.
/// Type pages are found through <see cref="NoteStore.List"/> names only; a type name never becomes a path.
/// </summary>
internal static class PageTypes
{
    /// <summary>The title of the top-level note whose subpages define the types.</summary>
    public const string FolderName = "types";

    /// <summary>The type of a person page.</summary>
    public const string Person = "person";

    /// <summary>The built-in type for organizations.</summary>
    public const string Organization = "organization";

    /// <summary>The key of the attribute that gives a page its types.</summary>
    public const string TypeKey = "type";

    /// <summary>The key of the attribute lines on a type page.</summary>
    public const string AttributeKey = "attribute";

    /// <summary>The built-in types, sorted by name.</summary>
    public static IReadOnlyList<PageType> BuiltIn { get; } =
    [
        new PageType(Organization, []),
        new PageType(Person, ["affiliation", "role"]),
    ];

    /// <summary>
    /// Every type, sorted by name (ordinal): the built-ins, each replaced by a type page of the same name. Each direct
    /// subpage of a top-level <c>types</c> note (ignoring case) is the type <c>&lt;title lower-cased&gt;</c>; its
    /// attributes are the values of its <c>attribute::</c> lines that pass <see cref="AttributeParser.NormalizeKey"/>,
    /// in order, distinct. With two pages of one name the first listed wins. An unreadable type page is skipped, and
    /// with a sensitive top-level <c>types</c> note no type page is read. Never throws.
    /// </summary>
    public static IReadOnlyList<PageType> Load(NoteStore store)
    {
        Dictionary<string, PageType> types = BuiltIn.ToDictionary(t => t.Name, StringComparer.Ordinal);
        try
        {
            if (!store.TryFindSensitive(FolderName, out _))
            {
                HashSet<string> defined = new(StringComparer.Ordinal);
                foreach (NoteInfo note in store.List())
                {
                    if (note.Parent is null || note.Parent.Contains('/', StringComparison.Ordinal)
                        || !string.Equals(note.Parent, FolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string name = note.Title.ToLowerInvariant();
                    if (defined.Contains(name))
                    {
                        continue;
                    }

                    string text;
                    try
                    {
                        text = store.Read(note);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    defined.Add(name);
                    types[name] = new PageType(name, AttributesOf(text));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The type pages can't be listed: the types read so far stay.
        }

        return types.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The type called <paramref name="name"/> in <paramref name="types"/>, or <c>null</c>.</summary>
    public static PageType? Find(IReadOnlyList<PageType> types, string name) =>
        types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// The types of the page <paramref name="noteName"/> with <paramref name="attributes"/>: its <c>type::</c> values
    /// (by <see cref="AttributeParser.ValueKey"/>, so lower-cased), distinct, in order; a person page
    /// (<see cref="People.PersonOf"/>) without a <c>type::</c> line is of type <c>person</c>.
    /// </summary>
    public static IReadOnlyList<string> TypesOf(string noteName, IReadOnlyList<PageAttribute> attributes)
    {
        List<string> types = attributes
            .Where(a => a.Key == TypeKey)
            .Select(a => AttributeParser.ValueKey(a.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (types.Count == 0 && People.PersonOf(noteName) is not null)
        {
            types.Add(Person);
        }

        return types;
    }

    /// <summary>The new person page's text after its heading: a blank line and one <c>&lt;attr&gt;::</c> line per attribute.</summary>
    public static string Template(PageType? type)
    {
        if (type is null || type.Attributes.Count == 0)
        {
            return string.Empty;
        }

        return "\n" + string.Concat(type.Attributes.Select(a => a + "::\n"));
    }

    private static List<string> AttributesOf(string text)
    {
        List<string> keys = [];
        foreach (PageAttribute attribute in AttributeParser.Parse(text))
        {
            if (attribute.Key == AttributeKey && AttributeParser.NormalizeKey(attribute.Value) is { } key && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
