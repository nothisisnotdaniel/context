namespace Context.Tests;

// 171: the day's end in the daily note (AC-3 to AC-8, AC-10 to AC-12).
public sealed class DayEndTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 14);
    private static readonly DateOnly Next = new(2026, 11, 15);
    private static readonly DateOnly Before = new(2026, 11, 13);
    private const string SealedFixture = "{\"v\":1,\"text\":\"c2VhbGVk\"}";

    private const string NewDayNote =
        "# 2026-11-14\n\n## Appointments\n- No appointments.\n\n## Todos\n- Nothing due.\n";

    private const string ExampleWorked =
        "## Worked\n- Arrived 08:00, left 17:00, worked 8:30\n- #12 A: 4:00\n- B: 4:30\n";

    private readonly string parent;
    private readonly string root;
    private readonly string times;
    private SortedDictionary<string, string> timesBefore;
    private DateTime clock = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    public DayEndTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-dayend-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        times = Path.Combine(parent, "Times");
        Directory.CreateDirectory(times);
        timesBefore = FileSnapshot.Take(times);
    }

    // AC-8: no test ever changes Times' folder; the snapshot is retaken only when a test writes a day file itself.
    public void Dispose()
    {
        try
        {
            Assert.Equal(timesBefore, FileSnapshot.Take(times));
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private TodoStore Todos() => new(root, () => clock = clock.AddMinutes(1));

    private NoteStore Notes() => new(root);

    private string SettingsPath => Path.Combine(root, SettingsStore.FileName);

    private DayEnd Make(string? timesFolder = "", bool settings = true) =>
        new(Notes(), Todos(), new CalendarStore(root, IcsParserTests.Zone), null,
            timesFolder == string.Empty ? times : timesFolder, settings ? new SettingsStore(root) : null);

    private void WriteTimes(DateOnly day, string json)
    {
        File.WriteAllText(Path.Combine(times, DailyNote.NameFor(day) + ".json"), json);
        timesBefore = FileSnapshot.Take(times);
    }

    // A Times day file in the entries format from "kind HH:mm" pairs.
    private void WriteEntries(DateOnly day, params string[] entries)
    {
        IEnumerable<string> items = entries.Select(e =>
        {
            string[] parts = e.Split(' ');
            return $"{{\"kind\":\"{parts[0]}\",\"time\":\"{parts[1]}\"}}";
        });
        WriteTimes(day, "{\"entries\":[" + string.Join(',', items) + "]}");
    }

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private void Seal(string name)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + NoteStore.SensitiveExtension), SealedFixture);
    }

    private string Note(DateOnly day) => File.ReadAllText(Path.Combine(root, DailyNote.NameFor(day) + ".md"));

    private bool NoteExists(DateOnly day) => File.Exists(Path.Combine(root, DailyNote.NameFor(day) + ".md"));

    // AC-3: a missing note is created through Ensure, then the section is appended after a blank line.
    [Fact]
    public void RefreshWorked_MissingNote_CreatesItWithTheSection()
    {
        WriteTimes(Day, TimesDayTests.Example);

        DayEndResult result = Make().RefreshWorked(Day);

        Assert.Equal(DayEndKind.Done, result.Kind);
        Assert.True(result.Changed);
        Assert.Equal(NewDayNote + "\n" + ExampleWorked, Note(Day));
        Assert.False(File.Exists(SettingsPath));
    }

    // AC-3: exactly one line instead of the figures.
    [Theory]
    [InlineData(null, "- No Times bookings for this day.")]
    [InlineData("not json", "- Can't read Times' day file.")]
    [InlineData("{\"absence\":\"vacation\"}", "- Absent (vacation).")]
    [InlineData("{\"absence\":\"sick\"}", "- Absent (sick).")]
    [InlineData("{\"absence\":\"publicHoliday\"}", "- Absent (public holiday).")]
    [InlineData("{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"}]}", "- No leaving booked in Times yet.")]
    [InlineData("{\"entries\":[]}", "- No leaving booked in Times yet.")]
    public void RefreshWorked_NoFigures_WritesOneLine(string? json, string line)
    {
        if (json is not null)
        {
            WriteTimes(Day, json);
        }

        Make().RefreshWorked(Day);

        Assert.Equal(NewDayNote + "\n## Worked\n" + line + "\n", Note(Day));
    }

    // AC-3: no Times folder at all.
    [Fact]
    public void RefreshWorked_NoTimesFolder_SaysNoBookings()
    {
        Make(timesFolder: null).RefreshWorked(Day);

        Assert.EndsWith("\n## Worked\n- No Times bookings for this day.\n", Note(Day), StringComparison.Ordinal);
    }

    // AC-3: names cleaned to one line (no heading can start), an open group, several names of one work item.
    [Fact]
    public void RefreshWorked_CleansNamesAndMarksOpenGroups()
    {
        WriteTimes(
            Day,
            """
            {"entries":[
              {"kind":"arrival","time":"07:45"},
              {"kind":"checkpoint","time":"07:45","name":"Spec\n## Hack","workItem":3},
              {"kind":"checkpoint","time":"09:00","name":"Review","workItem":3},
              {"kind":"leaving","time":"11:00"},
              {"kind":"checkpoint","time":"20:00","name":"Late"}
            ]}
            """);

        Make().RefreshWorked(Day);

        Assert.EndsWith(
            "\n## Worked\n- Arrived 07:45, left 11:00, worked 3:15\n- #3 Spec ## Hack, Review: 3:15\n- Late: 0:00 (open)\n",
            Note(Day),
            StringComparison.Ordinal);
    }

    // AC-3: a legacy file with no arrival but a leaving.
    [Fact]
    public void RefreshWorked_LegacyWithoutArrival_ShowsDashes()
    {
        WriteTimes(Day, "{\"arrival\":null,\"leaving\":\"17:00\"}");

        Make().RefreshWorked(Day);

        Assert.EndsWith("\n## Worked\n- Arrived --:--, left 17:00, worked 0:00\n", Note(Day), StringComparison.Ordinal);
    }

    // AC-4: an existing section is replaced up to the next "## " line; the rest stays byte for byte; no second one.
    [Fact]
    public void RefreshWorked_ExistingSection_ReplacedOnce()
    {
        const string Head = "# 2026-11-14\r\n\r\nintro\r\n## Worked extra\r\n\r\n";
        const string Tail = "## Notes\r\nkeep this\r\n#### Worked\r\n";
        WriteNote("2026-11-14", Head + "## Worked\r\n- old line\r\n### sub\r\n\r\n" + Tail);
        WriteTimes(Day, TimesDayTests.Example);
        DayEnd dayEnd = Make();

        dayEnd.RefreshWorked(Day);
        string first = File.ReadAllText(Path.Combine(root, "2026-11-14.md"));
        DateTime written = File.GetLastWriteTimeUtc(Path.Combine(root, "2026-11-14.md"));
        File.SetLastWriteTimeUtc(Path.Combine(root, "2026-11-14.md"), written.AddHours(-1));
        DayEndResult again = dayEnd.RefreshWorked(Day);

        Assert.Equal(Head + ExampleWorked.Replace("\n", "\r\n") + "\r\n" + Tail, first);
        Assert.False(again.Changed);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(Path.Combine(root, "2026-11-14.md")));
    }

    // AC-4: a section at the end is replaced to the end; a note without a final newline gets one before appending.
    [Fact]
    public void RefreshWorked_SectionAtEndAndNoFinalNewline()
    {
        WriteTimes(Day, "{\"absence\":\"sick\"}");
        WriteNote("2026-11-14", "# 2026-11-14\n\n## Worked\n- old\n- older");
        Make().RefreshWorked(Day);
        Assert.Equal("# 2026-11-14\n\n## Worked\n- Absent (sick).\n", Note(Day));

        WriteNote("2026-11-14", "# 2026-11-14\ntext");
        Make().RefreshWorked(Day);
        Assert.Equal("# 2026-11-14\ntext\n\n## Worked\n- Absent (sick).\n", Note(Day));
    }

    // AC-4: a change on disk between read and save is read again (3 retries); always changing fails.
    [Fact]
    public void RefreshWorked_ChangedOnDisk_RetriesThenFails()
    {
        WriteTimes(Day, "{\"absence\":\"sick\"}");
        WriteNote("2026-11-14", "# 2026-11-14\n");
        string path = Path.Combine(root, "2026-11-14.md");
        int calls = 0;
        DayEnd dayEnd = Make();
        dayEnd.BeforeNoteSave = () =>
        {
            calls++;
            if (calls == 1)
            {
                File.AppendAllText(path, "added elsewhere\n");
            }
        };

        Assert.Equal(DayEndKind.Done, dayEnd.RefreshWorked(Day).Kind);
        Assert.Equal(2, calls);
        Assert.Equal("# 2026-11-14\nadded elsewhere\n\n## Worked\n- Absent (sick).\n", Note(Day));

        WriteNote("2026-11-14", "# 2026-11-14\n");
        calls = 0;
        dayEnd.BeforeNoteSave = () =>
        {
            calls++;
            File.AppendAllText(path, "x\n");
        };
        DayEndResult failed = dayEnd.RefreshWorked(Day);

        Assert.Equal(DayEndKind.WriteFailed, failed.Kind);
        Assert.Equal("2026-11-14", failed.Name);
        Assert.Equal(DayEnd.MaxRetries + 1, calls);
        Assert.DoesNotContain("## Worked", Note(Day), StringComparison.Ordinal);
    }

    // AC-5 and AC-6: the moved todos in both notes; D+1's note is created after the move; done and later todos stay.
    [Fact]
    public void MoveTodos_ListsMovedTodosInBothNotes()
    {
        TodoStore todos = Todos();
        TodoItem late = todos.Add("Late\nline", new DateOnly(2026, 11, 10));
        TodoItem due = todos.Add("Due", Day);
        TodoItem later = todos.Add("Later", new DateOnly(2026, 11, 16));
        TodoItem done = todos.Add("Done", Day);
        todos.Complete(done.Id);
        WriteNote("2026-11-14", "# 2026-11-14\n\n## Worked\n- Absent (sick).\n\n## Notes\nkeep\n");

        DayEndResult result = Make().MoveTodos(Day);

        Assert.Equal(DayEndKind.Done, result.Kind);
        Assert.Equal([late.Id, due.Id], result.Moved.Select(t => t.Id));
        Assert.Equal(Next, result.To);
        Assert.Equal(
            "# 2026-11-14\n\n## Worked\n- Absent (sick).\n\n## Moved to tomorrow\n- Late line\n- Due\n\n## Notes\nkeep\n",
            Note(Day));
        Assert.Equal(
            "# 2026-11-15\n\n## Appointments\n- No appointments.\n\n## Todos\n- Late line\n- Due\n" +
            "\n## Moved from 2026-11-14\n- Late line\n- Due\n",
            Note(Next));
        Assert.Equal(new DateOnly(2026, 11, 16), todos.List().Single(t => t.Id == later.Id).Due);
        Assert.Equal(Day, todos.ListDone().Single().Due);

        // A second run moves nothing and keeps the lists.
        string here = Note(Day);
        string there = Note(Next);
        DayEndResult again = Make().MoveTodos(Day);
        Assert.Empty(again.Moved);
        Assert.False(again.Changed);
        Assert.Equal(here, Note(Day));
        Assert.Equal(there, Note(Next));
    }

    // AC-5 and AC-6: existing sections get the new lines at their end; the rest is unchanged byte for byte.
    [Fact]
    public void MoveTodos_ExistingSections_AppendAtTheirEnd()
    {
        Todos().Add("Second", Day);
        WriteNote("2026-11-14", "# 2026-11-14\r\n## Moved to tomorrow\r\n- First\r\n\r\n## Later\r\nx\r\n");
        WriteNote("2026-11-15", "# 2026-11-15\n## Moved from 2026-11-14\n- First\n## Moved from 2026-11-13\n- Old\n");

        Make().MoveTodos(Day);

        Assert.Equal(
            "# 2026-11-14\r\n## Moved to tomorrow\r\n- First\r\n- Second\r\n\r\n## Later\r\nx\r\n",
            File.ReadAllText(Path.Combine(root, "2026-11-14.md")));
        Assert.Equal(
            "# 2026-11-15\n## Moved from 2026-11-14\n- First\n- Second\n## Moved from 2026-11-13\n- Old\n",
            Note(Next));
    }

    // AC-5: without a Worked section the moved list goes at the end; a Worked section at the very end is followed.
    [Fact]
    public void MoveTodos_NoWorkedSection_AppendsAtTheEnd()
    {
        Todos().Add("Task", Day);
        WriteNote("2026-11-14", "# 2026-11-14\nnotes");

        Make().MoveTodos(Day);

        Assert.Equal("# 2026-11-14\nnotes\n\n## Moved to tomorrow\n- Task\n", Note(Day));

        Todos().Add("Other", Day);
        WriteNote("2026-11-14", "# 2026-11-14\n## Worked\n- Absent (sick).");
        Make().MoveTodos(Day);
        Assert.Equal("# 2026-11-14\n## Worked\n- Absent (sick).\n\n## Moved to tomorrow\n- Other\n", Note(Day));
    }

    // AC-6: nothing due writes no list and creates neither note.
    [Fact]
    public void MoveTodos_NothingDue_WritesNothing()
    {
        Todos().Add("Later", new DateOnly(2026, 11, 20));
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        DayEndResult result = Make().MoveTodos(Day);

        Assert.Equal(DayEndKind.Done, result.Kind);
        Assert.False(result.Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-7: a sensitive D: nothing is read or written (notes, todos.json, settings.json, Times).
    [Fact]
    public void Sensitive_Day_ReadsAndWritesNothing()
    {
        WriteTimes(Day, TimesDayTests.Example);
        Todos().Add("Task", Day);
        Seal("2026-11-14");
        File.WriteAllText(SettingsPath, "broken");
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "also broken");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        DayEndResult closed = Make().CloseDay(Day);
        DayEndResult moved = Make().MoveTodos(Day);
        DayEndResult auto = Make().AutoClose(Day, new DateTime(2026, 11, 14, 18, 0, 0));

        Assert.Equal(DayEndKind.Sensitive, closed.Kind);
        Assert.Equal("2026-11-14", closed.Name);
        Assert.Equal(DayEndKind.Sensitive, moved.Kind);
        Assert.Equal(DayEndKind.Done, auto.Kind);
        Assert.False(auto.Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-7: a sensitive D+1: Worked is still written to D, no todo moves, no list, D+1 untouched.
    [Fact]
    public void Sensitive_NextDay_MovesNothingButKeepsWorked()
    {
        WriteTimes(Day, TimesDayTests.Example);
        Todos().Add("Task", Day);
        Seal("2026-11-15");
        string todosBefore = File.ReadAllText(Path.Combine(root, TodoStore.FileName));

        DayEndResult result = Make().CloseDay(Day);

        Assert.Equal(DayEndKind.Sensitive, result.Kind);
        Assert.Equal("2026-11-15", result.Name);
        Assert.Empty(result.Moved);
        Assert.Equal(NewDayNote.Replace("- Nothing due.", "- Task") + "\n" + ExampleWorked, Note(Day));
        Assert.Equal(todosBefore, File.ReadAllText(Path.Combine(root, TodoStore.FileName)));
        Assert.False(NoteExists(Next));
        Assert.Equal(SealedFixture, File.ReadAllText(Path.Combine(root, "2026-11-15.ctxs")));
    }

    // AC-7: an unreadable todos.json moves nothing, Worked is still written, the file is left alone.
    [Fact]
    public void TodosUnreadable_KeepsWorked()
    {
        WriteTimes(Day, TimesDayTests.Example);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, TodoStore.FileName), "not json");

        DayEndResult result = Make().CloseDay(Day);

        Assert.Equal(DayEndKind.TodosUnreadable, result.Kind);
        Assert.EndsWith("\n" + ExampleWorked, Note(Day), StringComparison.Ordinal);
        Assert.Equal("not json", File.ReadAllText(Path.Combine(root, TodoStore.FileName)));
        Assert.False(NoteExists(Next));
    }

    // AC-7: a note that can't be written.
    [Fact]
    public void WriteFailed_NamesTheNote()
    {
        WriteTimes(Day, TimesDayTests.Example);
        WriteNote("2026-11-14", "# 2026-11-14\n" + new string('x', (int)NoteStore.MaxEditBytes - 20));

        DayEndResult result = Make().CloseDay(Day);

        Assert.Equal(DayEndKind.WriteFailed, result.Kind);
        Assert.Equal("2026-11-14", result.Name);
    }

    // CloseDay does both steps and never touches settings.json.
    [Fact]
    public void CloseDay_WorkedThenMove()
    {
        WriteTimes(Day, TimesDayTests.Example);
        Todos().Add("Task", Day);

        DayEndResult result = Make().CloseDay(Day);

        Assert.Equal(DayEndKind.Done, result.Kind);
        Assert.Equal("2026-11-14", result.Name);
        Assert.Single(result.Moved);
        Assert.Equal(
            NewDayNote.Replace("- Nothing due.", "- Task") + "\n" + ExampleWorked + "\n## Moved to tomorrow\n- Task\n",
            Note(Day));
        Assert.EndsWith("\n## Moved from 2026-11-14\n- Task\n", Note(Next), StringComparison.Ordinal);
        Assert.False(File.Exists(SettingsPath));
    }

    // AC-10 and AC-11: the day's sequence of leavings.
    [Fact]
    public void AutoClose_LeavingSequence()
    {
        TodoStore todos = Todos();
        TodoItem task = todos.Add("Task", Day);
        DayEnd dayEnd = Make();

        // A lunch leaving at 12:00 writes Worked and moves nothing.
        WriteEntries(Day, "arrival 08:00", "leaving 12:00");
        DayEndResult lunch = dayEnd.AutoClose(Day, At(12, 1));
        Assert.True(lunch.Changed);
        Assert.Empty(lunch.Moved);
        Assert.EndsWith("## Worked\n- Arrived 08:00, left 12:00, worked 4:00\n", Note(Day), StringComparison.Ordinal);
        Assert.Equal(Day, todos.List().Single().Due);
        Assert.Contains("\"worked\": \"2026-11-14T12:00\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);

        // The arrival after it changes nothing.
        WriteEntries(Day, "arrival 08:00", "leaving 12:00", "arrival 13:00");
        string note = Note(Day);
        string settings = File.ReadAllText(SettingsPath);
        DayEndResult back = dayEnd.AutoClose(Day, At(13, 1));
        Assert.False(back.Changed);
        Assert.Equal(note, Note(Day));
        Assert.Equal(settings, File.ReadAllText(SettingsPath));

        // 16:59 refreshes Worked but moves nothing.
        WriteEntries(Day, "arrival 08:00", "leaving 12:00", "arrival 13:00", "leaving 16:59");
        DayEndResult early = dayEnd.AutoClose(Day, At(16, 59));
        Assert.True(early.Changed);
        Assert.Empty(early.Moved);
        Assert.Contains("left 16:59, worked 7:59", Note(Day), StringComparison.Ordinal);
        Assert.Equal(Day, todos.List().Single().Due);

        // 17:00 refreshes and moves.
        WriteEntries(Day, "arrival 08:00", "leaving 12:00", "arrival 13:00", "leaving 17:00");
        DayEndResult evening = dayEnd.AutoClose(Day, At(17, 0));
        Assert.Equal(DayEndKind.Done, evening.Kind);
        Assert.Equal([task.Id], evening.Moved.Select(t => t.Id));
        Assert.Contains("left 17:00, worked 8:00\n\n## Moved to tomorrow\n- Task\n", Note(Day), StringComparison.Ordinal);
        Assert.EndsWith("## Moved from 2026-11-14\n- Task\n", Note(Next), StringComparison.Ordinal);
        Assert.Contains("\"moved\": \"2026-11-14\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);

        // A later 18:00 leaving refreshes Worked but moves nothing again.
        todos.Add("Added after", Day);
        WriteEntries(Day, "arrival 08:00", "leaving 12:00", "arrival 13:00", "leaving 17:00", "arrival 17:30", "leaving 18:00");
        DayEndResult late = dayEnd.AutoClose(Day, At(18, 0));
        Assert.True(late.Changed);
        Assert.Empty(late.Moved);
        Assert.Contains("left 18:00, worked 8:30\n\n## Moved to tomorrow\n- Task\n", Note(Day), StringComparison.Ordinal);
        Assert.Equal(Day, todos.List().Single(t => t.Text == "Added after").Due);

        // Repeated ticks with the same leaving write nothing.
        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        Assert.False(dayEnd.AutoClose(Day, At(18, 0, 30)).Changed);
        Assert.False(dayEnd.AutoClose(Day, At(19, 0)).Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-10: a leaving booked ahead of now waits until it is reached.
    [Fact]
    public void AutoClose_LeavingLaterThanNow_Waits()
    {
        WriteEntries(Day, "arrival 08:00", "leaving 18:00");

        Assert.False(Make().AutoClose(Day, At(17, 30)).Changed);
        Assert.False(NoteExists(Day));
        Assert.True(Make().AutoClose(Day, At(18, 0)).Changed);
    }

    // AC-12: the first call of a new day closes yesterday once and moves its todos to today.
    [Fact]
    public void AutoClose_CatchUp_ClosesYesterdayOnce()
    {
        TodoStore todos = Todos();
        TodoItem old = todos.Add("Old", new DateOnly(2026, 11, 11));
        WriteEntries(Before, "arrival 08:00", "leaving 16:00");
        WriteEntries(new DateOnly(2026, 11, 12), "arrival 08:00", "leaving 18:00");
        DayEnd dayEnd = Make();

        DayEndResult result = dayEnd.AutoClose(Day, new DateTime(2026, 11, 14, 0, 0, 20));

        Assert.Equal(DayEndKind.Done, result.Kind);
        Assert.Equal([old.Id], result.Moved.Select(t => t.Id));
        Assert.Equal(Day, todos.List().Single().Due);
        Assert.Contains("## Worked\n- Arrived 08:00, left 16:00, worked 8:00\n\n## Moved to tomorrow\n- Old\n", Note(Before), StringComparison.Ordinal);
        Assert.EndsWith("## Moved from 2026-11-13\n- Old\n", Note(Day), StringComparison.Ordinal);
        Assert.Contains("\"moved\": \"2026-11-13\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.False(NoteExists(new DateOnly(2026, 11, 12)));

        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        Assert.False(dayEnd.AutoClose(Day, new DateTime(2026, 11, 14, 0, 0, 50)).Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-12: no catch-up when yesterday was moved already, or today was, or yesterday has not left.
    [Theory]
    [InlineData("{\"dayEnd\":{\"moved\":\"2026-11-13\"}}", true)]
    [InlineData("{\"dayEnd\":{\"moved\":\"2026-11-14\"}}", true)]
    [InlineData("{}", false)]
    public void AutoClose_CatchUp_Skipped(string settings, bool yesterdayLeft)
    {
        Todos().Add("Old", new DateOnly(2026, 11, 11));
        if (yesterdayLeft)
        {
            WriteEntries(Before, "arrival 08:00", "leaving 16:00");
        }
        else
        {
            WriteEntries(Before, "arrival 08:00");
        }

        File.WriteAllText(SettingsPath, settings);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        Assert.False(Make().AutoClose(Day, new DateTime(2026, 11, 14, 0, 0, 20)).Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-12: a broken settings.json stops every automatic action and is never overwritten.
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"dayEnd\":{\"moved\":\"14.11.2026\"}}")]
    [InlineData("{\"dayEnd\":{\"worked\":\"2026-11-14 12:00\"}}")]
    [InlineData("{\"dayEnd\":\"x\"}")]
    public void AutoClose_BrokenSettings_DoesNothing(string settings)
    {
        Todos().Add("Task", Before);
        WriteEntries(Before, "arrival 08:00", "leaving 18:00");
        WriteEntries(Day, "arrival 08:00", "leaving 18:00");
        File.WriteAllText(SettingsPath, settings);
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        DayEndResult result = Make().AutoClose(Day, At(19, 0));

        Assert.Equal(DayEndKind.SettingsUnreadable, result.Kind);
        Assert.False(result.Changed);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-11: tomorrow's note sensitive: Worked is refreshed, the move waits and is not recorded.
    [Fact]
    public void AutoClose_TomorrowSensitive_DoesNotRecordTheMove()
    {
        Todos().Add("Task", Day);
        Seal("2026-11-15");
        WriteEntries(Day, "arrival 08:00", "leaving 18:00");

        DayEndResult result = Make().AutoClose(Day, At(18, 1));

        Assert.True(result.Changed);
        Assert.Empty(result.Moved);
        Assert.DoesNotContain("moved", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    // The CLI's DayEnd has no settings store: AutoClose does nothing.
    [Fact]
    public void AutoClose_WithoutSettingsStore_DoesNothing()
    {
        WriteEntries(Day, "arrival 08:00", "leaving 18:00");

        Assert.False(Make(settings: false).AutoClose(Day, At(19, 0)).Changed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void FormatDuration_HoursAndMinutes()
    {
        Assert.Equal("0:00", DayEnd.FormatDuration(TimeSpan.Zero));
        Assert.Equal("8:05", DayEnd.FormatDuration(new TimeSpan(8, 5, 0)));
        Assert.Equal("25:30", DayEnd.FormatDuration(new TimeSpan(25, 30, 0)));
    }

    [Fact]
    public void Results_ToStringIsTheTypeName()
    {
        Assert.Equal(nameof(DayEndResult), new DayEndResult(DayEndKind.Done, "x", [], Day, false).ToString());
        Assert.Equal(nameof(DayEndSettings), new DayEndSettings("x", Day).ToString());
        Assert.Equal(nameof(TimesEntry), new TimesEntry(TimesEntryKind.Arrival, new TimeOnly(8, 0)).ToString());
        Assert.Equal(nameof(TimesGroup), new TimesGroup(null, ["x"], TimeSpan.Zero, false).ToString());
    }

    private static DateTime At(int hour, int minute, int second = 0) => new(2026, 11, 14, hour, minute, second);
}
