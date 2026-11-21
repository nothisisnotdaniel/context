using System.Globalization;
using System.Text;

namespace Context.Tests;

// 167: the meeting note in Core (number, name, text, ensure).
public sealed class MeetingNoteTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public MeetingNoteTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-meetingnote-" + Guid.NewGuid().ToString("N"));
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

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);

    private static Appointment Meeting(string title, string? location = null, IReadOnlyList<string>? attendees = null) =>
        new(title, location, At(15, 9), At(15, 9, 30), false, attendees);

    private void WriteFile(string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string[] TopFiles() =>
        Directory.GetFiles(root).Select(f => Path.GetFileName(f)).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    // AC-5/AC-6: only ASCII digits giving an int of at least 1 are a number.
    [Theory]
    [InlineData("1", 1)]
    [InlineData("007", 7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("00", 0)]
    [InlineData("-1", 0)]
    [InlineData("+1", 0)]
    [InlineData(" 1", 0)]
    [InlineData("1 2", 0)]
    [InlineData("1.0", 0)]
    [InlineData("x", 0)]
    [InlineData("2147483648", 0)]
    [InlineData("99999999999999999999", 0)]
    [InlineData("١", 0)]
    [InlineData("１", 0)]
    public void TryParseNumber_DigitsOnly(string text, int expected)
    {
        bool parsed = MeetingNote.TryParseNumber(text, out int number);

        Assert.Equal(expected > 0, parsed);
        if (parsed)
        {
            Assert.Equal(expected, number);
        }
    }

    // AC-1/AC-5: the number is 1-based; none or out of range gives the fixed texts.
    [Fact]
    public void Pick_OneBased_AndRangeTexts()
    {
        Appointment a = Meeting("A");
        Appointment b = Meeting("B");

        Assert.Same(a, MeetingNote.Pick([a, b], 1, out string? none));
        Assert.Null(none);
        Assert.Same(b, MeetingNote.Pick([a, b], 2, out _));
        Assert.Null(MeetingNote.Pick([a, b], 3, out string? beyond));
        Assert.Equal("There is no appointment 3 today.", beyond);
        Assert.Null(MeetingNote.Pick([], 1, out string? empty));
        Assert.Equal("There are no appointments today.", empty);
    }

    // AC-2: the tricky titles give clean names.
    // 220 AC-1/AC-2: the title part is cleaned as before, leading dots and spaces go, a reserved stem gets "Meeting ".
    [Theory]
    [InlineData("Planning", "Planning")]
    [InlineData("  Weekly   sync  ", "Weekly sync")]
    [InlineData("Größe: Plan", "Größe Plan")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a b c d e f g h i j")]
    [InlineData("../../etc/passwd", "etc passwd")]
    [InlineData("Line\none\r\ntwo\tthree", "Line one two three")]
    [InlineData("bell\u0007x\u0085y", "bell\uFFFDx\uFFFDy")]
    [InlineData("notes.md", "notes")]
    [InlineData("a.md.md", "a")]
    [InlineData("x. .md .", "x")]
    [InlineData("X.MD", "X")]
    [InlineData("Review...", "Review")]
    [InlineData("...", "Meeting")]
    [InlineData(".md", "Meeting")]
    [InlineData("", "Meeting")]
    [InlineData("   ", "Meeting")]
    [InlineData("///", "Meeting")]
    [InlineData(".hidden", "hidden")]
    [InlineData(". .x", "x")]
    [InlineData("CON", "Meeting CON")]
    [InlineData("nul.txt", "Meeting nul.txt")]
    [InlineData(".LPT1", "Meeting LPT1")]
    [InlineData("a  b", "a b")]
    public void NameFor_CleansTitle(string title, string part)
    {
        Appointment appointment = Meeting(title);

        Assert.Equal(part, MeetingNote.TitlePartFor(appointment));
        Assert.Equal("Meetings/" + part + "/2026-11-15", MeetingNote.NameFor(appointment));
        AssertValidName(MeetingNote.NameFor(appointment));
    }

    // 220 AC-1: a long title is cut to 100 chars, never inside a surrogate pair, and cleaned again after the cut.
    [Fact]
    public void NameFor_LongTitles_CutCleanly()
    {
        Assert.Equal(new string('a', 100), MeetingNote.TitlePartFor(Meeting(new string('a', 200))));

        // The emoji's high surrogate would be char 100: the whole pair goes.
        Assert.Equal(new string('a', 99), MeetingNote.TitlePartFor(Meeting(new string('a', 99) + "\U0001F600" + "zzz")));

        // The pair fits exactly.
        Assert.Equal(
            new string('a', 98) + "\U0001F600",
            MeetingNote.TitlePartFor(Meeting(new string('a', 98) + "\U0001F600" + "zzz")));

        // The cut leaves ".md", dots and spaces at the end.
        Assert.Equal(
            new string('a', 90) + " x",
            MeetingNote.TitlePartFor(Meeting(new string('a', 90) + " x.md. .md" + "zzzzzzzzzz")));

        Assert.Equal("Meeting", MeetingNote.TitlePartFor(Meeting(new string('.', 200))));

        // "Meeting " plus a reserved part is cut and trimmed again.
        string reserved = "CON." + new string('b', 87) + " .md.x";
        string part = MeetingNote.TitlePartFor(Meeting(reserved));
        Assert.Equal("Meeting CON." + new string('b', 87), part);
        Assert.True(part.Length <= NoteStore.MaxTitleLength);
    }

    // 220 AC-1/AC-2: every name passes ValidateTitle segment by segment and equals its own NormalizeTitle.
    [Fact]
    public void NameFor_AlwaysValid()
    {
        string[] pieces = [".", " ", ".md", ".MD", "/", "\\", ":", "*", "?", "\"", "<", ">", "|", "\0", "\u001F", "\u007F", "\n", "a", "CON", "nul", "\U0001F600", "\uD800", "\uDC00", "\t"];
        Random random = new Random(167);
        for (int i = 0; i < 3000; i++)
        {
            StringBuilder title = new StringBuilder();
            int count = random.Next(0, 60);
            for (int j = 0; j < count; j++)
            {
                title.Append(pieces[random.Next(pieces.Length)]);
            }

            Appointment appointment = Meeting(title.ToString());
            AssertValidName(MeetingNote.NameFor(appointment));
            AssertValidLegacyName(MeetingNote.LegacyNameFor(appointment));
        }
    }

    // 220 AC-1: the legacy name keeps today's rule (89-character cut, leading dots and reserved stems stay).
    [Theory]
    [InlineData("Planning", "2026-11-15 Planning")]
    [InlineData("  Weekly   sync  ", "2026-11-15 Weekly sync")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "2026-11-15 a b c d e f g h i j")]
    [InlineData("../../etc/passwd", "2026-11-15 .. .. etc passwd")]
    [InlineData("Line\none\r\ntwo\tthree", "2026-11-15 Line one two three")]
    [InlineData("notes.md", "2026-11-15 notes")]
    [InlineData("x. .md .", "2026-11-15 x")]
    [InlineData("...", "2026-11-15 Meeting")]
    [InlineData("///", "2026-11-15 Meeting")]
    [InlineData(".hidden", "2026-11-15 .hidden")]
    [InlineData("CON", "2026-11-15 CON")]
    [InlineData("nul.txt", "2026-11-15 nul.txt")]
    public void LegacyNameFor_CleansTitle(string title, string expected)
    {
        string name = MeetingNote.LegacyNameFor(Meeting(title));

        Assert.Equal(expected, name);
        AssertValidLegacyName(name);
    }

    // 220 AC-1: the legacy cut stays at 89 chars, never inside a surrogate pair.
    [Fact]
    public void LegacyNameFor_LongTitles_CutAt89()
    {
        Assert.Equal("2026-11-15 " + new string('a', 89), MeetingNote.LegacyNameFor(Meeting(new string('a', 200))));
        Assert.Equal(
            "2026-11-15 " + new string('a', 88),
            MeetingNote.LegacyNameFor(Meeting(new string('a', 88) + "\U0001F600" + "zzz")));
        Assert.Equal(
            "2026-11-15 " + new string('a', 79) + " x",
            MeetingNote.LegacyNameFor(Meeting(new string('a', 79) + " x.md. .md" + "zzzzzzzzzz")));
    }

    // 220 AC-11: the new and the legacy name are this meeting's note, in any case; nothing else is.
    [Fact]
    public void IsNoteOf_NewAndLegacyNames()
    {
        Appointment appointment = Meeting("Planning");

        Assert.True(MeetingNote.IsNoteOf(appointment, "Meetings/Planning/2026-11-15"));
        Assert.True(MeetingNote.IsNoteOf(appointment, "meetings/PLANNING/2026-11-15"));
        Assert.True(MeetingNote.IsNoteOf(appointment, "2026-11-15 Planning"));
        Assert.True(MeetingNote.IsNoteOf(appointment, "2026-11-15 planning"));
        Assert.False(MeetingNote.IsNoteOf(appointment, "Meetings/Planning"));
        Assert.False(MeetingNote.IsNoteOf(appointment, "Meetings/Planning/2026-11-16"));
        Assert.False(MeetingNote.IsNoteOf(appointment, "2026-11-16 Planning"));
        Assert.False(MeetingNote.IsNoteOf(appointment, "Planning"));
        Assert.False(MeetingNote.IsNoteOf(appointment, "Work/2026-11-15 Planning"));
        Assert.False(MeetingNote.IsNoteOf(appointment, string.Empty));
    }

    // AC-2: the date is the start's local date, invariant under any culture.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    [InlineData("")]
    public void NameFor_InvariantDate(string culture)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Appointment late = new Appointment("Late", null, At(14, 23), At(15, 1), false);
            Assert.Equal("Meetings/Late/2026-11-14", MeetingNote.NameFor(late));
            Assert.Equal("2026-11-14 Late", MeetingNote.LegacyNameFor(late));
            Assert.Equal("# 2026-11-14", MeetingNote.Format(late).Split('\n')[0]);
            Assert.Equal("- Time: 2026-11-14 23:00 to 2026-11-15 01:00", MeetingNote.Format(late).Split('\n')[5]);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // AC-3: the exact text with location and attendees, valid and invalid as mentions.
    [Fact]
    public void Format_ExactText()
    {
        Appointment appointment = new Appointment(
            "Budget review",
            "Room 4",
            At(15, 14),
            At(15, 15, 30),
            false,
            ["Anna Schmidt", "Bob [ext]", "Eve*", "CON"]);

        string text = MeetingNote.Format(appointment);

        Assert.Equal(
            "# 2026-11-15\n\n#meeting\n\n- Title: Budget review\n- Time: 2026-11-15 14:00-15:30\n"
            + "- Location: Room 4\n- Attendees: @[Anna Schmidt], Bob [ext], Eve*, CON\n"
            + "\n## Notes\n\n## Decisions\n\n## Actions\n",
            text);
        Assert.Contains(MeetingNote.Tag, TagParser.Parse(text));
        Assert.Equal(["Anna Schmidt"], PersonParser.Parse(text));
    }

    // AC-3: no title, no location, no attendees.
    [Fact]
    public void Format_Empty()
    {
        Appointment appointment = new Appointment(string.Empty, null, At(15, 9), At(15, 10), false);

        Assert.Equal(
            "# 2026-11-15\n\n#meeting\n\n- Title: (no title)\n- Time: 2026-11-15 09:00-10:00\n- Attendees: none\n"
            + "\n## Notes\n\n## Decisions\n\n## Actions\n",
            MeetingNote.Format(appointment));
        Assert.DoesNotContain("Location", MeetingNote.Format(new Appointment("x", string.Empty, At(15, 9), At(15, 10), false)));
    }

    // AC-3: the five time shapes.
    [Theory]
    [InlineData(15, 9, 0, 15, 10, 30, false, "2026-11-15 09:00-10:30")]
    [InlineData(15, 9, 0, 15, 9, 0, false, "2026-11-15 09:00")]
    [InlineData(15, 22, 0, 16, 1, 0, false, "2026-11-15 22:00 to 2026-11-16 01:00")]
    [InlineData(15, 22, 0, 16, 0, 0, false, "2026-11-15 22:00 to 2026-11-16 00:00")]
    [InlineData(15, 0, 0, 16, 0, 0, true, "2026-11-15 all day")]
    [InlineData(15, 0, 0, 15, 0, 0, true, "2026-11-15 all day")]
    [InlineData(14, 0, 0, 17, 0, 0, true, "2026-11-14 to 2026-11-16 all day")]
    public void Format_TimeShapes(int sd, int sh, int sm, int ed, int eh, int em, bool allDay, string expected)
    {
        Appointment appointment = new Appointment("T", null, At(sd, sh, sm), At(ed, eh, em), allDay);

        Assert.Equal("- Time: " + expected, MeetingNote.Format(appointment).Split('\n')[5]);
    }

    // AC-3: every inserted value is one line; control characters can't start a line of their own.
    [Fact]
    public void Format_OutsideText_IsOneLine()
    {
        Appointment appointment = new Appointment(
            "Evil\n# heading\r\n- item",
            "Here\nthere\u0007",
            At(15, 9),
            At(15, 10),
            false,
            ["Name\nNext"]);

        string[] lines = MeetingNote.Format(appointment).Split('\n');

        Assert.Equal(
            [
                "# 2026-11-15",
                string.Empty,
                "#meeting",
                string.Empty,
                "- Title: Evil # heading - item",
                "- Time: 2026-11-15 09:00-10:00",
                "- Location: Here there�",
                "- Attendees: Name Next",
                string.Empty,
                "## Notes",
                string.Empty,
                "## Decisions",
                string.Empty,
                "## Actions",
                string.Empty,
            ],
            lines);
    }

    // 220 AC-3: an empty folder gets Meetings, Meetings/<part> and the note with Format's exact bytes.
    [Fact]
    public void Ensure_NoNote_CreatesParentsAndNote()
    {
        Appointment appointment = Meeting("Größe: Plan", "Raum", ["Jürgen"]);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), appointment);

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal("Meetings/Größe Plan/2026-11-15", result.Name);
        byte[] note = File.ReadAllBytes(Path.Combine(root, "Meetings", "Größe Plan", "2026-11-15.md"));
        Assert.Equal(new UTF8Encoding(false).GetBytes(MeetingNote.Format(appointment)), note);
        Assert.Contains(MeetingNote.Tag, TagParser.Parse(Encoding.UTF8.GetString(note)));
        Assert.Equal(Encoding.UTF8.GetBytes("# Meetings\n"), File.ReadAllBytes(Path.Combine(root, "Meetings.md")));
        Assert.Equal(
            Encoding.UTF8.GetBytes("# Größe Plan\n"),
            File.ReadAllBytes(Path.Combine(root, "Meetings", "Größe Plan.md")));
        Assert.Equal(["Meetings.md"], TopFiles());
        Assert.Equal(
            ["Meetings.md", "Meetings/Größe Plan.md", "Meetings/Größe Plan/2026-11-15.md"],
            FileSnapshot.Take(root).Keys.ToArray());
    }

    // 220 AC-4: existing parents in another case are reused unchanged and their spelling is kept.
    [Fact]
    public void Ensure_ExistingParents_ReusedInTheirSpelling()
    {
        WriteFile("meetings.md", "mine\r\n");
        WriteFile("meetings/planning.md", "plan\r\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal("meetings/planning/2026-11-15", result.Name);
        SortedDictionary<string, string> after = FileSnapshot.Take(root);
        Assert.Equal(before["meetings.md"], after["meetings.md"]);
        Assert.Equal(before["meetings/planning.md"], after["meetings/planning.md"]);
        Assert.Equal(["meetings.md", "meetings/planning.md", "meetings/planning/2026-11-15.md"], after.Keys.ToArray());
    }

    // 220 AC-4: only the Meetings page exists: it is reused and the meeting's page is created under it.
    [Fact]
    public void Ensure_OnlyMeetingsPage_CreatesMeetingPage()
    {
        WriteFile("MEETINGS.md", "top");

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal("MEETINGS/Planning/2026-11-15", result.Name);
        Assert.Equal("top", File.ReadAllText(Path.Combine(root, "MEETINGS.md")));
        Assert.Equal("# Planning\n", File.ReadAllText(Path.Combine(root, "MEETINGS", "Planning.md")));
    }

    // 220 AC-5: an existing new-style note (any case) gives Existing with its name and no file changes.
    [Fact]
    public void Ensure_ExistingNote_WritesNothing()
    {
        WriteFile("Meetings.md", "# Meetings\n");
        WriteFile("Meetings/PLANNING.md", "# PLANNING\n");
        WriteFile("Meetings/PLANNING/2026-11-15.md", "kept\r\n");
        File.SetLastWriteTimeUtc(
            Path.Combine(root, "Meetings", "PLANNING", "2026-11-15.md"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(MeetingNoteKind.Existing, result.Kind);
        Assert.Equal("Meetings/PLANNING/2026-11-15", result.Name);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 220 AC-6: a legacy plain note (also in another case) or a sealed one: its name, nothing is written.
    [Theory]
    [InlineData("2026-11-15 Planning.md", "2026-11-15 Planning")]
    [InlineData("2026-11-15 PLANNING.md", "2026-11-15 PLANNING")]
    [InlineData("2026-11-15 Planning.ctxs", "2026-11-15 Planning")]
    [InlineData("2026-11-15 planning.ctxs", "2026-11-15 planning")]
    public void Ensure_LegacyNote_WritesNothing(string file, string name)
    {
        MeetingNoteKind kind = file.EndsWith(".md", StringComparison.Ordinal) ? MeetingNoteKind.Existing : MeetingNoteKind.Sensitive;
        WriteFile(file, "kept\r\n");
        WriteFile("Other.md", "other");
        File.SetLastWriteTimeUtc(Path.Combine(root, file), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(kind, result.Kind);
        Assert.Equal(name, result.Name);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 220 AC-6: the legacy name keeps its own rule: a ".hidden" meeting finds "2026-11-15 .hidden".
    [Fact]
    public void Ensure_LegacyNoteWithLeadingDot_Found()
    {
        WriteFile("2026-11-15 .hidden.md", "kept");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting(".hidden"));

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Existing, "2026-11-15 .hidden"), result);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 220 AC-6: a new-style note wins over a legacy one.
    [Fact]
    public void Ensure_NewAndLegacy_NewWins()
    {
        WriteFile("2026-11-15 Planning.ctxs", "sealed");
        WriteFile("Meetings.md", "# Meetings\n");
        WriteFile("Meetings/Planning.md", "# Planning\n");
        WriteFile("Meetings/Planning/2026-11-15.md", "new");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Existing, "Meetings/Planning/2026-11-15"), result);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 220: a subpage of the legacy name elsewhere doesn't count: the new-style note is made.
    [Fact]
    public void Ensure_SubpageOfLegacyName_CreatesNewNote()
    {
        WriteFile("Work.md", "# Work\n");
        WriteFile("Work/2026-11-15 Planning.md", "sub");

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Created, "Meetings/Planning/2026-11-15"), result);
        Assert.Equal("sub", File.ReadAllText(Path.Combine(root, "Work", "2026-11-15 Planning.md")));
    }

    // 220 AC-7: a note that appears between the check and the create counts as existing; its bytes stay.
    [Fact]
    public void Ensure_NoteAppearsInBetween_Existing()
    {
        MeetingNoteResult result = MeetingNote.Ensure(
            new NoteStore(root),
            Meeting("Planning"),
            () =>
            {
                WriteFile("Meetings.md", "# Meetings\n");
                WriteFile("Meetings/Planning.md", "# Planning\n");
                WriteFile("Meetings/Planning/2026-11-15.md", "theirs");
            });

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Existing, "Meetings/Planning/2026-11-15"), result);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(root, "Meetings", "Planning", "2026-11-15.md")));
        Assert.Equal("# Meetings\n", File.ReadAllText(Path.Combine(root, "Meetings.md")));
        Assert.Equal("# Planning\n", File.ReadAllText(Path.Combine(root, "Meetings", "Planning.md")));
    }

    // 220 AC-7: parent pages that appear in between are reused unchanged and the note is created under them.
    [Fact]
    public void Ensure_ParentsAppearInBetween_Reused()
    {
        MeetingNoteResult result = MeetingNote.Ensure(
            new NoteStore(root),
            Meeting("Planning"),
            () =>
            {
                WriteFile("meetings.md", "top");
                WriteFile("meetings/planning.md", "page");
            });

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Created, "meetings/planning/2026-11-15"), result);
        Assert.Equal("top", File.ReadAllText(Path.Combine(root, "meetings.md")));
        Assert.Equal("page", File.ReadAllText(Path.Combine(root, "meetings", "planning.md")));
        Assert.Equal(["meetings.md"], TopFiles());
    }

    // 220 AC-8: a sensitive top-level Meetings: a plain IOException, nothing written.
    [Fact]
    public void Ensure_SensitiveMeetings_ThrowsIOException()
    {
        WriteFile("Meetings.ctxs", "sealed");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        IOException error = Assert.Throws<IOException>(() => MeetingNote.Ensure(new NoteStore(root), Meeting("Planning")));

        Assert.IsNotType<NoteExistsException>(error);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 220 AC-8: a sensitive Meetings that appears in between is a plain IOException too.
    [Fact]
    public void Ensure_SensitiveMeetingsAppearsInBetween_ThrowsIOException()
    {
        IOException error = Assert.Throws<IOException>(() => MeetingNote.Ensure(
            new NoteStore(root), Meeting("Planning"), () => WriteFile("meetings.ctxs", "sealed")));

        Assert.IsNotType<NoteExistsException>(error);
        Assert.Equal(["meetings.ctxs"], TopFiles());
    }

    // 245 ------------------------------------------------------------------------------------------

    private static Appointment Weekly() => new("Weekly", null, new DateTime(2026, 11, 19, 9, 0, 0), new DateTime(2026, 11, 19, 9, 30, 0), false, null);

    private string NotePath(string name) => Path.Combine([root, .. name.Split('/')]) + ".md";

    private void WriteWeekly(string date, string text)
    {
        WriteFile("Meetings.md", "# Meetings\n");
        WriteFile(Path.Combine("Meetings", "Weekly.md"), "# Weekly\n");
        WriteFile(Path.Combine("Meetings", "Weekly", date + ".md"), text);
    }

    private string Created() => File.ReadAllText(NotePath("Meetings/Weekly/2026-11-19"));

    // 245 AC-1/AC-6: the open todo of the previous note is carried over with a link; the old note is unchanged.
    [Fact]
    public void Ensure_PreviousNote_CarriesOverOpenPoints()
    {
        WriteWeekly("2026-11-12", "# 2026-11-12\n\n- [ ] Send budget\n- [x] Done thing\n");
        string previous = NotePath("Meetings/Weekly/2026-11-12");
        DateTime written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(previous, written);
        byte[] bytes = File.ReadAllBytes(previous);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly());

        Assert.Equal(new MeetingNoteResult(MeetingNoteKind.Created, "Meetings/Weekly/2026-11-19"), result);
        Assert.Equal(
            MeetingNote.Format(Weekly()) + "\n## From last time\n\nFrom [[Meetings/Weekly/2026-11-12]]:\n\n- [ ] Send budget\n",
            Created());
        Assert.Equal(bytes, File.ReadAllBytes(previous));
        Assert.Equal(written, File.GetLastWriteTimeUtc(previous));
    }

    // 330 AC-3: a "[>]" line of the previous note, also one inside Open points, comes over once as "- [ ]"; the
    // previous note's bytes stay as they were.
    [Fact]
    public void Ensure_CarriedLines_CopiedAsOpenCheckboxes_PreviousUnchanged()
    {
        WriteWeekly("2026-11-12", "# 2026-11-12\n\n- [>] Ask about budget\n- [x] Done\n## Open points\n- [>] Hiring plan\n");
        string previous = NotePath("Meetings/Weekly/2026-11-12");
        byte[] bytes = File.ReadAllBytes(previous);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly());

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal(
            MeetingNote.Format(Weekly())
                + "\n## From last time\n\nFrom [[Meetings/Weekly/2026-11-12]]:\n\n- [ ] Ask about budget\n- [ ] Hiring plan\n",
            Created());
        Assert.Equal(bytes, File.ReadAllBytes(previous));
    }

    // 245 AC-2: only the latest earlier occurrence is copied.
    [Fact]
    public void Ensure_SeveralOccurrences_LatestEarlierCopied()
    {
        WriteWeekly("2026-11-05", "- [ ] old\n");
        WriteWeekly("2026-11-12", "- [ ] recent\n");
        WriteWeekly("2026-11-26", "- [ ] later\n");
        WriteFile(Path.Combine("Meetings", "Weekly", "Agenda.md"), "- [ ] agenda\n");

        MeetingNote.Ensure(new NoteStore(root), Weekly());

        Assert.EndsWith("From [[Meetings/Weekly/2026-11-12]]:\n\n- [ ] recent\n", Created(), StringComparison.Ordinal);
    }

    // 245 AC-5: no previous note, or one with nothing open: exactly Format's text.
    [Theory]
    [InlineData(null)]
    [InlineData("# 2026-11-12\n\n- [x] Done thing\nnotes\n")]
    public void Ensure_NothingToCarry_FormatOnly(string? previous)
    {
        if (previous is not null)
        {
            WriteWeekly("2026-11-12", previous);
        }

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly());

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal(new UTF8Encoding(false).GetBytes(MeetingNote.Format(Weekly())), File.ReadAllBytes(NotePath("Meetings/Weekly/2026-11-19")));
    }

    // 245 AC-7: an existing note (new-style, legacy plain or sealed) is returned without reading the previous note.
    [Theory]
    [InlineData("Meetings/Weekly/2026-11-19.md")]
    [InlineData("2026-11-19 Weekly.md")]
    [InlineData("2026-11-19 Weekly.ctxs")]
    public void Ensure_ExistingNote_PreviousNotRead(string file)
    {
        MeetingNoteKind kind = file.EndsWith(".ctxs", StringComparison.Ordinal) ? MeetingNoteKind.Sensitive : MeetingNoteKind.Existing;
        WriteWeekly("2026-11-12", "- [ ] Send budget\n");
        WriteFile(file, "kept");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);
        int reads = 0;

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly(), null, _ => reads++);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(0, reads);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 245 AC-8: a previous note over 1 MiB gives a note without the section.
    [Fact]
    public void Ensure_PreviousTooLarge_NoSection()
    {
        WriteWeekly("2026-11-12", "- [ ] Send budget\n" + new string('x', (int)MeetingCarryOver.MaxPreviousBytes));

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly());

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal(MeetingNote.Format(Weekly()), Created());
    }

    // 245 AC-8: a previous note whose read throws (gone, or access denied) gives a note without the section.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ensure_PreviousUnreadable_NoSection(bool removed)
    {
        WriteWeekly("2026-11-12", "- [ ] Send budget\n");

        MeetingNoteResult result = MeetingNote.Ensure(
            new NoteStore(root),
            Weekly(),
            null,
            note =>
            {
                if (removed)
                {
                    File.Delete(note.Path);
                    return;
                }

                throw new UnauthorizedAccessException();
            });

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal(MeetingNote.Format(Weekly()), Created());
    }

    // A new-style name: Meetings, a valid title part and the date, each a valid segment.
    private static void AssertValidName(string name)
    {
        string[] segments = name.Split('/');
        Assert.Equal(3, segments.Length);
        Assert.Equal("Meetings", segments[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", segments[2]);
        foreach (string segment in segments)
        {
            Assert.Null(NoteStore.ValidateTitle(segment));
            Assert.Equal(segment, NoteStore.NormalizeTitle(segment));
            Assert.True(segment.Length <= NoteStore.MaxTitleLength);
            Assert.DoesNotContain(segment, c => "/\\:*?\"<>|".Contains(c, StringComparison.Ordinal) || char.IsControl(c));
        }
    }

    private static void AssertValidLegacyName(string name)
    {
        Assert.Null(NoteStore.ValidateTitle(name));
        Assert.Equal(name, NoteStore.NormalizeTitle(name));
        Assert.True(name.Length <= NoteStore.MaxTitleLength);
        Assert.DoesNotContain(name, c => "/\\:*?\"<>|".Contains(c, StringComparison.Ordinal) || char.IsControl(c));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} ", name);
    }

    private static NoteTopic Topic(string note, int line, string person, string text) =>
        new(note, line, person, text, "next: @[" + person + "] " + text);

    // 253 AC-6: the topics section sits before "## From last time", in the stated format.
    [Fact]
    public void Ensure_Topics_SectionBeforeCarryOver()
    {
        WriteWeekly("2026-11-12", "- [ ] Send budget\n");
        NoteTopic[] topics =
        [
            Topic("Plan", 3, "Ann Lee", "budget  \u0007 review"),
            Topic("people/Bob", 1, "Bob", "travel"),
            Topic("A [x] #b", 2, "Bob", "odd"),
            Topic("Tick`s", 2, "Bob", "tick"),
        ];

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly(), topics: topics);

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal(
            MeetingNote.Format(Weekly())
                + "\n## Topics\n\n"
                + "- @[Ann Lee]: " + SafeText.Clean("budget  \u0007 review").Replace('\n', ' ') + " (from [[Plan]])\n"
                + "- @[Bob]: travel (from [[people/Bob]])\n"
                + "- @[Bob]: odd (from A [x] #b)\n"
                + "- @[Bob]: tick (from Tick`s)\n"
                + "\n## From last time\n\nFrom [[Meetings/Weekly/2026-11-12]]:\n\n- [ ] Send budget\n",
            Created());
    }

    // 253 AC-6: at most 200 topic lines, then one line with the rest's count.
    [Fact]
    public void Ensure_ManyTopics_Cut()
    {
        NoteTopic[] topics = Enumerable.Range(1, 203).Select(i => Topic("Plan", i, "Ann", "t" + i)).ToArray();

        MeetingNote.Ensure(new NoteStore(root), Weekly(), topics: topics);

        string[] lines = Created().Split('\n');
        Assert.Equal(200, lines.Count(l => l.StartsWith("- @[Ann]: ", StringComparison.Ordinal)));
        Assert.Contains("- @[Ann]: t200 (from [[Plan]])", lines);
        Assert.EndsWith("- … and 3 more.\n", Created(), StringComparison.Ordinal);
    }

    // 253 AC-6: an existing note is never written, topics or not.
    [Fact]
    public void Ensure_Topics_ExistingNoteWritesNothing()
    {
        WriteWeekly("2026-11-19", "kept\r\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Weekly(), topics: [Topic("Plan", 1, "Ann", "x")]);

        Assert.Equal(MeetingNoteKind.Existing, result.Kind);
        Assert.Equal(before, FileSnapshot.Take(root));
    }

    // 253 AC-6: with null or no topics the bytes are exactly today's output.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ensure_NoTopics_SameBytes(bool empty)
    {
        WriteWeekly("2026-11-12", "- [ ] Send budget\n");
        string other = Path.Combine(parent, "Other");
        Directory.CreateDirectory(other);
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(other, Path.GetRelativePath(root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        MeetingNote.Ensure(new NoteStore(other), Weekly());
        MeetingNote.Ensure(new NoteStore(root), Weekly(), topics: empty ? [] : null);

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(other, "Meetings", "Weekly", "2026-11-19.md")),
            File.ReadAllBytes(NotePath("Meetings/Weekly/2026-11-19")));
    }

    // 381 ------------------------------------------------------------------------------------------

    private static readonly DateTime Noon = At(15, 12);

    private static Appointment Timed(string title, int hour, int minute, int endHour, int endMinute) =>
        new(title, null, At(15, hour, minute), At(15, endHour, endMinute), false);

    private static Appointment AllDay(string title) => new(title, null, At(15, 0), At(16, 0), true);

    private static Appointment? Select(IReadOnlyList<Appointment> list, string argument, DateTime now, out string? error) =>
        MeetingNote.Select(list, argument, now, out error);

    // 381 AC-1: no argument (or blank) picks the first running timed appointment; all-day ones are never picked.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t ")]
    public void Select_Blank_PicksRunning(string argument)
    {
        Appointment allDay = AllDay("Holiday");
        Appointment first = Timed("First", 11, 0, 13, 0);
        Appointment second = Timed("Second", 11, 30, 12, 30);
        Appointment later = Timed("Later", 14, 0, 15, 0);

        Assert.Same(first, Select([allDay, later, first, second], argument, Noon, out string? error));
        Assert.Null(error);
    }

    // 381 AC-1: nothing running: the timed one with the earliest start after now, ties in list order; an end equal to
    // now no longer runs and a start equal to now runs.
    [Fact]
    public void Select_Blank_NothingRunning_PicksNextEarliest_TiesInListOrder()
    {
        Appointment past = Timed("Past", 9, 0, 12, 0);
        Appointment late = Timed("Late", 16, 0, 17, 0);
        Appointment tieA = Timed("Tie A", 14, 0, 15, 0);
        Appointment tieB = Timed("Tie B", 14, 0, 14, 30);

        Assert.Same(tieA, Select([past, late, tieA, tieB], string.Empty, Noon, out _));
        Assert.Same(late, Select([past, late], string.Empty, Noon, out _));
        Appointment startsNow = Timed("Now", 12, 0, 12, 30);
        Assert.Same(startsNow, Select([past, late, startsNow], string.Empty, Noon, out _));
    }

    // 381 AC-1: only all-day or past appointments: the fixed line; none at all: the old line.
    [Fact]
    public void Select_Blank_NoneLeft_Errors()
    {
        Assert.Null(Select([AllDay("Holiday"), Timed("Past", 9, 0, 10, 0)], string.Empty, Noon, out string? error));
        Assert.Equal("There is no current or next appointment today.", error);
        Assert.Equal(MeetingNote.NoCurrentText, error);

        Assert.Null(Select([], string.Empty, Noon, out error));
        Assert.Equal("There are no appointments today.", error);
        Assert.Null(Select([], "Standup", Noon, out error));
        Assert.Equal("There are no appointments today.", error);
    }

    // 381 AC-2: digits only (as given) is the number, as Pick; an all-day appointment can be picked by number.
    [Fact]
    public void Select_Digits_IsTheNumber()
    {
        Appointment allDay = AllDay("Holiday");
        Appointment timed = Timed("Standup", 9, 0, 9, 15);

        Assert.Same(allDay, Select([allDay, timed], "1", Noon, out _));
        Assert.Same(timed, Select([allDay, timed], "02", Noon, out _));
        Assert.Null(Select([allDay, timed], "3", Noon, out string? error));
        Assert.Equal("There is no appointment 3 today.", error);
    }

    // 381 AC-3: H:mm and HH:mm pick the first timed appointment with start <= t < end; no match is the fixed line.
    [Theory]
    [InlineData("9:30", "Standup")]
    [InlineData("09:30", "Standup")]
    [InlineData(" 9:30 ", "Standup")]
    [InlineData("10:00", "Review")]
    [InlineData("10:59", "Review")]
    public void Select_Time_PicksRunningThen(string argument, string expected)
    {
        Appointment allDay = AllDay("Holiday");
        Appointment standup = Timed("Standup", 9, 0, 10, 0);
        Appointment review = Timed("Review", 10, 0, 11, 0);
        Appointment overlap = Timed("Overlap", 9, 15, 10, 30);

        Assert.Equal(expected, Select([allDay, standup, review, overlap], argument, Noon, out _)?.Title);
    }

    [Theory]
    [InlineData("11:00", "There is no appointment at 11:00 today.")]
    [InlineData("08:05", "There is no appointment at 8:05 today.")]
    [InlineData("0:00", "There is no appointment at 0:00 today.")]
    [InlineData("23:59", "There is no appointment at 23:59 today.")]
    public void Select_Time_NoMatch_FixedLine(string argument, string expected)
    {
        Assert.Null(Select([AllDay("Holiday"), Timed("Review", 10, 0, 11, 0)], argument, Noon, out string? error));
        Assert.Equal(expected, error);
    }

    // 381 AC-3/AC-4: only a real time is a time; 25:00, 9:5 or 9.30 are titles.
    [Theory]
    [InlineData("9:30", true)]
    [InlineData("09:30", true)]
    [InlineData("23:59", true)]
    [InlineData("0:00", true)]
    [InlineData("24:00", false)]
    [InlineData("25:00", false)]
    [InlineData("9:60", false)]
    [InlineData("9:5", false)]
    [InlineData("009:30", false)]
    [InlineData("9.30", false)]
    [InlineData(":30", false)]
    [InlineData("９:30", false)]
    public void TryParseTime_RealTimesOnly(string text, bool expected)
    {
        Assert.Equal(expected, MeetingNote.TryParseTime(text, out _));
    }

    [Fact]
    public void Select_InvalidTime_IsATitle()
    {
        Appointment odd = Timed("Call 25:00 sharp", 9, 0, 10, 0);

        Assert.Same(odd, Select([Timed("Other", 10, 0, 11, 0), odd], "25:00", Noon, out _));
        Assert.Null(Select([odd], "24:00", Noon, out string? error));
        Assert.Equal("There is no appointment called 24:00 today.", error);
    }

    // 381 AC-4: an exact title (ignoring case) beats an earlier contains match; else the first containing it.
    [Fact]
    public void Select_Title_ExactBeatsContains_ThenFirstContains()
    {
        Appointment weeklyReview = Timed("Weekly review", 9, 0, 10, 0);
        Appointment review = Timed("Review", 13, 0, 14, 0);
        Appointment budgetReview = Timed("Budget REVIEW", 15, 0, 16, 0);

        Assert.Same(review, Select([weeklyReview, review, budgetReview], "review", Noon, out _));
        Assert.Same(weeklyReview, Select([weeklyReview, budgetReview], "REVIEW", Noon, out _));
        Assert.Same(budgetReview, Select([weeklyReview, budgetReview], "budget   review", Noon, out _));
        Appointment holiday = AllDay("Holiday");
        Assert.Same(holiday, Select([weeklyReview, holiday], "holiday", Noon, out _));
    }

    // 381 AC-4: no match is one line naming the text; outside text stays one line.
    [Fact]
    public void Select_Title_NoMatch_OneLine()
    {
        Assert.Null(Select([Timed("Review", 9, 0, 10, 0)], "Planning", Noon, out string? error));
        Assert.Equal("There is no appointment called Planning today.", error);

        Assert.Null(Select([Timed("Review", 9, 0, 10, 0)], "a\nb\u0007", Noon, out error));
        Assert.Equal("There is no appointment called a b� today.", error);
        Assert.DoesNotContain('\n', error!);
    }

    // 381 AC-5: a new note is Format (now ending in the three sections), then the topics, then the carry-over.
    [Fact]
    public void Ensure_WithTopicsAndCarryOver_ExactBytes()
    {
        WriteWeekly("2026-11-12", "# 2026-11-12\n\n## Actions\n\n- { } Send budget\n- [ ] Book room\n");
        NoteTopic topic = new NoteTopic("people/Anna", 3, "Anna", "Ask about Q4", "next: @[Anna] Ask about Q4");

        MeetingNote.Ensure(new NoteStore(root), Weekly(), topics: [topic]);

        Assert.Equal(
            "# 2026-11-19\n\n#meeting\n\n- Title: Weekly\n- Time: 2026-11-19 09:00-09:30\n- Attendees: none\n"
            + "\n## Notes\n\n## Decisions\n\n## Actions\n"
            + "\n## Topics\n\n- @[Anna]: Ask about Q4 (from [[people/Anna]])\n"
            + "\n## From last time\n\nFrom [[Meetings/Weekly/2026-11-12]]:\n\n- { } Send budget\n- [ ] Book room\n",
            Created());
    }

    // 381 AC-5: an existing note is never changed (no sections added).
    [Fact]
    public void Ensure_ExistingNote_NoSectionsAdded()
    {
        WriteWeekly("2026-11-19", "mine\n");
        byte[] before = File.ReadAllBytes(NotePath("Meetings/Weekly/2026-11-19"));

        Assert.Equal(MeetingNoteKind.Existing, MeetingNote.Ensure(new NoteStore(root), Weekly()).Kind);
        Assert.Equal(before, File.ReadAllBytes(NotePath("Meetings/Weekly/2026-11-19")));
    }

    // 381 AC-9: the booking name drops every #<n>/AB#<n> token the parser finds, collapses, trims and caps at 200.
    [Theory]
    [InlineData("Sprint review AB#1234", "Sprint review")]
    [InlineData("#12 Planning  #34 sync", "Planning sync")]
    [InlineData("ab#7 Retro", "Retro")]
    [InlineData("Release #1a and xAB#12 and AB#0", "Release #1a and xAB#12 and AB#0")]
    [InlineData("Release #1.0", "Release .0")]
    [InlineData("AB#12a", "AB#12a")]
    [InlineData("AB#1234", "Meeting")]
    [InlineData("", "Meeting")]
    [InlineData("Line\nbreak #5", "Line break")]
    [InlineData("`#12` code", "`#12` code")]
    [InlineData("#05 Weekly", "05 Weekly")]
    [InlineData("#007 Bond", "007 Bond")]
    [InlineData("#0 Zero", "0 Zero")]
    [InlineData("#abc Topic", "abc Topic")]
    [InlineData("## - #05 Weekly", "05 Weekly")]
    [InlineData("--version", "version")]
    [InlineData("-x #1", "x")]
    [InlineData("#", "Meeting")]
    [InlineData("--", "Meeting")]
    [InlineData("Weekly #05", "Weekly #05")]
    public void BookingName_RemovesWorkItems(string title, string expected)
    {
        Assert.Equal(expected, MeetingNote.BookingName(new Appointment(title, null, At(15, 9), At(15, 10), false)));
    }

    [Fact]
    public void BookingName_CappedAt200()
    {
        string name = MeetingNote.BookingName(new Appointment(new string('x', 300), null, At(15, 9), At(15, 10), false));

        Assert.Equal(new string('x', 200), name);
        Assert.Equal(200, MeetingNote.MaxBookingName);
    }

    // 381 AC-9: checkpoint, H:mm, #<position> when the note's first work item is on the quicklist, then the name.
    [Fact]
    public void BookingArguments_QuicklistHitAndMiss()
    {
        Appointment appointment = new Appointment("Sprint AB#1234", null, At(15, 9, 5), At(15, 10), false);
        IReadOnlyList<(int WorkItem, string Title)> projects = [(77, "Other"), (1234, "Sprint"), (1234, "Again")];

        Assert.Equal(
            ["checkpoint", "9:05", "#2", "Sprint"],
            MeetingNote.BookingArguments(appointment, "- Title: Sprint AB#1234\nsee #77\n", projects));
        Assert.Equal(
            ["checkpoint", "9:05", "#1", "Sprint"],
            MeetingNote.BookingArguments(appointment, "#77 first, then AB#1234", projects));
        Assert.Equal(
            ["checkpoint", "9:05", "Sprint"],
            MeetingNote.BookingArguments(appointment, "#555 only", projects));
        Assert.Equal(["checkpoint", "9:05", "Sprint"], MeetingNote.BookingArguments(appointment, "no items", projects));
        Assert.Equal(["checkpoint", "9:05", "Sprint"], MeetingNote.BookingArguments(appointment, "AB#1234", []));
    }

    // 381 AC-9/AC-10: the texts for each run result; Times' output is one cleaned, capped line.
    [Fact]
    public void BookingOutcome_TextsPerKind()
    {
        Appointment appointment = new Appointment("Sprint #9", null, At(15, 14, 0), At(15, 15), false);

        Assert.True(MeetingNote.BookingOutcome(appointment, new TimesRunResult(TimesRunKind.Done, 0, "ok", string.Empty), out string text));
        Assert.Equal("Booked 14:00 Sprint in Times.", text);
        Assert.False(MeetingNote.BookingOutcome(appointment, TimesRunResult.NotFound(), out text));
        Assert.Equal("Times isn't installed, so nothing was booked.", text);
        Assert.False(MeetingNote.BookingOutcome(appointment, TimesRunResult.TimedOut(), out text));
        Assert.Equal("Times didn't answer in time.", text);
        Assert.False(MeetingNote.BookingOutcome(
            appointment, new TimesRunResult(TimesRunKind.Done, 2, "out line\n", "\n  \n bad\u001b time \nmore\n"), out text));
        Assert.Equal("Times refused the booking: bad� time", text);
        Assert.False(MeetingNote.BookingOutcome(
            appointment, new TimesRunResult(TimesRunKind.Done, 1, "\nfrom stdout\n", string.Empty), out text));
        Assert.Equal("Times refused the booking: from stdout", text);
        Assert.False(MeetingNote.BookingOutcome(
            appointment, new TimesRunResult(TimesRunKind.Done, 1, new string('y', 500), string.Empty), out text));
        Assert.Equal("Times refused the booking: " + new string('y', 200), text);
        Assert.False(MeetingNote.BookingOutcome(appointment, new TimesRunResult(TimesRunKind.Done, 3, string.Empty, string.Empty), out text));
        Assert.Equal("Times refused the booking: exit code 3", text);
        Assert.Equal("Book Sprint at 14:00 in Times?", MeetingNote.BookingQuestion(appointment));
    }
}
