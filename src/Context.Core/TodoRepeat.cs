using System.Globalization;

namespace Context;

/// <summary>How a recurring todo repeats.</summary>
internal enum TodoRepeatKind
{
    /// <summary>Every day.</summary>
    Daily,

    /// <summary>Every week on <see cref="TodoRepeat.Weekday"/>.</summary>
    Weekly,

    /// <summary>Every month on <see cref="TodoRepeat.Day"/>, or the month's last day when it is shorter.</summary>
    Monthly,
}

/// <summary>
/// The rule a recurring todo follows: every day, every week on one weekday, or every month on one day (1..31). It is
/// stored in <c>todos.json</c> as the <c>repeat</c> string <c>day</c>, <c>monday</c>..<c>sunday</c> or
/// <c>month-1</c>..<c>month-31</c> (lower case, exactly; see <see cref="TryParseStored"/>).
/// </summary>
internal sealed record TodoRepeat
{
    private TodoRepeat(TodoRepeatKind kind, DayOfWeek weekday, int day)
    {
        Kind = kind;
        Weekday = weekday;
        Day = day;
    }

    /// <summary>Every day.</summary>
    public static TodoRepeat Daily { get; } = new TodoRepeat(TodoRepeatKind.Daily, DayOfWeek.Sunday, 0);

    public TodoRepeatKind Kind { get; }

    /// <summary>The weekday of a <see cref="TodoRepeatKind.Weekly"/> repeat; <see cref="DayOfWeek.Sunday"/> otherwise.</summary>
    public DayOfWeek Weekday { get; }

    /// <summary>The day of month (1..31) of a <see cref="TodoRepeatKind.Monthly"/> repeat; 0 otherwise.</summary>
    public int Day { get; }

    /// <summary>The stored form: <c>day</c>, <c>monday</c>..<c>sunday</c> or <c>month-N</c>.</summary>
    public string Stored => Kind switch
    {
        TodoRepeatKind.Daily => "day",
        TodoRepeatKind.Weekly => Weekday.ToString().ToLowerInvariant(),
        _ => "month-" + Day.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The text shown next to the todo: <c>every day</c>, <c>every month</c> or <c>every Monday</c> (English).</summary>
    public string Label => Kind switch
    {
        TodoRepeatKind.Daily => "every day",
        TodoRepeatKind.Weekly => "every " + Weekday.ToString(),
        _ => "every month",
    };

    /// <summary>Every week on <paramref name="weekday"/>.</summary>
    public static TodoRepeat Weekly(DayOfWeek weekday)
    {
        if (weekday < DayOfWeek.Sunday || weekday > DayOfWeek.Saturday)
        {
            throw new ArgumentOutOfRangeException(nameof(weekday));
        }

        return new TodoRepeat(TodoRepeatKind.Weekly, weekday, 0);
    }

    /// <summary>Every month on day <paramref name="day"/> (1..31).</summary>
    public static TodoRepeat Monthly(int day)
    {
        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day));
        }

        return new TodoRepeat(TodoRepeatKind.Monthly, DayOfWeek.Sunday, day);
    }

    /// <summary>
    /// Reads the stored form. Only the exact lower-case strings are accepted (<c>"Monday"</c>, <c>"week"</c>,
    /// <c>"month-0"</c>, <c>"month-01"</c> and <c>"month-32"</c> are not), so a file only ever holds what this build
    /// writes.
    /// </summary>
    public static bool TryParseStored(string value, out TodoRepeat repeat)
    {
        repeat = Daily;
        if (value == "day")
        {
            return true;
        }

        foreach (DayOfWeek weekday in Enum.GetValues<DayOfWeek>())
        {
            if (value == weekday.ToString().ToLowerInvariant())
            {
                repeat = Weekly(weekday);
                return true;
            }
        }

        const string Prefix = "month-";
        if (value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            string digits = value[Prefix.Length..];
            if (digits.Length is >= 1 and <= 2 && digits[0] != '0' && digits.All(char.IsAsciiDigit))
            {
                int day = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
                if (day <= 31)
                {
                    repeat = Monthly(day);
                    return true;
                }
            }
        }

        return false;
    }
}
