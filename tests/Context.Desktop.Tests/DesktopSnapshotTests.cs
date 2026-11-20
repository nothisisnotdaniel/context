namespace Context.Tests;

public sealed class DesktopSnapshotTests
{
    // The raw text never leaks through formatting.
    [Fact]
    public void ToString_OnlyTypeName()
    {
        DesktopSnapshot snapshot = new DesktopSnapshot(
            false, DesktopFixture.Today, null, [new TodoItem(Guid.NewGuid(), "secret", DesktopFixture.Today, DateTime.UtcNow)],
            false, [], false, "/secret/folder");

        Assert.Equal("DesktopSnapshot", snapshot.ToString());
        Assert.Equal("DesktopSnapshot", $"{snapshot}");
    }

    // 227 AC-7: a private event in a calendar file never reaches Snapshot.Current; the two public ones are 1 and 2.
    [Fact]
    public async Task Current_LeavesOutPrivateAppointments()
    {
        using DesktopFixture data = new DesktopFixture();
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T091500"),
            DesktopFixture.Event("Doctor", "20261029T100000", "20261029T110000")
                .Replace("END:VEVENT", "CLASS:PRIVATE\r\nEND:VEVENT", StringComparison.Ordinal),
            DesktopFixture.Event("Review", "20261029T120000", "20261029T130000")));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Equal(["Standup", "Review"], snapshot.Current!.Appointments.Select(a => a.Title).ToArray());
        Assert.Equal(2, snapshot.Current.Related.Count);
    }

    // 287 AC-13: an open tied todo is hinted when its .ics occurrence was removed by EXDATE, cancelled by an override or
    // moved to another start; not when it still exists (the moved one's new start included), nor for a past day.
    [Fact]
    public async Task MeetingHints_IcsOccurrenceRemovedCancelledOrMoved()
    {
        using DesktopFixture data = new DesktopFixture();
        data.WriteCalendar("meetings.ics", DesktopFixture.Calendar(
            MeetingCalendar.Sync("EXDATE:20261105T110000"),
            MeetingCalendar.WithUid(
                DesktopFixture.Event("Sync", "20261112T140000", "20261112T143000"), MeetingCalendar.SyncUid,
                "RECURRENCE-ID:20261112T110000"),
            MeetingCalendar.WithUid(
                DesktopFixture.Event("Sync", "20261119T110000", "20261119T113000"), MeetingCalendar.SyncUid,
                "RECURRENCE-ID:20261119T110000", "STATUS:CANCELLED")));
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem today = store.Add("Today", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink());
        TodoItem removed = store.Add("Removed", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink(1));
        TodoItem moved = store.Add("Moved", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink(2));
        TodoItem cancelled = store.Add("Cancelled", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink(3));
        TodoItem newStart = store.Add(
            "New start", DesktopFixture.Today, null, null,
            MeetingLink.For(MeetingCalendar.At("Sync", new DateTime(2026, 11, 12, 14, 0, 0), MeetingCalendar.SyncUid)));
        TodoItem past = store.Add("Past", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink(-1));
        TodoItem plain = store.Add("Plain", DesktopFixture.Today);

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Equal(
            new[] { removed.Id, moved.Id, cancelled.Id }.Order().ToArray(),
            snapshot.MeetingHints.Order().ToArray());
        Assert.DoesNotContain(today.Id, snapshot.MeetingHints);
        Assert.DoesNotContain(newStart.Id, snapshot.MeetingHints);
        Assert.DoesNotContain(past.Id, snapshot.MeetingHints);
        Assert.DoesNotContain(plain.Id, snapshot.MeetingHints);
    }

    // 287 AC-13: a todo tied to an occurrence of the cached Exchange feed that still exists has no hint, one whose
    // occurrence the feed removed has; nothing is downloaded. Without a cache, a UID the files don't have is never hinted.
    [Fact]
    public async Task MeetingHints_CachedFeed_NoDownload_NoCacheNoHint()
    {
        using DesktopFixture data = new DesktopFixture();
        data.Http = FakeHttpHandler.Ok(DesktopFixture.Calendar());
        string feedEvent = MeetingCalendar.WithUid(
            DesktopFixture.Event("Board", "20261030T090000", "20261030T100000"), "feed-1",
            "RRULE:FREQ=WEEKLY", "EXDATE:20261106T090000");
        TodoStore store = MeetingCalendar.Store(data);
        DateTime board = new DateTime(2026, 10, 30, 9, 0, 0);
        TodoItem exists = store.Add("Exists", DesktopFixture.Today, null, null, MeetingLink.For(MeetingCalendar.At("Board", board, "feed-1")));
        TodoItem removed = store.Add(
            "Removed", DesktopFixture.Today, null, null, MeetingLink.For(MeetingCalendar.At("Board", board.AddDays(7), "feed-1")));

        DesktopSnapshot noCache = await data.RefreshAsync();
        data.SaveLink("https://example.org/calendar.ics");
        data.WriteCache(DesktopFixture.Calendar(feedEvent), data.UtcNow);
        DesktopSnapshot cached = await data.RefreshAsync();

        Assert.Empty(noCache.MeetingHints);
        Assert.Equal([removed.Id], cached.MeetingHints.ToArray());
        Assert.DoesNotContain(exists.Id, cached.MeetingHints);
        Assert.Equal(0, data.Http.Requests);
    }

    // 287 AC-13: an outlook: todo is hinted only from an Ok Outlook read without its occurrence; with Outlook off or not
    // running it shows no hint.
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task MeetingHints_Outlook_OnlyFromAnOkRead(bool on, bool running, bool hinted)
    {
        using DesktopFixture data = new DesktopFixture();
        new SettingsStore(data.Root).SaveOutlook(on);
        data.Outlook.AppointmentKind = running ? OutlookReadKind.Ok : OutlookReadKind.NotRunning;
        DateTime start = new DateTime(2026, 10, 30, 9, 0, 0);
        data.Outlook.Days[new DateOnly(2026, 10, 30)] = [MeetingCalendar.At("Review", start, "outlook:abc")];
        TodoStore store = MeetingCalendar.Store(data);
        TodoItem exists = store.Add("Exists", DesktopFixture.Today, null, null, MeetingLink.For(MeetingCalendar.At("Review", start, "outlook:abc")));
        TodoItem gone = store.Add(
            "Gone", DesktopFixture.Today, null, null, MeetingLink.For(MeetingCalendar.At("Review", start.AddHours(1), "outlook:abc")));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Equal(hinted ? [gone.Id] : [], snapshot.MeetingHints.ToArray());
        Assert.DoesNotContain(exists.Id, snapshot.MeetingHints);
    }

    // 287 AC-13: while a calendar file can't be read, no .ics todo is hinted.
    [Fact]
    public async Task MeetingHints_UnreadableCalendarFile_NoHint()
    {
        using DesktopFixture data = new DesktopFixture();
        data.WriteCalendar("meetings.ics", DesktopFixture.Calendar(MeetingCalendar.Sync("EXDATE:20261105T110000")));
        data.WriteCalendar("broken.ics", "not a calendar");
        MeetingCalendar.Store(data).Add("Removed", DesktopFixture.Today, null, null, MeetingCalendar.SyncLink(1));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Empty(snapshot.MeetingHints);
    }
}
