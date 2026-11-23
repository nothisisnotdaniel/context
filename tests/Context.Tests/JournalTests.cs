using System.Globalization;
using Xunit.Abstractions;

namespace Context.Tests;

public sealed class JournalTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private readonly string root = Directory.CreateTempSubdirectory().FullName;
    private readonly ITestOutputHelper output;

    public JournalTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public void Dispose() => Directory.Delete(root, true);

    private static Journal Open(NoteStore notes, DateOnly? oldest, Func<DateOnly, IReadOnlyList<Appointment>> appointments) =>
        new(notes.List, note => JournalNoteFile.Open(notes.Root, note), Today, oldest, appointments);

    [Fact]
    public void LowercaseMeetingsRoot_QualifiesWithoutCalendar()
    {
        NoteStore notes = new(root);
        notes.Create("meetings");
        notes.CreateChild("meetings", "Standup");
        notes.CreateChild("meetings/Standup", "2026-10-29");
        Journal journal = Open(notes, null, _ => []);
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
        Journal journal = Open(new NoteStore(root), calendar.Oldest(Today, null), date => calendar.Day(date).Appointments);
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
        Journal journal = Open(notes, appointmentDay, day =>
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
        Journal journal = Open(new NoteStore(root), oldest, date => calendar.Day(date).Appointments);
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
        Journal journal = Open(notes, null, _ => []);
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

    [Fact]
    public void DailyReplacedBetweenBatches_LeavesOnlyMeetingAndAppointment()
    {
        NoteStore notes = new(Path.Combine(root, "notes"));
        notes.CreateWithText("2026-10-29", "First batch");
        DateOnly date = Today.AddDays(-14);
        string name = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        NoteInfo daily = notes.CreateWithText(name, "Older daily note");
        notes.CreateWithText(name + " Standup", "#meeting");
        DateTime start = date.ToDateTime(new TimeOnly(9, 0));
        Appointment appointment = new("Standup", null, start, start.AddHours(1), false);
        Journal journal = Open(notes, date, day => day == date ? [appointment] : []);
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);
        Assert.False(journal.Exhausted);

        string target = Path.Combine(root, "fixture.txt");
        File.WriteAllText(target, "Outside the test notes folder");
        File.Delete(daily.Path);
        try
        {
            File.CreateSymbolicLink(daily.Path, target);
            output.WriteLine("Replaced the listed daily note with a file link.");
        }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            // Windows without permission to create file links still exercises a replaced, ineligible entry.
            Directory.CreateDirectory(daily.Path);
            output.WriteLine("Used a replacement directory after Windows denied file-link creation (1314).");
        }

        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(date, entry.Date);
        Assert.Null(entry.Daily);
        Assert.Null(entry.Body);
        Assert.Equal([name + " Standup"], entry.Meetings);
        Assert.Same(appointment, Assert.Single(entry.Appointments));
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void DifferentOpenedFile_LeavesDailyOutAndKeepsBothLinks()
    {
        NoteStore notes = new(Path.Combine(root, "notes"));
        NoteInfo daily = notes.CreateWithText("2026-10-29", "Local fixture");
        notes.CreateWithText("2026-10-29 Tagged", "#meeting");
        NoteInfo other = new NoteStore(Path.Combine(root, "other")).CreateWithText(daily.Name, "Other fixture");
        DateTime start = Today.ToDateTime(new TimeOnly(9, 0));
        Appointment item = new("Scheduled", null, start, start.AddHours(1), false);
        // The listed pathname is still eligible. Give its reader a different real file handle, without racing threads.
        Journal journal = new(notes.List, note => note.Name == daily.Name
            ? JournalNoteFile.Open(notes.Root, note, _ => File.OpenHandle(other.Path))
            : JournalNoteFile.Open(notes.Root, note), Today, Today, _ => [item]);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Null(day.Body);
        Assert.Null(day.Daily);
        Assert.Equal(["2026-10-29 Tagged"], day.Meetings);
        Assert.Same(item, Assert.Single(day.Appointments));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableNoteListing_PreservesCalendar(bool denied)
    {
        CalendarStore calendar = new(root, TimeZoneInfo.Utc);
        Directory.CreateDirectory(calendar.Folder);
        File.WriteAllText(Path.Combine(calendar.Folder, "local.ics"),
            "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Scheduled\nDTSTART:20261028T090000Z\nEND:VEVENT\nEND:VCALENDAR");
        IReadOnlyList<NoteInfo> List() => throw (denied ? new UnauthorizedAccessException() : new IOException());
        Journal journal = new(List, note => JournalNoteFile.Open(root, note), Today, calendar.Oldest(Today, null),
            date => calendar.Day(date).Appointments);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(Today.AddDays(-1), day.Date);
        Assert.Equal("Scheduled", Assert.Single(day.Appointments).Title);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void DuplicateDailyExtensions_UseTheEditorsFirstExactMatch()
    {
        NoteStore notes = new(root);
        const string Name = "2026-10-29";
        File.WriteAllText(Path.Combine(root, Name + ".md"), "Lowercase extension");
        string upper = Path.Combine(root, Name + ".MD");
        // A case-insensitive directory can store only one spelling. Supply the second listing entry from a fixture subfolder.
        if (File.Exists(upper))
        {
            upper = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "variants")).FullName, Name + ".MD");
        }
        File.WriteAllText(upper, "Uppercase extension");
        List<NoteInfo> listed = [.. notes.List()];
        if (listed.Count == 1)
        {
            listed.Add(new NoteInfo(Name, Today.ToDateTime(TimeOnly.MinValue), upper));
            output.WriteLine("The fixture used a subfolder for the second extension on this case-insensitive volume.");
        }
        else
        {
            output.WriteLine("The fixture listed both extensions in the notes directory.");
        }
        Assert.Equal(2, listed.Count);
        Assert.True(notes.TryFind(Name, out NoteInfo? editor));
        Assert.Equal(editor!.Path, listed[0].Path);
        Journal journal = new(() => listed, note => JournalNoteFile.Open(root, note), Today, null, _ => []);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(Name, day.Daily);
        Assert.Equal(notes.ReadForEdit(editor).Text, day.Body);
    }
}
