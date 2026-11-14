namespace Context.Tests;

public sealed class TagsViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public TagsViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-tagsview-" + Guid.NewGuid().ToString("N"));
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

    private string DbPath => Path.Combine(root, TagIndex.FileName);

    private void WriteNote(string name, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".md"), text);
    }

    private TagsView Build(string argument) => TagsView.Build(new TagIndex(root), new NoteStore(root), argument);

    // AC-5
    [Fact]
    public void Build_NoTags_AllTagsEmpty()
    {
        WriteNote("A", "no tags");

        TagsView view = Build("  ");

        Assert.Equal(TagsViewKind.AllTags, view.Kind);
        Assert.Empty(view.Tags);
        Assert.Empty(view.Notes);
    }

    // AC-5
    [Fact]
    public void Build_AllTags_WithCounts()
    {
        WriteNote("A", "#work #[big plan]");
        WriteNote("B", "#work");

        TagsView view = Build(string.Empty);

        Assert.Equal(TagsViewKind.AllTags, view.Kind);
        Assert.Equal(
            [("big plan", 1), ("work", 2)],
            view.Tags.Select(t => (t.Tag, t.Count)).OrderBy(t => t.Tag, StringComparer.Ordinal).ToArray());
    }

    // AC-5: #work, work, [a b] and #[a b] are normalised.
    [Theory]
    [InlineData("work", "work")]
    [InlineData("#Work", "work")]
    [InlineData("[Big   Plan]", "big plan")]
    [InlineData("#[big plan]", "big plan")]
    public void Build_Tag_Normalised_NotesWithTag(string argument, string tag)
    {
        WriteNote("A [1]", "#work #[big plan]");
        WriteNote("B", "#work");

        TagsView view = Build(argument);

        Assert.Equal(TagsViewKind.NotesWithTag, view.Kind);
        Assert.Equal(tag, view.Tag);
        Assert.Contains("A [1]", view.Notes);
    }

    // AC-5
    [Theory]
    [InlineData("#[no such]", "no such")]
    [InlineData("#", "")]
    public void Build_Miss_NoSuchTag(string argument, string tag)
    {
        WriteNote("A", "#work");

        TagsView view = Build(argument);

        Assert.Equal(TagsViewKind.NoSuchTag, view.Kind);
        Assert.Equal(tag, view.Tag);
        Assert.Empty(view.Notes);
    }

    // AC-5
    [Theory]
    [InlineData("")]
    [InlineData("work")]
    public void Build_BadDb_IndexUnreadable(string argument)
    {
        WriteNote("A", "#work");
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        TagsView view = Build(argument);

        Assert.Equal(TagsViewKind.IndexUnreadable, view.Kind);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // AC-6 (145): every kind with every field, as raw data (no escaping, no markup).
    [Fact]
    public void Build_EveryKind_EveryField()
    {
        WriteNote("N", "no tags");
        AssertView(Build(string.Empty), TagsViewKind.AllTags, string.Empty, [], []);

        WriteNote("A", "#work #[big plan]");
        WriteNote("B", "#work #[x]y");
        AssertView(
            Build(string.Empty), TagsViewKind.AllTags, string.Empty, [("big plan", 1), ("work", 2), ("x", 1)], []);
        AssertView(Build("[Big   Plan]"), TagsViewKind.NotesWithTag, "big plan", [], ["A"]);
        AssertView(Build("#[no such]"), TagsViewKind.NoSuchTag, "no such", [], []);

        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);
        AssertView(Build(string.Empty), TagsViewKind.IndexUnreadable, string.Empty, [], []);
    }

    // AC-6 (145): the notes list is NoteStore.List() itself: raw name, modified time and path, sorted by name.
    [Fact]
    public void NotesList_NameModifiedPath_Raw()
    {
        Assert.Empty(new NoteStore(root).List());

        DateTime modified = new(2026, 10, 1, 8, 15, 0, DateTimeKind.Local);
        WriteNote("Shopping", "# Shopping\n");
        WriteNote("Odd [red]name", "x");
        File.SetLastWriteTime(Path.Combine(root, "Shopping.md"), modified);
        File.SetLastWriteTime(Path.Combine(root, "Odd [red]name.md"), modified);

        IReadOnlyList<NoteInfo> notes = new NoteStore(root).List();

        Assert.Equal(["Odd [red]name", "Shopping"], notes.Select(n => n.Name));
        Assert.All(notes, n => Assert.Equal(modified, n.Modified));
        Assert.Equal(
            [Path.Combine(root, "Odd [red]name.md"), Path.Combine(root, "Shopping.md")],
            notes.Select(n => n.Path));
    }

    private static void AssertView(
        TagsView view, TagsViewKind kind, string tag, (string Tag, int Count)[] tags, string[] notes)
    {
        Assert.Equal(kind, view.Kind);
        Assert.Equal(tag, view.Tag);
        Assert.Equal(tags, view.Tags.Select(t => (t.Tag, t.Count)));
        Assert.Equal(notes, view.Notes);
    }

    // AC-3 (042)
    [Fact]
    public void ToString_EveryKind_HidesRawText()
    {
        const string Marker = "SECRET-042\u001b[31m";
        TagsViewKind[] kinds =
        [
            TagsViewKind.AllTags,
            TagsViewKind.NotesWithTag,
            TagsViewKind.NoSuchTag,
            TagsViewKind.IndexUnreadable,
        ];

        foreach (TagsViewKind kind in kinds)
        {
            TagsView view = new TagsView(kind, Marker, [new TagCount(Marker, 2)], [Marker]);

            string text = view.ToString();

            Assert.Equal("TagsView", text);
            Assert.Equal(text, $"{view}");
            Assert.DoesNotContain("SECRET-042", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
        }
    }

    // AC-6 (042)
    [Fact]
    public void Equals_EqualArguments_StillEqual()
    {
        IReadOnlyList<TagCount> tags = [new TagCount("work", 2)];
        IReadOnlyList<string> notes = ["A"];

        TagsView first = new TagsView(TagsViewKind.AllTags, string.Empty, tags, notes);
        TagsView second = new TagsView(TagsViewKind.AllTags, string.Empty, tags, notes);

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new TagsView(TagsViewKind.NoSuchTag, string.Empty, tags, notes));
    }
}
