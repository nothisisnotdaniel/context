namespace Context;

/// <summary>
/// A <c>!d</c> decision found by <see cref="DecisionParser.Parse"/>: a marked paragraph (job 343, the <c>!d</c> grammar
/// of job 349). Its number token is <c>&lt;prefix&gt;-&lt;n&gt;</c> (<see cref="Prefix"/> set), a plain <c>&lt;n&gt;</c>
/// (<see cref="Prefix"/> <c>null</c>, <see cref="Number"/> set; always renumbered) or missing (bare).
/// </summary>
internal sealed record ParsedDecision
{
    public ParsedDecision(
        int line, string? prefix, int? number, string text, int markerIndex, int tokenIndex, int tokenLength)
    {
        Line = line;
        Prefix = prefix;
        Number = number;
        Text = text;
        MarkerIndex = markerIndex;
        TokenIndex = tokenIndex;
        TokenLength = tokenLength;
    }

    /// <summary>
    /// Returns only the type name: the text is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(ParsedDecision);

    /// <summary>The 1-based number of the marker line in the note.</summary>
    public int Line { get; }

    /// <summary>The token's prefix in lower case (<c>ab12</c>), or <c>null</c> for a plain number or a bare marker.</summary>
    public string? Prefix { get; }

    /// <summary>The token's number, or <c>null</c> for a bare marker.</summary>
    public int? Number { get; }

    /// <summary>Whether the marker has no number token.</summary>
    public bool Bare => Number is null;

    /// <summary>Whether the token is a plain number (<c>!d 12</c>), which a save always replaces.</summary>
    public bool Plain => Number is not null && Prefix is null;

    /// <summary>
    /// The paragraph without its marker and token (raw): each line trimmed and collapsed, the empty ones left out,
    /// joined with <c>\n</c>, at most <see cref="DecisionParser.MaxTextLength"/> chars. Empty for a marker with no text.
    /// </summary>
    public string Text { get; }

    /// <summary>The first line of <see cref="Text"/>.</summary>
    public string Headline => Text.IndexOf('\n', StringComparison.Ordinal) is int end and >= 0 ? Text[..end] : Text;

    /// <summary>The index in the note of the marker's <c>!</c>.</summary>
    public int MarkerIndex { get; }

    /// <summary>
    /// The index of the number token (after <c>!d</c> and one space or tab), or of the char after <c>!d</c> for a bare
    /// marker (where a number goes, after a space).
    /// </summary>
    public int TokenIndex { get; }

    /// <summary>The length of the number token; 0 for a bare marker.</summary>
    public int TokenLength { get; }

    /// <summary>The index after the marker and its token: where the Copy reference widget sits.</summary>
    public int MarkerEnd => TokenLength > 0 ? TokenIndex + TokenLength : MarkerIndex + DecisionParser.Marker.Length;

    /// <summary>The token as written in lower case (<c>ab12-1234</c>, <c>12</c>), or <c>null</c> when bare.</summary>
    public string? Token => Number is int n ? DecisionParser.Label(Prefix, n) : null;
}

/// <summary>
/// A <c>#!d</c> reference found by <see cref="DecisionParser.References"/> (job 349): <c>#!d1234</c> (<see cref="Prefix"/>
/// <c>null</c>: the own folder's) or <c>#!dab12-1234</c> (the prefix in lower case).
/// </summary>
internal readonly record struct DecisionRef(string? Prefix, int Number)
{
    /// <summary>The reference without <c>#!d</c>: <c>1234</c> or <c>ab12-1234</c>.</summary>
    public string Text => DecisionParser.Label(Prefix, Number);
}

/// <summary>
/// Finds <c>!d</c> decisions (a marked paragraph) and <c>#!d1234</c> / <c>#!dab12-1234</c> references in Markdown text
/// (jobs 343 and 349). Mirrored by <c>editor/src/decisions.js</c>; both are tested on the same fixtures.
/// </summary>
internal static class DecisionParser
{
    /// <summary>The most characters a decision's text keeps; a longer one is cut (never inside a surrogate pair).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>The highest decision number: 9 digits.</summary>
    public const int MaxNumber = 999_999_999;

    /// <summary>The marker every decision starts with.</summary>
    public const string Marker = "!d";

    /// <summary>The token every reference starts with.</summary>
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
    /// marker line is outside fences: an optional BOM, optional spaces or tabs, an optional <c>-</c>, <c>*</c> or
    /// <c>+</c> plus one space, then <c>!d</c>, then nothing or a space or tab. Directly after <c>!d</c> and that one
    /// space or tab, a number token is <c>&lt;p&gt;-&lt;n&gt;</c> (<c>p</c> 4 <see cref="DecisionFolder.Alphabet"/> chars,
    /// any case) or a plain <c>&lt;n&gt;</c> (1-9 digits starting with 1-9), followed by nothing or a space or tab;
    /// anything else is the paragraph's text. A marker counts when it has a list marker, is in the head (only blank
    /// lines, headings and <c>key:: value</c> lines before it), or follows a blank line, an ATX heading or a fence line.
    /// Its paragraph runs to the line before the next blank line, heading, fence line or list-marker line.
    /// </summary>
    public static IReadOnlyList<ParsedDecision> Markers(string markdown)
    {
        List<ParsedDecision> found = [];
        List<(int Start, string Line)> lines = Lines(markdown);
        foreach ((int index, Match marker) in Scan(lines, MarkerOf))
        {
            found.Add(Decision(lines, index, marker));
        }

        return found;
    }

    /// <summary>
    /// Job 343's markers, for the migration only (<see cref="DecisionMigration"/>): the decisions whose paragraph
    /// starts with <c>#!d</c> or <c>#!d&lt;n&gt;</c> at a decision position (the same positions as <see cref="Markers"/>)
    /// and has text, as (index of the <c>#</c>, length of the token with its digits, number or <c>null</c>).
    /// </summary>
    public static IReadOnlyList<(int Index, int Length, int? Number)> LegacyMarkers(string markdown)
    {
        List<(int Index, int Length, int? Number)> found = [];
        List<(int Start, string Line)> lines = Lines(markdown);
        foreach ((int index, Match marker) in Scan(lines, LegacyMarkerOf))
        {
            if (Decision(lines, index, marker).Text.Length > 0)
            {
                found.Add((lines[index].Start + marker.At, marker.End - marker.At, marker.Number));
            }
        }

        return found;
    }

    /// <summary>
    /// The distinct references in <paramref name="markdown"/>, in order of first appearance. A reference is
    /// <c>#!d&lt;n&gt;</c> or <c>#!d&lt;p&gt;-&lt;n&gt;</c> (<c>p</c> 4 <see cref="DecisionFolder.Alphabet"/> chars, any
    /// case; <c>n</c> 1-9 digits starting with 1-9) at a line start or after whitespace, outside fences and code spans,
    /// and not followed by a letter, digit, <c>-</c>, <c>_</c> or <c>#</c>. A marker starts with <c>!</c>, so it is never
    /// one.
    /// </summary>
    public static IReadOnlyList<DecisionRef> References(string markdown)
    {
        List<DecisionRef> found = [];
        HashSet<DecisionRef> seen = [];
        if (!markdown.Contains(Token, StringComparison.Ordinal))
        {
            return found;
        }

        foreach ((_, _, string line) in TagParser.IndexedLinesOutsideFences(markdown))
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
                    && ReferenceAt(line, i, out DecisionRef reference, out int end))
                {
                    if (seen.Add(reference))
                    {
                        found.Add(reference);
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
    /// Whether <paramref name="text"/> (trimmed) is a reference as typed: <c>1234</c>, <c>#!d1234</c>,
    /// <c>ab12-1234</c> or <c>#!dab12-1234</c>, any case; the prefix comes back in lower case, <c>null</c> for a plain
    /// number.
    /// </summary>
    public static bool TryParseReference(string text, out DecisionRef reference)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith(Token, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[Token.Length..];
        }

        if (NumberToken(trimmed, 0, out string? prefix, out int number) is int length && length == trimmed.Length)
        {
            reference = new DecisionRef(prefix, number);
            return true;
        }

        reference = default;
        return false;
    }

    /// <summary>A number or token without <c>#!d</c>: <c>1234</c> without a prefix, else <c>ab12-1234</c>.</summary>
    public static string Label(string? prefix, int number) =>
        (prefix is null ? string.Empty : prefix + "-") + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How a reference is written: <c>#!d1234</c> or <c>#!dab12-1234</c>.</summary>
    public static string Format(string? prefix, int number) => Token + Label(prefix, number);

    /// <summary>How a reference to a decision of the own folder is written: <c>#!d1234</c>.</summary>
    public static string Format(int number) => Format(null, number);

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

    // The counted marker lines: outside fences, listed, in the head, or after a blank line, heading or fence line.
    private static IEnumerable<(int Index, Match Marker)> Scan(List<(int Start, string Line)> lines, Func<string, Match?> markerOf)
    {
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

            if (markerOf(line) is { } marker
                && (marker.Listed || head || previous is LineKind.Blank or LineKind.Heading or LineKind.Fence))
            {
                yield return (i, marker);
            }

            LineKind kind = Kind(line);
            head = head && (kind is LineKind.Blank or LineKind.Heading || IsAttributeLine(line));
            previous = kind;
        }
    }

    private static ParsedDecision Decision(List<(int Start, string Line)> lines, int index, Match marker)
    {
        string line = lines[index].Line;
        List<string> parts = [];
        AddPart(parts, line.AsSpan(marker.End));
        for (int i = index + 1; i < lines.Count; i++)
        {
            string next = lines[i].Line;
            if (IsFence(next) || Kind(next) is not LineKind.Text || MarkerOf(next) is { Listed: true } || IsListLine(next))
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

        int lineStart = lines[index].Start;
        return new ParsedDecision(
            index + 1,
            marker.Prefix,
            marker.Number,
            text.TrimEnd(),
            lineStart + marker.At,
            lineStart + marker.TokenAt,
            marker.TokenLength);
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

    // Where the marker starts on line (after the BOM, indent and list marker), or -1, and whether it is listed.
    private static int MarkerStart(string line, out bool listed)
    {
        int i = line.Length > 0 && line[0] == '﻿' ? 1 : 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        listed = false;
        if (i + 1 < line.Length && line[i] is '-' or '*' or '+' && line[i + 1] == ' ')
        {
            listed = true;
            i += 2;
        }

        return i;
    }

    // The !d marker at the start of line, or null when the line is no marker line.
    private static Match? MarkerOf(string line)
    {
        int i = MarkerStart(line, out bool listed);
        if (!line.AsSpan(i).StartsWith(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        int after = i + Marker.Length;
        if (after < line.Length && line[after] is not (' ' or '\t'))
        {
            return null;
        }

        int tokenAt = after + 1;
        if (after < line.Length && NumberToken(line, tokenAt, out string? prefix, out int number) is int length
            && (tokenAt + length == line.Length || line[tokenAt + length] is ' ' or '\t'))
        {
            return new Match(i, tokenAt + length, tokenAt, length, prefix, number, listed);
        }

        return new Match(i, after, after, 0, null, null, listed);
    }

    // Job 343's #!d marker at the start of line (the token, its digits, then nothing or a space or tab), or null.
    private static Match? LegacyMarkerOf(string line)
    {
        int i = MarkerStart(line, out bool listed);
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

        int? number = digits == 0 ? null : ParseDigits(line, digitsAt, digits);
        return new Match(i, end, digitsAt, digits, null, number, listed);
    }

    // The number token at start: <p>-<n> (prefix lower-cased) or a plain <n>; its length, or null when there is none.
    // The char after it is the caller's to check.
    private static int? NumberToken(string text, int start, out string? prefix, out int number)
    {
        prefix = null;
        number = 0;
        if (start + DecisionFolder.PrefixLength + 1 < text.Length
            && text[start + DecisionFolder.PrefixLength] == '-'
            && DecisionFolder.IsPrefix(text.Substring(start, DecisionFolder.PrefixLength)))
        {
            int at = start + DecisionFolder.PrefixLength + 1;
            int count = Digits(text, at);
            if (count > 0 && !(at + count < text.Length && text[at + count] is >= '0' and <= '9'))
            {
                prefix = text.Substring(start, DecisionFolder.PrefixLength).ToLowerInvariant();
                number = ParseDigits(text, at, count);
                return at + count - start;
            }
        }

        int digits = Digits(text, start);
        if (digits == 0 || (start + digits < text.Length && text[start + digits] is >= '0' and <= '9'))
        {
            return null;
        }

        number = ParseDigits(text, start, digits);
        return digits;
    }

    private static int ParseDigits(string text, int start, int count) =>
        int.Parse(text.AsSpan(start, count), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);

    // A reference at index i ('#'): the reference and the index after it, when the token, its number and the char after
    // (none, or not a letter, digit, '-', '_' or '#') fit.
    private static bool ReferenceAt(string line, int i, out DecisionRef reference, out int end)
    {
        reference = default;
        end = i;
        if (!line.AsSpan(i).StartsWith(Token, StringComparison.Ordinal))
        {
            return false;
        }

        int at = i + Token.Length;
        if (NumberToken(line, at, out string? prefix, out int number) is not int length)
        {
            return false;
        }

        end = at + length;
        if (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '-' or '_' or '#'))
        {
            return false;
        }

        reference = new DecisionRef(prefix, number);
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

    // A marker on its line: where it starts (its '!' or '#'), the index after it and its token, the token's place and
    // length (0 when bare), the token's prefix and number, and whether a list marker came first.
    private sealed record Match(int At, int End, int TokenAt, int TokenLength, string? Prefix, int? Number, bool Listed);

    private enum LineKind
    {
        Blank,
        Heading,
        Fence,
        Text,
    }
}
