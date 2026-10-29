namespace Context.Tests;

public sealed class IcsParserTests
{
    // UTC+1 with no clock changes, so tests never depend on the machine's zone.
    internal static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+01", TimeSpan.FromHours(1), "Test+01", "Test+01");

    private static string Calendar(params string[] lines) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";

    private static string Event(params string[] lines) =>
        Calendar(["BEGIN:VEVENT", .. lines, "END:VEVENT"]);

    private static Appointment Single(string text) => Assert.Single(IcsParser.Parse(text, Zone));

    // AC-1
    [Fact]
    public void Parse_Utc_ConvertedToLocal()
    {
        Appointment a = Single(Event("SUMMARY:Standup", "DTSTART:20261029T080000Z", "DTEND:20261029T083000Z"));

        Assert.Equal("Standup", a.Title);
        Assert.Null(a.Location);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), a.Start);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 30, 0), a.End);
        Assert.False(a.AllDay);
    }

    // AC-1
    [Fact]
    public void Parse_Tzid_ConvertedFromThatZone()
    {
        Appointment a = Single(Event(
            "SUMMARY:x", "DTSTART;TZID=UTC:20261029T100000", "DTEND;TZID=\"UTC\":20261029T110000"));

        Assert.Equal(new DateTime(2026, 10, 29, 11, 0, 0), a.Start);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 0, 0), a.End);
    }

    // AC-1
    [Fact]
    public void Parse_Tzid_IanaZone()
    {
        // Tokyo is UTC+9 all year: 18:00 there is 09:00 UTC, 10:00 in Zone.
        Appointment a = Single(Event("DTSTART;TZID=Asia/Tokyo:20261029T180000"));

        Assert.Equal(new DateTime(2026, 10, 29, 10, 0, 0), a.Start);
    }

    // AC-1
    [Fact]
    public void Parse_UnknownTzid_TreatedAsLocal()
    {
        Appointment a = Single(Event("DTSTART;TZID=Nowhere/Atlantis:20261029T100000"));

        Assert.Equal(new DateTime(2026, 10, 29, 10, 0, 0), a.Start);
    }

    // AC-1
    [Fact]
    public void Parse_Floating_IsLocal_AndMissingEndEqualsStart()
    {
        Appointment a = Single(Event("DTSTART:20261029T143000"));

        Assert.Equal(new DateTime(2026, 10, 29, 14, 30, 0), a.Start);
        Assert.Equal(a.Start, a.End);
        Assert.False(a.AllDay);
    }

    // AC-1
    [Fact]
    public void Parse_AllDay_EndExclusive_MissingEndIsOneDay()
    {
        Appointment one = Single(Event("DTSTART;VALUE=DATE:20261029"));
        Appointment many = Single(Event("DTSTART;VALUE=DATE:20261028", "DTEND;VALUE=DATE:20261031"));

        Assert.True(one.AllDay);
        Assert.Equal(new DateTime(2026, 10, 29), one.Start);
        Assert.Equal(new DateTime(2026, 10, 30), one.End);
        Assert.True(many.AllDay);
        Assert.Equal(new DateTime(2026, 10, 28), many.Start);
        Assert.Equal(new DateTime(2026, 10, 31), many.End);
    }

    // AC-1: folded lines, property names ignoring case, escapes.
    [Fact]
    public void Parse_UnfoldsLines_IgnoresCase_Unescapes()
    {
        string text = "begin:vcalendar\nBEGIN:VEVENT\nsummary:Long\r\n  title\\, part\n\ttwo\nLocation:Room 1\\;2\\n" +
            "A\\\\B\\Nc\\x\nDtStart:20261029T100000\nEND:VEVENT\nEND:VCALENDAR";

        Appointment a = Single(text);

        Assert.Equal("Long title, parttwo", a.Title);
        Assert.Equal("Room 1;2\nA\\B\nc\\x", a.Location);
    }

    // AC-1
    [Fact]
    public void Parse_MissingSummary_IsEmptyTitle()
    {
        Assert.Equal(string.Empty, Single(Event("DTSTART:20261029T100000")).Title);
    }

    // AC-1: nested VALARM properties and unknown properties are ignored.
    [Fact]
    public void Parse_IgnoresNestedComponents_AndOtherProperties()
    {
        Appointment a = Single(Event(
            "SUMMARY:Outer",
            "RRULE:FREQ=DAILY",
            "X-THING;A=\"b:c;d\":e",
            "BEGIN:VALARM",
            "SUMMARY:Alarm",
            "DTSTART:20200101T000000",
            "END:VALARM",
            "DTSTART:20261029T100000"));

        Assert.Equal("Outer", a.Title);
        Assert.Equal(new DateTime(2026, 10, 29, 10, 0, 0), a.Start);
    }

    // AC-2: cancelled, missing or bad DTSTART, end before start are skipped.
    [Fact]
    public void Parse_SkipsInvalidEvents()
    {
        string text = Calendar(
            "BEGIN:VEVENT", "SUMMARY:cancelled", "STATUS:CANCELLED", "DTSTART:20261029T100000", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:no start", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:bad start", "DTSTART:2026-10-29", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:backwards", "DTSTART:20261029T100000", "DTEND:20261029T090000", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:ok", "STATUS:CONFIRMED", "DTSTART:20261029T100000", "END:VEVENT");

        IReadOnlyList<Appointment> all = IcsParser.Parse(text, Zone);

        Assert.Equal("ok", Assert.Single(all).Title);
    }

    // AC-5
    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("BEGIN:VEVENT\nDTSTART:20261029T100000\nEND:VEVENT")]
    public void Parse_NoCalendar_Throws(string text)
    {
        Assert.Throws<FormatException>(() => IcsParser.Parse(text, Zone));
    }

    // Security: a long hostile line is scanned linearly.
    [Fact]
    public void Parse_LongLine_IsFast()
    {
        string hostile = Event(
            "SUMMARY;" + new string('"', 200_000) + new string('\\', 200_000),
            "SUMMARY:" + new string('\\', 200_000),
            "DTSTART:20261029T100000");

        IReadOnlyList<Appointment> all = IcsParser.Parse(hostile, Zone);

        Assert.Single(all);
    }
}
