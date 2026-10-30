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

    // AC-7: the renderer only maps the view.
    [Fact]
    public void Render_HandBuiltViews_MatchGolden()
    {
        TagsView none = new TagsView(TagsViewKind.AllTags, string.Empty, [], []);
        Assert.Equal(ViewParityTests.Goldens["Tags_None"], ViewParityTests.Render(TagCommands.Render(none), "/r"));
        TagsView list = new TagsView(
            TagsViewKind.AllTags,
            string.Empty,
            [new TagCount("big plan", 1), new TagCount("work", 2), new TagCount("x", 1)],
            []);
        Assert.Equal(ViewParityTests.Goldens["Tags_List"], ViewParityTests.Render(TagCommands.Render(list), "/r"));
        TagsView hit = new TagsView(TagsViewKind.NotesWithTag, "big plan", [], ["A [1]"]);
        Assert.Equal(ViewParityTests.Goldens["Tags_Hit_Spaces"], ViewParityTests.Render(TagCommands.Render(hit), "/r"));
        TagsView miss = new TagsView(TagsViewKind.NoSuchTag, "no such", [], []);
        Assert.Equal(ViewParityTests.Goldens["Tags_Miss"], ViewParityTests.Render(TagCommands.Render(miss), "/r"));
        TagsView bad = new TagsView(TagsViewKind.IndexUnreadable, string.Empty, [], []);
        Assert.Equal(ViewParityTests.Goldens["Tags_BadDb_All"], ViewParityTests.Render(TagCommands.Render(bad), "/r"));
    }

    // AC-7: /notes renders NoteStore.List() data without touching the store.
    [Fact]
    public void RenderNotes_HandBuilt_MatchesGolden()
    {
        DateTime modified = new(2026, 10, 1, 8, 15, 0, DateTimeKind.Local);
        NoteInfo[] notes =
        [
            new NoteInfo("Odd [red]name", modified, "/r/x.md"),
            new NoteInfo("Shopping", modified, "/r/y.md"),
        ];

        Assert.Equal(ViewParityTests.Goldens["Notes_List"], ViewParityTests.Render(NoteCommands.Render(notes), "/r"));
        Assert.Equal(ViewParityTests.Goldens["Notes_Empty"], ViewParityTests.Render(NoteCommands.Render([]), "/r"));
    }
}
