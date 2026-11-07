using System.Globalization;

namespace Context;

/// <summary>The input rules for a new todo, shared by <c>/todo</c> and the <c>todo add</c> command line.</summary>
internal static class TodoInput
{
    /// <summary>The longest todo text that is accepted.</summary>
    public const int MaxTextLength = 200;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Parses <c>yyyy-MM-dd</c> (invariant culture, exact), <c>today</c> or <c>tomorrow</c> (any case).
    /// Returns false for anything else.
    /// </summary>
    public static bool TryParseDate(string token, DateOnly today, out DateOnly date)
    {
        if (string.Equals(token, "today", StringComparison.OrdinalIgnoreCase))
        {
            date = today;
            return true;
        }

        if (string.Equals(token, "tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(1);
            return true;
        }

        return DateOnly.TryParseExact(token, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
