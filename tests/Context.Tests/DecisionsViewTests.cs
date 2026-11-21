namespace Context.Tests;

// 180, 343, 349: the data of decisions [tag]: every !d decision paragraph, dated, with its label and people, newest first.
public sealed class DecisionsViewTests : IDisposable
{
    // UTC+1 with no clock changes, so no test depends on the machine's zone.
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+01", TimeSpan.FromHours(1), "Test+01", "Test+01");

    private readonly string parent;
    private readonly string root;
    private readonly TagIndex index;
    private readonly NoteStore store;

    public DecisionsViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-decisionsview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        index = new TagIndex(root);
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private string Write(string name, string text, DateTime? modifiedUtc = null)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteFolder();
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modifiedUtc ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private void WriteFolder()
    {
        string file = Path.Combine(root, DecisionFolder.FileName);
        if (!File.Exists(file))
        {
            File.WriteAllText(file, "{\"prefix\":\"ab12\",\"guid\":\"11111111-2222-3333-4444-555555555555\",\"migrated\":true}");
        }
    }

    private DecisionsView Build(string tag = "") => DecisionsView.Build(index, store, tag, Zone);

    private static string Describe(Decision d) =>
        $"{d.Date:yyyy-MM-dd}|{d.Note}|{d.Line}|{string.Join(",", d.People)}|{d.Text}";

    private static string Fields(Decision d) =>
        $"{d.Note}|{d.Line}|{d.Label}|{d.Title}|{d.Headline}|{d.Text}";

    // AC-4: a meeting or daily note's title date is the decision's date.
    [Fact]
    public void Build_TitleDate_IsTheDate()
    {
        Write("2026-11-10 Weekly sync", "# x\n!d ship it");
        Write("2026-11-11", "!d daily");
        Write("meetings", "# meetings");
        Write("meetings/2026-11-09 Sub", "!d nested");

        DecisionsView view = Build();

        Assert.Equal(DecisionsViewKind.Found, view.Kind);
        Assert.Equal(string.Empty, view.Tag);
        Assert.Equal(
            ["2026-11-11|2026-11-11|1||daily", "2026-11-10|2026-11-10 Weekly sync|2||ship it", "2026-11-09|meetings/2026-11-09 Sub|1||nested"],
            view.Decisions.Select(Describe));
    }

    // AC-4: no title date (or no real one): the last write time as a day in the zone; just before midnight UTC is the
    // next day at UTC+1.
    [Fact]
    public void Build_NoTitleDate_LastWriteDayInZone()
    {
        Write("Plans", "!d late", new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Utc));
        Write("2026-13-01 Bad", "!d bad date", new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc));
        Write("2026-11-101", "!d digits after", new DateTime(2026, 3, 3, 10, 0, 0, DateTimeKind.Utc));

        DecisionsView view = Build();

        Assert.Equal(
            ["2026-11-11|Plans|1||late", "2026-03-04|2026-13-01 Bad|1||bad date", "2026-03-03|2026-11-101|1||digits after"],
            view.Decisions.Select(Describe));
    }

    // AC-4: date descending, then note ignoring case, then ordinal, then line.
    [Fact]
    public void Build_Sort_DateNoteLine()
    {
        // Names differ by more than case, so the test runs the same on case-insensitive file systems.
        Write("2026-11-10 c", "!d c1");
        Write("2026-11-10 B", "!d B1\n- !d B2");
        Write("2026-11-10 a", "!d a3\n\n!d a1");
        Write("2026-11-12 z", "!d newest");

        string[] order = Build().Decisions.Select(d => $"{d.Note}:{d.Line}").ToArray();

        Assert.Equal(["2026-11-12 z:1", "2026-11-10 a:1", "2026-11-10 a:3", "2026-11-10 B:1", "2026-11-10 B:2", "2026-11-10 c:1"], order);
    }

    // 343 AC-10: people in the whole paragraph, distinct ignoring case, in text order.
    [Fact]
    public void Build_People_DistinctInLineOrder()
    {
        Write("Plan", "!d ship it with @[Anna]\nand @[anna] and @[Bo]");

        Decision decision = Assert.Single(Build().Decisions);

        Assert.Equal(["Anna", "Bo"], decision.People);
    }

    // AC-5: an alias on a person page resolves to the page's title.
    [Fact]
    public void Build_People_AliasResolvesToPersonPage()
    {
        Write("people", "# people");
        Write("people/Robert", "# Robert\naliases:: Bo");
        Write("Plan", "!d ask @[Bo] and @[Robert] and @[Cy]");

        Decision decision = Assert.Single(Build().Decisions);

        Assert.Equal(["Robert", "Cy"], decision.People);
    }

    // AC-6: a tag (as work, #work or [work]) lists only the decisions of notes with that tag.
    [Theory]
    [InlineData("work")]
    [InlineData("#work")]
    [InlineData("[work]")]
    [InlineData(" #Work ")]
    public void Build_TagFilter(string tag)
    {
        Write("Plan", "#work\n\n!d ship it");
        Write("Home", "#home\n\n!d paint");

        DecisionsView view = Build(tag);

        Assert.Equal(DecisionsViewKind.Found, view.Kind);
        Assert.Equal("work", view.Tag);
        Assert.Equal(["Plan"], view.Decisions.Select(d => d.Note));
    }

    // AC-6: a bracket tag with a space filters too.
    [Fact]
    public void Build_TagFilter_BracketTag()
    {
        Write("Plan", "#[project alpha]\n\n!d go");
        Write("Home", "!d paint");

        DecisionsView view = Build("#[Project  Alpha]");

        Assert.Equal("project alpha", view.Tag);
        Assert.Equal(["Plan"], view.Decisions.Select(d => d.Note));
    }

    // AC-6: no decision is NoDecisions, with or without a tag.
    [Fact]
    public void Build_None_NoDecisions()
    {
        Write("Plan", "#work\nnothing decided");

        Assert.Equal(DecisionsViewKind.NoDecisions, Build().Kind);
        DecisionsView tagged = Build("other");
        Assert.Equal(DecisionsViewKind.NoDecisions, tagged.Kind);
        Assert.Equal("other", tagged.Tag);
        Assert.Empty(tagged.Decisions);
    }

    // AC-6: an invalid tag is InvalidTag and the index is not opened (no context.db, no folder).
    [Theory]
    [InlineData("#1x")]
    [InlineData("#")]
    [InlineData("a/b")]
    public void Build_InvalidTag_IndexNotOpened(string tag)
    {
        DecisionsView view = Build(tag);

        Assert.Equal(DecisionsViewKind.InvalidTag, view.Kind);
        Assert.Empty(view.Decisions);
        Assert.Equal(0, index.Reads);
        Assert.False(Directory.Exists(root));
    }

    // AC-6: a corrupt context.db is IndexUnreadable and is not overwritten.
    [Fact]
    public void Build_BadIndex_IndexUnreadable_FileUnchanged()
    {
        Write("Plan", "!d x");
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");
        byte[] before = File.ReadAllBytes(DbPath);

        DecisionsView view = Build();

        Assert.Equal(DecisionsViewKind.IndexUnreadable, view.Kind);
        Assert.Empty(view.Decisions);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // AC-3: a sensitive note's decisions never appear.
    [Fact]
    public void Build_SensitiveNote_NotListed()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), "!d hidden");
        Write("Plan", "!d open");

        Assert.Equal(["Plan"], Build().Decisions.Select(d => d.Note));
    }

    // 343 AC-10: number, title (last segment), headline and the full text, field by field; the old syntax is gone.
    [Fact]
    public void Build_Fields_NumberTitleHeadlineText()
    {
        Write("projects", "# projects");
        Write("projects/Launch", "# Launch\n\n!d ab12-12 Ship in March.\nThe beta was fine.\n\n- !d bare item\n\nDecision: old");

        Assert.Equal(
            ["projects/Launch|3|12|Launch|Ship in March.|Ship in March.\nThe beta was fine.", "projects/Launch|6||Launch|bare item|bare item"],
            Build().Decisions.Select(Fields));
    }

    // 343 AC-11: Find gives the owner's decision by number; an unknown number is NoDecisions; a bad index IndexUnreadable.
    [Fact]
    public void Find_ByNumber()
    {
        Write("Plan", "!d ab12-7 go @[Anna]\nnow", new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc));

        DecisionsView found = DecisionsView.Find(index, store, new DecisionRef(null, 7), Zone);
        Assert.Equal(DecisionsViewKind.Found, found.Kind);
        Decision decision = Assert.Single(found.Decisions);
        Assert.Equal("Plan|1|7|Plan|go @[Anna]|go @[Anna]\nnow", Fields(decision));
        Assert.Equal(["Anna"], decision.People);
        Assert.Equal(new DateOnly(2026, 2, 3), decision.Date);

        Assert.Equal(DecisionsViewKind.NoDecisions, DecisionsView.Find(index, store, new DecisionRef(null, 8), Zone).Kind);
    }

    [Fact]
    public void Find_BadIndex_IndexUnreadable()
    {
        Write("Plan", "!d ab12-7 x");
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");

        Assert.Equal(DecisionsViewKind.IndexUnreadable, DecisionsView.Find(index, store, new DecisionRef(null, 7), Zone).Kind);
    }

    // 349 AC-9: an own decision's label is the number, another prefix's (an alias too) is <p>-<n>; the reference adds
    // #!d; an unnumbered one has neither.
    [Fact]
    public void Build_Labels_OwnAndOther()
    {
        Write("Plan", "!d ab12-4 own\n\n!d zz99-5 other\n\n!d bare");

        Decision[] decisions = Build().Decisions.ToArray();

        Assert.Equal(["4", "zz99-5", null], decisions.Select(d => d.Label));
        Assert.Equal(["#!d4", "#!dzz99-5", null], decisions.Select(d => d.Reference));
    }

    // 349 AC-5, AC-12: copies are one decision with its other places; Find takes the own number, the own prefix or
    // another prefix.
    [Fact]
    public void Find_ByReference_WithPlaces()
    {
        Write("Owner", "!d ab12-7 same", new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc));
        Write("Copy", "x\n\n!d ab12-7 same", new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc));
        Write("Other", "!d zz99-7 theirs");

        Decision listed = Assert.Single(Build().Decisions, d => d.Label == "7");
        Assert.Equal([("Copy", 3)], listed.Places);

        foreach (DecisionRef reference in new[] { new DecisionRef(null, 7), new DecisionRef("ab12", 7) })
        {
            Decision found = Assert.Single(DecisionsView.Find(index, store, reference, Zone).Decisions);
            Assert.Equal(("Owner", "7", 1), (found.Note, found.Label, found.Places.Count));
        }

        Decision other = Assert.Single(DecisionsView.Find(index, store, new DecisionRef("zz99", 7), Zone).Decisions);
        Assert.Equal(("Other", "zz99-7"), (other.Note, other.Label));
        Assert.Equal(DecisionsViewKind.NoDecisions, DecisionsView.Find(index, store, new DecisionRef("zz99", 8), Zone).Kind);
    }

    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal("DecisionsView", new DecisionsView(DecisionsViewKind.NoDecisions, "secret", []).ToString());
        Assert.Equal("Decision", new Decision("n", 1, 5, new DateOnly(2026, 1, 1), "secret", ["p"]).ToString());
    }
}
