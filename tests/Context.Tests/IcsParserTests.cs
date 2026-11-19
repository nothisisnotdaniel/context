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

    // 156 AC-5: each ATTENDEE's CN, quotes removed, unescaped and normalized like a mention; no CN or a bad name is
    // skipped, and the address is never read.
    [Fact]
    public void Parse_Attendees_CnOnly_Normalized()
    {
        Appointment a = Single(Event(
            "SUMMARY:Review",
            "DTSTART:20261029T080000Z",
            "ATTENDEE;ROLE=REQ-PARTICIPANT;CN=\"Schmidt\\, Anna\":mailto:anna@example.com",
            "ATTENDEE;CN=Bob:mailto:bob@example.com",
            "ATTENDEE:mailto:nocn@example.com",
            "ATTENDEE;CN=\"  Carl \t  Lee \";PARTSTAT=ACCEPTED:mailto:carl@example.com",
            "ATTENDEE;CN=\"a/b\":mailto:ab@example.com",
            "ATTENDEE;CN=\"\":mailto:empty@example.com",
            "ATTENDEE;CN=\"Dora:Lee\":mailto:dora@example.com",
            "ATTENDEE;CN=\"Long",
            "  name\":mailto:long@example.com"));

        Assert.Equal(["Schmidt, Anna", "Bob", "Carl Lee", "Long name"], a.Attendees);
        Assert.DoesNotContain(a.Attendees, n => n.Contains('@') || n.Contains("mailto", StringComparison.OrdinalIgnoreCase));
    }

    // 156 AC-5: an event without attendees, and a hand-built appointment, have none.
    [Fact]
    public void Parse_NoAttendees_Empty()
    {
        Assert.Empty(Single(Event("DTSTART:20261029T080000Z")).Attendees);
        Assert.Empty(new Appointment("x", null, DateTime.MinValue, DateTime.MinValue, false).Attendees);
    }

    // Job 227 AC-1: CLASS:PRIVATE / CONFIDENTIAL (any case, spaces, folded) are left out of both overloads.
    [Theory]
    [InlineData("CLASS:PRIVATE")]
    [InlineData("CLASS:CONFIDENTIAL")]
    [InlineData("CLASS:private")]
    [InlineData("class:Confidential")]
    [InlineData("CLASS:  PRIVATE  ")]
    [InlineData("CLASS:PRI\r\n VATE")]
    [InlineData("CLASS;X-PARAM=1:PRIVATE")]
    public void Parse_PrivateClass_LeftOut(string classLine)
    {
        string text = Calendar(
            "BEGIN:VEVENT", "SUMMARY:secret", classLine,
            "DTSTART:20261029T100000", "DTEND:20261029T110000", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:open", "DTSTART:20261029T120000", "END:VEVENT");

        Assert.Equal("open", Assert.Single(IcsParser.Parse(text, Zone)).Title);
        Assert.Equal("open", Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 29))).Title);
    }

    // Job 227 AC-1: a private CLASS on any of several CLASS lines hides the event.
    [Fact]
    public void Parse_PrivateOnSecondClassLine_LeftOut()
    {
        string text = Event("SUMMARY:secret", "CLASS:PUBLIC", "CLASS:PRIVATE", "DTSTART:20261029T100000");

        Assert.Empty(IcsParser.Parse(text, Zone));
        Assert.Empty(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 29)));
    }

    // Job 227 AC-1: PUBLIC, an unknown CLASS or none still show.
    [Theory]
    [InlineData("CLASS:PUBLIC")]
    [InlineData("CLASS:X-SECRETISH")]
    [InlineData("CLASS:PRIVATELY")]
    [InlineData("X-NOTHING:1")]
    public void Parse_OtherClass_Shown(string classLine)
    {
        string text = Event("SUMMARY:open", classLine, "DTSTART:20261029T100000");

        Assert.Equal("open", Single(text).Title);
        Assert.Equal("open", Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 29))).Title);
    }

    private static string WeeklyWith(string[] series, string[] overrideLines) => Calendar(
    [
        "BEGIN:VEVENT", "UID:w@example.com", "SUMMARY:Weekly", "DTSTART:20261006T100000", "DTEND:20261006T110000",
        "RRULE:FREQ=WEEKLY", .. series, "END:VEVENT",
        "BEGIN:VEVENT", "UID:w@example.com", "RECURRENCE-ID:20261013T100000", .. overrideLines, "END:VEVENT",
    ]);

    // Job 227 AC-2: a private series shows no occurrence and none of its overrides (even one without CLASS).
    [Fact]
    public void Parse_PrivateSeries_ShowsNothing()
    {
        string text = WeeklyWith(["CLASS:PRIVATE"], ["SUMMARY:Moved", "DTSTART:20261013T150000"]);

        foreach (int day in new[] { 6, 13, 20, 27 })
        {
            Assert.Empty(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, day)));
        }
    }

    // Job 227 AC-2: a private override of a public series removes only that occurrence.
    [Fact]
    public void Parse_PrivateOverride_RemovesItsOccurrence()
    {
        string text = WeeklyWith([], ["SUMMARY:Secret", "CLASS:CONFIDENTIAL", "DTSTART:20261013T150000"]);

        Assert.Empty(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 13)));
        Assert.Equal("Weekly", Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 6))).Title);
        Assert.Equal("Weekly", Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 20))).Title);
    }

    // Job 227 AC-3: a calendar with only private events still parses (no FormatException).
    [Fact]
    public void Parse_OnlyPrivateEvents_EmptyNotThrowing()
    {
        string text = Event("SUMMARY:secret", "CLASS:PRIVATE", "DTSTART:20261029T100000");

        Assert.Empty(IcsParser.Parse(text, Zone));
        Assert.Empty(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 29)));
    }

    // 287 AC-1: every occurrence and the override of a series get the event's UID; parsed appointments still equal
    // ones built without a Uid.
    [Fact]
    public void Parse_Uid_OnEveryOccurrenceAndOverride_NotPartOfEquals()
    {
        string text = Calendar(
            "BEGIN:VEVENT", "UID:abc", "SUMMARY:Weekly", "DTSTART:20261006T100000", "DTEND:20261006T110000",
            "RRULE:FREQ=WEEKLY", "END:VEVENT",
            "BEGIN:VEVENT", "UID:abc", "RECURRENCE-ID:20261013T100000", "SUMMARY:Moved", "DTSTART:20261013T140000",
            "DTEND:20261013T150000", "END:VEVENT",
            "BEGIN:VEVENT", "SUMMARY:One-off", "DTSTART:20261020T090000", "END:VEVENT");

        Appointment first = Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 6)));
        Appointment moved = Assert.Single(IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 13)));
        IReadOnlyList<Appointment> third = IcsParser.Parse(text, Zone, new DateOnly(2026, 10, 20));

        Assert.Equal("abc", first.Uid);
        Assert.Equal("abc", moved.Uid);
        Assert.Equal("Moved", moved.Title);
        Assert.Equal(["abc", null], third.Select(a => a.Uid));
        Assert.Null(third.Single(a => a.Title == "One-off").Uid);
        Assert.Equal(["abc", "abc", null], IcsParser.Parse(text, Zone).Select(a => a.Uid));
        Assert.Equal(
            new Appointment("Weekly", null, new DateTime(2026, 10, 6, 10, 0, 0), new DateTime(2026, 10, 6, 11, 0, 0), false),
            first);
        Assert.Equal(
            new Appointment("Weekly", null, new DateTime(2026, 10, 6, 10, 0, 0), new DateTime(2026, 10, 6, 11, 0, 0), false).GetHashCode(),
            first.GetHashCode());
    }
}
