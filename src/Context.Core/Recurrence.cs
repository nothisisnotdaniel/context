using System.Globalization;

namespace Context;

/// <summary>How often a <see cref="RecurrenceRule"/> repeats.</summary>
internal enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// The steps one <see cref="IcsParser"/> call may spend on expanding rules. Once it is used up, the remaining
/// events show only their non-rule occurrences.
/// </summary>
internal sealed class RecurrenceBudget
{
    /// <summary>The steps (periods plus candidate days) one <c>Parse</c> call may spend in total.</summary>
    public const int StepsPerParse = 1_000_000;

    private int left;

    public RecurrenceBudget(int steps = StepsPerParse)
    {
        left = steps;
    }

    /// <summary>Takes <paramref name="steps"/> steps; false (and nothing taken) when not that many are left.</summary>
    public bool Take(int steps)
    {
        if (steps > left)
        {
            left = 0;
            return false;
        }

        left -= steps;
        return true;
    }
}

/// <summary>
/// The supported part of an RFC 5545 <c>RRULE</c>: <c>FREQ=DAILY|WEEKLY|MONTHLY|YEARLY</c> with <c>INTERVAL</c>,
/// <c>COUNT</c>, <c>UNTIL</c>, <c>BYDAY</c>, <c>BYMONTHDAY</c>, <c>BYMONTH</c> and <c>WKST</c>. Anything else
/// (another <c>FREQ</c>, <c>BYSETPOS</c>, <c>BYWEEKNO</c>, <c>BYYEARDAY</c>, <c>BYHOUR</c>, <c>BYMINUTE</c>,
/// <c>BYSECOND</c>, a bad value) makes <see cref="Parse"/> return <c>null</c>. Dates are worked out on day numbers,
/// so no step can overflow <see cref="DateTime"/>.
/// </summary>
internal sealed class RecurrenceRule
{
    /// <summary>The periods (days, weeks, months or years) one event may examine.</summary>
    public const int MaxPeriodsPerEvent = 50_000;

    private const int MaxYear = 9999;
    private const int MaxOrdinal = 53;
    private static readonly int MaxDayNumber = DateOnly.MaxValue.DayNumber;

    private readonly RecurrenceFrequency frequency;
    private readonly int interval;
    private readonly int? count;
    private readonly List<WeekdayNumber>? byDay;
    private readonly List<int>? byMonthDay;
    private readonly List<int>? byMonth;
    private readonly DayOfWeek weekStart;

    private RecurrenceRule(
        RecurrenceFrequency frequency,
        int interval,
        int? count,
        RecurrenceUntil? until,
        List<WeekdayNumber>? byDay,
        List<int>? byMonthDay,
        List<int>? byMonth,
        DayOfWeek weekStart)
    {
        this.frequency = frequency;
        this.interval = interval;
        this.count = count;
        Until = until;
        this.byDay = byDay;
        this.byMonthDay = byMonthDay;
        this.byMonth = byMonth;
        this.weekStart = weekStart;
    }

    /// <summary>The rule's <c>UNTIL</c>, or <c>null</c> when it has none.</summary>
    public RecurrenceUntil? Until { get; }

    /// <summary>Reads an <c>RRULE</c> value, or returns <c>null</c> when it is invalid or uses an unsupported part.</summary>
    public static RecurrenceRule? Parse(string value)
    {
        RecurrenceFrequency? frequency = null;
        int interval = 1;
        int? count = null;
        RecurrenceUntil? until = null;
        List<WeekdayNumber>? byDay = null;
        List<int>? byMonthDay = null;
        List<int>? byMonth = null;
        DayOfWeek weekStart = DayOfWeek.Monday;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawPart in value.Split(';'))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            int equals = part.IndexOf('=');
            if (equals <= 0)
            {
                return null;
            }

            string name = part[..equals].Trim().ToUpperInvariant();
            string partValue = part[(equals + 1)..].Trim();
            if (!seen.Add(name))
            {
                return null;
            }

            switch (name)
            {
                case "FREQ":
                    frequency = ReadFrequency(partValue);
                    if (frequency is null)
                    {
                        return null;
                    }

                    break;
                case "INTERVAL":
                    if (!TryPositive(partValue, out interval))
                    {
                        return null;
                    }

                    break;
                case "COUNT":
                    if (!TryPositive(partValue, out int countValue))
                    {
                        return null;
                    }

                    count = countValue;
                    break;
                case "UNTIL":
                    until = RecurrenceUntil.Read(partValue);
                    if (until is null)
                    {
                        return null;
                    }

                    break;
                case "BYDAY":
                    byDay = ReadList<WeekdayNumber>(partValue, WeekdayNumber.TryRead);
                    if (byDay is null)
                    {
                        return null;
                    }

                    break;
                case "BYMONTHDAY":
                    byMonthDay = ReadList<int>(partValue, TryMonthDay);
                    if (byMonthDay is null)
                    {
                        return null;
                    }

                    break;
                case "BYMONTH":
                    byMonth = ReadList<int>(partValue, TryMonth);
                    if (byMonth is null)
                    {
                        return null;
                    }

                    break;
                case "WKST":
                    DayOfWeek? start = ReadWeekday(partValue);
                    if (start is null)
                    {
                        return null;
                    }

                    weekStart = start.Value;
                    break;
                case "BYSETPOS":
                case "BYWEEKNO":
                case "BYYEARDAY":
                case "BYHOUR":
                case "BYMINUTE":
                case "BYSECOND":
                    return null;
                default:
                    // Extension parts (X-...) carry no meaning for the dates.
                    break;
            }
        }

        if (frequency is null || (count is not null && until is not null))
        {
            return null;
        }

        bool ordinals = byDay is not null && byDay.Any(d => d.Ordinal != 0);
        if (ordinals && frequency is RecurrenceFrequency.Daily or RecurrenceFrequency.Weekly)
        {
            return null;
        }

        if (byMonthDay is not null && frequency == RecurrenceFrequency.Weekly)
        {
            return null;
        }

        return new RecurrenceRule(
            frequency.Value, interval, count, until, byDay, byMonthDay, byMonth, weekStart);
    }

    /// <summary>
    /// The rule's occurrences after <paramref name="start"/> (the <c>DTSTART</c> wall-clock time, which is always
    /// the first occurrence and counts toward <c>COUNT</c>) whose day is in <c>[from, to]</c> and that are not after
    /// <paramref name="until"/> (inclusive, in the same wall clock), in order. Returns <c>null</c> when
    /// <paramref name="budget"/> runs out. Without <c>COUNT</c> it skips ahead to <paramref name="from"/>; it never
    /// examines more than <see cref="MaxPeriodsPerEvent"/> periods.
    /// </summary>
    public List<DateTime>? Occurrences(
        DateTime start, DateTime? until, DateOnly from, DateOnly to, RecurrenceBudget budget,
        Func<DateTime, bool>? firstEligible = null)
    {
        DateOnly first = DateOnly.FromDateTime(start);
        TimeOnly time = TimeOnly.FromDateTime(start);
        long period = 0;
        if (count is null)
        {
            period = Math.Max(0, (PeriodsBetween(first, from) / interval) - 1);
        }

        long untilDay = until is null ? long.MaxValue : DateOnly.FromDateTime(until.Value).DayNumber;
        int counted = 1;
        List<DateTime> result = [];
        List<int> candidates = [];
        for (int examined = 0; examined < MaxPeriodsPerEvent; examined++)
        {
            if (!budget.Take(1))
            {
                return null;
            }

            long offset = period * interval;
            if (!PeriodStart(first, offset, out long periodStart))
            {
                break;
            }

            if (periodStart > to.DayNumber || periodStart > untilDay)
            {
                break;
            }

            candidates.Clear();
            Candidates(first, offset, candidates);
            if (!budget.Take(candidates.Count))
            {
                return null;
            }

            candidates.Sort();
            int previous = -1;
            foreach (int dayNumber in candidates)
            {
                if (dayNumber == previous)
                {
                    continue;
                }

                previous = dayNumber;
                DateOnly date = DateOnly.FromDayNumber(dayNumber);
                DateTime at = date.ToDateTime(time);
                if (at <= start)
                {
                    continue;
                }

                if (date > to || (until is not null && at > until.Value))
                {
                    return result;
                }

                if (count is not null)
                {
                    counted++;
                    if (counted > count.Value)
                    {
                        return result;
                    }
                }

                if (date >= from && (firstEligible is null || firstEligible(at)))
                {
                    result.Add(at);
                    if (firstEligible is not null)
                    {
                        return result;
                    }
                }
            }

            period++;
        }

        return result;
    }

    /// <summary>How many whole periods lie between the period of <paramref name="first"/> and that of <paramref name="day"/>.</summary>
    private long PeriodsBetween(DateOnly first, DateOnly day)
    {
        long between = frequency switch
        {
            RecurrenceFrequency.Daily => (long)day.DayNumber - first.DayNumber,
            RecurrenceFrequency.Weekly => (WeekStart(day.DayNumber) - WeekStart(first.DayNumber)) / 7,
            RecurrenceFrequency.Monthly => MonthIndex(day) - MonthIndex(first),
            _ => (long)day.Year - first.Year,
        };
        return Math.Max(0, between);
    }

    /// <summary>The day number of the first day of the period <paramref name="offset"/> periods after the first one.</summary>
    private bool PeriodStart(DateOnly first, long offset, out long dayNumber)
    {
        dayNumber = 0;
        switch (frequency)
        {
            case RecurrenceFrequency.Daily:
                dayNumber = first.DayNumber + offset;
                return dayNumber <= MaxDayNumber;
            case RecurrenceFrequency.Weekly:
                dayNumber = WeekStart(first.DayNumber) + (7 * offset);
                return dayNumber <= MaxDayNumber;
            case RecurrenceFrequency.Monthly:
                long month = MonthIndex(first) + offset;
                if (month / 12 > MaxYear)
                {
                    return false;
                }

                dayNumber = new DateOnly((int)(month / 12), (int)(month % 12) + 1, 1).DayNumber;
                return true;
            default:
                long year = first.Year + offset;
                if (year > MaxYear)
                {
                    return false;
                }

                dayNumber = new DateOnly((int)year, 1, 1).DayNumber;
                return true;
        }
    }

    /// <summary>Adds the day numbers the rule produces in the period <paramref name="offset"/> periods after the first.</summary>
    private void Candidates(DateOnly first, long offset, List<int> into)
    {
        switch (frequency)
        {
            case RecurrenceFrequency.Daily:
                DateOnly day = DateOnly.FromDayNumber((int)(first.DayNumber + offset));
                if (InMonths(day.Month) && MatchesMonthDay(day) && MatchesWeekday(day.DayOfWeek))
                {
                    into.Add(day.DayNumber);
                }

                break;
            case RecurrenceFrequency.Weekly:
                long weekStartDay = WeekStart(first.DayNumber) + (7 * offset);
                if (byDay is null)
                {
                    AddWeekday(weekStartDay, first.DayOfWeek, into);
                }
                else
                {
                    foreach (WeekdayNumber weekday in byDay)
                    {
                        AddWeekday(weekStartDay, weekday.Day, into);
                    }
                }

                break;
            case RecurrenceFrequency.Monthly:
                long month = MonthIndex(first) + offset;
                int monthYear = (int)(month / 12);
                int monthOfYear = (int)(month % 12) + 1;
                if (InMonths(monthOfYear))
                {
                    MonthDays(first, monthYear, monthOfYear, into);
                }

                break;
            default:
                int year = (int)(first.Year + offset);
                if (byDay is not null && byMonthDay is null && byMonth is null)
                {
                    int yearStart = new DateOnly(year, 1, 1).DayNumber;
                    int yearEnd = new DateOnly(year, 12, 31).DayNumber;
                    foreach (WeekdayNumber weekday in byDay)
                    {
                        AddWeekdays(yearStart, yearEnd, weekday, into);
                    }

                    break;
                }

                if (byMonth is not null)
                {
                    foreach (int m in byMonth)
                    {
                        MonthDays(first, year, m, into);
                    }
                }
                else if (byMonthDay is not null)
                {
                    for (int m = 1; m <= 12; m++)
                    {
                        MonthDays(first, year, m, into);
                    }
                }
                else
                {
                    MonthDays(first, year, first.Month, into);
                }

                break;
        }
    }

    private void AddWeekday(long weekStartDay, DayOfWeek day, List<int> into)
    {
        long dayNumber = weekStartDay + (((int)day - (int)weekStart + 7) % 7);
        if (dayNumber < 0 || dayNumber > MaxDayNumber)
        {
            return;
        }

        DateOnly date = DateOnly.FromDayNumber((int)dayNumber);
        if (InMonths(date.Month))
        {
            into.Add((int)dayNumber);
        }
    }

    /// <summary>The days of one month: <c>BYDAY</c> (ordinals within the month) and/or <c>BYMONTHDAY</c>, else DTSTART's day.</summary>
    private void MonthDays(DateOnly first, int year, int month, List<int> into)
    {
        int days = DateTime.DaysInMonth(year, month);
        int monthStart = new DateOnly(year, month, 1).DayNumber;
        if (byDay is null && byMonthDay is null)
        {
            if (first.Day <= days)
            {
                into.Add(monthStart + first.Day - 1);
            }

            return;
        }

        if (byDay is not null)
        {
            List<int> weekdays = [];
            foreach (WeekdayNumber weekday in byDay)
            {
                AddWeekdays(monthStart, monthStart + days - 1, weekday, weekdays);
            }

            foreach (int dayNumber in weekdays)
            {
                if (MatchesMonthDay(DateOnly.FromDayNumber(dayNumber)))
                {
                    into.Add(dayNumber);
                }
            }

            return;
        }

        foreach (int monthDay in byMonthDay!)
        {
            int day = monthDay > 0 ? monthDay : days + monthDay + 1;
            if (day >= 1 && day <= days)
            {
                into.Add(monthStart + day - 1);
            }
        }
    }

    /// <summary>Adds the <paramref name="weekday"/>s in <c>[start, end]</c>: all of them, or the n-th from the start or end.</summary>
    private static void AddWeekdays(int start, int end, WeekdayNumber weekday, List<int> into)
    {
        int firstMatch = start + (((int)weekday.Day - (int)DateOnly.FromDayNumber(start).DayOfWeek + 7) % 7);
        if (weekday.Ordinal == 0)
        {
            for (int dayNumber = firstMatch; dayNumber <= end; dayNumber += 7)
            {
                into.Add(dayNumber);
            }
        }
        else if (weekday.Ordinal > 0)
        {
            int dayNumber = firstMatch + (7 * (weekday.Ordinal - 1));
            if (dayNumber <= end)
            {
                into.Add(dayNumber);
            }
        }
        else
        {
            int lastMatch = end - (((int)DateOnly.FromDayNumber(end).DayOfWeek - (int)weekday.Day + 7) % 7);
            int dayNumber = lastMatch - (7 * (-weekday.Ordinal - 1));
            if (dayNumber >= start)
            {
                into.Add(dayNumber);
            }
        }
    }

    private bool InMonths(int month) => byMonth is null || byMonth.Contains(month);

    private bool MatchesWeekday(DayOfWeek day) => byDay is null || byDay.Any(d => d.Day == day);

    private bool MatchesMonthDay(DateOnly date)
    {
        if (byMonthDay is null)
        {
            return true;
        }

        int days = DateTime.DaysInMonth(date.Year, date.Month);
        foreach (int monthDay in byMonthDay)
        {
            if (monthDay == date.Day || days + monthDay + 1 == date.Day)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The day number of the <see cref="weekStart"/> on or before <paramref name="dayNumber"/>; may be negative.</summary>
    private long WeekStart(long dayNumber)
    {
        // Day number 0 (0001-01-01) is a Monday.
        int dayOfWeek = (int)(((dayNumber % 7) + 7 + (int)DayOfWeek.Monday) % 7);
        return dayNumber - ((dayOfWeek - (int)weekStart + 7) % 7);
    }

    private static long MonthIndex(DateOnly date) => ((long)date.Year * 12) + date.Month - 1;

    private static RecurrenceFrequency? ReadFrequency(string value) => value.ToUpperInvariant() switch
    {
        "DAILY" => RecurrenceFrequency.Daily,
        "WEEKLY" => RecurrenceFrequency.Weekly,
        "MONTHLY" => RecurrenceFrequency.Monthly,
        "YEARLY" => RecurrenceFrequency.Yearly,
        _ => null,
    };

    internal static DayOfWeek? ReadWeekday(string value) => value.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null,
    };

    private delegate bool TryRead<T>(string value, out T result);

    /// <summary>Reads a comma list (distinct values, file order), or <c>null</c> when an item is invalid or it is empty.</summary>
    private static List<T>? ReadList<T>(string value, TryRead<T> read)
    {
        List<T> result = [];
        HashSet<T> seen = [];
        foreach (string item in value.Split(','))
        {
            if (!read(item.Trim(), out T parsed))
            {
                return null;
            }

            if (seen.Add(parsed))
            {
                result.Add(parsed);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static bool TryPositive(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 1;

    private static bool TryMonthDay(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result)
        && result != 0 && result >= -31 && result <= 31;

    private static bool TryMonth(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 1 && result <= 12;

    /// <summary>One <c>BYDAY</c> item: a weekday with an optional ordinal (0 = none, <c>-1</c> = last).</summary>
    private readonly struct WeekdayNumber : IEquatable<WeekdayNumber>
    {
        public WeekdayNumber(int ordinal, DayOfWeek day)
        {
            Ordinal = ordinal;
            Day = day;
        }

        public int Ordinal { get; }

        public DayOfWeek Day { get; }

        public static bool TryRead(string value, out WeekdayNumber result)
        {
            result = default;
            if (value.Length < 2)
            {
                return false;
            }

            DayOfWeek? day = ReadWeekday(value[^2..]);
            if (day is null)
            {
                return false;
            }

            string number = value[..^2];
            int ordinal = 0;
            if (number.Length > 0)
            {
                if (number.Length > 3
                    || !int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal)
                    || ordinal == 0
                    || ordinal > MaxOrdinal
                    || ordinal < -MaxOrdinal)
                {
                    return false;
                }
            }

            result = new WeekdayNumber(ordinal, day.Value);
            return true;
        }

        public bool Equals(WeekdayNumber other) => Ordinal == other.Ordinal && Day == other.Day;

        public override bool Equals(object? obj) => obj is WeekdayNumber other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Ordinal, Day);
    }
}

/// <summary>An <c>UNTIL</c> value: a date, a floating date-time or a UTC (<c>…Z</c>) date-time.</summary>
internal sealed class RecurrenceUntil
{
    private RecurrenceUntil(DateTime value, bool date, bool utc)
    {
        Value = value;
        Date = date;
        Utc = utc;
    }

    /// <summary>The date (midnight) or date-time as written, kind unspecified.</summary>
    public DateTime Value { get; }

    /// <summary>True for a date value: the whole day is included.</summary>
    public bool Date { get; }

    /// <summary>True for a <c>…Z</c> value.</summary>
    public bool Utc { get; }

    public static RecurrenceUntil? Read(string value)
    {
        if (value.Length == 8)
        {
            if (DateTime.TryParseExact(
                value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return new RecurrenceUntil(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), true, false);
            }

            return null;
        }

        bool utc = value.EndsWith('Z') || value.EndsWith('z');
        string bare = utc ? value[..^1] : value;
        if (DateTime.TryParseExact(
            bare, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return new RecurrenceUntil(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), false, utc);
        }

        return null;
    }
}
