using System.Globalization;
using System.Text;

namespace Context.Tests;

// 164: the daily note in Core (name, text, compose, ensure).
public sealed class DailyNoteTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly TimeZoneInfo Zone = IcsParserTests.Zone;

    private readonly string parent;
    private readonly string root;
    private DateTime clock = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    public DailyNoteTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-dailynote-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private TodoStore Todos() => new(root, () => clock = clock.AddMinutes(1));

    private string Compose(CalendarFeed? feed = null) =>
        DailyNote.Compose(Todos(), new CalendarStore(root, Zone), feed, Today);

    private void WriteFile(string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\n" + string.Concat(events) + "END:VCALENDAR\r\n";

    // AC-1: the name is yyyy-MM-dd under any culture.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    [InlineData("")]
    public void NameFor_IsIsoDateUnderAnyCulture(string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("2026-11-05", DailyNote.NameFor(new DateOnly(2026, 11, 5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // AC-2: the exact text with appointments (time text, no title, location), calendar problems and due todos.
    [Fact]
    public void Format_FullDay_ExactText()
    {
        string text = DailyNote.Format(
            Today,
            [
                new Appointment("Holiday", null, At(29, 0), At(30, 0), true),
                new Appointment("Standup", "Room 1", At(29, 9), At(29, 9, 30), false),
                new Appointment(string.Empty, string.Empty, At(28, 22), At(29, 1), false),
                new Appointment("Late", null, At(29, 23), At(30, 2), false),
            ],
            ["bad.ics"],
            CalendarFeedStatus.Failed,
            "HTTP 500",
            false,
            [new DueTodo(new DateOnly(2026, 10, 27), "old one", true), new DueTodo(Today, "call Anna", false)]);

        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n"
            + "- all day Holiday\n"
            + "- 09:00-09:30 Standup (Room 1)\n"
            + "- …-01:00 (no title)\n"
            + "- 23:00-… Late\n"
            + "- Can't read calendar/bad.ics.\n"
            + "- Can't download the Exchange calendar (HTTP 500).\n"
            + "\n## Todos\n"
            + "- old one (due 2026-10-27)\n"
            + "- call Anna\n",
            text);
    }

    // AC-2: nothing there: the two empty lines; an unreadable todos.json has its own line.
    [Fact]
    public void Format_Empty_AndUnreadableTodos()
    {
        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- No appointments.\n\n## Todos\n- Nothing due.\n",
            DailyNote.Format(Today, [], [], CalendarFeedStatus.NotConfigured, null, false, []));
        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- No appointments.\n\n## Todos\n- Can't read todos.json.\n",
            DailyNote.Format(Today, [], [], CalendarFeedStatus.Stale, "timed out", true, []));
    }

    // AC-2: every inserted text is one line, so it can't add a heading or a line of its own.
    [Fact]
    public void Format_OutsideText_IsOneLine()
    {
        string text = DailyNote.Format(
            Today,
            [new Appointment("A\n# Heading\r\nB", "x\ny\u001b", At(29, 9), At(29, 10), false)],
            ["f\n## Todos"],
            CalendarFeedStatus.Failed,
            "r\nx",
            false,
            [new DueTodo(Today, "t\n# evil\ttab", false)]);

        string[] lines = text.Split('\n');
        Assert.Equal(["# 2026-10-29", "## Appointments", "## Todos"], lines.Where(l => l.StartsWith('#')).ToArray());
        Assert.Contains("- 09:00-10:00 A # Heading B (x y�)", lines);
        Assert.Contains("- Can't read calendar/f ## Todos.", lines);
        Assert.Contains("- Can't download the Exchange calendar (r x).", lines);
        Assert.Contains("- t # evil    tab", lines);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
    }

    // AC-3: due today and overdue are in, tomorrow and done are out, a recurring occurrence shows; no context.db.
    [Fact]
    public void Compose_DueTodosAndRecurringAppointment_NoIndex()
    {
        TodoStore todos = Todos();
        todos.Add("due today", Today);
        todos.Add("overdue", new DateOnly(2026, 10, 1));
        todos.Add("tomorrow", Today.AddDays(1));
        TodoItem done = todos.Add("done one", Today);
        todos.Complete(done.Id);
        WriteFile(Path.Combine(CalendarStore.FolderName, "work.ics"), Calendar(
            "BEGIN:VEVENT\r\nSUMMARY:Weekly\r\nDTSTART:20261001T090000\r\nDTEND:20261001T093000\r\n"
            + "RRULE:FREQ=WEEKLY\r\nEND:VEVENT\r\n"));

        string text = Compose();

        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- 09:00-09:30 Weekly\n\n## Todos\n"
            + "- overdue (due 2026-10-01)\n- due today\n",
            text);
        Assert.False(File.Exists(Path.Combine(root, TagIndex.FileName)));
    }

    // AC-3: a bad todos.json and an unreadable calendar file are written into the note.
    [Fact]
    public void Compose_UnreadableData_LinesInTheNote()
    {
        WriteFile("todos.json", "{nope");
        WriteFile(Path.Combine(CalendarStore.FolderName, "bad.ics"), "not a calendar");

        Assert.Equal(
            "# 2026-10-29\n\n## Appointments\n- No appointments.\n- Can't read calendar/bad.ics.\n\n## Todos\n"
            + "- Can't read todos.json.\n",
            Compose());
    }

    // AC-4: no note: the top-level note is created with compose's text (UTF-8, no BOM).
    [Fact]
    public void Ensure_NoNote_CreatesIt()
    {
        NoteStore notes = new NoteStore(root);

        DailyNoteResult result = DailyNote.Ensure(notes, Today, () => "# 2026-10-29\nü\n");

        Assert.Equal(DailyNoteKind.Created, result.Kind);
        Assert.Equal("2026-10-29", result.Name);
        Assert.Equal(Encoding.UTF8.GetBytes("# 2026-10-29\nü\n"), File.ReadAllBytes(Path.Combine(root, "2026-10-29.md")));
        Assert.Equal(["2026-10-29.md"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)).OfType<string>().ToArray());
    }

    // AC-5: an existing plain or sensitive note: compose is never called and nothing changes.
    [Theory]
    [InlineData(".md")]
    [InlineData(".ctxs")]
    public void Ensure_Existing_WritesNothing(string extension)
    {
        DailyNoteKind kind = extension == ".md" ? DailyNoteKind.Existing : DailyNoteKind.Sensitive;
        WriteFile("2026-10-29" + extension, "kept\r\n");
        WriteFile("Other.md", "other");
        File.SetLastWriteTimeUtc(Path.Combine(root, "2026-10-29" + extension), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        DailyNoteResult result = DailyNote.Ensure(
            new NoteStore(root), Today, () => throw new InvalidOperationException("compose called"));

        Assert.Equal(kind, result.Kind);
        Assert.Equal("2026-10-29", result.Name);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // AC-5: a note that appears between the check and the create counts as existing; its bytes stay.
    [Fact]
    public void Ensure_NoteAppearsInBetween_Existing()
    {
        DailyNoteResult result = DailyNote.Ensure(new NoteStore(root), Today, () =>
        {
            WriteFile("2026-10-29.md", "theirs");
            return "mine";
        });

        Assert.Equal(DailyNoteKind.Existing, result.Kind);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(root, "2026-10-29.md")));
        Assert.Equal(["2026-10-29.md"], Directory.GetFiles(root).Select(f => Path.GetFileName(f)).OfType<string>().ToArray());
    }
}
