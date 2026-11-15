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
    [Theory]
    [InlineData("Planning", "2026-11-15 Planning")]
    [InlineData("  Weekly   sync  ", "2026-11-15 Weekly sync")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "2026-11-15 a b c d e f g h i j")]
    [InlineData("../../etc/passwd", "2026-11-15 .. .. etc passwd")]
    [InlineData("Line\none\r\ntwo\tthree", "2026-11-15 Line one two three")]
    [InlineData("bell\u0007x\u0085y", "2026-11-15 bell�x�y")]
    [InlineData("notes.md", "2026-11-15 notes")]
    [InlineData("a.md.md", "2026-11-15 a")]
    [InlineData("x. .md .", "2026-11-15 x")]
    [InlineData("X.MD", "2026-11-15 X")]
    [InlineData("Review...", "2026-11-15 Review")]
    [InlineData("...", "2026-11-15 Meeting")]
    [InlineData(".md", "2026-11-15 Meeting")]
    [InlineData("", "2026-11-15 Meeting")]
    [InlineData("   ", "2026-11-15 Meeting")]
    [InlineData("///", "2026-11-15 Meeting")]
    [InlineData(".hidden", "2026-11-15 .hidden")]
    [InlineData("CON", "2026-11-15 CON")]
    [InlineData("nul.txt", "2026-11-15 nul.txt")]
    [InlineData("a  b", "2026-11-15 a b")]
    public void NameFor_CleansTitle(string title, string expected)
    {
        string name = MeetingNote.NameFor(Meeting(title));

        Assert.Equal(expected, name);
        AssertValidName(name);
    }

    // AC-2: a long title is cut to 89 chars, never inside a surrogate pair, and cleaned again after the cut.
    [Fact]
    public void NameFor_LongTitles_CutCleanly()
    {
        string longName = MeetingNote.NameFor(Meeting(new string('a', 200)));
        Assert.Equal("2026-11-15 " + new string('a', 89), longName);
        AssertValidName(longName);

        // The emoji's high surrogate would be char 89: the whole pair goes.
        string emoji = MeetingNote.NameFor(Meeting(new string('a', 88) + "\U0001F600" + "zzz"));
        Assert.Equal("2026-11-15 " + new string('a', 88), emoji);
        AssertValidName(emoji);

        // The pair fits exactly.
        string fits = MeetingNote.NameFor(Meeting(new string('a', 87) + "\U0001F600" + "zzz"));
        Assert.Equal("2026-11-15 " + new string('a', 87) + "\U0001F600", fits);
        AssertValidName(fits);

        // The cut leaves ".md", dots and spaces at the end.
        string dotted = MeetingNote.NameFor(Meeting(new string('a', 79) + " x.md. .md" + "zzzzzzzzzz"));
        Assert.Equal("2026-11-15 " + new string('a', 79) + " x", dotted);
        AssertValidName(dotted);

        string only = MeetingNote.NameFor(Meeting(new string('.', 200)));
        Assert.Equal("2026-11-15 Meeting", only);
    }

    // AC-2: every name passes ValidateTitle on every OS and equals its own NormalizeTitle, for many odd titles.
    [Fact]
    public void NameFor_AlwaysValid()
    {
        string[] pieces = [".", " ", ".md", ".MD", "/", "\\", ":", "*", "?", "\"", "<", ">", "|", "\0", "\u001F", "\u007F", "\n", "a", "CON", "\U0001F600", "\uD800", "\uDC00", "\t"];
        Random random = new Random(167);
        for (int i = 0; i < 3000; i++)
        {
            StringBuilder title = new StringBuilder();
            int count = random.Next(0, 60);
            for (int j = 0; j < count; j++)
            {
                title.Append(pieces[random.Next(pieces.Length)]);
            }

            AssertValidName(MeetingNote.NameFor(Meeting(title.ToString())));
        }
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
            Assert.Equal("2026-11-14 Late", MeetingNote.NameFor(late));
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
            "# 2026-11-15 Budget review\n\n#meeting\n\n- Title: Budget review\n- Time: 2026-11-15 14:00-15:30\n"
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
            "# 2026-11-15 Meeting\n\n#meeting\n\n- Title: (no title)\n- Time: 2026-11-15 09:00-10:00\n- Attendees: none\n",
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
                "# 2026-11-15 Evil # heading - item",
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

    // AC-4: no note: the top-level note is created with Format's exact bytes.
    [Fact]
    public void Ensure_NoNote_CreatesIt()
    {
        Appointment appointment = Meeting("Größe: Plan", "Raum", ["Jürgen"]);

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), appointment);

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal("2026-11-15 Größe Plan", result.Name);
        Assert.Equal(
            new UTF8Encoding(false).GetBytes(MeetingNote.Format(appointment)),
            File.ReadAllBytes(Path.Combine(root, "2026-11-15 Größe Plan.md")));
        Assert.Equal(["2026-11-15 Größe Plan.md"], TopFiles());
    }

    // AC-4: an existing plain note (also in another case) or a sealed one: nothing is written.
    [Theory]
    [InlineData("2026-11-15 Planning.md", "2026-11-15 Planning")]
    [InlineData("2026-11-15 PLANNING.md", "2026-11-15 PLANNING")]
    [InlineData("2026-11-15 Planning.ctxs", "2026-11-15 Planning")]
    [InlineData("2026-11-15 planning.ctxs", "2026-11-15 planning")]
    public void Ensure_Existing_WritesNothing(string file, string name)
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

    // AC-4: a subpage of that name doesn't count: the note is made at the top level.
    [Fact]
    public void Ensure_SubpageOfThatName_CreatesTopLevel()
    {
        WriteFile("Work.md", "# Work\n");
        WriteFile("Work/2026-11-15 Planning.md", "sub");

        MeetingNoteResult result = MeetingNote.Ensure(new NoteStore(root), Meeting("Planning"));

        Assert.Equal(MeetingNoteKind.Created, result.Kind);
        Assert.Equal("sub", File.ReadAllText(Path.Combine(root, "Work", "2026-11-15 Planning.md")));
        Assert.True(File.Exists(Path.Combine(root, "2026-11-15 Planning.md")));
    }

    // AC-4: a note that appears between the check and the create counts as existing; its bytes stay.
    [Fact]
    public void Ensure_NoteAppearsInBetween_Existing()
    {
        MeetingNoteResult result = MeetingNote.Ensure(
            new NoteStore(root), Meeting("Planning"), () => WriteFile("2026-11-15 Planning.md", "theirs"));

        Assert.Equal(MeetingNoteKind.Existing, result.Kind);
        Assert.Equal("2026-11-15 Planning", result.Name);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(root, "2026-11-15 Planning.md")));
        Assert.Equal(["2026-11-15 Planning.md"], TopFiles());
    }

    private static void AssertValidName(string name)
    {
        Assert.Null(NoteStore.ValidateTitle(name));
        Assert.Equal(name, NoteStore.NormalizeTitle(name));
        Assert.True(name.Length <= NoteStore.MaxTitleLength);
        Assert.DoesNotContain(name, c => "/\\:*?\"<>|".Contains(c, StringComparison.Ordinal) || char.IsControl(c));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} ", name);
    }
}
