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
            + "- Location: Room 4\n- Attendees: @[Anna Schmidt], Bob [ext], Eve*, CON\n",
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
            "# 2026-11-15\n\n#meeting\n\n- Title: (no title)\n- Time: 2026-11-15 09:00-10:00\n- Attendees: none\n",
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
}
