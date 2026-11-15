namespace Context;

/// <summary>Finds <c>[[Note name]]</c> links (the wiki-link syntax of Obsidian, Logseq and Foam) in Markdown text.</summary>
internal static class NoteLinkParser
{
    /// <summary>
    /// The longest link target: a note's full name (<see cref="NoteStore.MaxNameLength"/>, so a subpage's
    /// <c>Parent/Title</c> fits) plus <c>.md</c>. A longer target is not a link at all.
    /// </summary>
    public const int MaxTargetLength = NoteStore.MaxNameLength + 3;

    /// <summary>
    /// The distinct link targets in <paramref name="markdown"/>, in order of first appearance. A link is
    /// <c>[[</c> anywhere in a line, then text with no <c>[</c> up to the first <c>]</c> on the same line, and
    /// that <c>]</c> directly followed by a second <c>]</c>. The target is that text, trimmed (inner spaces kept
    /// as written); an empty target or one longer than <see cref="MaxTargetLength"/> is not a link. Targets are
    /// distinct ignoring case (ordinal); the first spelling is kept. Lines inside fenced code blocks and text
    /// inside inline backtick spans are skipped, as in <see cref="TagParser"/>; a backtick run that opens a matched code
    /// span between <c>[[</c> and that <c>]</c> means no link (a backtick with no match is plain text in the target).
    /// <c>#[tag]</c> and <c>@[person]</c> are never links.
    /// Targets are raw text: they are only compared with note names, never turned into a path.
    /// </summary>
    public static IReadOnlyList<string> Parse(string markdown)
    {
        List<string> targets = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in TagParser.LinesOutsideFences(markdown))
        {
            ParseLine(line, targets, seen);
        }

        return targets;
    }

    private static void ParseLine(string line, List<string> targets, HashSet<string> seen)
    {
        // The first ']' and the first '[' at or after the current text start; remembered so a line full of
        // "[[" is still scanned once (each search only moves forward).
        // nextSpan is the first run opening a matched code span at or after the text start, found the same way.
        CodeSpans? spans = CodeSpans.For(line);
        int nextClose = -1;
        int nextOpen = -1;
        int nextSpan = -1;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '`')
            {
                i = spans!.Skip(i);
                continue;
            }

            if (c != '[' || i + 1 >= line.Length || line[i + 1] != '[')
            {
                i++;
                continue;
            }

            // A "#[" bracket tag or an "@[" mention is never a link: skip its '[' so "#[[x]]" and "@[[x]]" are no link.
            if (i > 0 && line[i - 1] is '#' or '@')
            {
                i++;
                continue;
            }

            int textStart = i + 2;
            if (nextClose < textStart)
            {
                nextClose = IndexOrEnd(line, ']', textStart);
            }

            if (nextOpen < textStart)
            {
                nextOpen = IndexOrEnd(line, '[', textStart);
            }

            if (nextSpan < textStart)
            {
                nextSpan = spans?.NextSpanStart(textStart, line.Length) ?? line.Length;
            }

            bool closed = nextClose + 1 < line.Length && line[nextClose + 1] == ']';
            if (closed && nextOpen > nextClose && nextSpan < nextClose)
            {
                // A code span opens inside the target: no link; carry on at that span, so it is skipped.
                i = nextSpan;
                continue;
            }

            if (closed && nextOpen > nextClose)
            {
                ReadOnlySpan<char> target = line.AsSpan(textStart, nextClose - textStart).Trim();
                if (target.Length > 0 && target.Length <= MaxTargetLength)
                {
                    string text = target.ToString();
                    if (seen.Add(text))
                    {
                        targets.Add(text);
                    }
                }

                i = nextClose + 2;
                continue;
            }

            // Not a link: carry on at the next '[', so "[[[a]]" still finds "[[a]]".
            i++;
        }
    }

    private static int IndexOrEnd(string line, char c, int start)
    {
        int index = line.IndexOf(c, start);
        return index < 0 ? line.Length : index;
    }
}
