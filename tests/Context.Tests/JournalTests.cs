using System.Globalization;

namespace Context.Tests;

public sealed class JournalTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private readonly string root = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void LowercaseMeetingsRoot_QualifiesWithoutCalendar()
    {
        NoteStore notes = new(root);
        notes.Create("meetings");
        notes.CreateChild("meetings", "Standup");
        notes.CreateChild("meetings/Standup", "2026-10-29");
        Journal journal = new(notes, Today, null, _ => []);
        Assert.Equal(["meetings/Standup/2026-10-29"], Assert.Single(journal.ReadBatch()).Meetings);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void HistoryProbeLimit_PreservesSurvivingAppointment()
    {
        CalendarStore calendar = new(root, TimeZoneInfo.Utc);
        Directory.CreateDirectory(calendar.Folder);
        DateOnly start = Today.AddDays(-RecurrenceRule.MaxPeriodsPerEvent);
        string Stamp(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T090000Z";
        string exclusions = string.Join(",", Enumerable.Range(0, RecurrenceRule.MaxPeriodsPerEvent)
            .Select(i => Stamp(start.AddDays(i))));
        string text = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Review\nDTSTART:" + Stamp(start)
            + "\nRRULE:FREQ=DAILY\nEXDATE:" + exclusions + "\nEND:VEVENT\nEND:VCALENDAR";
        File.WriteAllText(Path.Combine(calendar.Folder, "series.ics"), text);
        Journal journal = new(new NoteStore(root), Today, calendar.Oldest(Today, null), date => calendar.Day(date).Appointments);
        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(Today, day.Date);
        Assert.Equal("Review", Assert.Single(day.Appointments).Title);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void DailyMeetingAndLegacyNotes_KeepBodiesAndStoredTargets()
    {
        NoteStore notes = new(root);
        notes.CreateWithText("2026-10-29", "# 2026-10-29\r\nFirst\r\nSecond");
        notes.CreateWithText("2026-10-28", "# Other heading\nText");
        notes.CreateWithText("2026-10-27", "# 2026-10-27\n   ");
        notes.CreateWithText("2026-10-26", "# 2026-10-26");
        notes.CreateWithText("2026-10-27 Tagged", "A #meeting note");
        notes.CreateWithText("2026-10-27 Ignored", "`#meeting`");
        notes.CreateWithText("2026-10-25 Review", "No tag");
        notes.Create(MeetingNote.FolderName);
        notes.CreateChild("Meetings", "Standup");
        notes.CreateChild("Meetings/Standup", "2026-10-24");
        File.WriteAllText(Path.Combine(root, "2020-01-01.ctxs"), "sealed");
        DateOnly appointmentDay = Today.AddDays(-4);
        DateTime start = appointmentDay.ToDateTime(new TimeOnly(9, 0));
        Appointment meeting = new("Review", null, start, start.AddHours(1), false);
        List<DateOnly> reads = [];
        Journal journal = new(notes, Today, appointmentDay, day =>
        {
            reads.Add(day);
            return day == appointmentDay ? [meeting] : [];
        });

        IReadOnlyList<JournalDay> batch = journal.ReadBatch();
        Assert.Equal([29, 28, 27, 25, 24], batch.Select(d => d.Date.Day));
        Assert.Equal("First\nSecond", batch[0].Body);
        Assert.Equal("# Other heading\nText", batch[1].Body);
        Assert.Equal("2026-10-27", batch[2].Daily);
        Assert.Equal(["2026-10-27 Tagged"], batch[2].Meetings);
        Assert.Equal(["2026-10-25 Review"], batch[3].Meetings);
        Assert.Same(meeting, Assert.Single(batch[3].Appointments));
        Assert.Equal(["Meetings/Standup/2026-10-24"], batch[4].Meetings);
        Assert.True(journal.Exhausted);
        Assert.Empty(journal.ReadBatch());
        Assert.Equal(Enumerable.Range(0, 6).Select(i => Today.AddDays(-i)), reads);
    }

    [Fact]
    public void ExplicitDateBeforeSeriesStart_SetsCalendarHistoryBound()
    {
        CalendarStore calendar = new(root, TimeZoneInfo.Utc);
        Directory.CreateDirectory(calendar.Folder);
        string text = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Review\nUID:visible\nDTSTART:20261102T090000Z\n"
            + "DTEND:20261102T100000Z\nRDATE:20261028T090000Z\nEND:VEVENT\n"
            + "BEGIN:VEVENT\nUID:hidden\nCLASS:PRIVATE\nDTSTART:20200101T090000Z\nEND:VEVENT\n"
            + "BEGIN:VEVENT\nUID:hidden\nRECURRENCE-ID:20200102T090000Z\nDTSTART:20200102T090000Z\nEND:VEVENT\nEND:VCALENDAR";
        File.WriteAllText(Path.Combine(calendar.Folder, "series.ics"), text);
        DateOnly? oldest = calendar.Oldest(Today, null);
        Assert.Equal(Today.AddDays(-1), oldest);
        Journal journal = new(new NoteStore(root), Today, oldest, date => calendar.Day(date).Appointments);
        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(Today.AddDays(-1), entry.Date);
        Assert.Equal("Review", Assert.Single(entry.Appointments).Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableDaily_PreservesOtherLinks(bool oversized)
    {
        NoteStore notes = new(root);
        string name = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        NoteInfo daily = notes.Create(name);
        notes.CreateWithText(name + " Standup", "#meeting");
        Journal journal = new(notes, Today, null, _ => []);
        if (oversized)
        {
            using FileStream file = File.OpenWrite(daily.Path);
            file.SetLength(NoteStore.MaxNoteBytes + 1);
        }
        else
        {
            File.Delete(daily.Path);
        }
        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Null(entry.Daily);
        Assert.Equal([name + " Standup"], entry.Meetings);
    }
}
