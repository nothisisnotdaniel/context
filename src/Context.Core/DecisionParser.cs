namespace Context;

/// <summary>A <c>#!d</c> decision found by <see cref="DecisionParser.Parse"/>: a marked paragraph (job 343).</summary>
internal sealed record ParsedDecision
{
    public ParsedDecision(int line, int? number, string text, int markerIndex, int markerLength, int stripLength)
    {
        Line = line;
        Number = number;
        Text = text;
        MarkerIndex = markerIndex;
        MarkerLength = markerLength;
        StripLength = stripLength;
    }

    /// <summary>
    /// Returns only the type name: the text is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(ParsedDecision);

    /// <summary>The 1-based number of the marker line in the note.</summary>
    public int Line { get; }

    /// <summary>The number of a numbered marker (<c>#!d1234</c>), or <c>null</c> for a bare <c>#!d</c>.</summary>
    public int? Number { get; }

    /// <summary>
    /// The paragraph without its marker (raw): each line trimmed and collapsed, the empty ones left out, joined with
    /// <c>\n</c>, at most <see cref="DecisionParser.MaxTextLength"/> chars. Empty for a marker with no text.
    /// </summary>
    public string Text { get; }

    /// <summary>The first line of <see cref="Text"/>.</summary>
    public string Headline => Text.IndexOf('\n', StringComparison.Ordinal) is int end and >= 0 ? Text[..end] : Text;

    /// <summary>The index in the note of the marker's <c>#</c>.</summary>
    public int MarkerIndex { get; }

    /// <summary>The length of the marker token (<c>#!d</c> plus its digits).</summary>
    public int MarkerLength { get; }

    /// <summary>
    /// How many chars <see cref="DecisionParser.StripMarkers"/> removes at <see cref="MarkerIndex"/>: the token, the
    /// spaces and tabs after it and, when the marker is the whole line (no list marker), the line ending too.
    /// </summary>
    public int StripLength { get; }
}

/// <summary>
/// Finds <c>#!d</c> decisions (a marked paragraph) and <c>#!d1234</c> references in Markdown text (job 343). Mirrored
/// by <c>editor/src/decisions.js</c>; both are tested on the same fixtures.
/// </summary>
internal static class DecisionParser
{
    /// <summary>The most characters a decision's text keeps; a longer one is cut (never inside a surrogate pair).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>The highest decision number: 9 digits.</summary>
    public const int MaxNumber = 999_999_999;

    /// <summary>The marker every decision and reference starts with.</summary>
    public const string Token = "#!d";

    private const string Fence = "```";

    /// <summary>
    /// The decisions in <paramref name="markdown"/>, in document order: every counted marker
    /// (<see cref="Markers"/>) whose <see cref="ParsedDecision.Text"/> is not empty.
    /// </summary>
    public static IReadOnlyList<ParsedDecision> Parse(string markdown) =>
        Markers(markdown).Where(d => d.Text.Length > 0).ToList();

    /// <summary>
    /// Every counted marker in <paramref name="markdown"/>, in document order, those with an empty text included. A
    /// marker line is outside fences: optional spaces or tabs, an optional <c>-</c>, <c>*</c> or <c>+</c> plus one space,
    /// then <c>#!d</c>, then either nothing or a space or tab (bare) or 1-9 digits starting with 1-9 and then nothing or
    /// a space or tab (numbered). It counts when it has a list marker, is the first body line (only blank lines,
    /// headings and <c>key:: value</c> lines before it), or follows a blank line, an ATX heading or a fence line. Its
    /// paragraph runs to the line before the next blank line, heading, fence line or list-marker line.
    /// </summary>
    public static IReadOnlyList<ParsedDecision> Markers(string markdown)
    {
        List<ParsedDecision> found = [];
        List<(int Start, string Line)> lines = Lines(markdown);
        bool inFence = false;
        bool head = true;
        LineKind previous = LineKind.Blank;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Line;
            if (IsFence(line))
            {
                inFence = !inFence;
                previous = LineKind.Fence;
                head = false;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            if (Marker(line) is { } marker
                && (marker.Listed || head || previous is LineKind.Blank or LineKind.Heading or LineKind.Fence))
            {
                found.Add(Decision(markdown, lines, i, marker));
            }

            LineKind kind = Kind(line);
            head = head && (kind is LineKind.Blank or LineKind.Heading || IsAttributeLine(line));
            previous = kind;
        }

        return found;
    }

    /// <summary>
    /// The distinct numbers referenced in <paramref name="markdown"/>, in order of first appearance. A reference is
    /// <c>#!d&lt;n&gt;</c> (1-9 digits starting with 1-9) at a line start or after whitespace, outside fences and code
    /// spans, not the marker of a decision (<see cref="Parse"/>; a lone <c>#!d12</c> paragraph is a reference), and not
    /// followed by a letter, digit, <c>-</c>, <c>_</c> or <c>#</c>.
    /// </summary>
    public static IReadOnlyList<int> References(string markdown)
    {
        HashSet<int> markers = Parse(markdown).Select(d => d.MarkerIndex).ToHashSet();
        List<int> found = [];
        HashSet<int> seen = [];
        foreach ((_, int start, string line) in TagParser.IndexedLinesOutsideFences(markdown))
        {
            CodeSpans? spans = CodeSpans.For(line);
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '`' && spans is not null)
                {
                    i = spans.Skip(i);
                    continue;
                }

                if (line[i] == '#'
                    && (i == 0 || char.IsWhiteSpace(line[i - 1]) || (i == 1 && line[0] == '﻿'))
                    && !markers.Contains(start + i)
                    && ReferenceAt(line, i, out int number, out int end))
                {
                    if (seen.Add(number))
                    {
                        found.Add(number);
                    }

                    i = end;
                    continue;
                }

                i++;
            }
        }

        return found;
    }

    /// <summary>
    /// <paramref name="markdown"/> with the marker of every numbered decision (<see cref="Parse"/>) removed
    /// (<see cref="ParsedDecision.StripLength"/>), so a pasted decision becomes a plain paragraph; bare markers and
    /// references (a lone <c>#!d12</c> too) stay.
    /// </summary>
    public static string StripMarkers(string markdown)
    {
        IReadOnlyList<ParsedDecision> numbered = Parse(markdown).Where(d => d.Number is not null).ToList();
        if (numbered.Count == 0)
        {
            return markdown;
        }

        System.Text.StringBuilder builder = new(markdown.Length);
        int at = 0;
        foreach (ParsedDecision marker in numbered)
        {
            builder.Append(markdown, at, marker.MarkerIndex - at);
            at = marker.MarkerIndex + marker.StripLength;
        }

        builder.Append(markdown, at, markdown.Length - at);
        return builder.ToString();
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a decision number as typed (<c>1234</c> or <c>#!d1234</c>, trimmed): 1-9 digits
    /// starting with 1-9.
    /// </summary>
    public static bool TryParseNumber(string text, out int number)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith(Token, StringComparison.Ordinal))
        {
            trimmed = trimmed[Token.Length..];
        }

        int length = Digits(trimmed, 0);
        if (length == 0 || length != trimmed.Length)
        {
            number = 0;
            return false;
        }

        number = int.Parse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>How <paramref name="number"/> is written: <c>#!d1234</c>.</summary>
    public static string Format(int number) =>
        Token + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The lines of the text with the index of each first char; a trailing '\r' is not part of the line.
    private static List<(int Start, string Line)> Lines(string markdown)
    {
        List<(int Start, string Line)> lines = [];
        int start = 0;
        while (true)
        {
            int end = markdown.IndexOf('\n', start);
            int stop = end < 0 ? markdown.Length : end;
            int length = stop > start && markdown[stop - 1] == '\r' ? stop - start - 1 : stop - start;
            lines.Add((start, markdown.Substring(start, length)));
            if (end < 0)
            {
                return lines;
            }

            start = end + 1;
        }
    }

    private static ParsedDecision Decision(
        string markdown, List<(int Start, string Line)> lines, int index, (int At, int Length, int? Number, bool Listed) marker)
    {
        string line = lines[index].Line;
        int after = marker.At + marker.Length;
        int rest = after;
        while (rest < line.Length && line[rest] is ' ' or '\t')
        {
            rest++;
        }

        int strip = rest - marker.At;
        int lineStart = lines[index].Start;
        if (!marker.Listed && rest == line.Length && index + 1 < lines.Count)
        {
            // The marker is the whole line: its line ending goes too, so the paragraph starts on the next line.
            strip = lines[index + 1].Start - (lineStart + marker.At);
        }

        List<string> parts = [];
        AddPart(parts, line.AsSpan(after));
        for (int i = index + 1; i < lines.Count; i++)
        {
            string next = lines[i].Line;
            if (IsFence(next) || Kind(next) is not LineKind.Text || Marker(next) is { Listed: true } || IsListLine(next))
            {
                break;
            }

            AddPart(parts, next.AsSpan());
        }

        string text = string.Join('\n', parts);
        if (text.Length > MaxTextLength)
        {
            int cut = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
            text = text[..cut];
        }

        return new ParsedDecision(index + 1, marker.Number, text.TrimEnd(), lineStart + marker.At, marker.Length, strip);
    }

    private static void AddPart(List<string> parts, ReadOnlySpan<char> line)
    {
        System.Text.StringBuilder builder = new(line.Length);
        TagParser.Collapse(line, builder, int.MaxValue, null);
        if (builder.Length > 0)
        {
            parts.Add(builder.ToString());
        }
    }

    // The marker at the start of line: where its '#' is, the token length, the number (null when bare) and whether
    // a list marker comes first; null when the line is no marker line.
    private static (int At, int Length, int? Number, bool Listed)? Marker(string line)
    {
        int i = line.Length > 0 && line[0] == '﻿' ? 1 : 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        bool listed = false;
        if (i + 1 < line.Length && line[i] is '-' or '*' or '+' && line[i + 1] == ' ')
        {
            listed = true;
            i += 2;
        }

        if (!line.AsSpan(i).StartsWith(Token, StringComparison.Ordinal))
        {
            return null;
        }

        int digitsAt = i + Token.Length;
        int digits = Digits(line, digitsAt);
        int end = digitsAt + digits;
        if (end < line.Length && line[end] is not (' ' or '\t'))
        {
            return null;
        }

        int? number = digits == 0
            ? null
            : int.Parse(line.AsSpan(digitsAt, digits), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        return (i, end - i, number, listed);
    }

    // A reference at index i ('#'): the number and the index after it, when the token, its digits and the char after
    // (none, or not a letter, digit, '-', '_' or '#') fit.
    private static bool ReferenceAt(string line, int i, out int number, out int end)
    {
        number = 0;
        end = i;
        if (!line.AsSpan(i).StartsWith(Token, StringComparison.Ordinal))
        {
            return false;
        }

        int digitsAt = i + Token.Length;
        int digits = Digits(line, digitsAt);
        end = digitsAt + digits;
        if (digits == 0 || (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_' or '#')))
        {
            return false;
        }

        number = int.Parse(line.AsSpan(digitsAt, digits), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    // The count of ASCII digits at start (1-9 first, at most 9), or 0. Ten or more digits give 9, so the caller sees a
    // digit after them and refuses.
    private static int Digits(string text, int start)
    {
        if (start >= text.Length || text[start] is < '1' or > '9')
        {
            return 0;
        }

        int end = start + 1;
        while (end < text.Length && end - start < 9 && text[end] is >= '0' and <= '9')
        {
            end++;
        }

        return end - start;
    }

    private static bool IsFence(string line) => line.TrimStart().StartsWith(Fence, StringComparison.Ordinal);

    private static bool IsListLine(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        return i + 1 < line.Length && line[i] is '-' or '*' or '+' && line[i + 1] == ' ';
    }

    // A column-0 key (a letter, then letters, digits, '-' or '_'), directly followed by "::": the attribute block that
    // the editor hides before the first body line.
    private static bool IsAttributeLine(string line)
    {
        if (line.Length == 0 || !char.IsLetter(line[0]))
        {
            return false;
        }

        int end = 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_'))
        {
            end++;
        }

        return line.AsSpan(end).StartsWith("::", StringComparison.Ordinal);
    }

    private static LineKind Kind(string line)
    {
        int i = line.Length > 0 && line[0] == '﻿' ? 1 : 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        if (i == line.Length)
        {
            return LineKind.Blank;
        }

        int hashes = 0;
        while (i + hashes < line.Length && line[i + hashes] == '#')
        {
            hashes++;
        }

        if (hashes is >= 1 and <= 6 && (i + hashes == line.Length || line[i + hashes] is ' ' or '\t'))
        {
            return LineKind.Heading;
        }

        return LineKind.Text;
    }

    private enum LineKind
    {
        Blank,
        Heading,
        Fence,
        Text,
    }
}
