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

    /// <summary>
    /// Parses the unit after <c>every</c>: <c>day</c>, <c>month</c> or an English weekday name (<c>monday</c>..
    /// <c>sunday</c>), in any case; anything else (<c>week</c>, <c>mon</c>, <c>2</c>, empty) is rejected.
    /// <paramref name="firstDue"/> is today for <c>day</c> and <c>month</c> (a monthly repeat is anchored on today's
    /// day of month), and the first date on or after today on that weekday for a weekday.
    /// </summary>
    public static bool TryParseRepeat(string token, DateOnly today, out TodoRepeat repeat, out DateOnly firstDue)
    {
        repeat = TodoRepeat.Daily;
        firstDue = today;
        if (string.Equals(token, "day", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(token, "month", StringComparison.OrdinalIgnoreCase))
        {
            repeat = TodoRepeat.Monthly(today.Day);
            return true;
        }

        foreach (DayOfWeek weekday in Enum.GetValues<DayOfWeek>())
        {
            if (string.Equals(token, weekday.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                repeat = TodoRepeat.Weekly(weekday);
                firstDue = today.AddDays(((int)weekday - (int)today.DayOfWeek + 7) % 7);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The next due date of a recurring todo that was due <paramref name="due"/>: the first date after both
    /// <paramref name="due"/> and <paramref name="today"/> that fits <paramref name="repeat"/>, so an overdue todo
    /// never comes back with a past date. A monthly repeat on a day the month doesn't have falls on its last day.
    /// </summary>
    public static DateOnly NextDue(TodoRepeat repeat, DateOnly due, DateOnly today)
    {
        DateOnly after = due > today ? due : today;
        switch (repeat.Kind)
        {
            case TodoRepeatKind.Daily:
                return after.AddDays(1);
            case TodoRepeatKind.Weekly:
                int days = ((int)repeat.Weekday - (int)after.DayOfWeek + 7) % 7;
                return after.AddDays(days == 0 ? 7 : days);
            default:
                DateOnly month = new DateOnly(after.Year, after.Month, 1);
                while (true)
                {
                    DateOnly candidate = new DateOnly(
                        month.Year, month.Month, Math.Min(repeat.Day, DateTime.DaysInMonth(month.Year, month.Month)));
                    if (candidate > after)
                    {
                        return candidate;
                    }

                    month = month.AddMonths(1);
                }
        }
    }
}
