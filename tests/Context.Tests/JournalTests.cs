using System.Globalization;
using Microsoft.Win32.SafeHandles;
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

    private static Journal Open(NoteStore notes, DateOnly? oldest, Func<DateOnly, IReadOnlyList<Appointment>> appointments)
    {
        JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(notes.Root));
        return new Journal(directory.ListNotes, directory.OpenNote, Today, oldest, appointments, sources: directory);
    }

    private static DateOnly? Oldest(CalendarStore calendar)
    {
        using JournalDirectory folder = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(calendar.Folder));
        return calendar.Oldest(Today, null, folder.ReadCalendars());
    }

    [Fact]
    public void LowercaseMeetingsRoot_QualifiesWithoutCalendar()
    {
        NoteStore notes = new(root);
        notes.Create("meetings");
        notes.CreateChild("meetings", "Standup");
        notes.CreateChild("meetings/Standup", "2026-10-29");
        using Journal journal = Open(notes, null, _ => []);
        Assert.Equal(["meetings/Standup/2026-10-29"], Assert.Single(journal.ReadBatch()).Meetings);
        Assert.True(journal.Exhausted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingMeetingParent_LeavesOnlyEditorReachableLinks(bool removeMeetingsPage)
    {
        NoteStore notes = new(root);
        NoteInfo meetings = notes.Create("Meetings");
        NoteInfo series = notes.CreateChild(meetings.Name, "Series");
        notes.CreateChild(meetings.Name, "Sibling");
        const string Date = "2026-10-15";
        // This subfolder is owned by Series.md through the editor's case-insensitive fallback.
        string folder = Directory.CreateDirectory(Path.Combine(root, "Meetings", "series")).FullName;
        string datedFile = Path.Combine(folder, Date + ".md");
        File.WriteAllText(datedFile, "Series fixture");
        string target = series.Name + "/" + Date;
        NoteInfo sibling = notes.CreateChild("Meetings/Sibling", Date);
        NoteInfo legacy = notes.CreateWithText(Date + " Independent", "#meeting");
        notes.CreateWithText("2026-10-29", "First batch");
        Assert.True(notes.TryFind(target, out NoteInfo? editor));
        Assert.Equal("Series fixture", notes.ReadForEdit(editor!).Text);
        DateOnly older = Today.AddDays(-14);
        DateTime start = older.ToDateTime(new TimeOnly(9, 0));
        Appointment appointment = new("Scheduled", null, start, start.AddHours(1), false);
        using Journal journal = Open(notes, older, day => day == older ? [appointment] : []);
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);

        File.Move(removeMeetingsPage ? meetings.Path : series.Path, Path.Combine(root, "Archived.md"));
        Assert.True(File.Exists(datedFile));
        Assert.False(notes.TryFind(target, out _));
        Assert.Equal(!removeMeetingsPage, notes.TryFind(sibling.Name, out _));

        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(older, entry.Date);
        Assert.Equal(removeMeetingsPage ? [legacy.Name] : new[] { legacy.Name, sibling.Name }, entry.Meetings);
        Assert.Same(appointment, Assert.Single(entry.Appointments));
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
        using Journal journal = Open(new NoteStore(root), Oldest(calendar), date => calendar.Day(date).Appointments);
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
        using Journal journal = Open(notes, appointmentDay, day =>
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
    public void DailyQualification_UsesTheDecodedBodyBeforeCleaningItForDisplay()
    {
        NoteStore notes = new(root);
        notes.CreateWithText("2026-10-29", "# 2026-10-29\r\n  \r\t\n");
        notes.CreateWithText("2026-10-28", "#\t2026-10-28");
        using Journal journal = Open(notes, null, _ => []);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(Today.AddDays(-1), day.Date);
        Assert.Equal("2026-10-28", day.Daily);
        Assert.Equal("#    2026-10-28", day.Body);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void LegacyMeetingTag_IsReadAgainWhenItsBatchArrives()
    {
        NoteStore notes = new(root);
        notes.CreateWithText("2026-10-29", "First batch");
        const string Prefix = "2026-10-15 ";
        NoteInfo removed = notes.CreateWithText(Prefix + "Removed", "#meeting");
        NoteInfo oversized = notes.CreateWithText(Prefix + "Oversized", "#meeting");
        NoteInfo newlyTagged = notes.CreateWithText(Prefix + "Added", "Ordinary text");
        NoteInfo matched = notes.CreateWithText(Prefix + "Scheduled", "#meeting");
        DateOnly older = Today.AddDays(-14);
        DateTime start = older.ToDateTime(new TimeOnly(9, 0));
        Appointment appointment = new("Scheduled", null, start, start.AddHours(1), false);
        using Journal journal = Open(notes, older, day => day == older ? [appointment] : []);
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);

        File.WriteAllText(removed.Path, "Only an inline code sample: `#meeting`");
        using (FileStream file = File.OpenWrite(oversized.Path))
        {
            file.SetLength(NoteStore.MaxNoteBytes + 1);
        }
        File.WriteAllText(newlyTagged.Path, "Now a #meeting");
        File.WriteAllText(matched.Path, "Appointment match");

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(older, day.Date);
        Assert.Equal([newlyTagged.Name, matched.Name], day.Meetings);
        Assert.Same(appointment, Assert.Single(day.Appointments));
        Assert.True(journal.Exhausted);
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
        DateOnly? oldest = Oldest(calendar);
        Assert.Equal(Today.AddDays(-1), oldest);
        using Journal journal = Open(new NoteStore(root), oldest, date => calendar.Day(date).Appointments);
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
        using Journal journal = Open(notes, null, _ => []);
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
        using Journal journal = Open(notes, date, day => day == date ? [appointment] : []);
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
    public void ReplacedNoteDirectory_RejectsItsListingAndNotesButKeepsTheAppointment()
    {
        NoteStore notes = new(Path.Combine(root, "notes"));
        NoteStore other = new(Path.Combine(root, "other"));
        DateOnly older = Today.AddDays(-14);
        string name = older.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        notes.CreateWithText("2026-10-29", "First batch");
        foreach (NoteStore store in new[] { notes, other })
        {
            store.CreateWithText(name, store == notes ? "Listed daily" : "Other daily");
            store.CreateWithText(name + " Standup", "#meeting");
        }
        DateTime start = older.ToDateTime(new TimeOnly(9, 0));
        Appointment appointment = new("Scheduled", null, start, start.AddHours(1), false);
        using JournalDirectory original = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(notes.Root));
        using Journal journal = new(original.ListNotes, original.OpenNote, Today, older, day => day == older ? [appointment] : []);
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);

        Directory.Move(notes.Root, Path.Combine(root, "previous"));
        Directory.Move(other.Root, notes.Root);
        using JournalDirectory replacement = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(notes.Root));
        Assert.Contains(replacement.ListNotes(), note => note.Name == name);
        Assert.ThrowsAny<IOException>(() => original.ListNotes());
        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(older, entry.Date);
        Assert.Null(entry.Daily);
        Assert.Null(entry.Body);
        Assert.Empty(entry.Meetings);
        Assert.Same(appointment, Assert.Single(entry.Appointments));
        Assert.True(journal.Exhausted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedDirectory_ListsItsOwnNotesAndKeepsTheSibling(bool changeRoot)
    {
        NoteStore store = new(Path.Combine(root, "notes"));
        store.Create("Meetings");
        store.CreateChild("Meetings", "Selected");
        store.CreateChild("Meetings", "Sibling");
        NoteInfo selected = store.CreateChild("Meetings/Selected", "2026-10-28");
        NoteInfo sibling = store.CreateChild("Meetings/Sibling", "2026-10-29");
        DateTime modified = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(selected.Path, modified);
        string target = changeRoot ? store.Root : Path.GetDirectoryName(selected.Path)!;
        NoteStore replacement = new(Path.Combine(root, "replacement"));
        replacement.CreateWithText("2001-01-01", "Other fixture");
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(store.Root));
        int changes = 0;
        directory.BeforeEnumerate = path =>
        {
            if (path == target)
            {
                Directory.Move(target, Path.Combine(root, "previous"));
                Directory.Move(replacement.Root, target);
                changes++;
            }
        };

        IReadOnlyList<NoteInfo> listed = directory.ListNotes();
        Assert.Equal(1, changes);
        Assert.Equal(modified, Assert.Single(listed, n => n.Name == selected.Name).Modified.ToUniversalTime());
        Assert.Contains(listed, n => n.Name == sibling.Name);
        Assert.DoesNotContain(listed, n => n.Title == "2001-01-01");
        List<DateOnly> reads = [];
        using Journal journal = new(() => listed, directory.OpenNote, Today, null, date => { reads.Add(date); return []; });
        IReadOnlyList<JournalDay> batch = journal.ReadBatch();
        if (changeRoot)
        {
            Assert.Empty(batch);
        }
        else
        {
            Assert.Equal([sibling.Name], Assert.Single(batch).Meetings);
        }
        Assert.Equal([Today, Today.AddDays(-1)], reads);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public void VerifiedCalendarDirectory_ListsOnlyItsOriginalNames()
    {
        string calendar = Directory.CreateDirectory(Path.Combine(root, "calendar")).FullName;
        string replacement = Directory.CreateDirectory(Path.Combine(root, "replacement")).FullName;
        File.WriteAllText(Path.Combine(calendar, "listed.ics"), "Original fixture");
        File.WriteAllText(Path.Combine(replacement, "other.ics"), "Other fixture");
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(calendar));
        directory.BeforeEnumerate = path =>
        {
            Directory.Move(path, Path.Combine(root, "previous"));
            Directory.Move(replacement, path);
        };

        (string name, string? text) = Assert.Single(directory.ReadCalendars());
        Assert.Equal("listed.ics", name);
        Assert.Null(text);
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
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(notes.Root));
        using Journal journal = new(directory.ListNotes, note => note.Name == daily.Name
            ? directory.OpenNote(note, () => File.OpenHandle(other.Path))
            : directory.OpenNote(note), Today, Today, _ => [item]);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Null(day.Body);
        Assert.Null(day.Daily);
        Assert.Equal(["2026-10-29 Tagged"], day.Meetings);
        Assert.Same(item, Assert.Single(day.Appointments));
    }

    [Fact]
    public void RedirectedMeetingFolder_IsRejectedBeforeOpeningTheNote()
    {
        NoteStore notes = new(root);
        notes.Create("Meetings");
        notes.CreateChild("Meetings", "Series");
        NoteInfo meeting = notes.CreateChild("Meetings/Series", "2026-10-29");
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(root));
        using (FileStream original = directory.OpenNote(meeting))
        {
            Assert.True(original.CanRead);
        }

        string meetings = Path.Combine(root, "Meetings");
        string series = Path.Combine(meetings, "Series");
        string stored = Path.Combine(meetings, "Stored");
        Directory.Move(series, stored);
        Func<SafeFileHandle, FileAttributes>? readDirectoryAttributes = null;
        int directories = 0;
        try
        {
            Directory.CreateSymbolicLink(series, stored);
            Assert.True(File.GetAttributes(series).HasFlag(FileAttributes.ReparsePoint));
            Assert.ThrowsAny<IOException>(() => directory.OpenNote(meeting));
            output.WriteLine("Read-time check used a local directory link.");
        }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            // The native directory handles and the leaf are real. Supply the reparse bit on the second parent
            // when the Windows test account cannot create a directory link.
            Directory.Move(stored, series);
            readDirectoryAttributes = handle =>
            {
                FileAttributes type = File.GetAttributes(handle);
                return ++directories == 2 ? type | FileAttributes.ReparsePoint : type;
            };
            output.WriteLine("Windows denied directory-link creation (1314). The fixture supplied the Series handle's reparse bit.");
        }

        Assert.ThrowsAny<IOException>(() => directory.OpenNote(meeting,
            () => throw new InvalidOperationException("The leaf open was reached."), readDirectoryAttributes));
        if (readDirectoryAttributes is not null)
        {
            Assert.Equal(2, directories);
        }
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
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(root));
        using Journal journal = new(List, directory.OpenNote, Today, Oldest(calendar),
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
        using JournalDirectory directory = Assert.IsType<JournalDirectory>(JournalDirectory.Bind(root));
        using Journal journal = new(() => listed, directory.OpenNote, Today, null, _ => []);

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(Name, day.Daily);
        Assert.Equal(notes.ReadForEdit(editor).Text, day.Body);
    }
}
