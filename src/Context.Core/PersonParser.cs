namespace Context;

/// <summary>
/// Finds <c>@[Person name]</c> mentions in Markdown text. A person's page is the note
/// <see cref="People.PersonNote"/>, and the mention's name only ever becomes a file name through
/// <see cref="NoteStore.ValidateTitle"/> and <see cref="NoteStore.CreateChild"/>.
/// </summary>
internal static class PersonParser
{
    // Refused on every OS (Windows refuses them in file names), so a mention means the same on every machine and the
    // editor bundle finds the same mentions as this parser. '[' keeps "@[[x]]" from being a mention and a link.
    private const string RefusedChars = "[<>\"|?*";

    /// <summary>
    /// The distinct mentions in <paramref name="markdown"/>, in order of first appearance. A mention is <c>@[</c> at the
    /// start of a line or after whitespace, then text up to the first <c>]</c> on the same line, not directly followed
    /// by <c>(</c>. The name is that text trimmed with each whitespace run turned into one space; a name that is empty,
    /// fails <see cref="NoteStore.ValidateTitle"/> or holds one of <c>[ &lt; &gt; " | ? *</c> is no mention. Mentions are
    /// distinct by <see cref="Key"/> (ignoring case); the first spelling is kept. Lines inside fenced code blocks and
    /// text inside inline backtick spans are skipped, as in <see cref="TagParser"/>. <c>#[tag]</c> and
    /// <c>[[link]]</c> are never mentions.
    /// </summary>
    public static IReadOnlyList<string> Parse(string markdown)
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string line in TagParser.LinesOutsideFences(markdown))
        {
            ParseLine(line, names, seen);
        }

        return names;
    }

    /// <summary>The key a person is stored and matched by: the name lower-cased (invariant).</summary>
    public static string Key(string name) => name.ToLowerInvariant();

    /// <summary>
    /// The person a user typed: <c>Anna Schmidt</c> or <c>@[Anna Schmidt]</c>, normalized like a mention
    /// (<see cref="Normalize"/>). <c>null</c> when it is no valid name.
    /// </summary>
    public static string? NormalizeTyped(string typed)
    {
        string trimmed = typed.Trim();
        if (trimmed.Length >= 3 && trimmed.StartsWith("@[", StringComparison.Ordinal) && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[2..^1];
        }

        return Normalize(trimmed);
    }

    /// <summary>
    /// Trims <paramref name="text"/> and turns each whitespace run into one space. Returns <c>null</c> when the result
    /// is empty, fails <see cref="NoteStore.ValidateTitle"/> or holds one of <c>[ &lt; &gt; " | ? *</c>.
    /// </summary>
    internal static string? Normalize(ReadOnlySpan<char> text)
    {
        // Stops at the first refused character, or once the name is too long, so the work per "@[" is bounded by the
        // next '[' or by the title length, not by the distance to the next ']'.
        System.Text.StringBuilder builder = new(NoteStore.MaxTitleLength + 1);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (RefusedChars.Contains(c, StringComparison.Ordinal))
            {
                return null;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
            if (builder.Length > NoteStore.MaxTitleLength)
            {
                return null;
            }
        }

        string name = builder.ToString();
        return name.Length == 0 || NoteStore.ValidateTitle(name) is not null ? null : name;
    }

    private static void ParseLine(string line, List<string> names, HashSet<string> seen)
    {
        int i = 0;
        int nextClose = -1;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = TagParser.SkipCodeSpan(line, i);
                continue;
            }

            bool start = c == '@' && (i == 0 || char.IsWhiteSpace(line[i - 1]));
            if (!start || i + 1 >= line.Length || line[i + 1] != '[')
            {
                i++;
                continue;
            }

            // The first ']' at or after i + 2; remembered so a line full of "@[" is still scanned once.
            if (nextClose < i + 2)
            {
                nextClose = line.IndexOf(']', i + 2);
                if (nextClose < 0)
                {
                    nextClose = line.Length;
                }
            }

            if (nextClose < line.Length && (nextClose + 1 >= line.Length || line[nextClose + 1] != '('))
            {
                string? name = Normalize(line.AsSpan(i + 2, nextClose - i - 2));
                if (name is not null)
                {
                    if (seen.Add(Key(name)))
                    {
                        names.Add(name);
                    }

                    i = nextClose + 1;
                    continue;
                }
            }

            // Not a mention: carry on just after "@[", so code spans inside are still seen.
            i += 2;
        }
    }
}

/// <summary>Where person pages live: the subpages of the top-level note <c>people</c>.</summary>
internal static class People
{
    /// <summary>The title of the top-level note whose subpages are the person pages.</summary>
    public const string FolderName = "people";

    /// <summary>The full name of the page of the person <paramref name="name"/>: <c>people/&lt;name&gt;</c>.</summary>
    public static string PersonNote(string name) => FolderName + "/" + name;

    /// <summary>
    /// The person a note is the page of: its title when its parent is the top-level <c>people</c> note (ignoring case),
    /// else <c>null</c>. A subpage of a person page is no person page.
    /// </summary>
    public static string? PersonOf(string noteName)
    {
        int slash = noteName.IndexOf('/');
        if (slash < 0 || noteName.IndexOf('/', slash + 1) >= 0)
        {
            return null;
        }

        return string.Equals(noteName[..slash], FolderName, StringComparison.OrdinalIgnoreCase) ? noteName[(slash + 1)..] : null;
    }
}
