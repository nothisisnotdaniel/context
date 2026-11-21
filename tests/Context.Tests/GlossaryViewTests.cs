namespace Context.Tests;

// 380: the glossary: every Term:: meaning line of the plain notes, one term per upper-invariant spelling.
public sealed class GlossaryViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;
    private readonly TagIndex index;
    private readonly NoteStore store;

    public GlossaryViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-glossaryview-" + Guid.NewGuid().ToString("N"));
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

    private void Write(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void WriteAB()
    {
        Write("A", "API:: first\n");
        Write("b", "Api:: second\nZed:: z\n");
    }

    // 380 AC-3: terms API then Zed; API has first (A) then second (b), field by field.
    [Fact]
    public void Build_GroupsByTermKey_SortsTermsAndMeanings()
    {
        WriteAB();

        GlossaryView view = GlossaryView.Build(index, store, string.Empty);

        Assert.Equal(GlossaryViewKind.Found, view.Kind);
        Assert.Equal(["API", "Zed"], view.Terms.Select(t => t.Term));
        GlossaryTerm api = view.Terms[0];
        Assert.Equal(2, api.Meanings.Count);
        Assert.Equal("A", api.Meanings[0].Note);
        Assert.Equal(0, api.Meanings[0].Line);
        Assert.Equal("first", api.Meanings[0].Meaning);
        Assert.Equal("b", api.Meanings[1].Note);
        Assert.Equal(0, api.Meanings[1].Line);
        Assert.Equal("second", api.Meanings[1].Meaning);
        GlossaryMeaning zed = Assert.Single(view.Terms[1].Meanings);
        Assert.Equal(("b", 1, "z"), (zed.Note, zed.Line, zed.Meaning));
        Assert.Equal(nameof(GlossaryView), view.ToString());
        Assert.Equal(nameof(GlossaryTerm), api.ToString());
        Assert.Equal(nameof(GlossaryMeaning), zed.ToString());
    }

    // 380 AC-3: a filter keeps a term with all its meanings when the term or a meaning contains it, ignoring case.
    [Theory]
    [InlineData("seco")]
    [InlineData("  SECO ")]
    [InlineData("ap")]
    public void Build_Filter_KeepsTermWithAllMeanings(string filter)
    {
        WriteAB();

        GlossaryView view = GlossaryView.Build(index, store, filter);

        Assert.Equal(GlossaryViewKind.Found, view.Kind);
        GlossaryTerm api = Assert.Single(view.Terms);
        Assert.Equal("API", api.Term);
        Assert.Equal(["first", "second"], api.Meanings.Select(m => m.Meaning));
    }

    // 380 AC-3: a filter nothing contains is NoMatches.
    [Fact]
    public void Build_NoMatch_NoMatches()
    {
        WriteAB();

        GlossaryView view = GlossaryView.Build(index, store, "zz");

        Assert.Equal(GlossaryViewKind.NoMatches, view.Kind);
        Assert.Empty(view.Terms);
    }

    // 380 AC-3: notes without a glossary line give Empty, with or without a filter.
    [Fact]
    public void Build_NoGlossaryLines_Empty()
    {
        Write("Plain", "# Plain\ntype:: person\n");

        Assert.Equal(GlossaryViewKind.Empty, GlossaryView.Build(index, store, string.Empty).Kind);
        Assert.Equal(GlossaryViewKind.Empty, GlossaryView.Build(index, store, "x").Kind);
    }

    // 380 AC-3: a 201-char filter is TooLong without opening the index; 200 chars is read.
    [Fact]
    public void Build_FilterOver200_TooLong_IndexNotOpened()
    {
        WriteAB();

        GlossaryView view = GlossaryView.Build(index, store, new string('a', GlossaryView.MaxFilterLength + 1));

        Assert.Equal(GlossaryViewKind.TooLong, view.Kind);
        Assert.Equal(0, index.Reads);
        Assert.False(File.Exists(DbPath));
        Assert.Equal(GlossaryViewKind.NoMatches, GlossaryView.Build(index, store, new string('a', GlossaryView.MaxFilterLength)).Kind);
    }

    // 380 AC-3: a corrupt context.db is IndexUnreadable and is not overwritten.
    [Fact]
    public void Build_BadIndex_IndexUnreadable_FileUnchanged()
    {
        WriteAB();
        File.WriteAllText(DbPath, "this is not a database, just some text that fills the header....");
        byte[] before = File.ReadAllBytes(DbPath);

        GlossaryView view = GlossaryView.Build(index, store, string.Empty);

        Assert.Equal(GlossaryViewKind.IndexUnreadable, view.Kind);
        Assert.Empty(view.Terms);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // 380: Narrow filters the loaded rows again without a read; the term spelling is the first meaning's; a subpage's
    // title is its last segment.
    [Fact]
    public void Narrow_NoRead_FirstSpelling_Title()
    {
        Write("b", "Api:: second\n");
        Write("A", "# A\n");
        Write("A/Sub", "API:: first\n");
        GlossaryView all = GlossaryView.Build(index, store, string.Empty);
        int reads = index.Reads;

        GlossaryView narrowed = GlossaryView.Narrow(all, "first");

        Assert.Equal(reads, index.Reads);
        GlossaryTerm term = Assert.Single(narrowed.Terms);
        Assert.Equal("API", term.Term);
        Assert.Equal(["A/Sub", "b"], term.Meanings.Select(m => m.Note));
        Assert.Equal("Sub", term.Meanings[0].Title);
        Assert.Equal(GlossaryViewKind.Found, GlossaryView.Narrow(narrowed, string.Empty).Kind);
        Assert.Single(GlossaryView.Narrow(narrowed, " ").Terms);
    }
}
