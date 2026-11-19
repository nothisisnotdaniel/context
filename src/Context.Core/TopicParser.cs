namespace Context;

/// <summary>An open <c>next: @[Name] topic</c> line found by <see cref="TopicParser.Parse"/>.</summary>
internal sealed record ParsedTopic
{
    public ParsedTopic(int line, string person, string text, string raw)
    {
        Line = line;
        Person = person;
        Text = text;
        Raw = raw;
    }

    /// <summary>
    /// Returns only the type name: the person, text and line are raw outside text, so formatting this record must
    /// never print them.
    /// </summary>
    public override string ToString() => nameof(ParsedTopic);

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The mention's normalized name (<see cref="PersonParser.Normalize"/>).</summary>
    public string Person { get; }

    /// <summary>The text after the mention, collapsed, trimmed and cut (raw), never empty.</summary>
    public string Text { get; }

    /// <summary>The whole line as written (without its line end).</summary>
    public string Raw { get; }
}

/// <summary>
/// Finds topics for the next 1:1 (job 253): lines <c>next: @[Full Name] topic</c>, also as a list item
/// (<c>- next: ...</c>) or an open task (<c>- [ ] next: ...</c>). The note stays the only source.
/// </summary>
internal static class TopicParser
{
    /// <summary>The line's keyword, matched ignoring ASCII case.</summary>
    public const string Prefix = "next:";

    /// <summary>The most characters a topic's text keeps; a longer one is cut (never inside a surrogate pair).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>
    /// The open topics in <paramref name="markdown"/>, in line order. A line outside fenced code blocks
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) is one when it is optional spaces or tabs; optionally
    /// <c>-</c>, <c>*</c> or <c>+</c> and one space, optionally followed by <c>[ ]</c> and one space; then
    /// <c>next:</c> (ASCII, ignoring case), optional spaces or tabs, and an <c>@[Name]</c> mention right there by the
    /// <see cref="WaitingFor.TryParse"/> rules. The rest of the line, collapsed (<see cref="TagParser.Collapse"/>),
    /// trimmed and cut to <see cref="MaxTextLength"/>, is the text; an empty text is no topic. A done
    /// (<c>[x]</c>) line is no topic.
    /// </summary>
    public static IReadOnlyList<ParsedTopic> Parse(string markdown)
    {
        List<ParsedTopic> found = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            ParsedTopic? topic = ParseLine(number, line);
            if (topic is not null)
            {
                found.Add(topic);
            }
        }

        return found;
    }

    /// <summary>The topic on the one <paramref name="line"/> (numbered <paramref name="number"/>), or <c>null</c>.</summary>
    internal static ParsedTopic? ParseLine(int number, string line)
    {
        int i = KeywordStart(line, out _, out _);
        if (i < 0)
        {
            return null;
        }

        i += Prefix.Length;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        if (i + 1 >= line.Length || line[i] != '@' || line[i + 1] != '[')
        {
            return null;
        }

        int open = i + 2;
        int close = line.IndexOfAny([']', '\n', '\r'], open);
        if (close < 0 || line[close] != ']' || (close + 1 < line.Length && line[close + 1] == '('))
        {
            return null;
        }

        string? person = PersonParser.Normalize(line.AsSpan(open, close - open));
        if (person is null)
        {
            return null;
        }

        System.Text.StringBuilder builder = new();
        TagParser.Collapse(line.AsSpan(close + 1), builder, int.MaxValue, null);
        if (builder.Length > MaxTextLength)
        {
            builder.Length = char.IsHighSurrogate(builder[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        }

        string text = builder.ToString().Trim();
        return text.Length == 0 ? null : new ParsedTopic(number, person, text, line);
    }

    /// <summary>
    /// The done form of a topic line: <c>- [ ] next:</c> becomes <c>- [x] next:</c>, <c>- next:</c> becomes
    /// <c>- [x] next:</c> and a bare <c>next:</c> becomes <c>- [x] next:</c>; the indentation and every other character
    /// stay. <c>null</c> when <paramref name="line"/> has no topic keyword where a topic's is.
    /// </summary>
    internal static string? DoneForm(string line)
    {
        int keyword = KeywordStart(line, out int marker, out bool box);
        if (keyword < 0)
        {
            return null;
        }

        if (box)
        {
            // "- [ ] next:": the box sits right after the marker and its space.
            return string.Concat(line.AsSpan(0, marker + 2), "[x]", line.AsSpan(marker + 5));
        }

        if (marker >= 0)
        {
            return string.Concat(line.AsSpan(0, marker + 2), "[x] ", line.AsSpan(marker + 2));
        }

        return string.Concat(line.AsSpan(0, keyword), "- [x] ", line.AsSpan(keyword));
    }

    // The index of "next:" when the line starts like a topic line, else -1; marker is the list marker's index (or -1)
    // and box tells whether an open "[ ] " follows it.
    private static int KeywordStart(string line, out int marker, out bool box)
    {
        marker = -1;
        box = false;
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        if (i + 1 < line.Length && line[i] is '-' or '*' or '+' && line[i + 1] == ' ')
        {
            marker = i;
            i += 2;
            if (string.CompareOrdinal(line, i, NoteTodoParser.OpenBox, 0, NoteTodoParser.OpenBox.Length) == 0
                && i + 3 < line.Length
                && line[i + 3] == ' ')
            {
                box = true;
                i += 4;
            }
        }

        return line.Length - i >= Prefix.Length && line.AsSpan(i, Prefix.Length).Equals(Prefix, StringComparison.OrdinalIgnoreCase)
            ? i
            : -1;
    }
}
