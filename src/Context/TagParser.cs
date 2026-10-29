namespace Context;

/// <summary>Finds <c>#tags</c> in Markdown text.</summary>
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
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = SkipCodeSpan(line, i);
                continue;
            }

            if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]))
                && i + 1 < line.Length && char.IsLetter(line[i + 1]))
            {
                int end = i + 2;
                while (end < line.Length && IsTagChar(line[end]))
                {
                    end++;
                }

                int length = end - i - 1;
                if (length <= MaxTagLength)
                {
                    string tag = line.Substring(i + 1, length).ToLowerInvariant();
                    if (seen.Add(tag))
                    {
                        tags.Add(tag);
                    }
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

    private static bool IsTagChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_';
}
