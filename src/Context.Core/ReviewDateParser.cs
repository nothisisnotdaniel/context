using System.Globalization;

namespace Context;

/// <summary>A <c>review:yyyy-MM-dd</c> line found by <see cref="ReviewDateParser.Parse"/>.</summary>
internal sealed record ParsedReview
{
    public ParsedReview(int line, DateOnly due, string raw)
    {
        Line = line;
        Due = due;
        Raw = raw;
    }

    /// <summary>Returns only the type name: the line is raw outside text, so formatting this record must never print it.</summary>
    public override string ToString() => nameof(ParsedReview);

    /// <summary>The 1-based number of the line in the note.</summary>
    public int Line { get; }

    /// <summary>The day the note should be checked again.</summary>
    public DateOnly Due { get; }

    /// <summary>The whole line as written (without its line end).</summary>
    public string Raw { get; }
}

/// <summary>
/// Finds the review date of a note (job 256): a line <c>review:yyyy-MM-dd</c> that says when the note should be checked
/// again. The note stays the only source.
/// </summary>
internal static class ReviewDateParser
{
    /// <summary>The line's keyword, matched ignoring ASCII case only.</summary>
    public const string Prefix = "review:";

    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>
    /// The first review line in <paramref name="markdown"/>, or <c>null</c>. A line outside fenced code blocks
    /// (<see cref="TagParser.NumberedLinesOutsideFences"/>) is one when it is optional spaces or tabs; optionally
    /// <c>-</c>, <c>*</c> or <c>+</c> and one space; <c>review:</c> (ASCII, ignoring ASCII case); optional spaces or
    /// tabs; <c>yyyy-MM-dd</c> in ASCII digits that is a real date; then only spaces or tabs. Later review lines don't
    /// count.
    /// </summary>
    public static ParsedReview? Parse(string markdown)
    {
        foreach ((int number, string line) in TagParser.NumberedLinesOutsideFences(markdown))
        {
            if (ParseLine(number, line) is { } review)
            {
                return review;
            }
        }

        return null;
    }

    /// <summary>The review date on the one <paramref name="line"/> (numbered <paramref name="number"/>), or <c>null</c>.</summary>
    internal static ParsedReview? ParseLine(int number, string line)
    {
        int date = DateStart(line);
        if (date < 0)
        {
            return null;
        }

        for (int k = date + DayFormat.Length; k < line.Length; k++)
        {
            if (line[k] is not (' ' or '\t'))
            {
                return null;
            }
        }

        return TryParseDay(line.AsSpan(date, DayFormat.Length), out DateOnly due)
            ? new ParsedReview(number, due, line)
            : null;
    }

    /// <summary>
    /// <paramref name="raw"/> with only its date replaced by <paramref name="day"/> (<c>yyyy-MM-dd</c>, any culture);
    /// the indentation, the marker, the keyword's spelling, the spaces and the trailing whitespace stay. <c>null</c>
    /// when <paramref name="raw"/> is no review line.
    /// </summary>
    public static string? SetDate(string raw, DateOnly day)
    {
        if (ParseLine(1, raw) is null)
        {
            return null;
        }

        int date = DateStart(raw);
        return string.Concat(
            raw.AsSpan(0, date),
            day.ToString(DayFormat, CultureInfo.InvariantCulture),
            raw.AsSpan(date + DayFormat.Length));
    }

    // The index of the date when the line starts like a review line and is long enough for one, else -1.
    private static int DateStart(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        if (i + 1 < line.Length && line[i] is '-' or '*' or '+' && line[i + 1] == ' ')
        {
            i += 2;
        }

        if (line.Length - i < Prefix.Length)
        {
            return -1;
        }

        for (int k = 0; k < Prefix.Length; k++)
        {
            char c = line[i + k];
            // ASCII only: OrdinalIgnoreCase would also take letters such as the long s (U+017F) for 's'.
            char lower = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            if (lower != Prefix[k])
            {
                return -1;
            }
        }

        i += Prefix.Length;
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        return line.Length - i >= DayFormat.Length ? i : -1;
    }

    // Exactly yyyy-MM-dd in ASCII digits, a real date.
    private static bool TryParseDay(ReadOnlySpan<char> day, out DateOnly date)
    {
        date = default;
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
