namespace Context;

/// <summary>
/// The first-line title of a note (job 353): the line the editor hides (<c>titleSplit</c> in
/// <c>editor/src/markdown.js</c>, job 202) when it is <c># </c> and then text that, trimmed, equals the note's full
/// name or its last segment ignoring case. Pure strings, no files.
/// </summary>
internal static class NoteTitleLine
{
    private const char Bom = '﻿';

    /// <summary>
    /// <paramref name="text"/> with its title line rewritten from <paramref name="oldName"/> to
    /// <paramref name="newName"/> (full note names). The first line (after a leading U+FEFF, which is kept) must start
    /// with exactly <c>"# "</c>, and the rest of that line, trimmed, must be non-empty and equal the old full name or
    /// the old last segment, ignoring case (invariant). The text after <c>"# "</c> up to the line end (a <c>\r</c>
    /// before the <c>\n</c> is kept) then becomes the new full name when it matched the full name, else the new last
    /// segment. Everything else stays the same. Returns the same instance when nothing changes, including a case-only
    /// rename.
    /// </summary>
    public static string Rewrite(string text, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)
            || string.Equals(oldName, newName, StringComparison.InvariantCultureIgnoreCase))
        {
            return text;
        }

        int start = text[0] == Bom ? 1 : 0;
        if (!text.AsSpan(start).StartsWith("# ", StringComparison.Ordinal))
        {
            return text;
        }

        int from = start + 2;
        int end = text.IndexOf('\n', from);
        if (end < 0)
        {
            end = text.Length;
        }

        if (end > from && text[end - 1] == '\r')
        {
            end--;
        }

        string title = text[from..end].Trim().ToLowerInvariant();
        string full = oldName.Trim().ToLowerInvariant();
        string last = LastSegment(full);
        string replacement;
        if (title.Length == 0)
        {
            return text;
        }
        else if (string.Equals(title, full, StringComparison.Ordinal))
        {
            replacement = newName.Trim();
        }
        else if (string.Equals(title, last, StringComparison.Ordinal))
        {
            replacement = LastSegment(newName.Trim());
        }
        else
        {
            return text;
        }

        if (text.AsSpan(from, end - from).SequenceEqual(replacement))
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, from), replacement, text.AsSpan(end));
    }

    private static string LastSegment(string name) => name[(name.LastIndexOf('/') + 1)..].Trim();
}
