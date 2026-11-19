namespace Context.Tests;

public sealed class MeetingLinkTests
{
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private static readonly DateTime Monday10 = new(2026, 11, 16, 10, 0, 0);

    private static string Calendar(params string[] lines) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";

    private static string Weekly(params string[] extra) => Calendar(
        ["BEGIN:VEVENT", "UID:abc", "SUMMARY:Weekly sync", "DTSTART:20261116T100000", "DTEND:20261116T110000",
            "RRULE:FREQ=WEEKLY", .. extra, "END:VEVENT"]);

    // The in-memory day function over IcsParser.Parse, counting the days read.
    private static Func<DateOnly, IReadOnlyList<Appointment>> DayOf(string text, List<DateOnly>? read = null) => day =>
    {
        read?.Add(day);
        return IcsParser.Parse(text, Zone, day);
    };

    private static Appointment At(DateTime start, string? uid = "abc", string title = "Weekly sync") =>
        new(title, null, start, start.AddHours(1), false, null, uid);

    private static MeetingLink Link(DateTime start) => MeetingLink.For(At(start))!;

    // AC-4: no link without a Uid or with one over 1000 characters; 1000 gives one.
    [Fact]
    public void For_NeedsAUidOfAtMost1000Chars()
    {
        Assert.Null(MeetingLink.For(At(Monday10, uid: null)));
        Assert.Null(MeetingLink.For(At(Monday10, uid: "")));
        Assert.Null(MeetingLink.For(At(Monday10, uid: new string('u', 1001))));

        MeetingLink? link = MeetingLink.For(At(Monday10, uid: new string('u', 1000)));

        Assert.NotNull(link);
        Assert.Equal(1000, link.Uid.Length);
    }

    // AC-4: the start is kept to the minute, Day is its date, the title is one line and cut to 200 chars.
    [Fact]
    public void For_StartToTheMinute_TitleOneLineAndCut()
    {
        MeetingLink link = MeetingLink.For(At(Monday10.AddSeconds(42), title: "a\nb\u0007"))!;

        Assert.Equal(Monday10, link.Start);
        Assert.Equal(new DateOnly(2026, 11, 16), link.Day);
        Assert.Equal("a b�", link.Title);

        string cut = MeetingLink.For(At(Monday10, title: new string('x', 199) + "\U0001F600" + "tail"))!.Title;
        Assert.Equal(new string('x', 199), cut);
        Assert.Equal(200, MeetingLink.For(At(Monday10, title: new string('y', 300)))!.Title.Length);
        Assert.Equal("MeetingLink", link.ToString());
    }

    // AC-4: Matches needs the same Uid (ordinal) and the same minute.
    [Fact]
    public void Matches_SameUidOrdinal_SameMinute()
    {
        MeetingLink link = Link(Monday10);

        Assert.True(link.Matches(At(Monday10)));
        Assert.True(link.Matches(At(Monday10.AddSeconds(30), title: "Renamed")));
        Assert.False(link.Matches(At(Monday10.AddMinutes(1))));
        Assert.False(link.Matches(At(Monday10.AddMinutes(-1))));
        Assert.False(link.Matches(At(Monday10, uid: "ABC")));
        Assert.False(link.Matches(At(Monday10, uid: null)));
    }

    // AC-5: a weekly series' next occurrence is 7 days later.
    [Fact]
    public void NextOccurrence_Weekly_SevenDaysLater()
    {
        Appointment? next = MeetingLink.NextOccurrence(Link(Monday10), DayOf(Weekly()), Monday10.AddMinutes(-30));

        Assert.NotNull(next);
        Assert.Equal(Monday10.AddDays(7), next.Start);
        Assert.Equal("abc", next.Uid);
    }

    // AC-5: an occurrence removed by EXDATE is skipped; the one after is found.
    [Fact]
    public void NextOccurrence_SkipsExdate()
    {
        Appointment? next = MeetingLink.NextOccurrence(
            Link(Monday10), DayOf(Weekly("EXDATE:20261123T100000")), Monday10);

        Assert.Equal(Monday10.AddDays(14), next?.Start);
    }

    // AC-5: occurrences that start at or before localNow are skipped.
    [Fact]
    public void NextOccurrence_SkipsOccurrencesAtOrBeforeNow()
    {
        MeetingLink link = Link(Monday10);

        Assert.Equal(Monday10.AddDays(14), MeetingLink.NextOccurrence(link, DayOf(Weekly()), Monday10.AddDays(7))?.Start);
        Assert.Equal(
            Monday10.AddDays(21), MeetingLink.NextOccurrence(link, DayOf(Weekly()), Monday10.AddDays(14).AddMinutes(5))?.Start);
    }

    // AC-5: a one-off event has no next occurrence.
    [Fact]
    public void NextOccurrence_OneOff_Null()
    {
        string text = Calendar(
            "BEGIN:VEVENT", "UID:abc", "SUMMARY:Once", "DTSTART:20261116T100000", "DTEND:20261116T110000", "END:VEVENT");
        List<DateOnly> read = [];

        Assert.Null(MeetingLink.NextOccurrence(Link(Monday10), DayOf(text, read), Monday10));
        Assert.Equal(MeetingLink.MaxSearchDays + 1, read.Count);
        Assert.Equal(new DateOnly(2026, 11, 16), read[0]);
        Assert.Equal(new DateOnly(2026, 11, 16).AddDays(MeetingLink.MaxSearchDays), read[^1]);
    }

    // AC-5: a series whose next occurrence is more than 100 days away gives null; 100 days is still found.
    [Fact]
    public void NextOccurrence_MoreThan100DaysAway_Null()
    {
        string every101 = Calendar(
            "BEGIN:VEVENT", "UID:abc", "SUMMARY:Rare", "DTSTART:20261116T100000", "DTEND:20261116T110000",
            "RRULE:FREQ=DAILY;INTERVAL=101", "END:VEVENT");
        string every100 = every101.Replace("INTERVAL=101", "INTERVAL=100", StringComparison.Ordinal);

        Assert.Null(MeetingLink.NextOccurrence(Link(Monday10), DayOf(every101), Monday10));
        Assert.Equal(Monday10.AddDays(100), MeetingLink.NextOccurrence(Link(Monday10), DayOf(every100), Monday10)?.Start);
    }

    // AC-5: an appointment of another Uid is never the next occurrence.
    [Fact]
    public void NextOccurrence_OtherUid_Ignored()
    {
        string other = Weekly().Replace("UID:abc", "UID:other", StringComparison.Ordinal);

        Assert.Null(MeetingLink.NextOccurrence(Link(Monday10), DayOf(other), Monday10));
    }

    // Missing: true when no appointment of the day matches, false when one does.
    [Fact]
    public void Missing_WhenNoAppointmentMatches()
    {
        MeetingLink link = Link(Monday10);

        Assert.False(MeetingLink.Missing(link, IcsParser.Parse(Weekly(), Zone, link.Day)));
        Assert.True(MeetingLink.Missing(link, IcsParser.Parse(Weekly("EXDATE:20261116T100000"), Zone, link.Day)));
        Assert.True(MeetingLink.Missing(link, []));
    }
}
