namespace Context;

/// <summary>A <c>Decision:</c> line found by <see cref="DecisionParser.Parse"/>.</summary>
internal sealed record ParsedDecision
{
    public ParsedDecision(int line, string text)
    {
        Line = line;
        Text = text;
    }

    /// <summary>
    /// Returns only the type name: the text is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(ParsedDecision);

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The rest of the line after <c>Decision:</c>, trimmed and collapsed (raw), never empty.</summary>
    public string Text { get; }
}

/// <summary>Finds <c>Decision: ...</c> lines in Markdown text.</summary>
internal static class DecisionParser
{
    /// <summary>The most characters a decision's text keeps; a longer one is cut (never inside a surrogate pair).</summary>
    public const int MaxTextLength = 1000;

    private const string Marker = "Decision:";

    /// <summary>
    /// The decisions in <paramref name="markdown"/>, in line order. A line outside fenced code blocks
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) is a decision when it is optional spaces or tabs, an
    /// optional list marker (<c>-</c>, <c>*</c> or <c>+</c> and one space), then <c>Decision:</c> (ignoring case). The
    /// text is the rest of the line, trimmed with each whitespace run turned into one space
    /// (<see cref="TagParser.Collapse"/>) and cut to <see cref="MaxTextLength"/>; an empty rest is no decision.
    /// </summary>
    public static IReadOnlyList<ParsedDecision> Parse(string markdown)
    {
        List<ParsedDecision> found = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            string? text = ParseLine(line);
            if (text is not null)
            {
                found.Add(new ParsedDecision(number, text));
            }
        }

        return found;
    }

    private static string? ParseLine(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        if (i + 1 < line.Length && (line[i] is '-' or '*' or '+') && line[i + 1] == ' ')
        {
            i += 2;
        }

        if (line.Length - i < Marker.Length
            || string.Compare(line, i, Marker, 0, Marker.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return null;
        }

        System.Text.StringBuilder builder = new();
        TagParser.Collapse(line.AsSpan(i + Marker.Length), builder, int.MaxValue, null);
        if (builder.Length > MaxTextLength)
        {
            int cut = char.IsHighSurrogate(builder[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
            builder.Length = cut;
        }

        string text = builder.ToString().TrimEnd();
        return text.Length == 0 ? null : text;
    }
}
