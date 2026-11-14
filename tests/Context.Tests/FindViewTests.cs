namespace Context.Tests;

// 157 AC-6 / AC-7: the data of find <key> <value>.
public sealed class FindViewTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public FindViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-findview-" + Guid.NewGuid().ToString("N"));
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

    private void Write(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private FindView Build(string key, string value) => FindView.Build(new TagIndex(root), new NoteStore(root), key, value);

    // AC-7: matches sorted ignoring case then ordinal, key and value normalized.
    [Fact]
    public void Build_Found_Sorted_Normalized()
    {
        Write("c", "role:: Bereichsleiter\nrole:: bereichsleiter");
        Write("B", "role:: bereichsleiter");
        Write("a", "Role:: BEREICHSLEITER");
        Write("d", "role:: Chef");

        FindView view = Build(" ROLE ", "  Bereichsleiter ");

        Assert.Equal(FindViewKind.Found, view.Kind);
        Assert.Equal("role", view.Key);
        Assert.Equal("Bereichsleiter", view.Value);
        Assert.Equal(["a", "B", "c"], view.Notes);
        Assert.Equal(nameof(FindView), view.ToString());
    }

    // AC-7: no match is its own kind with no notes.
    [Fact]
    public void Build_NoMatches()
    {
        Write("a", "role:: Chef");

        FindView view = Build("role", "nobody");

        Assert.Equal(FindViewKind.NoMatches, view.Kind);
        Assert.Empty(view.Notes);
    }

    // AC-6 / AC-7: type person also lists person pages without a type:: line, each once.
    [Fact]
    public void Build_TypePerson_IncludesUntypedPersonPages()
    {
        Write("people", "# people");
        Write("people/Anna", "role:: Chef");
        Write("people/Bob", "type:: person");
        Write("people/ACME", "type:: organization");
        Write("people/Anna/Notes", "# Notes");
        Write("Carl", "type:: [[Person]]");
        Write("Dora", "# Dora");

        Assert.Equal(["Carl", "people/Anna", "people/Bob"], Build("type", "Person").Notes);
        Assert.Equal(["people/ACME"], Build("type", "organization").Notes);
    }

    // AC-7: a bad key or empty value reads nothing.
    [Theory]
    [InlineData("1x", "v")]
    [InlineData("a b", "v")]
    [InlineData("role", "   ")]
    public void Build_InvalidQuery_ReadsNothing(string key, string value)
    {
        Write("a", "role:: v");

        Assert.Equal(FindViewKind.InvalidQuery, Build(key, value).Kind);
        Assert.False(File.Exists(DbPath));
    }

    // AC-7: an unreadable index is a kind; the file is left alone.
    [Fact]
    public void Build_BadDb_IndexUnreadable()
    {
        Write("a", "role:: v");
        byte[] garbage = new byte[2048];
        new Random(9).NextBytes(garbage);
        File.WriteAllBytes(DbPath, garbage);

        Assert.Equal(FindViewKind.IndexUnreadable, Build("role", "v").Kind);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }
}
