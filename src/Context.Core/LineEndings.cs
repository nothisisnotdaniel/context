using System.Text;

namespace Context;

/// <summary>
/// The line endings of a note's text. <see cref="Read"/> turns <c>\r\n</c> into <c>\n</c> and records each break's
/// ending; <see cref="Apply"/> writes edited text back with the untouched lines' own endings and the most common
/// ending for new and changed lines.
/// </summary>
internal static class LineEndings
{
    /// <summary>
    /// <paramref name="decoded"/> with <c>\r\n</c> turned into <c>\n</c>; <c>HadCrlf</c> when <c>\r\n</c> is the most
    /// common ending (on a tie the first break's ending wins); <c>LineCrlf</c> one flag per <c>\n</c> (true for
    /// <c>\r\n</c>), or <c>null</c> when every break has the same ending or there is none.
    /// </summary>
    public static (string Text, bool HadCrlf, IReadOnlyList<bool>? LineCrlf) Read(string decoded)
    {
        string text = decoded.Replace("\r\n", "\n", StringComparison.Ordinal);
        int crlf = 0;
        int lf = 0;
        bool first = false;
        for (int i = 0; i < decoded.Length; i++)
        {
            if (decoded[i] == '\n')
            {
                bool isCrlf = i > 0 && decoded[i - 1] == '\r';
                if (crlf + lf == 0)
                {
                    first = isCrlf;
                }

                if (isCrlf)
                {
                    crlf++;
                }
                else
                {
                    lf++;
                }
            }
        }

        if (crlf == 0 || lf == 0)
        {
            return (text, crlf > 0, null);
        }

        // Mixed: only now is the per-break list built.
        List<bool> flags = new List<bool>(crlf + lf);
        for (int i = 0; i < decoded.Length; i++)
        {
            if (decoded[i] == '\n')
            {
                flags.Add(i > 0 && decoded[i - 1] == '\r');
            }
        }

        bool majority = crlf == lf ? first : crlf > lf;
        return (text, majority, flags);
    }

    /// <summary>
    /// The on-disk text (without a BOM) for <paramref name="plainText"/> (with <c>\n</c>) saved over
    /// <paramref name="opened"/>. A uniform note gets its one ending throughout. In a mixed note every line that
    /// <see cref="TextDiff.Lines"/> marks <see cref="DiffKind.Same"/> keeps its own ending, and added lines (and a
    /// break after an old last line that had none) get the most common one; when the diff is
    /// <see cref="TextDiffResult.TooDifferent"/>, or the flags don't match the text, the whole text gets it.
    /// </summary>
    public static string Apply(EditableNote opened, string plainText)
    {
        IReadOnlyList<bool>? flags = opened.LineCrlf;
        if (flags is null || flags.Count != Breaks(opened.Text))
        {
            return Uniform(plainText, opened.HadCrlf);
        }

        TextDiffResult diff = TextDiff.Lines(opened.Text, plainText);
        if (diff.TooDifferent)
        {
            return Uniform(plainText, opened.HadCrlf);
        }

        StringBuilder written = new StringBuilder(plainText.Length + flags.Count);
        int oldLine = 0;
        int pos = 0;
        foreach (DiffLine line in diff.Lines)
        {
            bool crlf;
            if (line.Kind == DiffKind.Removed)
            {
                oldLine++;
                continue;
            }

            if (line.Kind == DiffKind.Same)
            {
                crlf = oldLine < flags.Count ? flags[oldLine] : opened.HadCrlf;
                oldLine++;
            }
            else
            {
                crlf = opened.HadCrlf;
            }

            int end = plainText.IndexOf('\n', pos);
            if (end < 0)
            {
                break;
            }

            written.Append(plainText, pos, end - pos).Append(crlf ? "\r\n" : "\n");
            pos = end + 1;
        }

        // The last line has no break; anything the diff didn't account for gets the most common ending.
        return written.Append(Uniform(plainText[pos..], opened.HadCrlf)).ToString();
    }

    private static string Uniform(string plainText, bool crlf) =>
        crlf ? plainText.Replace("\n", "\r\n", StringComparison.Ordinal) : plainText;

    private static int Breaks(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
