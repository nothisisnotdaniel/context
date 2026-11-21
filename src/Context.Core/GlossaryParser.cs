namespace Context;

/// <summary>A <c>Term:: meaning</c> line found by <see cref="GlossaryParser.Parse"/> (job 380).</summary>
internal sealed record GlossaryLine
{
    public GlossaryLine(int line, string term, string meaning)
    {
        Line = line;
        Term = term;
        Meaning = meaning;
    }

    /// <summary>
    /// Returns only the type name: the term and meaning are raw outside text, so formatting this record must never print
    /// them.
    /// </summary>
    public override string ToString() => nameof(GlossaryLine);

    /// <summary>The 0-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The term before the first <c>::</c>, collapsed (<see cref="TagParser.Collapse"/>), 1 to <see cref="GlossaryParser.MaxTermLength"/> chars (raw).</summary>
    public string Term { get; }

    /// <summary>The rest of the line, trimmed and collapsed, 1 to <see cref="GlossaryParser.MaxMeaningLength"/> chars (raw).</summary>
    public string Meaning { get; }
}

/// <summary>
/// Finds glossary lines (job 380): <c>Term:: meaning</c> at the start of a line, outside fenced code blocks, with an
/// upper-case first letter, so lower-case attribute keys (<c>type::</c>, <c>role::</c>) never count. The note stays the
/// only source; <see cref="AttributeParser"/> is not changed by it.
/// </summary>
internal static class GlossaryParser
{
    /// <summary>What ends the term.</summary>
    public const string Separator = "::";

    /// <summary>The most characters a term has after collapsing; a longer one is no glossary line.</summary>
    public const int MaxTermLength = 100;

    /// <summary>The most characters a meaning has after collapsing; a longer one is no glossary line.</summary>
    public const int MaxMeaningLength = 400;

    /// <summary>
    /// The glossary lines of <paramref name="markdown"/>, in line order. A line outside fenced code blocks
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) is one when its column 0 is an upper-case letter
    /// (<see cref="char.IsUpper(char)"/>), the text before its first <c>::</c> does not end in whitespace and, collapsed
    /// (<see cref="TagParser.Collapse"/>), is 1 to <see cref="MaxTermLength"/> chars with no backtick, <c>[</c>,
    /// <c>]</c> or <c>#</c>, and the rest, trimmed and collapsed, is 1 to <see cref="MaxMeaningLength"/> chars. Lines
    /// are distinct by term and meaning (both ignoring case); the first spelling is kept.
    /// </summary>
    public static IReadOnlyList<GlossaryLine> Parse(string markdown)
    {
        List<GlossaryLine> found = [];
        HashSet<(string Term, string Meaning)> seen = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            GlossaryLine? parsed = ParseLine(number - 1, line);
            if (parsed is not null && seen.Add((Key(parsed.Term), Key(parsed.Meaning))))
            {
                found.Add(parsed);
            }
        }

        return found;
    }

    /// <summary>The upper-invariant key a term is grouped by (<c>note_glossary.term_key</c>).</summary>
    public static string Key(string term) => term.ToUpperInvariant();

    /// <summary>The glossary line on the one <paramref name="line"/> (numbered <paramref name="number"/>), or <c>null</c>.</summary>
    internal static GlossaryLine? ParseLine(int number, string line)
    {
        if (line.Length == 0 || !char.IsUpper(line[0]))
        {
            return null;
        }

        int separator = line.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || char.IsWhiteSpace(line[separator - 1]))
        {
            return null;
        }

        System.Text.StringBuilder term = new();
        if (!TagParser.Collapse(line.AsSpan(0, separator), term, MaxTermLength, (c, _) => c is not ('`' or '[' or ']' or '#'))
            || term.Length == 0)
        {
            return null;
        }

        System.Text.StringBuilder meaning = new();
        // Stops once past MaxMeaningLength: such a line is no glossary line.
        if (!TagParser.Collapse(line.AsSpan(separator + Separator.Length), meaning, MaxMeaningLength, null) || meaning.Length == 0)
        {
            return null;
        }

        return new GlossaryLine(number, term.ToString(), meaning.ToString());
    }
}
