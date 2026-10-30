namespace Context.Tests;

public sealed class LinksViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public LinksViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-linksview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
        WriteNote("A", "#work #[big plan]");
        WriteNote("B [b]", "#work #[big plan]");
        WriteNote("c", "#work");
        WriteNote("Lonely", "#solo");
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private LinksView Build(string argument) => LinksView.Build(new TagIndex(root), new NoteStore(root), argument);

    // AC-5: no argument, and the index isn't touched.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_NoArgument_MissingArgument(string argument)
    {
        LinksView view = Build(argument);

        Assert.Equal(LinksViewKind.MissingArgument, view.Kind);
        Assert.Null(view.Note);
        Assert.False(File.Exists(DbPath));
    }

    // AC-5
    [Fact]
    public void Build_Unknown_NoSuchNote_KeepsTypedName()
    {
        LinksView view = Build("  Nope [x] ");

        Assert.Equal(LinksViewKind.NoSuchNote, view.Kind);
        Assert.Equal("Nope [x]", view.Typed);
        Assert.Null(view.Note);
    }

    // AC-5: /links name.md finds the note; case is ignored.
    [Theory]
    [InlineData("a.md")]
    [InlineData("A.MD")]
    [InlineData("a")]
    public void Build_MdSuffixAndCase_FindsNote(string argument)
    {
        LinksView view = Build(argument);

        Assert.Equal(LinksViewKind.Links, view.Kind);
        Assert.Equal("A", view.Note);
        Assert.Equal(argument, view.Typed);
    }

    // AC-5
    [Fact]
    public void Build_NoLinks()
    {
        LinksView view = Build("Lonely");

        Assert.Equal(LinksViewKind.NoLinks, view.Kind);
        Assert.Equal("Lonely", view.Note);
        Assert.Empty(view.Links);
    }

    // AC-5
    [Fact]
    public void Build_Links_WithSharedTags()
    {
        LinksView view = Build("A");

        Assert.Equal(LinksViewKind.Links, view.Kind);
        Assert.Equal(["B [b]", "c"], view.Links.Select(l => l.Note).ToArray());
        Assert.Equal(["big plan", "work"], view.Links[0].SharedTags);
        Assert.Equal(["work"], view.Links[1].SharedTags);
    }

    // AC-5
    [Fact]
    public void Build_BadDb_IndexUnreadable()
    {
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        LinksView view = Build("A");

        Assert.Equal(LinksViewKind.IndexUnreadable, view.Kind);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // AC-7: the renderer only maps the view.
    [Fact]
    public void Render_HandBuiltViews_MatchGolden()
    {
        (string Golden, LinksView View)[] cases =
        [
            ("Links_NoArgument", new LinksView(LinksViewKind.MissingArgument, string.Empty, null, [])),
            ("Links_Unknown", new LinksView(LinksViewKind.NoSuchNote, "Nope [x]", null, [])),
            ("Links_NoLinks", new LinksView(LinksViewKind.NoLinks, "Lonely", "Lonely", [])),
            ("Links_BadDb", new LinksView(LinksViewKind.IndexUnreadable, "A", null, [])),
            (
                "Links_Links",
                new LinksView(
                    LinksViewKind.Links,
                    "A",
                    "A",
                    [new NoteLink("B [b]", ["big plan", "work"]), new NoteLink("c", ["work"])])
            ),
        ];

        foreach ((string golden, LinksView view) in cases)
        {
            Assert.Equal(ViewParityTests.Goldens[golden], ViewParityTests.Render(TagCommands.Render(view), "/r"));
        }
    }
}
