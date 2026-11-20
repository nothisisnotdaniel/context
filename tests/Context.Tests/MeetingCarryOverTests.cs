namespace Context.Tests;

// 245: the agenda carry-over's pure steps (previous note, open points, section text).
public sealed class MeetingCarryOverTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public MeetingCarryOverTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-carryover-" + Guid.NewGuid().ToString("N"));
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

    private void WriteNote(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // AC-2: the latest earlier yyyy-MM-dd subpage; later, equal and other names are ignored.
    [Fact]
    public void Previous_LatestEarlierDateSubpage()
    {
        WriteNote("Meetings", "# Meetings\n");
        WriteNote("Meetings/Weekly", "# Weekly\n");
        foreach (string title in new[] { "2026-11-05", "2026-11-12", "2026-11-26", "2026-11-19", "Agenda", "2026-13-01", "2026-11-18 x" })
        {
            WriteNote("Meetings/Weekly/" + title, "x");
        }

        WriteNote("Meetings/Other", "# Other\n");
        WriteNote("Meetings/Other/2026-11-18", "x");

        NoteInfo? previous = MeetingCarryOver.Previous(new NoteStore(root), "Meetings/weekly", new DateOnly(2026, 11, 19));

        Assert.NotNull(previous);
        Assert.Equal("Meetings/Weekly/2026-11-12", previous.Name);
    }

    // AC-2: no earlier dated subpage, or no page at all: null.
    [Fact]
    public void Previous_NoneEarlier_Null()
    {
        WriteNote("Meetings", "# Meetings\n");
        WriteNote("Meetings/Weekly", "# Weekly\n");
        WriteNote("Meetings/Weekly/2026-11-26", "x");
        WriteNote("Meetings/Weekly/Agenda", "x");

        NoteStore notes = new NoteStore(root);
        Assert.Null(MeetingCarryOver.Previous(notes, "Meetings/Weekly", new DateOnly(2026, 11, 19)));
        Assert.Null(MeetingCarryOver.Previous(notes, "Meetings/Missing", new DateOnly(2026, 11, 19)));
    }

    // AC-1: open todos only, done ones left out.
    [Fact]
    public void Extract_OpenTodosOnly()
    {
        Assert.Equal(["- [ ] Send budget"], MeetingCarryOver.Extract("# 2026-11-12\n\n- [ ] Send budget\n- [x] Done thing\n"));
    }

    // AC-3: an Open points section (any level, any case) without its heading, up to the next same or higher heading;
    // a deeper heading is copied, fenced headings don't count, a todo inside appears once.
    [Fact]
    public void Extract_OpenPointsSection()
    {
        string markdown = string.Join(
            "\n",
            "# Note",
            "- [ ] before",
            "### open POINTS ##",
            "",
            "Budget question",
            "#### Detail",
            "- [ ] inside todo",
            "```",
            "## not a heading",
            "```",
            "after fence",
            "",
            "## Next",
            "plain text",
            "- [ ] after");

        Assert.Equal(
            [
                "- [ ] before",
                "Budget question",
                "#### Detail",
                "- [ ] inside todo",
                "```",
                "## not a heading",
                "```",
                "after fence",
                "- [ ] after",
            ],
            MeetingCarryOver.Extract(markdown));
    }

    // AC-3: a section runs to the end; a same-level heading ends it; a heading inside a fence doesn't start one.
    [Fact]
    public void Extract_SectionEndsAndFencedHeadingIgnored()
    {
        string markdown = "```\n## Open points\nfenced\n```\n## Open points\none\n## Other\nskip\n# Open Points\n\ntwo\n\n";

        Assert.Equal(["one", "two"], MeetingCarryOver.Extract(markdown));
    }

    // AC-4: fenced todos outside a section are skipped, indented todos are trimmed, CRLF gives \n-only lines.
    [Fact]
    public void Extract_FencesIndentAndCrlf()
    {
        string markdown = "# N\r\n```\r\n- [ ] fenced\r\n```\r\n  \t- [ ] indented\r\n## Open points\r\nkept\r\n";

        IReadOnlyList<string> lines = MeetingCarryOver.Extract(markdown);

        Assert.Equal(["- [ ] indented", "kept"], lines);
        Assert.DoesNotContain(lines, l => l.Contains('\r', StringComparison.Ordinal));
    }

    // AC-5: nothing open gives no lines.
    [Fact]
    public void Extract_NothingOpen_Empty()
    {
        Assert.Empty(MeetingCarryOver.Extract("# N\n- [x] done\ntext\n## Open points\n\n## Next\n"));
    }

    // 329 AC-4: open "[ ]" checkboxes and open "{ }" todos are both carried, in line order, each once; done ones of
    // either kind are not; an open-points todo stays once.
    [Fact]
    public void Extract_CheckboxesAndTodos_InLineOrder()
    {
        string markdown = "# N\n  - { } todo one\n- [ ] box one\n- {x} done todo\n- [x] done box\n"
            + "* [ ] box two\n```\n- { } fenced\n```\n## Open points\n- { } inside\n- [ ] also\n";

        Assert.Equal(
            ["- { } todo one", "- [ ] box one", "* [ ] box two", "- { } inside", "- [ ] also"],
            MeetingCarryOver.Extract(markdown));
        Assert.Empty(MeetingCarryOver.Extract("- {x} a\n- [x] b\n- [ ] \n- { }x c\n"));
    }

    // AC-1: the section text with a link to the previous note.
    [Fact]
    public void Section_LinksPreviousNote()
    {
        Assert.Equal(
            "\n## From last time\n\nFrom [[Meetings/Weekly/2026-11-12]]:\n\n- [ ] a\nb\n",
            MeetingCarryOver.Section("Meetings/Weekly/2026-11-12", ["- [ ] a", "b"]));
    }

    // AC-9: a name a link can't carry is written as plain text.
    [Theory]
    [InlineData("Meetings/C# sync/2026-11-12")]
    [InlineData("Meetings/a [b/2026-11-12")]
    [InlineData("Meetings/a ]b/2026-11-12")]
    [InlineData("Meetings/a `b/2026-11-12")]
    public void Section_UnlinkableName_PlainText(string name)
    {
        string text = MeetingCarryOver.Section(name, ["x"]);

        Assert.Equal($"\n## From last time\n\nFrom {name}:\n\nx\n", text);
        Assert.DoesNotContain("[[", text, StringComparison.Ordinal);
    }

    // AC-10: at most 200 lines, then a line with the rest's count.
    [Fact]
    public void Section_OverMaxLines_Cut()
    {
        string[] points = Enumerable.Range(1, 201).Select(i => $"- [ ] p{i}").ToArray();

        string[] lines = MeetingCarryOver.Section("P", MeetingCarryOver.Extract(string.Join("\n", points))).Split('\n');

        Assert.Equal(MeetingCarryOver.MaxLines, lines.Count(l => l.StartsWith("- [ ] p", StringComparison.Ordinal)));
        Assert.Equal("- [ ] p200", lines[^3]);
        Assert.Equal("- … and 1 more in the previous note.", lines[^2]);
        Assert.Equal(string.Empty, lines[^1]);
    }

    // AC-10: exactly 200 lines get no extra line.
    [Fact]
    public void Section_AtMaxLines_NoExtraLine()
    {
        string[] points = Enumerable.Range(1, 200).Select(i => $"p{i}").ToArray();

        Assert.DoesNotContain("more in the previous note", MeetingCarryOver.Section("P", points), StringComparison.Ordinal);
    }

    // 330 AC-2: "[>]" lines come over as "- [ ] " plus the text after the marker as written, in line order with the
    // open todos, checkboxes and open points; done lines do not.
    [Fact]
    public void Extract_CarriedLines_AsOpenCheckboxes_InLineOrder()
    {
        string markdown = "# N\n- { } todo\n  * [>] carried  `code`  due:2026-01-01\n- [x] done box\n- {x} done todo\n"
            + "- [ ] box\n+ [>] second\n```\n- [>] fenced\n```\n## Open points\n- open point\n## Next\n- [>] last\n";

        Assert.Equal(
            ["- { } todo", "- [ ] carried  `code`  due:2026-01-01", "- [ ] box", "- [ ] second", "- open point", "- [ ] last"],
            MeetingCarryOver.Extract(markdown));
    }

    // 330 AC-3: a "[>]" line inside an Open points section appears once, as an open checkbox.
    [Fact]
    public void Extract_CarriedLineInOpenPoints_Once()
    {
        string markdown = "## Open points\n- plain\n- [>] carried\r\n- [ ] box\n";

        Assert.Equal(["- plain", "- [ ] carried", "- [ ] box"], MeetingCarryOver.Extract(markdown));
    }
}
