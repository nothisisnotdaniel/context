namespace Context.Tests;

// 268: similar notes: title keys, shingles, candidate pairs and the view built from context.db.
public sealed class SimilarNotesTests : IDisposable
{
    private readonly string parent;
    private readonly string root;
    private readonly TagIndex index;
    private readonly NoteStore store;

    public SimilarNotesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-similar-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
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

    private void Write(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // count distinct words <prefix>a, <prefix>b, ... (letters only, so no digit rule applies), joined by spaces.
    private static string Words(string prefix, int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => prefix + Letters(i)));

    private static string Letters(int number)
    {
        string text = string.Empty;
        do
        {
            text = (char)('a' + (number % 26)) + text;
            number /= 26;
        }
        while (number > 0);
        return text;
    }

    private static HashSet<string> Set(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => prefix + i).ToHashSet(StringComparer.Ordinal);

    private static string Describe(SimilarPair p) =>
        $"{p.First}|{p.Second}|{p.TitleMatch}|{(p.ContentScore is { } s ? s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "-")}";

    // AC-1: the key is the last segment's letter-or-digit runs, lower-cased, joined by one space.
    [Theory]
    [InlineData("Projects/Q3 Plan--Draft!", "q3 plan draft")]
    [InlineData("  Budget   2027 ", "budget 2027")]
    [InlineData("ÄRGER über Müller", "ärger über müller")]
    [InlineData("2026-11-18", "2026 11 18")]
    [InlineData("---", "")]
    public void TitleKey_LastSegmentTokens(string title, string expected)
    {
        Assert.Equal(expected, SimilarNotes.TitleKey(title));
    }

    // AC-1: equal keys, one edit under 10 characters, two edits from 10 on; short keys, digit-only differences and keys
    // without a letter never match.
    [Theory]
    [InlineData("budget plan", "budget plan", "Same")]
    [InlineData("notes", "notez", "Similar")]
    [InlineData("budget", "budgets", "Similar")]
    [InlineData("budget", "budgetxy", "None")]
    [InlineData("budgets", "bxdgetz", "None")]
    [InlineData("project plan", "projekt plam", "Similar")]
    [InlineData("project plan", "projekt plan s", "None")]
    [InlineData("project plan", "prxjekt plam", "None")]
    [InlineData("plan", "plans", "None")]
    [InlineData("plan", "plam", "None")]
    [InlineData("notes 1", "notes 2", "None")]
    [InlineData("project plan 1", "project plan 22", "None")]
    [InlineData("notes", "notes 1", "None")]
    [InlineData("2026 11 18", "2026 11 18", "None")]
    [InlineData("2026 11 18", "2026 11 19", "None")]
    public void TitleMatch_Rules(string first, string second, string match)
    {
        TitleMatchKind expected = Enum.Parse<TitleMatchKind>(match);
        Assert.Equal(expected, SimilarNotes.TitleMatch(first, second));
        Assert.Equal(expected, SimilarNotes.TitleMatch(second, first));
    }

    // AC-1: the banded distance is exact up to the limit and limit + 1 beyond it.
    [Theory]
    [InlineData("kitten", "sitting", 3, 3)]
    [InlineData("kitten", "sitting", 2, 3)]
    [InlineData("abc", "abc", 1, 0)]
    [InlineData("", "ab", 2, 2)]
    [InlineData("abcdef", "abcxef", 1, 1)]
    [InlineData("abcdef", "badcfe", 2, 3)]
    [InlineData("abcdefghij", "abcdefgh", 1, 2)]
    public void Distance_Banded(string a, string b, int limit, int expected)
    {
        Assert.Equal(expected, SimilarNotes.Distance(a, b, limit));
    }

    // AC-2: shingles are the distinct runs of 3 tokens over the whole text.
    [Fact]
    public void Shingles_DistinctRunsOfThree()
    {
        Assert.Equal(
            ["a b c", "b c d", "c d a", "d a b"],
            SimilarNotes.Shingles("# A b\nc, D! a b c").Order(StringComparer.Ordinal));
        Assert.Empty(SimilarNotes.Shingles("one two"));
    }

    // AC-2: the threshold is inclusive (0.5 matches, 0.49 doesn't) and both need at least 10 shingles.
    [Fact]
    public void ContentScore_ThresholdAndMinimum()
    {
        HashSet<string> a = Set("s", 12);
        HashSet<string> b = Set("s", 8);
        b.UnionWith(Set("t", 4));
        Assert.Equal(0.5, SimilarNotes.ContentScore(a, b));

        HashSet<string> c = Set("s", 50);
        HashSet<string> d = Set("s", 33);
        d.UnionWith(Set("t", 17));
        Assert.Null(SimilarNotes.ContentScore(c, d));

        Assert.Equal(1.0, SimilarNotes.ContentScore(Set("s", 10), Set("s", 10)));
        Assert.Null(SimilarNotes.ContentScore(Set("s", 9), Set("s", 9)));
        Assert.Null(SimilarNotes.ContentScore(Set("s", 9), Set("s", 10)));
    }

    // AC-2: pairs by content come from shared shingles; a text under 10 shingles never pairs by content.
    [Fact]
    public void Pairs_ContentMatch_FromTexts()
    {
        string shared = Words("w", 20);
        List<(string, string)> notes =
        [
            ("Alpha", shared),
            ("Omega", shared + " " + Words("x", 3)),
            ("Short one", Words("y", 11)),
            ("Brief two", Words("y", 11)),
        ];

        List<SimilarPair> pairs = SimilarNotes.Pairs(notes);

        SimilarPair pair = Assert.Single(pairs);
        Assert.Equal("Alpha", pair.First);
        Assert.Equal("Omega", pair.Second);
        Assert.Equal(TitleMatchKind.None, pair.TitleMatch);
        Assert.Equal(18.0 / 21.0, pair.ContentScore);
    }

    // AC-2: boilerplate shared by more than 50 notes finds no pair; in 50 notes it still does.
    [Theory]
    [InlineData(51, 0)]
    [InlineData(50, 50 * 49 / 2)]
    public void Pairs_Boilerplate_SkippedOver50Notes(int count, int expected)
    {
        string boilerplate = Words("template", 40);
        List<(string, string)> notes = Enumerable.Range(0, count)
            .Select(i => ("n" + Letters(i), boilerplate + " " + Words("own" + Letters(i), 2)))
            .ToList();

        Assert.Equal(expected, SimilarNotes.Pairs(notes).Count);
    }

    // AC-3: each pair once, First before Second ignoring case; both reasons first, then the score, then the names.
    [Fact]
    public void Build_SortedAndOrderedPairs()
    {
        string budget = Words("budget", 20);
        Write("budget plan", budget);
        Write("Budget plans", budget);
        Write("Omega", Words("same", 20));
        Write("alpha", Words("same", 20));
        Write("Kappa", Words("part", 16) + " " + Words("kappa", 4));
        Write("Sigma", Words("part", 16) + " " + Words("sigma", 4));
        Write("Meeting notes", "one two");
        Write("meeting note", "three four");

        SimilarNotesView view = SimilarNotesView.Build(index, store);

        Assert.Equal(SimilarNotesKind.Found, view.Kind);
        Assert.Equal(
            [
                "budget plan|Budget plans|Similar|1",
                "alpha|Omega|None|1",
                "Kappa|Sigma|None|0.636",
                "meeting note|Meeting notes|Similar|-",
            ],
            view.Pairs.Select(Describe));
    }

    // AC-3: sensitive notes and notes that can't be read (over the edit limit) never appear; no pair is NoSimilar.
    [Fact]
    public void Build_SensitiveAndUnreadable_NeverAppear()
    {
        string text = Words("secret", 20);
        Write("Plain", text);
        File.WriteAllText(Path.Combine(root, "Plain copy.ctxs"), text);
        File.WriteAllText(Path.Combine(root, "Secret.ctxs"), text);
        Write("Archive", "archive");
        Write("Archive/Report", text);
        File.WriteAllBytes(Path.Combine(root, "Report.md"), new byte[NoteStore.MaxEditBytes + 1]);

        SimilarNotesView view = SimilarNotesView.Build(index, store);

        Assert.Equal(SimilarNotesKind.Found, view.Kind);
        Assert.Equal(["Archive/Report|Plain|None|1"], view.Pairs.Select(Describe));
    }

    // AC-3: one note is never paired with itself; no pair gives NoSimilar; texts are read through one NoteTexts call.
    [Fact]
    public void Build_OneNote_NoSimilar_OneTextRead()
    {
        Write("Only", Words("only", 30));
        index.Sync(store);
        int before = index.Reads;

        SimilarNotesView view = SimilarNotesView.Build(index, store);

        Assert.Equal(SimilarNotesKind.NoSimilar, view.Kind);
        Assert.Empty(view.Pairs);
        Assert.True(index.Reads - before <= 2);
    }

    // AC-3: a corrupt context.db is IndexUnreadable and is not overwritten.
    [Fact]
    public void Build_BadIndex_IndexUnreadable_FileUnchanged()
    {
        Write("Plan", "text");
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");
        byte[] before = File.ReadAllBytes(DbPath);

        SimilarNotesView view = SimilarNotesView.Build(index, store);

        Assert.Equal(SimilarNotesKind.IndexUnreadable, view.Kind);
        Assert.Empty(view.Pairs);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    [Fact]
    public void ToString_IsTypeName()
    {
        Assert.Equal("SimilarNotesView", new SimilarNotesView(SimilarNotesKind.NoSimilar, []).ToString());
        Assert.Equal("SimilarPair", new SimilarPair("secret", "other", TitleMatchKind.Same, 1).ToString());
    }
}
