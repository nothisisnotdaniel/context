namespace Context.Tests;

public sealed class RecurrenceTests
{
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private static string Calendar(params string[] lines) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";

    private static string[] Vevent(params string[] lines) => ["BEGIN:VEVENT", .. lines, "END:VEVENT"];

    private static string Event(params string[] lines) => Calendar(Vevent(lines));

    /// <summary>A one-hour event at 09:00 (floating, so local) on <paramref name="start"/> with <paramref name="extra"/> lines.</summary>
    private static string Series(string start, params string[] extra) =>
        Event(["SUMMARY:Series", $"DTSTART:{start}T090000", $"DTEND:{start}T100000", .. extra]);

    private static IReadOnlyList<Appointment> On(string text, int year, int month, int day) =>
        IcsParser.Parse(text, Zone, new DateOnly(year, month, day));

    private static bool Shows(string text, int year, int month, int day) => On(text, year, month, day).Count > 0;

    private static void AssertDays(string text, int year, int month, int[] shown, int[] notShown)
    {
        foreach (int day in shown)
        {
            Assert.True(Shows(text, year, month, day), $"expected on {year}-{month}-{day}");
        }

        foreach (int day in notShown)
        {
            Assert.False(Shows(text, year, month, day), $"not expected on {year}-{month}-{day}");
        }
    }

    // AC-1
    [Fact]
    public void Daily_Interval_EveryOtherDay()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY;INTERVAL=2");

        AssertDays(text, 2026, 10, [1, 3, 5, 31], [2, 4, 30]);
        Assert.False(Shows(text, 2026, 9, 30));
    }

    // AC-1
    [Fact]
    public void Daily_OccurrenceHasStartTimeAndDuration()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY");

        Appointment a = Assert.Single(On(text, 2026, 10, 7));

        Assert.Equal(new Appointment("Series", null, new DateTime(2026, 10, 7, 9, 0, 0), new DateTime(2026, 10, 7, 10, 0, 0), false), a);
    }

    // AC-1
    [Fact]
    public void Weekly_DefaultsToDtstartWeekday()
    {
        // 2026-10-01 is a Thursday.
        string text = Series("20261001", "RRULE:FREQ=WEEKLY");

        AssertDays(text, 2026, 10, [1, 8, 15, 22, 29], [2, 7, 9, 14]);
    }

    // AC-1
    [Fact]
    public void Monthly_The31st_SkipsShortMonths()
    {
        string text = Series("20260131", "RRULE:FREQ=MONTHLY");

        Assert.True(Shows(text, 2026, 3, 31));
        Assert.True(Shows(text, 2026, 5, 31));
        Assert.False(Shows(text, 2026, 2, 28));
        Assert.False(Shows(text, 2026, 3, 1));
        Assert.False(Shows(text, 2026, 4, 30));
        Assert.False(Shows(text, 2026, 5, 1));
    }

    // AC-1
    [Fact]
    public void Monthly_Interval3()
    {
        string text = Series("20260115", "RRULE:FREQ=MONTHLY;INTERVAL=3");

        Assert.True(Shows(text, 2026, 4, 15));
        Assert.True(Shows(text, 2026, 10, 15));
        Assert.False(Shows(text, 2026, 2, 15));
        Assert.False(Shows(text, 2026, 5, 15));
    }

    // AC-1
    [Fact]
    public void Yearly_DtstartMonthAndDay_LeapDayOnlyInLeapYears()
    {
        string text = Series("20200229", "RRULE:FREQ=YEARLY");

        Assert.True(Shows(text, 2024, 2, 29));
        Assert.True(Shows(text, 2028, 2, 29));
        Assert.False(Shows(text, 2021, 2, 28));
        Assert.False(Shows(text, 2021, 3, 1));
    }

    // AC-1 (spec-review nit 1): DTSTART is an occurrence even when the rule wouldn't produce it.
    [Fact]
    public void DtstartOutsideRule_StillShows_RuleDaysToo()
    {
        // 2026-10-05 is a Monday; the rule says Tuesdays.
        string text = Series("20261005", "RRULE:FREQ=WEEKLY;BYDAY=TU");

        AssertDays(text, 2026, 10, [5, 6, 13, 20], [12, 7, 4]);
    }

    // AC-2
    [Fact]
    public void Count_LimitsOccurrences_DtstartIncluded()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY;COUNT=3");

        AssertDays(text, 2026, 10, [1, 2, 3], [4, 5]);
    }

    // AC-2
    [Fact]
    public void Count_ExcludedDatesStillCount()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY;COUNT=3", "EXDATE:20261002T090000");

        AssertDays(text, 2026, 10, [1, 3], [2, 4]);
    }

    // AC-2
    [Fact]
    public void Count_DtstartOutsideRule_CountsAsFirst()
    {
        string text = Series("20261005", "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=2");

        AssertDays(text, 2026, 10, [5, 6], [13]);
    }

    // AC-2
    [Fact]
    public void Until_Date_IsInclusive()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY;UNTIL=20261003");

        AssertDays(text, 2026, 10, [1, 2, 3], [4]);
    }

    // AC-2
    [Fact]
    public void Until_FloatingDateTime_IsInclusive()
    {
        AssertDays(Series("20261001", "RRULE:FREQ=DAILY;UNTIL=20261003T090000"), 2026, 10, [3], [4]);
        AssertDays(Series("20261001", "RRULE:FREQ=DAILY;UNTIL=20261003T085959"), 2026, 10, [2], [3]);
    }

    // AC-2
    [Fact]
    public void Until_Utc_ComparedInTheEventsZone()
    {
        // 10:00 Berlin (summer time) is 08:00 UTC.
        string at = Event(
            "DTSTART;TZID=Europe/Berlin:20261001T100000", "RRULE:FREQ=DAILY;UNTIL=20261003T080000Z");
        string before = Event(
            "DTSTART;TZID=Europe/Berlin:20261001T100000", "RRULE:FREQ=DAILY;UNTIL=20261003T075959Z");

        AssertDays(at, 2026, 10, [3], [4]);
        AssertDays(before, 2026, 10, [2], [3]);
    }

    // AC-2
    [Fact]
    public void NoCountNoUntil_NeverEnds()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY");

        Assert.True(Shows(text, 2036, 5, 5));
        Assert.True(Shows(text, 2126, 1, 1));
    }

    // AC-3
    [Fact]
    public void Weekly_ByDayList()
    {
        string text = Series("20261005", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR");

        AssertDays(text, 2026, 10, [5, 7, 9, 12, 14, 16], [6, 8, 10, 11, 13]);
    }

    // AC-3: the RFC 5545 example, where WKST changes which weeks count with INTERVAL=2.
    [Theory]
    [InlineData("MO", new[] { 5, 10, 19, 24 }, new[] { 17, 31, 26 })]
    [InlineData("SU", new[] { 5, 17, 19, 31 }, new[] { 10, 24 })]
    public void Weekly_WkstWithInterval2(string weekStart, int[] shown, int[] notShown)
    {
        string text = Series("19970805", $"RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=4;BYDAY=TU,SU;WKST={weekStart}");

        AssertDays(text, 1997, 8, shown, notShown);
        Assert.False(Shows(text, 1997, 9, 2));
    }

    // AC-3
    [Fact]
    public void Monthly_SecondTuesday()
    {
        string text = Series("20261013", "RRULE:FREQ=MONTHLY;BYDAY=2TU");

        AssertDays(text, 2026, 11, [10], [3, 17]);
        AssertDays(text, 2026, 12, [8], [1, 15]);
    }

    // AC-3
    [Fact]
    public void Monthly_LastFriday()
    {
        string text = Series("20261030", "RRULE:FREQ=MONTHLY;BYDAY=-1FR");

        AssertDays(text, 2026, 11, [27], [20, 6]);
        AssertDays(text, 2027, 1, [29], [22]);
    }

    // AC-3
    [Fact]
    public void Monthly_WeekdayWithoutOrdinal_EverySuchDay()
    {
        string text = Series("20261102", "RRULE:FREQ=MONTHLY;BYDAY=MO");

        AssertDays(text, 2026, 11, [2, 9, 16, 23, 30], [3, 10]);
    }

    // AC-3
    [Fact]
    public void Monthly_ByMonthDay_PositiveAndNegative()
    {
        string text = Series("20260101", "RRULE:FREQ=MONTHLY;BYMONTHDAY=1,15,-1");

        AssertDays(text, 2026, 2, [1, 15, 28], [2, 27]);
        AssertDays(text, 2026, 4, [1, 15, 30], [29]);
    }

    // AC-3
    [Fact]
    public void Monthly_ByMonth_Filters()
    {
        string text = Series("20260110", "RRULE:FREQ=MONTHLY;BYMONTH=1,6");

        Assert.True(Shows(text, 2026, 6, 10));
        Assert.True(Shows(text, 2027, 1, 10));
        Assert.False(Shows(text, 2026, 2, 10));
    }

    // AC-3
    [Fact]
    public void Yearly_ByMonthAndLastSunday()
    {
        // The last Sunday of March: 2026-03-29, 2027-03-28, 2028-03-26.
        string text = Series("20260329", "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU");

        AssertDays(text, 2027, 3, [28], [21, 29, 31]);
        AssertDays(text, 2028, 3, [26], [19, 28]);
    }

    // AC-3: without BYMONTH the ordinal counts within the year.
    [Fact]
    public void Yearly_TwentiethMonday()
    {
        string text = Series("19970519", "RRULE:FREQ=YEARLY;BYDAY=20MO");

        AssertDays(text, 1998, 5, [18], [11, 25]);
        AssertDays(text, 1999, 5, [17], [10, 24]);
    }

    // AC-3
    [Fact]
    public void Yearly_ByMonthAndMonthDay()
    {
        string text = Series("20260101", "RRULE:FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=4,-1");

        AssertDays(text, 2026, 7, [4, 31], [1, 5, 30]);
        Assert.False(Shows(text, 2026, 8, 4));
    }

    // AC-3
    [Fact]
    public void Daily_ByDay_IsAFilter()
    {
        string text = Series("20261005", "RRULE:FREQ=DAILY;BYDAY=MO,TU");

        AssertDays(text, 2026, 10, [5, 6, 12, 13], [7, 8, 9, 10, 11]);
    }

    // AC-4: the meeting stays at 10:00 Berlin time over the 2026-10-25 change.
    [Fact]
    public void Tzid_KeepsWallClock_AcrossDst_InThatZone()
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        string text = Event(
            "DTSTART;TZID=Europe/Berlin:20261020T100000",
            "DTEND;TZID=Europe/Berlin:20261020T110000",
            "RRULE:FREQ=WEEKLY");

        Appointment before = Assert.Single(IcsParser.Parse(text, berlin, new DateOnly(2026, 10, 20)));
        Appointment after = Assert.Single(IcsParser.Parse(text, berlin, new DateOnly(2026, 10, 27)));

        Assert.Equal(new DateTime(2026, 10, 20, 10, 0, 0), before.Start);
        Assert.Equal(new DateTime(2026, 10, 27, 10, 0, 0), after.Start);
        Assert.Equal(new DateTime(2026, 10, 27, 11, 0, 0), after.End);
    }

    // AC-4: seen from a fixed UTC+1 zone, the same meeting moves from 09:00 to 10:00.
    [Fact]
    public void Tzid_ConvertedToLocal_PerOccurrence()
    {
        string text = Event(
            "DTSTART;TZID=Europe/Berlin:20261020T100000",
            "DTEND;TZID=Europe/Berlin:20261020T110000",
            "RRULE:FREQ=WEEKLY");

        Appointment before = Assert.Single(On(text, 2026, 10, 20));
        Appointment after = Assert.Single(On(text, 2026, 10, 27));

        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0), before.Start);
        Assert.Equal(new DateTime(2026, 10, 27, 10, 0, 0), after.Start);
        Assert.Equal(TimeSpan.FromHours(1), after.End - after.Start);
    }

    // AC-4
    [Fact]
    public void Utc_KeepsUtcTime()
    {
        string text = Event("DTSTART:20261001T080000Z", "DTEND:20261001T083000Z", "RRULE:FREQ=WEEKLY");

        Appointment a = Assert.Single(On(text, 2026, 10, 29));

        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), a.Start);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 30, 0), a.End);
    }

    // AC-4
    [Fact]
    public void AllDay_StaysAllDay()
    {
        string text = Event(
            "SUMMARY:Gym", "DTSTART;VALUE=DATE:20261005", "DTEND;VALUE=DATE:20261006", "RRULE:FREQ=WEEKLY");

        Appointment a = Assert.Single(On(text, 2026, 10, 12));

        Assert.Equal(new Appointment("Gym", null, new DateTime(2026, 10, 12), new DateTime(2026, 10, 13), true), a);
        Assert.False(Shows(text, 2026, 10, 13));
    }

    // AC-4: the no-DTEND rules carry over (all-day: one day).
    [Fact]
    public void AllDay_NoDtend_OneDayEach()
    {
        string text = Event("DTSTART;VALUE=DATE:20261005", "RRULE:FREQ=DAILY;INTERVAL=7");

        Appointment a = Assert.Single(On(text, 2026, 10, 19));

        Assert.Equal(new DateTime(2026, 10, 20), a.End);
    }

    // AC-5
    [Fact]
    public void Rdate_ListsAndLines_AddOccurrencesWithDuration()
    {
        string text = Series(
            "20261001",
            "RDATE;TZID=Europe/Berlin:20261010T140000,20261011T150000",
            "RDATE:20261020T080000Z");

        Appointment tenth = Assert.Single(On(text, 2026, 10, 10));
        Appointment eleventh = Assert.Single(On(text, 2026, 10, 11));
        Appointment twentieth = Assert.Single(On(text, 2026, 10, 20));

        Assert.Equal(new DateTime(2026, 10, 10, 13, 0, 0), tenth.Start);
        Assert.Equal(new DateTime(2026, 10, 10, 14, 0, 0), tenth.End);
        Assert.Equal(new DateTime(2026, 10, 11, 14, 0, 0), eleventh.Start);
        Assert.Equal(new DateTime(2026, 10, 20, 9, 0, 0), twentieth.Start);
        Assert.True(Shows(text, 2026, 10, 1));
        Assert.False(Shows(text, 2026, 10, 2));
    }

    // AC-5
    [Fact]
    public void Rdate_DateValue_AllDayAndTimed()
    {
        string allDay = Event("DTSTART;VALUE=DATE:20261001", "RDATE;VALUE=DATE:20261015,20261016");
        string timed = Series("20261001", "RDATE;VALUE=DATE:20261015");

        Assert.True(Assert.Single(On(allDay, 2026, 10, 15)).AllDay);
        Assert.True(Shows(allDay, 2026, 10, 16));
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), Assert.Single(On(timed, 2026, 10, 15)).Start);
    }

    // AC-5
    [Fact]
    public void Rdate_SameAsRuleOccurrence_ShowsOnce()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY", "RDATE:20261005T090000", "RDATE:20261001T090000");

        Assert.Single(On(text, 2026, 10, 5));
        Assert.Single(On(text, 2026, 10, 1));
    }

    // AC-5
    [Fact]
    public void Rdate_Period_Ignored()
    {
        string text = Series(
            "20261001", "RDATE;VALUE=PERIOD:20261005T090000Z/PT1H", "RDATE:20261006T090000Z/20261006T100000Z");

        Assert.False(Shows(text, 2026, 10, 5));
        Assert.False(Shows(text, 2026, 10, 6));
        Assert.True(Shows(text, 2026, 10, 1));
    }

    // AC-5
    [Fact]
    public void Exdate_TzidUtcAndDate_RemoveOccurrences()
    {
        // 09:00 local (UTC+1) is 10:00 in Berlin summer time and 08:00 UTC.
        string text = Series(
            "20261001",
            "RRULE:FREQ=DAILY",
            "EXDATE;TZID=Europe/Berlin:20261003T100000",
            "EXDATE:20261004T080000Z,20261005T090000",
            "EXDATE;VALUE=DATE:20261006");

        AssertDays(text, 2026, 10, [1, 2, 7], [3, 4, 5, 6]);
    }

    // AC-5
    [Fact]
    public void Exdate_OtherTime_RemovesNothing()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY", "EXDATE:20261003T100000");

        Assert.True(Shows(text, 2026, 10, 3));
    }

    // AC-5
    [Fact]
    public void Exdate_Dtstart_RemovesIt()
    {
        string text = Series("20261001", "RRULE:FREQ=DAILY", "EXDATE:20261001T090000");

        AssertDays(text, 2026, 10, [2], [1]);
    }

    private static string WeeklySeries(params string[] extra) => Calendar(Vevent(
    [
        "UID:abc@example.com",
        "SUMMARY:Weekly",
        "DTSTART;TZID=Europe/Berlin:20261006T100000",
        "DTEND;TZID=Europe/Berlin:20261006T110000",
        "RRULE:FREQ=WEEKLY",
        .. extra,
    ]));

    private static string WithOverride(string series, params string[] lines)
    {
        string end = "END:VCALENDAR\r\n";
        return series[..^end.Length] + string.Join("\r\n", Vevent(lines)) + "\r\n" + end;
    }

    // AC-6
    [Fact]
    public void Override_MovedSameDay_ReplacesTheSlot()
    {
        string text = WithOverride(
            WeeklySeries(),
            "UID:abc@example.com",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "SUMMARY:Weekly (moved)",
            "LOCATION:Room 2",
            "DTSTART;TZID=Europe/Berlin:20261013T150000",
            "DTEND;TZID=Europe/Berlin:20261013T153000");

        Appointment a = Assert.Single(On(text, 2026, 10, 13));

        Assert.Equal("Weekly (moved)", a.Title);
        Assert.Equal("Room 2", a.Location);
        Assert.Equal(new DateTime(2026, 10, 13, 14, 0, 0), a.Start);
        Assert.Equal(new DateTime(2026, 10, 13, 14, 30, 0), a.End);
        Assert.Equal("Weekly", Assert.Single(On(text, 2026, 10, 20)).Title);
    }

    // AC-6
    [Fact]
    public void Override_MovedToAnotherDay()
    {
        string text = WithOverride(
            WeeklySeries(),
            "UID:abc@example.com",
            "RECURRENCE-ID:20261013T080000Z",
            "SUMMARY:Moved",
            "DTSTART;TZID=Europe/Berlin:20261014T100000");

        Assert.False(Shows(text, 2026, 10, 13));
        Assert.Equal("Moved", Assert.Single(On(text, 2026, 10, 14)).Title);
    }

    // AC-6
    [Fact]
    public void Override_Cancelled_OnlyRemoves()
    {
        string text = WithOverride(
            WeeklySeries(),
            "UID:abc@example.com",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "STATUS:CANCELLED",
            "DTSTART;TZID=Europe/Berlin:20261013T100000");

        Assert.False(Shows(text, 2026, 10, 13));
        Assert.True(Shows(text, 2026, 10, 6));
        Assert.True(Shows(text, 2026, 10, 20));
    }

    // AC-6
    [Fact]
    public void Override_OtherUid_RemovesNothing()
    {
        string text = WithOverride(
            WeeklySeries(),
            "UID:other",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "SUMMARY:Other",
            "DTSTART;TZID=Europe/Berlin:20261013T150000");

        Assert.Equal(2, On(text, 2026, 10, 13).Count);
    }

    // AC-6
    [Fact]
    public void Series_Cancelled_ShowsNothing()
    {
        string text = WithOverride(
            WeeklySeries("STATUS:CANCELLED"),
            "UID:abc@example.com",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "DTSTART;TZID=Europe/Berlin:20261013T150000");

        Assert.False(Shows(text, 2026, 10, 6));
        Assert.False(Shows(text, 2026, 10, 13));
        Assert.False(Shows(text, 2026, 10, 20));
    }

    // AC-6
    [Fact]
    public void Override_WithoutSeries_ShowsAsSingleEvent()
    {
        string text = Event(
            "UID:lonely",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "SUMMARY:Lonely",
            "DTSTART;TZID=Europe/Berlin:20261013T150000");

        Assert.Equal("Lonely", Assert.Single(On(text, 2026, 10, 13)).Title);
        Assert.False(Shows(text, 2026, 10, 20));
    }

    // AC-8
    [Theory]
    [InlineData("RRULE:FREQ=HOURLY")]
    [InlineData("RRULE:FREQ=MINUTELY")]
    [InlineData("RRULE:FREQ=SECONDLY")]
    [InlineData("RRULE:FREQ=SOMETIMES")]
    [InlineData("RRULE:FREQ=DAILY;BYSETPOS=1")]
    [InlineData("RRULE:FREQ=DAILY;BYWEEKNO=2")]
    [InlineData("RRULE:FREQ=DAILY;BYYEARDAY=5")]
    [InlineData("RRULE:FREQ=DAILY;BYHOUR=9")]
    [InlineData("RRULE:FREQ=DAILY;BYMINUTE=0")]
    [InlineData("RRULE:FREQ=DAILY;BYSECOND=0")]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=0")]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=x")]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=99999999999999")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=0")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=-1")]
    [InlineData("RRULE:FREQ=DAILY;UNTIL=2026")]
    [InlineData("RRULE:FREQ=DAILY;UNTIL=20261399")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=5;UNTIL=20261231")]
    [InlineData("RRULE:FREQ=DAILY;FREQ=WEEKLY")]
    [InlineData("RRULE:FREQ=DAILY;BYDAY=1MO")]
    [InlineData("RRULE:FREQ=WEEKLY;BYMONTHDAY=3")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=99MO")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=32")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTH=13")]
    [InlineData("RRULE:FREQ=DAILY;WKST=XX")]
    [InlineData("RRULE:INTERVAL=2")]
    [InlineData("RRULE:")]
    [InlineData("RRULE:FREQ")]
    [InlineData("RRULE:=;=;;")]
    [InlineData("RRULE:FREQ=DAILY\r\nRRULE:FREQ=WEEKLY")]
    public void UnsupportedOrInvalidRule_DtstartAndRdatesOnly(string rule)
    {
        string text = Series(
            "20261001", rule, "RDATE:20261003T090000,20261004T090000", "EXDATE:20261004T090000");

        AssertDays(text, 2026, 10, [1, 3], [2, 4, 5]);
    }

    // AC-8: an invalid rule still lets overrides replace their slot.
    [Fact]
    public void UnsupportedRule_OverrideStillApplies()
    {
        string text = WithOverride(
            WeeklySeries("RRULE:FREQ=HOURLY"),
            "UID:abc@example.com",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261006T100000",
            "SUMMARY:Moved",
            "DTSTART;TZID=Europe/Berlin:20261006T150000");

        Assert.Equal("Moved", Assert.Single(On(text, 2026, 10, 6)).Title);
        Assert.False(Shows(text, 2026, 10, 13));
    }

    // AC-8, AC-9: the ends of the calendar never throw.
    [Theory]
    [InlineData("DTSTART:99991231T230000Z", "RRULE:FREQ=DAILY")]
    [InlineData("DTSTART;TZID=Asia/Tokyo:99991231T230000", "RRULE:FREQ=YEARLY")]
    [InlineData("DTSTART;VALUE=DATE:99991231", "RRULE:FREQ=WEEKLY")]
    [InlineData("DTSTART:99991230T120000", "RRULE:FREQ=MONTHLY;BYDAY=-1FR")]
    [InlineData("DTSTART:00010101T000000Z", "RRULE:FREQ=WEEKLY;WKST=SU;BYDAY=SU,SA")]
    [InlineData("DTSTART;TZID=America/Los_Angeles:00010101T000000", "RRULE:FREQ=DAILY")]
    [InlineData("DTSTART:00010101T000000", "RRULE:FREQ=YEARLY;INTERVAL=2147483647")]
    [InlineData("DTSTART:00010101T000000", "RRULE:FREQ=DAILY;UNTIL=99991231T235959Z")]
    public void EndsOfTheCalendar_NeverThrow(string start, string rule)
    {
        string text = Event(start, "DTEND:99991231T235959", rule, "EXDATE:99991231T235959Z", "RDATE:00010101T000000Z");

        foreach (DateOnly day in new[] { DateOnly.MinValue, DateOnly.MinValue.AddDays(1), new DateOnly(2026, 10, 29), DateOnly.MaxValue.AddDays(-1), DateOnly.MaxValue })
        {
            _ = IcsParser.Parse(text, Zone, day);
        }
    }

    // AC-9: without COUNT, a series from long ago skips ahead to the day.
    [Fact]
    public void LongSeriesWithoutCount_SkipsAhead()
    {
        Assert.True(Shows(Series("00010101", "RRULE:FREQ=DAILY"), 2026, 10, 29));
        string weekly = Series("00010103", "RRULE:FREQ=WEEKLY;INTERVAL=3");
        DateOnly target = new DateOnly(1, 1, 3).AddDays(21 * 40_000);

        Assert.NotEmpty(IcsParser.Parse(weekly, Zone, target));
        Assert.Empty(IcsParser.Parse(weekly, Zone, target.AddDays(7)));
        Assert.Empty(IcsParser.Parse(weekly, Zone, target.AddDays(-1)));
    }

    // AC-9: with COUNT the series is stepped from DTSTART, at most MaxPeriodsPerEvent periods.
    [Fact]
    public void CountedSeries_StopsAfterMaxPeriods()
    {
        Assert.True(RecurrenceRule.MaxPeriodsPerEvent < 82_000);
        string far = Series("18000101", "RRULE:FREQ=DAILY;COUNT=1000000");
        string near = Series("19000101", "RRULE:FREQ=DAILY;COUNT=1000000");

        Assert.False(Shows(far, 2026, 10, 29));
        Assert.True(Shows(far, 1800, 1, 2));
        Assert.True(Shows(near, 2026, 10, 29));
    }

    // AC-9: once the parse budget is used up, later events show only DTSTART and RDATEs.
    [Fact]
    public void BudgetUsedUp_LaterEventsShowOnlyNonRuleOccurrences()
    {
        int hostile = (RecurrenceBudget.StepsPerParse / RecurrenceRule.MaxPeriodsPerEvent) + 2;
        List<string> lines = [];
        for (int i = 0; i < hostile; i++)
        {
            lines.AddRange(Vevent("DTSTART:00010101T090000", "RRULE:FREQ=DAILY;COUNT=1000000;BYMONTH=2;BYMONTHDAY=30"));
        }

        lines.AddRange(Vevent(
            "SUMMARY:Late", "DTSTART:20261001T090000", "RRULE:FREQ=DAILY", "RDATE:20261020T120000"));
        string text = Calendar([.. lines]);
        string alone = Calendar(Vevent(
            "SUMMARY:Late", "DTSTART:20261001T090000", "RRULE:FREQ=DAILY", "RDATE:20261020T120000"));

        Assert.True(Shows(text, 2026, 10, 1));
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0), Assert.Single(On(text, 2026, 10, 20)).Start);
        Assert.False(Shows(text, 2026, 10, 21));
        Assert.Equal(2, On(alone, 2026, 10, 20).Count);
        Assert.True(Shows(alone, 2026, 10, 21));
    }

    // AC-10: the one-argument form still gives one appointment per VEVENT, its DTSTART.
    [Fact]
    public void ParseWithoutDay_Unchanged_FirstOccurrenceOnly()
    {
        string text = WithOverride(
            WeeklySeries("RDATE:20261020T090000Z", "EXDATE;TZID=Europe/Berlin:20261006T100000"),
            "UID:abc@example.com",
            "RECURRENCE-ID;TZID=Europe/Berlin:20261013T100000",
            "SUMMARY:Moved",
            "DTSTART;TZID=Europe/Berlin:20261013T150000");

        IReadOnlyList<Appointment> all = IcsParser.Parse(text, Zone);

        Assert.Equal(["Weekly", "Moved"], all.Select(a => a.Title));
        Assert.Equal(new DateTime(2026, 10, 6, 9, 0, 0), all[0].Start);
    }

    // AC-10: overrides and occurrences come with the series' fields.
    [Fact]
    public void Occurrence_KeepsLocationAndAttendees()
    {
        string text = Event(
            "SUMMARY:Sync", "LOCATION:Room 1", "ATTENDEE;CN=Anna Schmidt:mailto:a@example.com",
            "DTSTART:20261001T090000", "RRULE:FREQ=DAILY");

        Appointment a = Assert.Single(On(text, 2026, 10, 5));

        Assert.Equal(new Appointment("Sync", "Room 1", new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 9, 0, 0), false, ["Anna Schmidt"]), a);
    }
}
