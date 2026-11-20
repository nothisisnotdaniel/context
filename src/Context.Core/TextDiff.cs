namespace Context;

/// <summary>How a line of a <see cref="TextDiff"/> differs.</summary>
internal enum DiffKind
{
    /// <summary>In both texts.</summary>
    Same,

    /// <summary>Only in the current text.</summary>
    Added,

    /// <summary>Only in the old text.</summary>
    Removed,
}

/// <summary>One line of a diff: its kind and its raw text (no line break).</summary>
internal sealed record DiffLine
{
    public DiffLine(DiffKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public DiffKind Kind { get; }

    public string Text { get; }

    public override string ToString() => nameof(DiffLine);
}

/// <summary>
/// A line diff: <see cref="Lines"/> in order, or <see cref="TooDifferent"/> (and no lines) when more than
/// <see cref="TextDiff.MaxChangedLines"/> lines were added or removed.
/// </summary>
internal sealed record TextDiffResult
{
    public TextDiffResult(bool tooDifferent, IReadOnlyList<DiffLine> lines)
    {
        TooDifferent = tooDifferent;
        Lines = lines;
    }

    public bool TooDifferent { get; }

    public IReadOnlyList<DiffLine> Lines { get; }

    public override string ToString() => nameof(TextDiffResult);
}

/// <summary>
/// Line diffs for the note history. Texts are split at <c>\n</c> with a trailing <c>\r</c> dropped from each line;
/// the <see cref="DiffKind.Same"/> and <see cref="DiffKind.Removed"/> lines give the old text, the
/// <see cref="DiffKind.Same"/> and <see cref="DiffKind.Added"/> lines the current one. The common start and end are
/// matched first, then Myers' algorithm runs on the rest with at most <see cref="MaxChangedLines"/> edits, so the work
/// is bounded by the length of the rest times that bound.
/// </summary>
internal static class TextDiff
{
    /// <summary>The most added plus removed lines a diff shows.</summary>
    public const int MaxChangedLines = 1000;

    public static TextDiffResult Lines(string old, string current)
    {
        string[] a = Split(old);
        string[] b = Split(current);
        int start = 0;
        while (start < a.Length && start < b.Length && string.Equals(a[start], b[start], StringComparison.Ordinal))
        {
            start++;
        }

        int endA = a.Length;
        int endB = b.Length;
        while (endA > start && endB > start && string.Equals(a[endA - 1], b[endB - 1], StringComparison.Ordinal))
        {
            endA--;
            endB--;
        }

        List<DiffLine>? middle = Middle(a, start, endA, b, start, endB);
        if (middle is null)
        {
            return new TextDiffResult(true, []);
        }

        List<DiffLine> lines = new List<DiffLine>(start + middle.Count + (a.Length - endA));
        for (int i = 0; i < start; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, a[i]));
        }

        lines.AddRange(middle);
        for (int i = endA; i < a.Length; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, a[i]));
        }

        return new TextDiffResult(false, lines);
    }

    private static string[] Split(string text)
    {
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        return lines;
    }

    // Myers' shortest edit script for a[a0..a1) against b[b0..b1), or null when it needs more than MaxChangedLines edits.
    private static List<DiffLine>? Middle(string[] a, int a0, int a1, string[] b, int b0, int b1)
    {
        int n = a1 - a0;
        int m = b1 - b0;
        if (Math.Abs(n - m) > MaxChangedLines)
        {
            return null;
        }

        int max = Math.Min(n + m, MaxChangedLines);
        int offset = max + 1;
        int[] v = new int[(2 * max) + 3];
        List<int[]> trace = [];
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[k - 1 + offset] < v[k + 1 + offset])
                    ? v[k + 1 + offset]
                    : v[k - 1 + offset] + 1;
                int y = x - k;
                while (x < n && y < m && string.Equals(a[a0 + x], b[b0 + y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[k + offset] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, a, a0, b, b0, n, m, offset);
                }
            }
        }

        return null;
    }

    private static List<DiffLine> Backtrack(List<int[]> trace, string[] a, int a0, string[] b, int b0, int n, int m, int offset)
    {
        List<DiffLine> reversed = [];
        int x = n;
        int y = m;
        for (int d = trace.Count - 1; d >= 0; d--)
        {
            int[] v = trace[d];
            int k = x - y;
            int prevK = k == -d || (k != d && v[k - 1 + offset] < v[k + 1 + offset]) ? k + 1 : k - 1;
            int prevX = v[prevK + offset];
            int prevY = prevX - prevK;
            while (x > prevX && y > prevY)
            {
                reversed.Add(new DiffLine(DiffKind.Same, a[a0 + x - 1]));
                x--;
                y--;
            }

            if (d > 0)
            {
                if (x == prevX)
                {
                    reversed.Add(new DiffLine(DiffKind.Added, b[b0 + y - 1]));
                }
                else
                {
                    reversed.Add(new DiffLine(DiffKind.Removed, a[a0 + x - 1]));
                }
            }

            x = prevX;
            y = prevY;
        }

        reversed.Reverse();
        return reversed;
    }
}
