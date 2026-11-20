using System.Globalization;
using System.Text;

namespace Context;

/// <summary>
/// The agenda carry-over (job 245): the still-open points of the previous occurrence's meeting note, copied into a new
/// meeting note under <c>## From last time</c>. The previous note is only read, never changed.
/// </summary>
internal static class MeetingCarryOver
{
    /// <summary>The most lines copied; with more, a last line says how many were left out.</summary>
    public const int MaxLines = 200;

    /// <summary>The largest previous note that is read (1 MiB); a larger one gives no section.</summary>
    public const long MaxPreviousBytes = 1024 * 1024;

    /// <summary>The heading of the section the points go under.</summary>
    public const string Heading = "From last time";

    /// <summary>The heading text (ignoring case) of a section that is copied whole.</summary>
    public const string OpenPointsHeading = "Open points";

    private const string DateFormat = "yyyy-MM-dd";

    private const string Fence = "```";

    /// <summary>
    /// The previous occurrence's note: among the subpages of <paramref name="meetingPage"/> (any case) whose title is
    /// exactly <c>yyyy-MM-dd</c> (invariant, a real date) and earlier than <paramref name="date"/>, the latest one;
    /// else <c>null</c>.
    /// </summary>
    public static NoteInfo? Previous(NoteStore notes, string meetingPage, DateOnly date)
    {
        string prefix = meetingPage + "/";
        NoteInfo? best = null;
        DateOnly bestDate = DateOnly.MinValue;
        foreach (NoteInfo note in notes.List())
        {
            if (note.Name.Length != prefix.Length + DateFormat.Length
                || !note.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string title = note.Name[prefix.Length..];
            if (!title.All(c => char.IsAsciiDigit(c) || c == '-')
                || !DateOnly.TryParseExact(title, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
                || day >= date)
            {
                continue;
            }

            if (best is null || day > bestDate)
            {
                best = note;
                bestDate = day;
            }
        }

        return best;
    }

    /// <summary>
    /// The open points of <paramref name="markdown"/> in line order: every open todo <see cref="NoteTodoParser.Parse"/>
    /// finds and every open checkbox <see cref="NoteTodoParser.ParseCheckboxes"/> finds (job 329; its raw line with
    /// leading spaces and tabs removed), every line <see cref="NoteTodoParser.ParseCarried"/> finds (job 330), written
    /// as <c>- [ ] </c> and the text after its marker as written, and the lines of every section whose ATX heading
    /// (outside fences) is <see cref="OpenPointsHeading"/> ignoring case, up to the next heading of the same or higher
    /// level outside fences, copied as written without the heading and without leading and trailing blank lines. A
    /// todo inside such a section appears once, as written (a <c>[&gt;]</c> line once, as <c>- [ ] </c> and its text). <c>\r</c> is dropped from every line.
    /// </summary>
    public static IReadOnlyList<string> Extract(string markdown)
    {
        string[] lines = markdown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Replace("\r", string.Empty, StringComparison.Ordinal);
        }

        // Kept lines by 0-based index: the text to copy.
        SortedDictionary<int, string> kept = [];
        foreach (ParsedNoteTodo todo in NoteTodoParser.Parse(markdown).Concat(NoteTodoParser.ParseCheckboxes(markdown)))
        {
            kept[todo.Line - 1] = lines[todo.Line - 1].TrimStart(' ', '\t');
        }

        bool inFence = false;
        int sectionLevel = 0;
        List<int> section = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            bool isFence = line.TrimStart().StartsWith(Fence, StringComparison.Ordinal);
            int level = 0;
            string? text = null;
            if (!inFence && !isFence)
            {
                text = AtxHeading(line, out level);
            }

            if (isFence)
            {
                inFence = !inFence;
            }

            // A heading of the same or higher level ends the open section; the check below may then start a new one at
            // this same heading (another Open points). A deeper heading falls through and is copied as a section line.
            if (text is not null && sectionLevel > 0 && level <= sectionLevel)
            {
                Keep(lines, section, kept);
                sectionLevel = 0;
            }

            if (text is not null && sectionLevel == 0
                && string.Equals(text, OpenPointsHeading, StringComparison.OrdinalIgnoreCase))
            {
                sectionLevel = level;
                continue;
            }

            if (sectionLevel > 0)
            {
                section.Add(i);
            }
        }

        if (sectionLevel > 0)
        {
            Keep(lines, section, kept);
        }

        // Job 330: a line marked for the next meeting comes over as an open checkbox, also inside an Open points
        // section (so this runs after the section pass, which copies lines as written).
        foreach (ParsedNoteTodo carried in NoteTodoParser.ParseCarried(markdown))
        {
            string line = lines[carried.Line - 1];
            int start = line.Length - line.TrimStart(' ', '\t').Length;
            kept[carried.Line - 1] = "- " + NoteTodoParser.OpenBox + " " + line[(start + 6)..];
        }

        return kept.Values.ToList();
    }

    /// <summary>
    /// The section text: <c>\n## From last time\n\nFrom [[&lt;name&gt;]]:\n\n</c> then the lines, each ending in
    /// <c>\n</c>. A name containing <c>[</c>, <c>]</c>, <c>#</c> or a backtick is written as plain text
    /// (<c>From &lt;name&gt;:</c>). At most <see cref="MaxLines"/> lines are copied; with more, a last line
    /// <c>- … and &lt;k&gt; more in the previous note.</c> follows.
    /// </summary>
    public static string Section(string previousName, IReadOnlyList<string> lines)
    {
        StringBuilder text = new StringBuilder();
        text.Append("\n## ").Append(Heading).Append("\n\n");
        bool plain = previousName.IndexOfAny(['[', ']', '#', '`']) >= 0;
        text.Append(plain ? $"From {previousName}:" : $"From [[{previousName}]]:").Append("\n\n");
        int count = Math.Min(lines.Count, MaxLines);
        for (int i = 0; i < count; i++)
        {
            text.Append(lines[i]).Append('\n');
        }

        if (lines.Count > MaxLines)
        {
            text.Append("- … and ")
                .Append((lines.Count - MaxLines).ToString(CultureInfo.InvariantCulture))
                .Append(" more in the previous note.\n");
        }

        return text.ToString();
    }

    // The heading text of an ATX heading line ("#".."######", one space, trailing #s and spaces removed), else null.
    private static string? AtxHeading(string line, out int level)
    {
        level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level == 0 || level > 6 || level >= line.Length || line[level] != ' ')
        {
            level = 0;
            return null;
        }

        return line[(level + 1)..].TrimEnd('#', ' ').Trim();
    }

    // Copies the section's lines as written, leading and trailing blank lines dropped, then empties it.
    private static void Keep(string[] lines, List<int> section, SortedDictionary<int, string> kept)
    {
        int first = 0;
        int last = section.Count - 1;
        while (first <= last && string.IsNullOrWhiteSpace(lines[section[first]]))
        {
            first++;
        }

        while (last >= first && string.IsNullOrWhiteSpace(lines[section[last]]))
        {
            last--;
        }

        for (int i = first; i <= last; i++)
        {
            kept[section[i]] = lines[section[i]];
        }

        section.Clear();
    }
}
