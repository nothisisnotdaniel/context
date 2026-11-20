namespace Context.Tests;

// 207 AC-1, AC-2: the tag filter grammar and the notes that match it.
public sealed class TagFilterTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    private readonly string parent;
    private readonly string root;
    private readonly TagIndex index;
    private readonly NoteStore store;

    public TagFilterTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-tagfilter-" + Guid.NewGuid().ToString("N"));
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

    private TagFilterView Build(string text) => TagFilterView.Build(index, store, text);

    // AC-1: what counts as a tag filter at all (whether or not it parses).
    [Theory]
    [InlineData("#work", true)]
    [InlineData("  #work  ", true)]
    [InlineData("#", true)]
    [InlineData("#a or #b", true)]
    [InlineData("not #done", true)]
    [InlineData("NOT   #done", true)]
    [InlineData("Not\t#done", true)]
    [InlineData("not#done", false)]
    [InlineData("nothing #done", false)]
    [InlineData("budget #work", false)]
    [InlineData("not", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsTagFilter(string text, bool expected)
    {
        Assert.Equal(expected, TagFilter.IsTagFilter(text));
    }

    // AC-1: valid filters, with their normalized include and exclude tags.
    [Theory]
    [InlineData("#work", "work", "")]
    [InlineData("  #Work  ", "work", "")]
    [InlineData("#job1234 and not #done", "job1234", "done")]
    [InlineData("#job1234 AND NOT #Done", "job1234", "done")]
    [InlineData("not #done and #work", "work", "done")]
    [InlineData("#a and #b and not #c and not #d", "a,b", "c,d")]
    [InlineData("#[Big  Project] and not #[on hold]", "big project", "on hold")]
    [InlineData("#[work] and #work", "work", "")]
    [InlineData("#a\tand\n#b", "a,b", "")]
    [InlineData("#a-b_c and #ü", "a-b_c,ü", "")]
    public void TryParse_Valid(string text, string include, string exclude)
    {
        Assert.True(TagFilter.TryParse(text, out IReadOnlyList<string> wanted, out IReadOnlyList<string> unwanted));

        Assert.Equal(Split(include), wanted.ToArray());
        Assert.Equal(Split(exclude), unwanted.ToArray());
    }

    // AC-1: or, parentheses, bare words, empty or invalid tags, dangling words and only not terms are invalid.
    [Theory]
    [InlineData("#a or #b")]
    [InlineData("#a OR #b")]
    [InlineData("(#a and #b)")]
    [InlineData("#a and (#b)")]
    [InlineData("#a and")]
    [InlineData("#a and not")]
    [InlineData("#a #b")]
    [InlineData("#a and and #b")]
    [InlineData("not #a")]
    [InlineData("not #a and not #b")]
    [InlineData("not not #a")]
    [InlineData("#")]
    [InlineData("# and #a")]
    [InlineData("#1234")]
    [InlineData("#a and #1234")]
    [InlineData("#a!")]
    [InlineData("#a and #b,")]
    [InlineData("#[]")]
    [InlineData("#[a")]
    [InlineData("#[a]b")]
    [InlineData("#[a.b]")]
    [InlineData("#a and budget")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Invalid(string text)
    {
        Assert.False(TagFilter.TryParse(text, out IReadOnlyList<string> include, out IReadOnlyList<string> exclude));
        Assert.Empty(include);
        Assert.Empty(exclude);
    }

    // AC-1: a tag longer than a note's tag can be is invalid.
    [Fact]
    public void TryParse_TagTooLong_Invalid()
    {
        Assert.True(TagFilter.TryParse("#" + new string('a', TagParser.MaxTagLength), out _, out _));
        Assert.False(TagFilter.TryParse("#" + new string('a', TagParser.MaxTagLength + 1), out _, out _));
    }

    // AC-1: over 200 characters after trimming is TooLong, checked before parsing; neither TooLong nor Invalid opens
    // the index (a broken context.db is left alone and no IndexUnreadable is reported).
    [Theory]
    [InlineData(201, true)]
    [InlineData(200, false)]
    public void Build_TooLongOrInvalid_ReadsNothing(int length, bool tooLong)
    {
        Write("A", "#a");
        Directory.CreateDirectory(root);
        File.WriteAllText(DbPath, NotADatabase);
        byte[] before = File.ReadAllBytes(DbPath);

        // Ends in an invalid word, so 200 characters is Invalid and 201 is TooLong (never parsed).
        string text = "  #a and " + new string('x', length - "#a and ".Length) + "  ";

        TagFilterView view = Build(text);

        Assert.Equal(tooLong ? TagFilterViewKind.TooLong : TagFilterViewKind.Invalid, view.Kind);
        Assert.Equal(text.Trim(), view.Query);
        Assert.Empty(view.Notes);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // AC-2: notes with every included tag and none of the excluded ones, sorted ignoring case (the ordinal tie-break needs
    // two names that differ only in case, which a case-insensitive file system can't hold).
    [Fact]
    public void Build_IncludeAndExclude_SortedMatches()
    {
        Write("beta", "#job1234\n");
        Write("Alpha", "text #Job1234 more\n");
        Write("Gamma", "#job1234 #extra\n");
        Write("Done", "#job1234\n#done\n");
        Write("Other", "#work\n");
        Write("projects", "# projects\n");
        Write("projects/Plan", "#[Job1234]\n");

        TagFilterView view = Build("#job1234 and not #done");

        Assert.Equal(TagFilterViewKind.Found, view.Kind);
        Assert.Equal("#job1234 and not #done", view.Query);
        Assert.Equal(["Alpha", "beta", "Gamma", "projects/Plan"], view.Notes.ToArray());
        Assert.Equal(["Gamma"], Build("#job1234 and #extra").Notes.ToArray());
        Assert.Equal(["Done"], Build("#done and #job1234").Notes.ToArray());
    }

    // AC-2: no match is NoMatches with no notes.
    [Fact]
    public void Build_NoMatch_NoMatches()
    {
        Write("A", "#a\n");

        TagFilterView view = Build("#a and not #a");

        Assert.Equal(TagFilterViewKind.NoMatches, view.Kind);
        Assert.Empty(view.Notes);
        Assert.Equal(TagFilterViewKind.NoMatches, Build("#missing").Kind);
    }

    // AC-2: a sensitive note never appears, whatever its bytes hold.
    [Fact]
    public void Build_SensitiveNote_NeverListed()
    {
        Write("Plain", "#secret\n");
        File.WriteAllText(Path.Combine(root, "Secret" + NoteStore.SensitiveExtension), "#secret\n");

        TagFilterView view = Build("#secret");

        Assert.Equal(["Plain"], view.Notes.ToArray());
    }

    // AC-2: a context.db that is not a database gives IndexUnreadable, never throws, and the file is unchanged.
    [Fact]
    public void Build_BadIndex_IndexUnreadable_FileUnchanged()
    {
        Write("A", "#a\n");
        File.WriteAllText(DbPath, NotADatabase);
        byte[] before = File.ReadAllBytes(DbPath);

        TagFilterView view = Build("#a");

        Assert.Equal(TagFilterViewKind.IndexUnreadable, view.Kind);
        Assert.Equal("#a", view.Query);
        Assert.Empty(view.Notes);
        Assert.Equal(before, File.ReadAllBytes(DbPath));
    }

    // The view never prints its raw text.
    [Fact]
    public void ToString_OnlyTypeName()
    {
        Assert.Equal(nameof(TagFilterView), new TagFilterView(TagFilterViewKind.Found, "#<b>", ["<b>"]).ToString());
    }

    private static string[] Split(string list) => list.Length == 0 ? [] : list.Split(',');
}
