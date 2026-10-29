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

            ParseLine(line, tags, seen);
        }

        return tags;
    }

    private static void ParseLine(string line, List<string> tags, HashSet<string> seen)
    {
        int i = 0;
        int nextClose = -1;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = SkipCodeSpan(line, i);
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

    // At a run of backticks: returns the index just after the matching closing run of the same length,
    // or just after the opening run when there is none (then the backticks are plain text).
    private static int SkipCodeSpan(string line, int start)
    {
        int open = start;
        while (open < line.Length && line[open] == '`')
        {
            open++;
        }

        int runLength = open - start;
        int i = open;
        while (i < line.Length)
        {
            if (line[i] != '`')
            {
                i++;
                continue;
            }

            int close = i;
            while (close < line.Length && line[close] == '`')
            {
                close++;
            }

            if (close - i == runLength)
            {
                return close;
            }

            i = close;
        }

        return open;
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
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            bool valid = builder.Length == 0 ? char.IsLetter(c) : IsTagChar(c);
            if (!valid)
            {
                return null;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
            if (builder.Length > MaxTagLength)
            {
                return null;
            }
        }

        return builder.Length == 0 ? null : builder.ToString().ToLowerInvariant();
    }

    /// <summary>Trims <paramref name="text"/> and turns each whitespace run into one space.</summary>
    internal static string CollapseWhitespace(ReadOnlySpan<char> text)
    {
        System.Text.StringBuilder builder = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
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
