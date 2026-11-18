using System.Text;

namespace Context;

/// <summary>
/// Rewrites the <c>[[link]]</c>s to a renamed note, and to its subpages, in a note's text. Pure string work: it never
/// touches the file system, and link targets are only compared with <see cref="NameLookup"/> names, never turned into
/// a path.
/// </summary>
internal static class NoteLinkRewriter
{
    // Characters a new title can't have when links have to be rewritten: they change how a link parses or resolves.
    private static readonly char[] UnsafeTitleChars = ['#', '|', '[', ']', '`'];

    /// <summary>
    /// <paramref name="text"/> with every link to <paramref name="oldName"/> or one of its subpages pointing at
    /// <paramref name="newName"/> instead, or the same instance when no link changes (also when both names are equal
    /// ignoring case). A link (<see cref="NoteLinkParser.FindSpans"/>) changes when the text before its first <c>|</c>
    /// resolves through <paramref name="before"/>'s <see cref="NameLookup.ResolveLink"/> (the whole text first) to
    /// <paramref name="oldName"/> or a name starting with <c>&lt;oldName&gt;/</c>. Only the note part's characters are
    /// replaced, with the new full name (<c>Old/Child</c> becomes <c>New/Child</c>); a written trailing <c>.md</c>, the
    /// <c>#heading</c>, the <c>|alias</c> and every other character stay as they were.
    /// </summary>
    public static string Rewrite(string text, NameLookup before, string oldName, string newName) =>
        Apply(text, before, oldName, newName, null, out _);

    /// <summary>
    /// Like <see cref="Rewrite"/>, then checks the result: every link still parses as the same link in the same place,
    /// and every rewritten one resolves through <paramref name="after"/> (the names after the rename) to its expected
    /// new name with the same heading. Also false when a link would change and the new title (the last segment of
    /// <paramref name="newName"/>) holds <c>#</c>, <c>|</c>, <c>[</c>, <c>]</c> or a backtick. On false,
    /// <paramref name="rewritten"/> is <paramref name="text"/> and nothing may be written.
    /// </summary>
    public static bool TryRewrite(
        string text, NameLookup before, NameLookup after, string oldName, string newName, out string rewritten)
    {
        List<Expected> expected = [];
        string result = Apply(text, before, oldName, newName, expected, out IReadOnlyList<NoteLinkSpan> oldSpans);
        rewritten = text;
        if (ReferenceEquals(result, text))
        {
            return true;
        }

        string title = newName[(newName.LastIndexOf('/') + 1)..];
        if (title.IndexOfAny(UnsafeTitleChars) >= 0)
        {
            return false;
        }

        IReadOnlyList<NoteLinkSpan> newSpans = NoteLinkParser.FindSpans(result);
        if (newSpans.Count != oldSpans.Count)
        {
            return false;
        }

        int next = 0;
        int shift = 0;
        for (int i = 0; i < oldSpans.Count; i++)
        {
            NoteLinkSpan old = oldSpans[i];
            NoteLinkSpan now = newSpans[i];
            if (next < expected.Count && expected[next].Index == i)
            {
                Expected want = expected[next++];
                if (now.Offset != old.Offset + shift || !string.Equals(now.Target, want.Target, StringComparison.Ordinal))
                {
                    return false;
                }

                NoteLinkTarget? resolved = after.ResolveLink(NotePart(now.Target));
                if (resolved is null
                    || !string.Equals(resolved.Note, want.Note, StringComparison.Ordinal)
                    || !string.Equals(resolved.Heading, want.Heading, StringComparison.Ordinal))
                {
                    return false;
                }

                shift += want.Target.Length - old.Target.Length;
            }
            else if (now.Offset != old.Offset + shift || !string.Equals(now.Target, old.Target, StringComparison.Ordinal))
            {
                return false;
            }
        }

        rewritten = result;
        return true;
    }

    /// <summary>
    /// The names in <paramref name="names"/> as they are after <paramref name="oldName"/> is renamed to
    /// <paramref name="newName"/>: that note and its subpages (<c>&lt;oldName&gt;/...</c>) get the new name.
    /// </summary>
    public static NameLookup Renamed(IEnumerable<string> names, string oldName, string newName) =>
        new(names.Select(n => Moved(n, oldName, newName) ?? n));

    /// <summary>
    /// The name <paramref name="name"/> has after <paramref name="oldName"/> is renamed to <paramref name="newName"/>:
    /// <c>newName</c> plus the rest of the name when it is that note or one of its subpages, else <c>null</c>.
    /// </summary>
    public static string? Moved(string name, string oldName, string newName)
    {
        if (string.Equals(name, oldName, StringComparison.Ordinal))
        {
            return newName;
        }

        return name.Length > oldName.Length + 1
            && name.StartsWith(oldName, StringComparison.Ordinal)
            && name[oldName.Length] == '/'
                ? newName + name[oldName.Length..]
                : null;
    }

    // The part of a target that names the note and heading: the text before the first '|', trimmed.
    private static string NotePart(string target)
    {
        int pipe = target.IndexOf('|', StringComparison.Ordinal);
        return (pipe < 0 ? target : target[..pipe]).Trim();
    }

    private static string Apply(
        string text, NameLookup before, string oldName, string newName, List<Expected>? expected,
        out IReadOnlyList<NoteLinkSpan> spans)
    {
        spans = [];
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        spans = NoteLinkParser.FindSpans(text);
        StringBuilder? builder = null;
        int copied = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            NoteLinkSpan span = spans[i];
            if (Replacement(span.Target, before, oldName, newName) is not { } change)
            {
                continue;
            }

            builder ??= new StringBuilder(text.Length + 64);
            builder.Append(text, copied, span.Offset - copied).Append(change.Name);
            copied = span.Offset + change.Length;
            expected?.Add(new Expected(i, change.Name + span.Target[change.Length..], change.Name, change.Heading));
        }

        if (builder is null)
        {
            return text;
        }

        builder.Append(text, copied, text.Length - copied);
        return builder.ToString();
    }

    // How a link's target changes: the first `length` characters (the note part as written, without a written
    // trailing .md) become `name`; null when the link doesn't name the renamed note or one of its subpages.
    private static Change? Replacement(string target, NameLookup before, string oldName, string newName)
    {
        // The target is trimmed, so the note part starts at 0.
        string part = NotePart(target);
        if (part.Length == 0 || before.ResolveLink(part) is not { } resolved
            || Moved(resolved.Note, oldName, newName) is not { } name)
        {
            return null;
        }

        string written = part;
        if (before.Resolve(part) is null)
        {
            // Resolved through the note part before the first '#' (ResolveLink's second step).
            written = part[..part.IndexOf('#', StringComparison.Ordinal)].TrimEnd();
        }

        int length = written.Length;
        if (before.Match(written) is null)
        {
            // Resolve matched it without one trailing .md: that .md stays as written.
            length -= 3;
        }

        return new Change(name, length, resolved.Heading);
    }

    private sealed class Change
    {
        public Change(string name, int length, string? heading)
        {
            Name = name;
            Length = length;
            Heading = heading;
        }

        public string Name { get; }

        public int Length { get; }

        public string? Heading { get; }
    }

    // A rewritten link: its index among the spans, its new target, the note and heading it must resolve to.
    private sealed class Expected
    {
        public Expected(int index, string target, string note, string? heading)
        {
            Index = index;
            Target = target;
            Note = note;
            Heading = heading;
        }

        public int Index { get; }

        public string Target { get; }

        public string Note { get; }

        public string? Heading { get; }
    }
}
