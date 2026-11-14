namespace Context;

/// <summary>An attribute line of a page, <c>key:: value</c>: the key normalized, the value as written (collapsed).</summary>
internal sealed record PageAttribute
{
    public PageAttribute(string key, string value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// Returns only the type name: the value is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(PageAttribute);

    /// <summary>The key, lower-cased (invariant); passes <see cref="AttributeParser.NormalizeKey"/>.</summary>
    public string Key { get; }

    /// <summary>The value (raw): the rest of the line trimmed, each whitespace run one space, 1 to 400 chars.</summary>
    public string Value { get; }
}

/// <summary>
/// Finds attribute lines (<c>role:: Bereichsleiter</c>, <c>affiliation:: [[ACME]]</c>, the Dataview/Logseq convention)
/// in Markdown text. Keys and values are raw note text: they only ever reach SQL as parameters and are never turned
/// into a path.
/// </summary>
internal static class AttributeParser
{
    /// <summary>The longest key, like a tag.</summary>
    public const int MaxKeyLength = 50;

    /// <summary>The longest value, after trimming and collapsing whitespace.</summary>
    public const int MaxValueLength = 400;

    private const string Separator = "::";

    /// <summary>
    /// The distinct attributes in <paramref name="markdown"/>, in order of first appearance. An attribute is a line
    /// outside fenced code blocks that starts (column 0) with a key (a letter first, then letters, digits, <c>-</c>,
    /// <c>_</c>; at most <see cref="MaxKeyLength"/> chars; lower-cased invariant), directly followed by <c>::</c>; the
    /// value is the rest of the line trimmed, each whitespace run one space. An empty value or one over
    /// <see cref="MaxValueLength"/> chars is no attribute. A key may repeat; pairs are distinct by key and
    /// <see cref="ValueKey"/>, the first spelling kept.
    /// </summary>
    public static IReadOnlyList<PageAttribute> Parse(string markdown)
    {
        List<PageAttribute> attributes = [];
        HashSet<(string Key, string ValueKey)> seen = [];
        foreach (string line in TagParser.LinesOutsideFences(markdown))
        {
            int end = KeyEnd(line);
            if (end == 0 || end > MaxKeyLength || !line.AsSpan(end).StartsWith(Separator, StringComparison.Ordinal))
            {
                continue;
            }

            string value = TagParser.CollapseWhitespace(line.AsSpan(end + Separator.Length));
            if (value.Length == 0 || value.Length > MaxValueLength)
            {
                continue;
            }

            string key = line[..end].ToLowerInvariant();
            if (seen.Add((key, ValueKey(value))))
            {
                attributes.Add(new PageAttribute(key, value));
            }
        }

        return attributes;
    }

    /// <summary>
    /// A typed key (<c>Role</c>, <c> role </c>) trimmed and lower-cased (invariant), or <c>null</c> when it is no valid
    /// key (see <see cref="Parse"/>).
    /// </summary>
    public static string? NormalizeKey(string key)
    {
        string trimmed = key.Trim();
        return trimmed.Length > 0 && KeyEnd(trimmed) == trimmed.Length && trimmed.Length <= MaxKeyLength
            ? trimmed.ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// The key a value is stored and matched by: for a value that is exactly one <c>[[Target]]</c> the trimmed target,
    /// otherwise the value (whitespace collapsed); then lower-cased (invariant). So <c>[[ACME]]</c>, <c>ACME</c> and
    /// <c>acme</c> match.
    /// </summary>
    public static string ValueKey(string value)
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
                    collapsed = target;
                }
            }
        }

        return collapsed.ToLowerInvariant();
    }

    // The length of the key at the start of the line: 0 when it doesn't start with a letter.
    private static int KeyEnd(string line)
    {
        if (line.Length == 0 || !char.IsLetter(line[0]))
        {
            return 0;
        }

        int end = 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_'))
        {
            end++;
        }

        return end;
    }
}
