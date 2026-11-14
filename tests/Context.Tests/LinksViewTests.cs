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

    // 048 AC-4/5: note links count as links; a link to a missing note alone gives Links, not NoLinks.
    [Fact]
    public void Build_NoteLinks_Kinds()
    {
        WriteNote("Only missing", "[[Nowhere]]");
        WriteNote("Linker", "[[Lonely]]");

        LinksView missing = Build("only missing");
        LinksView lonely = Build("Lonely");
        LinksView self = Build("Linker");

        Assert.Equal(LinksViewKind.Links, missing.Kind);
        NoteLink row = Assert.Single(missing.Links);
        Assert.Equal("Nowhere", row.Note);
        Assert.True(row.Missing);
        Assert.Equal(NoteLinkDirection.To, row.Direction);

        Assert.Equal(LinksViewKind.Links, lonely.Kind);
        Assert.Equal(NoteLinkDirection.From, Assert.Single(lonely.Links).Direction);
        Assert.Equal(NoteLinkDirection.To, Assert.Single(self.Links).Direction);
    }

    // 048 AC-4: a note that only links to itself still has no links.
    [Fact]
    public void Build_SelfLinkOnly_NoLinks()
    {
        WriteNote("Self", "[[Self]]");

        Assert.Equal(LinksViewKind.NoLinks, Build("Self").Kind);
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

    // AC-6 (145): every kind with every field, as raw data (no escaping, no markup).
    [Fact]
    public void Build_EveryKind_EveryField()
    {
        AssertView(Build(string.Empty), LinksViewKind.MissingArgument, string.Empty, null, []);
        AssertView(Build("Nope [x]"), LinksViewKind.NoSuchNote, "Nope [x]", null, []);
        AssertView(Build("Lonely"), LinksViewKind.NoLinks, "Lonely", "Lonely", []);
        AssertView(
            Build("A"),
            LinksViewKind.Links,
            "A",
            "A",
            [new NoteLink("B [b]", ["big plan", "work"]), new NoteLink("c", ["work"])]);

        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);
        AssertView(Build("A"), LinksViewKind.IndexUnreadable, "A", null, []);
    }

    // AC-6 (145), 048 AC-8: both ways (with a shared tag), a tag-only row, to, from and a missing note.
    [Fact]
    public void Build_NoteLinks_EveryField()
    {
        File.Delete(Path.Combine(root, "B [b].md"));
        File.Delete(Path.Combine(root, "Lonely.md"));
        WriteNote("A", "#work [[Beta]] [[Gamma]] [[delta.md]]");
        WriteNote("Beta", "#work [[A]]");
        WriteNote("Delta", "plain");
        WriteNote("Epsilon", "see [[a]]");

        AssertView(
            Build("A"),
            LinksViewKind.Links,
            "A",
            "A",
            [
                new NoteLink("Beta", ["work"], NoteLinkDirection.Both, missing: false),
                new NoteLink("c", ["work"]),
                new NoteLink("Delta", [], NoteLinkDirection.To, missing: false),
                new NoteLink("Epsilon", [], NoteLinkDirection.From, missing: false),
                new NoteLink("Gamma", [], NoteLinkDirection.To, missing: true),
            ]);
    }

    private static void AssertView(LinksView view, LinksViewKind kind, string typed, string? note, NoteLink[] links)
    {
        Assert.Equal(kind, view.Kind);
        Assert.Equal(typed, view.Typed);
        Assert.Equal(note, view.Note);
        Assert.Equal(links.Select(l => l.Note), view.Links.Select(l => l.Note));
        for (int i = 0; i < links.Length; i++)
        {
            Assert.Equal(links[i].SharedTags, view.Links[i].SharedTags);
            Assert.Equal(links[i].Direction, view.Links[i].Direction);
            Assert.Equal(links[i].Missing, view.Links[i].Missing);
        }
    }

    // AC-4 (042)
    [Fact]
    public void ToString_HidesRawText()
    {
        const string Marker = "SECRET-042\u001b[31m";
        LinksView view = new LinksView(LinksViewKind.Links, Marker, Marker, [new NoteLink(Marker, [Marker])]);

        string text = view.ToString();

        Assert.Equal("LinksView", text);
        Assert.Equal(text, $"{view}");
        Assert.DoesNotContain("SECRET-042", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
    }
}
