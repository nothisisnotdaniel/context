using System.Globalization;

namespace Context;

/// <summary>
/// The date maths of the top bar's month calendar: a Monday-first grid of 6 weeks, ISO 8601 week numbers and the
/// 1900-01-01..2100-12-31 range a day can be picked in.
/// </summary>
internal static class MonthGrid
{
    /// <summary>The number of days the grid shows: always 6 weeks.</summary>
    public const int Days = 42;

    /// <summary>The first day that can be picked.</summary>
    public static readonly DateOnly First = new(1900, 1, 1);

    /// <summary>The last day that can be picked.</summary>
    public static readonly DateOnly Last = new(2100, 12, 31);

    /// <summary>The first of the month that day is in.</summary>
    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>The Monday on or before the 1st of the month shown for month (any day of it).</summary>
    public static DateOnly Start(DateOnly month)
    {
        DateOnly first = MonthOf(month);
        int back = ((int)first.DayOfWeek + 6) % 7;
        return first.AddDays(-back);
    }

    /// <summary>The ISO 8601 week number of day.</summary>
    public static int Week(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>True when day is inside 1900-01-01..2100-12-31.</summary>
    public static bool InRange(DateOnly day) => day >= First && day <= Last;

    /// <summary>day kept inside 1900-01-01..2100-12-31.</summary>
    public static DateOnly Clamp(DateOnly day) => day < First ? First : day > Last ? Last : day;

    /// <summary>
    /// day moved by months, keeping the day of month but clamping it to the target month's length (31 Jan + 1 month is
    /// 28 or 29 Feb), then kept inside the range.
    /// </summary>
    public static DateOnly AddMonths(DateOnly day, int months)
    {
        int index = (day.Year * 12) + (day.Month - 1) + months;
        int year = index / 12;
        int month = (index % 12) + 1;
        if (year < First.Year)
        {
            return First;
        }

        if (year > Last.Year)
        {
            return Last;
        }

        return new DateOnly(year, month, Math.Min(day.Day, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>day moved by days, kept inside the range.</summary>
    public static DateOnly AddDays(DateOnly day, int days) => Clamp(day.AddDays(days));

    /// <summary>The month as shown in the header and the grid's label, for example <c>November 2026</c>.</summary>
    public static string MonthLabel(DateOnly month) => month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>A day's full name for its button, for example <c>Wednesday, 18 November 2026</c>.</summary>
    public static string DayLabel(DateOnly day) => day.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
}
