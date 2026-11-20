using System.Globalization;

namespace Context.Tests;

public sealed class TodoInputTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData("today", 2026, 11, 7)]
    [InlineData("TODAY", 2026, 11, 7)]
    [InlineData("Tomorrow", 2026, 11, 8)]
    [InlineData("2026-12-24", 2026, 12, 24)]
    [InlineData("2020-01-01", 2020, 1, 1)]
    public void TryParseDate_AcceptsDatesAndWords(string token, int year, int month, int day)
    {
        Assert.True(TodoInput.TryParseDate(token, Today, out DateOnly date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-01")]
    [InlineData("24.12.2026")]
    [InlineData("2026-1-2")]
    public void TryParseDate_RefusesAnythingElse(string token)
    {
        Assert.False(TodoInput.TryParseDate(token, Today, out _));
    }

    [Fact]
    public void MaxTextLength_Is200()
    {
        Assert.Equal(200, TodoInput.MaxTextLength);
    }

    // 168 AC-1: day, month and the English weekday names, in any case; the first due date follows the rule.
    [Theory]
    [InlineData("day", "day", 2026, 11, 7)]
    [InlineData("DAY", "day", 2026, 11, 7)]
    [InlineData("month", "month-7", 2026, 11, 7)]
    [InlineData("Month", "month-7", 2026, 11, 7)]
    [InlineData("saturday", "saturday", 2026, 11, 7)]
    [InlineData("Sunday", "sunday", 2026, 11, 8)]
    [InlineData("monday", "monday", 2026, 11, 9)]
    [InlineData("TUESDAY", "tuesday", 2026, 11, 10)]
    [InlineData("wednesday", "wednesday", 2026, 11, 11)]
    [InlineData("Thursday", "thursday", 2026, 11, 12)]
    [InlineData("friday", "friday", 2026, 11, 13)]
    public void TryParseRepeat_AcceptsUnits_FirstDue(string token, string stored, int year, int month, int day)
    {
        Assert.True(TodoInput.TryParseRepeat(token, Today, out TodoRepeat repeat, out DateOnly firstDue));
        Assert.Equal(stored, repeat.Stored);
        Assert.Equal(new DateOnly(year, month, day), firstDue);
    }

    // 168 AC-1: a monthly repeat is anchored on today's day of month.
    [Fact]
    public void TryParseRepeat_Month_AnchorsOnTodaysDay()
    {
        Assert.True(TodoInput.TryParseRepeat("month", new DateOnly(2026, 1, 31), out TodoRepeat repeat, out DateOnly firstDue));
        Assert.Equal(TodoRepeat.Monthly(31), repeat);
        Assert.Equal(new DateOnly(2026, 1, 31), firstDue);
    }

    // 168 AC-1
    [Theory]
    [InlineData("")]
    [InlineData("week")]
    [InlineData("mon")]
    [InlineData("2")]
    [InlineData("days")]
    [InlineData("monthly")]
    [InlineData(" day")]
    [InlineData("month-3")]
    [InlineData("montag")]
    public void TryParseRepeat_RefusesAnythingElse(string token)
    {
        Assert.False(TodoInput.TryParseRepeat(token, Today, out _, out _));
    }

    // 168 AC-2: the first date after both due and today that fits the rule; overdue todos never come back in the past.
    [Theory]
    [InlineData("day", "2026-11-07", "2026-11-07", "2026-11-08")]
    [InlineData("day", "2026-11-01", "2026-11-07", "2026-11-08")]
    [InlineData("day", "2026-11-20", "2026-11-07", "2026-11-21")]
    [InlineData("saturday", "2026-11-07", "2026-11-07", "2026-11-14")]
    [InlineData("monday", "2026-11-09", "2026-11-07", "2026-11-16")]
    [InlineData("monday", "2026-10-26", "2026-11-07", "2026-11-09")]
    [InlineData("sunday", "2026-10-25", "2026-11-08", "2026-11-15")]
    [InlineData("month-31", "2026-01-31", "2026-01-31", "2026-02-28")]
    [InlineData("month-31", "2026-02-28", "2026-02-28", "2026-03-31")]
    [InlineData("month-31", "2026-03-31", "2026-03-31", "2026-04-30")]
    [InlineData("month-31", "2028-01-31", "2028-01-31", "2028-02-29")]
    [InlineData("month-31", "2028-02-29", "2028-02-29", "2028-03-31")]
    [InlineData("month-30", "2027-01-30", "2027-01-30", "2027-02-28")]
    [InlineData("month-7", "2026-11-07", "2026-11-07", "2026-12-07")]
    [InlineData("month-7", "2026-08-07", "2026-11-07", "2026-12-07")]
    [InlineData("month-7", "2026-08-07", "2026-11-03", "2026-11-07")]
    [InlineData("month-15", "2026-12-15", "2026-12-15", "2027-01-15")]
    [InlineData("month-1", "2026-12-01", "2026-12-20", "2027-01-01")]
    public void NextDue_FitsTheRule_AfterDueAndToday(string stored, string due, string today, string expected)
    {
        Assert.True(TodoRepeat.TryParseStored(stored, out TodoRepeat repeat));

        DateOnly next = TodoInput.NextDue(repeat, DateOnly.Parse(due, CultureInfo.InvariantCulture), DateOnly.Parse(today, CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), next);
    }

    // 168 AC-2: month-31 over three months in a row, and a weekday repeat always lands on its weekday.
    [Fact]
    public void NextDue_ChainsAcrossMonths()
    {
        DateOnly due = new(2026, 1, 31);
        List<DateOnly> chain = [];
        for (int i = 0; i < 3; i++)
        {
            due = TodoInput.NextDue(TodoRepeat.Monthly(31), due, due);
            chain.Add(due);
        }

        Assert.Equal([new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)], chain);
        Assert.Equal(DayOfWeek.Wednesday, TodoInput.NextDue(TodoRepeat.Weekly(DayOfWeek.Wednesday), Today, Today).DayOfWeek);
    }

    // 168 AC-4: only the exact stored strings read back; everything else is a bad repeat.
    [Theory]
    [InlineData("day")]
    [InlineData("monday")]
    [InlineData("sunday")]
    [InlineData("month-1")]
    [InlineData("month-9")]
    [InlineData("month-31")]
    public void TryParseStored_RoundTrips(string stored)
    {
        Assert.True(TodoRepeat.TryParseStored(stored, out TodoRepeat repeat));
        Assert.Equal(stored, repeat.Stored);
    }

    // 168 AC-4
    [Theory]
    [InlineData("")]
    [InlineData("week")]
    [InlineData("Monday")]
    [InlineData("DAY")]
    [InlineData("month")]
    [InlineData("month-0")]
    [InlineData("month-01")]
    [InlineData("month-32")]
    [InlineData("month-")]
    [InlineData("month-1x")]
    [InlineData("month-\u0661")]
    public void TryParseStored_RefusesAnythingElse(string stored)
    {
        Assert.False(TodoRepeat.TryParseStored(stored, out _));
    }

    // 168 AC-8: the marker text, in English whatever the culture.
    [Fact]
    public void Label_IsEnglish()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("every day", TodoRepeat.Daily.Label);
            Assert.Equal("every month", TodoRepeat.Monthly(31).Label);
            Assert.Equal("every Monday", TodoRepeat.Weekly(DayOfWeek.Monday).Label);
            Assert.Equal("every Sunday", TodoRepeat.Weekly(DayOfWeek.Sunday).Label);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // 178: H:mm or HH:mm, 0:00 to 23:59, is a time.
    [Theory]
    [InlineData("14:00", 14, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("09:05", 9, 5)]
    [InlineData("0:00", 0, 0)]
    [InlineData("23:59", 23, 59)]
    public void TryParseTime_Valid(string token, int hour, int minute)
    {
        Assert.True(TodoInput.LooksLikeTime(token));
        Assert.True(TodoInput.TryParseTime(token, out TimeOnly time));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    // 178: time-shaped but out of range is a bad time, not text.
    [Theory]
    [InlineData("25:00")]
    [InlineData("24:00")]
    [InlineData("14:60")]
    [InlineData("99:99")]
    public void TryParseTime_OutOfRange_LooksLikeTimeButFails(string token)
    {
        Assert.True(TodoInput.LooksLikeTime(token));
        Assert.False(TodoInput.TryParseTime(token, out _));
    }

    // 178: anything else is neither a time nor time-shaped, so it stays text.
    [Theory]
    [InlineData("2pm")]
    [InlineData("14:00:00")]
    [InlineData("140:00")]
    [InlineData("14:0")]
    [InlineData(":00")]
    [InlineData("14h00")]
    [InlineData("1:2:")]
    [InlineData("\u0661\u0664:00")]
    [InlineData("")]
    [InlineData("Call")]
    public void TryParseTime_NotTimeShaped(string token)
    {
        Assert.False(TodoInput.LooksLikeTime(token));
        Assert.False(TodoInput.TryParseTime(token, out _));
    }
}
