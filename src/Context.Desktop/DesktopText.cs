using System.Globalization;

namespace Context;

/// <summary>Text helpers for the desktop components.</summary>
internal static class DesktopText
{
    /// <summary>The date format of every date the desktop shows.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Outside text made safe for one line: <see cref="SafeText.Clean"/>, then every <c>\n</c> becomes a space.
    /// Blazor still encodes the result when it renders it; this only removes control characters and line breaks.
    /// </summary>
    public static string OneLine(string raw) => SafeText.Clean(raw).Replace('\n', ' ');

    /// <summary>
    /// A todo's due date as typed in the palette, like the console's <c>/todo</c>: <c>today</c>, <c>tomorrow</c>
    /// (ignoring case, from <paramref name="today"/>) or <c>yyyy-MM-dd</c>.
    /// </summary>
    public static bool TryParseDue(string token, DateOnly today, out DateOnly due)
    {
        if (string.Equals(token, "today", StringComparison.OrdinalIgnoreCase))
        {
            due = today;
            return true;
        }

        if (string.Equals(token, "tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            due = today.AddDays(1);
            return true;
        }

        return DateOnly.TryParseExact(token, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out due);
    }
}
