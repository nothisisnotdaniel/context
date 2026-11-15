namespace Context;

/// <summary>Finds <c>#tags</c> and <c>#[tags with spaces]</c> in Markdown text.</summary>
internal static class TagParser
{
    /// <summary>The longest tag name (without the <c>#</c>). A longer run is not a tag at all.</summary>
    public const int MaxTagLength = 50;

    private const string Fence = "```";

    /// <summary>
    /// The distinct tags in <paramref name="markdown"/>, lower-cased (invariant), in order of first
    /// appearance. A tag is <c>#</c> at the start of a line or after whitespace, then a letter
    /// (<see cref="char.IsLetter(char)"/>), then letters, digits, <c>-</c> or <c>_</c>; it ends at the
    /// first other character. Names longer than <see cref="MaxTagLength"/> are not tags. Lines inside
    /// fenced code blocks (between <c>```</c> lines) and text inside inline backtick spans are skipped.
    /// A bracket tag is <c>#[</c> at the start of a line or after whitespace, then text up to the first
    /// <c>]</c> on the same line (not directly followed by <c>(</c>); see <see cref="NormalizeBracket"/>.
    /// Both forms give the same tag: <c>#work</c> and <c>#[Work]</c> are one tag.
    /// </summary>
    public static IReadOnlyList<string> Parse(string markdown)
    {
        List<string> tags = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string line in LinesOutsideFences(markdown))
        {
            ParseLine(line, tags, seen);
        }

        return tags;
    }

    /// <summary>
    /// The lines of <paramref name="markdown"/> (split on <c>\n</c>, trailing <c>\r</c> removed) that are not
    /// inside a fenced code block. The <c>```</c> fence lines themselves are left out too. Shared with
    /// <see cref="NoteLinkParser"/>.
    /// </summary>
    internal static IEnumerable<string> LinesOutsideFences(string markdown)
    {
        bool inFence = false;
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            yield return line;
        }
    }

    private static void ParseLine(string line, List<string> tags, HashSet<string> seen)
    {
        CodeSpans? spans = CodeSpans.For(line);
        int i = 0;
        int nextClose = -1;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = spans!.Skip(i);
                continue;
            }

            bool tagStart = c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]));
            if (tagStart && i + 1 < line.Length && line[i + 1] == '[')
            {
                // The first ']' at or after i + 2; remembered so a line full of "#[" is still scanned once.
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
                    string? bracketTag = NormalizeBracket(line.AsSpan(i + 2, nextClose - i - 2));
                    if (bracketTag is not null)
                    {
                        Add(bracketTag, tags, seen);
                        i = nextClose + 1;
                        continue;
                    }
                }

                // Not a tag: carry on just after "#[", so code spans inside are still seen.
                i += 2;
                continue;
            }

            if (tagStart && i + 1 < line.Length && char.IsLetter(line[i + 1]))
            {
                int end = i + 2;
                while (end < line.Length && IsTagChar(line[end]))
                {
                    end++;
                }

                int length = end - i - 1;
                if (length <= MaxTagLength)
                {
                    Add(line.Substring(i + 1, length).ToLowerInvariant(), tags, seen);
                }

                i = end;
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// How <paramref name="tag"/> is written in a note: <c>#tag</c>, or <c>#[tag]</c> when it contains a
    /// space.
    /// </summary>
    public static string Format(string tag) => tag.Contains(' ', StringComparison.Ordinal) ? $"#[{tag}]" : "#" + tag;

    /// <summary>
    /// Trims <paramref name="text"/>, turns each whitespace run into one space and lower-cases it
    /// (invariant). Returns <c>null</c> when the result doesn't start with a letter, contains anything
    /// other than letters, digits, <c>-</c>, <c>_</c> and spaces, or is longer than
    /// <see cref="MaxTagLength"/>.
    /// </summary>
    internal static string? NormalizeBracket(ReadOnlySpan<char> text)
    {
        // Stops at the first character that can't be in a tag, or once the name is too long, so the work
        // per "#[" is bounded by the next '#' or by MaxTagLength, not by the distance to the next ']'.
        System.Text.StringBuilder builder = new(MaxTagLength + 1);
        if (!Collapse(text, builder, MaxTagLength, static (c, length) => length == 0 ? char.IsLetter(c) : IsTagChar(c)))
        {
            return null;
        }

        return builder.Length == 0 ? null : builder.ToString().ToLowerInvariant();
    }

    /// <summary>Trims <paramref name="text"/> and turns each whitespace run into one space.</summary>
    internal static string CollapseWhitespace(ReadOnlySpan<char> text)
    {
        System.Text.StringBuilder builder = new(text.Length);
        Collapse(text, builder, int.MaxValue, null);
        return builder.ToString();
    }

    /// <summary>
    /// The one whitespace-collapsing loop: appends <paramref name="text"/> to the empty <paramref name="builder"/>,
    /// trimmed, with each whitespace run turned into one space. Stops and returns <c>false</c> at the first
    /// non-whitespace character that <paramref name="valid"/> refuses (called with the character and the length
    /// collapsed so far), or once the result is longer than <paramref name="maxLength"/>. Shared with
    /// <see cref="PersonParser"/>.
    /// </summary>
    internal static bool Collapse(
        ReadOnlySpan<char> text,
        System.Text.StringBuilder builder,
        int maxLength,
        Func<char, int, bool>? valid)
    {
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (valid is not null && !valid(c, builder.Length))
            {
                return false;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
            if (builder.Length > maxLength)
            {
                return false;
            }
        }

        return true;
    }

    private static void Add(string tag, List<string> tags, HashSet<string> seen)
    {
        if (seen.Add(tag))
        {
            tags.Add(tag);
        }
    }

    private static bool IsTagChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';
}

/// <summary>
/// The inline code spans of one line, found in one pass: a span is a run of backticks up to the next run of the
/// same length on the line; a run with no such match is plain text. Built only for a line that holds a backtick
/// (<see cref="For"/>), so every lookup is a dictionary hit and no path rescans the line per backtick run. Shared by
/// <see cref="TagParser"/>, <see cref="NoteLinkParser"/> and <see cref="PersonParser"/>.
/// </summary>
internal sealed class CodeSpans
{
    private readonly List<int> starts = [];
    private readonly List<int> ends = [];

    // For each run: the index of the next run of the same length, or -1 when there is none.
    private readonly List<int> matches = [];
    private readonly Dictionary<int, int> runAt = [];
    private int cursor;

    private CodeSpans(string line)
    {
        Dictionary<int, int> lastRunOfLength = [];
        int i = line.IndexOf('`');
        while (i >= 0)
        {
            int end = i;
            while (end < line.Length && line[end] == '`')
            {
                end++;
            }

            int run = starts.Count;
            starts.Add(i);
            ends.Add(end);
            matches.Add(-1);
            runAt[i] = run;
            if (lastRunOfLength.TryGetValue(end - i, out int previous))
            {
                matches[previous] = run;
            }

            lastRunOfLength[end - i] = run;
            i = end < line.Length ? line.IndexOf('`', end) : -1;
        }
    }

    /// <summary>The code spans of <paramref name="line"/>, or <c>null</c> (nothing allocated) when it has no backtick.</summary>
    public static CodeSpans? For(string line) => line.Contains('`', StringComparison.Ordinal) ? new CodeSpans(line) : null;

    /// <summary>
    /// At the backtick run starting at <paramref name="start"/>: the index just after the matching closing run, or just
    /// after the opening run when there is none (then the backticks are plain text). A <paramref name="start"/> inside a
    /// run (the scanners never land there) is treated as plain text up to the run's end.
    /// </summary>
    public int Skip(int start)
    {
        if (runAt.TryGetValue(start, out int run))
        {
            int match = matches[run];
            return match >= 0 ? ends[match] : ends[run];
        }

        int index = starts.BinarySearch(start);
        int containing = index >= 0 ? index : ~index - 1;
        return containing >= 0 && start < ends[containing] ? ends[containing] : start + 1;
    }

    /// <summary>
    /// The start of the first run at or after <paramref name="from"/> that opens a matched code span, or the line's
    /// length when there is none. Calls must come with <paramref name="from"/> never decreasing: the search only moves
    /// forward, so all calls together cost one pass over the runs.
    /// </summary>
    public int NextSpanStart(int from, int lineLength)
    {
        while (cursor < starts.Count && (starts[cursor] < from || matches[cursor] < 0))
        {
            cursor++;
        }

        return cursor < starts.Count ? starts[cursor] : lineLength;
    }
}
