using System.Globalization;

namespace Context;

/// <summary>An open <c>- { } ...</c> line found by <see cref="NoteTodoParser.Parse"/>.</summary>
internal sealed record ParsedNoteTodo
{
    public ParsedNoteTodo(int line, string text, DateOnly? due, string raw)
    {
        Line = line;
        Text = text;
        Due = due;
        Raw = raw;
    }

    /// <summary>
    /// Returns only the type name: the text is raw outside text, so formatting this record must never print it.
    /// </summary>
    public override string ToString() => nameof(ParsedNoteTodo);

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The text after the marker, the due token removed, trimmed and collapsed (raw), never empty.</summary>
    public string Text { get; }

    /// <summary>The date of the first valid <c>due:yyyy-MM-dd</c> token, or <c>null</c>.</summary>
    public DateOnly? Due { get; }

    /// <summary>The whole line as written (without its line end).</summary>
    public string Raw { get; }
}

/// <summary>
/// Finds open note todos (<c>- { } text</c>) in a note's text. A <c>- [ ] text</c> line is a checkbox that stays in its
/// note (job 329): it is no note todo.
/// </summary>
internal static class NoteTodoParser
{
    /// <summary>The most characters a note todo's text keeps; a longer one is cut (never inside a surrogate pair).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>
    /// The open box of a note-local checkbox (and of a <see cref="TopicParser"/> topic line, which uses it); never a
    /// note todo.
    /// </summary>
    public const string OpenBox = "[ ]";

    /// <summary>
    /// The box of a note-local checkbox carried to the next meeting (job 330): <see cref="MeetingCarryOver"/> copies it
    /// as an open checkbox; never a note todo.
    /// </summary>
    public const string CarryBox = "[>]";

    /// <summary>The open box of a note todo; <see cref="NoteTodos.Complete"/> turns its first one into <see cref="DoneTodoBox"/>.</summary>
    public const string TodoBox = "{ }";

    /// <summary>The done box of a note todo.</summary>
    public const string DoneTodoBox = "{x}";

    private const string DuePrefix = "due:";
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>
    /// The open todos in <paramref name="markdown"/>, in line order. A line outside fenced code blocks
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) is one when it is optional spaces or tabs, <c>-</c>,
    /// <c>*</c> or <c>+</c>, one space, <c>{ }</c> (<see cref="TodoBox"/>), one space, then text. The first whitespace-delimited token
    /// <c>due:yyyy-MM-dd</c> (ASCII digits, a real date, not inside a code span) sets <see cref="ParsedNoteTodo.Due"/>
    /// and is removed from the text; the text is then trimmed and collapsed (<see cref="TagParser.Collapse"/>) and cut
    /// to <see cref="MaxTextLength"/>. An empty text is no todo, and a line <see cref="TopicParser"/> reads as a
    /// topic is no todo.
    /// </summary>
    public static IReadOnlyList<ParsedNoteTodo> Parse(string markdown) => Parse(markdown, TodoBox);

    /// <summary>
    /// The open note-local checkboxes (<c>- [ ] text</c>) in <paramref name="markdown"/>, in line order, by the same
    /// rules as <see cref="Parse(string)"/> with <see cref="OpenBox"/> in place of <see cref="TodoBox"/> (a topic line
    /// is none). Only the meeting carry-over reads them; they are never note todos.
    /// </summary>
    public static IReadOnlyList<ParsedNoteTodo> ParseCheckboxes(string markdown) => Parse(markdown, OpenBox);

    /// <summary>
    /// The checkboxes marked for the next meeting (<c>- [&gt;] text</c>, job 330) in <paramref name="markdown"/>, in
    /// line order, by the same rules as <see cref="ParseCheckboxes(string)"/> with <see cref="CarryBox"/> in place of
    /// <see cref="OpenBox"/>. Only the meeting carry-over reads them; they are never note todos.
    /// </summary>
    public static IReadOnlyList<ParsedNoteTodo> ParseCarried(string markdown) => Parse(markdown, CarryBox);

    /// <summary>The todo on the one <paramref name="line"/> (numbered <paramref name="number"/>), or <c>null</c>.</summary>
    internal static ParsedNoteTodo? ParseLine(int number, string line) => ParseLine(number, line, TodoBox);

    /// <summary>The open checkbox (<c>- [ ] text</c>) on the one <paramref name="line"/>, or <c>null</c>.</summary>
    internal static ParsedNoteTodo? ParseCheckboxLine(int number, string line) => ParseLine(number, line, OpenBox);

    private static List<ParsedNoteTodo> Parse(string markdown, string box)
    {
        List<ParsedNoteTodo> found = [];
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            ParsedNoteTodo? todo = ParseLine(number, line, box);
            if (todo is not null)
            {
                found.Add(todo);
            }
        }

        return found;
    }

    // The open line with "box" after the list marker on the one line, or null.
    private static ParsedNoteTodo? ParseLine(int number, string line, string box)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        // "- { } " and at least one more character.
        if (line.Length - i < 7
            || line[i] is not ('-' or '*' or '+')
            || line[i + 1] != ' '
            || string.CompareOrdinal(line, i + 2, box, 0, box.Length) != 0
            || line[i + 5] != ' ')
        {
            return null;
        }

        // A topic for the next 1:1 (job 253) written as an open task is a topic, not a todo.
        if (TopicParser.ParseLine(number, line) is not null)
        {
            return null;
        }

        int start = i + 6;
        DateOnly? due = null;
        int dueStart = -1;
        int dueEnd = -1;
        bool[]? inCode = CodeMask(line);
        int j = start;
        while (j < line.Length)
        {
            if (char.IsWhiteSpace(line[j]))
            {
                j++;
                continue;
            }

            int end = j;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            // A due token has no backtick, so it is either wholly inside a code span or wholly outside.
            if ((inCode is null || !inCode[j]) && TryParseDue(line.AsSpan(j, end - j), out DateOnly date))
            {
                due = date;
                dueStart = j;
                dueEnd = end;
                break;
            }

            j = end;
        }

        System.Text.StringBuilder builder = new();
        if (dueStart < 0)
        {
            TagParser.Collapse(line.AsSpan(start), builder, int.MaxValue, null);
        }
        else
        {
            string rest = string.Concat(line.AsSpan(start, dueStart - start), " ", line.AsSpan(dueEnd));
            TagParser.Collapse(rest.AsSpan(), builder, int.MaxValue, null);
        }

        if (builder.Length > MaxTextLength)
        {
            int cut = char.IsHighSurrogate(builder[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
            builder.Length = cut;
        }

        string text = builder.ToString().TrimEnd();
        return text.Length == 0 ? null : new ParsedNoteTodo(number, text, due, line);
    }

    // True at every index inside a code span (backticks included); null for a line without a backtick.
    private static bool[]? CodeMask(string line)
    {
        CodeSpans? spans = CodeSpans.For(line);
        if (spans is null)
        {
            return null;
        }

        bool[] mask = new bool[line.Length];
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] != '`')
            {
                i++;
                continue;
            }

            int after = spans.Skip(i);
            for (int k = i; k < after; k++)
            {
                mask[k] = true;
            }

            i = after;
        }

        return mask;
    }

    // Exactly "due:" + yyyy-MM-dd in ASCII digits, a real date.
    private static bool TryParseDue(ReadOnlySpan<char> token, out DateOnly date)
    {
        date = default;
        if (token.Length != DuePrefix.Length + DayFormat.Length || !token.StartsWith(DuePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> day = token[DuePrefix.Length..];
        for (int k = 0; k < day.Length; k++)
        {
            bool dash = k is 4 or 7;
            if (dash ? day[k] != '-' : !char.IsAsciiDigit(day[k]))
            {
                return false;
            }
        }

        return DateOnly.TryParseExact(day, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
