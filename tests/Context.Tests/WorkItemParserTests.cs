namespace Context.Tests;

public sealed class WorkItemParserTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public WorkItemParserTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-workitemparser-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // 280 AC-1: at line start, after a space, before '.', ',' and ')'.
    [Theory]
    [InlineData("#1234")]
    [InlineData("see #1234")]
    [InlineData("tab\t#1234")]
    [InlineData("done #1234.")]
    [InlineData("for #1234, then")]
    [InlineData("(see #1234)")]
    [InlineData("first\n#1234 second")]
    [InlineData("#123456789")]
    public void Parse_FindsAReference(string text)
    {
        int expected = text.Contains("123456789", StringComparison.Ordinal) ? 123456789 : 1234;
        Assert.Equal([expected], WorkItemParser.Parse(text));
    }

    // 280 AC-1: nothing for these.
    [Theory]
    [InlineData("a#1234")]
    [InlineData("#0123")]
    [InlineData("#1234a")]
    [InlineData("#12-3")]
    [InlineData("#12_3")]
    [InlineData("#12#3")]
    [InlineData("#1234567890")]
    [InlineData("#")]
    [InlineData("#work")]
    [InlineData("```\n#1234\n```")]
    [InlineData("see `#1234` here")]
    [InlineData("see ``a #1234 b`` here")]
    public void Parse_FindsNothing(string text)
    {
        Assert.Empty(WorkItemParser.Parse(text));
    }

    // 280 AC-1: distinct ints in order of first appearance.
    [Fact]
    public void Parse_RepeatsGiveOneItem_InOrder()
    {
        Assert.Equal([1234], WorkItemParser.Parse("#1234 #1234"));
        Assert.Equal([7, 1234], WorkItemParser.Parse("#7 and #1234\nagain #7"));
    }

    // 280 AC-1: a reference is still no tag.
    [Fact]
    public void Reference_IsNoTag()
    {
        Assert.Empty(TagParser.Parse("#1234"));
        Assert.Equal(["work"], TagParser.Parse("#work #1234"));
    }

    [Fact]
    public void Mentions_IsParseContains()
    {
        Assert.True(WorkItemParser.Mentions("Fix #1234.", 1234));
        Assert.False(WorkItemParser.Mentions("Fix #12345", 1234));
    }

    [Theory]
    [InlineData("1234", true, 1234)]
    [InlineData("#1234", true, 1234)]
    [InlineData("0", false, 0)]
    [InlineData("0123", false, 0)]
    [InlineData("1234567890", false, 0)]
    [InlineData("12a", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("#", false, 0)]
    [InlineData(" 12", false, 0)]
    [InlineData("١٢", false, 0)]
    public void TryParseNumber_DigitsOnly(string text, bool valid, int item)
    {
        Assert.Equal(valid, WorkItemParser.TryParseNumber(text, out int parsed));
        Assert.Equal(item, parsed);
    }

    // 280 Scope: the page names.
    [Fact]
    public void PageNote_AndItemOf()
    {
        Assert.Equal("items/1234", WorkItems.PageNote(1234));
        Assert.Equal(1234, WorkItems.ItemOf("items/1234"));
        Assert.Equal(1234, WorkItems.ItemOf("Items/1234"));
        Assert.Null(WorkItems.ItemOf("items"));
        Assert.Null(WorkItems.ItemOf("items/#1234"));
        Assert.Null(WorkItems.ItemOf("items/0123"));
        Assert.Null(WorkItems.ItemOf("items/1234/notes"));
        Assert.Null(WorkItems.ItemOf("other/1234"));
        Assert.Null(WorkItems.ItemOf("x/items/1234"));
        Assert.Null(WorkItems.ItemOf("1234"));
    }

    // 280 AC-9 / Scope: the page is created with its parent, then found again unchanged.
    [Fact]
    public void EnsurePage_CreatesItemsAndPage_ThenFindsIt()
    {
        NoteStore notes = new NoteStore(root);

        WorkItemPage created = WorkItems.EnsurePage(notes, 1234);

        Assert.Equal(WorkItemPageKind.Created, created.Kind);
        Assert.Equal("items/1234", created.Name);
        Assert.Equal("# items\n", File.ReadAllText(Path.Combine(root, "items.md")));
        string page = Path.Combine(root, "items", "1234.md");
        Assert.Equal("# 1234\n", File.ReadAllText(page));

        File.WriteAllText(page, "# 1234\nmine");
        WorkItemPage again = WorkItems.EnsurePage(notes, 1234);

        Assert.Equal(WorkItemPageKind.Existing, again.Kind);
        Assert.Equal("items/1234", again.Name);
        Assert.Equal("# 1234\nmine", File.ReadAllText(page));
    }

    // An existing items note in another case is reused with its spelling.
    [Fact]
    public void EnsurePage_ReusesItemsInAnyCase()
    {
        File.WriteAllText(Path.Combine(root, "Items.md"), "# Items\n");

        WorkItemPage created = WorkItems.EnsurePage(new NoteStore(root), 7);

        Assert.Equal(WorkItemPageKind.Created, created.Kind);
        Assert.Equal("Items/7", created.Name);
        Assert.Equal("# Items\n", File.ReadAllText(Path.Combine(root, "Items.md")));
    }

    // A sensitive top-level items: nothing written.
    [Fact]
    public void EnsurePage_SensitiveItems_WritesNothing()
    {
        File.WriteAllText(Path.Combine(root, "items.ctxs"), "{}");
        SortedDictionary<string, string> before = FileSnapshot.Take(root);

        WorkItemPage result = WorkItems.EnsurePage(new NoteStore(root), 1234);

        Assert.Equal(WorkItemPageKind.Sensitive, result.Kind);
        Assert.Equal("items", result.Name);
        Assert.Equal(before, FileSnapshot.Take(root));
        Assert.Equal(nameof(WorkItemPage), result.ToString());
    }

    // 381 AC-7: AB#<n> (AB in any case) at line start or after whitespace, with the #<n> digit and end rules.
    [Theory]
    [InlineData("AB#1234")]
    [InlineData("ab#1234")]
    [InlineData("Ab#1234")]
    [InlineData("aB#1234")]
    [InlineData("see AB#1234")]
    [InlineData("tab\tAB#1234")]
    [InlineData("done AB#1234.")]
    [InlineData("for AB#1234, then")]
    [InlineData("first\nAB#1234 second")]
    public void Parse_FindsAzureReference(string text)
    {
        Assert.Equal([1234], WorkItemParser.Parse(text));
        Assert.True(WorkItemParser.Mentions(text, 1234));
    }

    // 381 AC-7: nothing for these; AB#<n> is never a tag.
    [Theory]
    [InlineData("xAB#12")]
    [InlineData("AB#0")]
    [InlineData("AB#012")]
    [InlineData("AB#12a")]
    [InlineData("AB#12-3")]
    [InlineData("AB#12#")]
    [InlineData("AB#1234567890")]
    [InlineData("AB #12x")]
    [InlineData("A#12x")]
    [InlineData("ABC#12")]
    [InlineData("`AB#12`")]
    [InlineData("```\nAB#12\n```")]
    public void Parse_AzureNonReferences_GiveNothing(string text)
    {
        Assert.Empty(WorkItemParser.Parse(text));
    }

    [Fact]
    public void Parse_AzureReference_AddsNoTag_KeepsOrder()
    {
        Assert.Empty(TagParser.Parse("Sprint AB#1234"));
        Assert.Equal([12, 1234, 5], WorkItemParser.Parse("#12 then AB#1234 and ab#12 and AB#5"));
    }

    // 381: FirstLineWith gives the index of the # also for AB#<n>, as the doc says.
    [Fact]
    public void FirstLineWith_AzureReference_IndexOfHash()
    {
        Assert.Equal((2, "see AB#1234", 6), WorkItemParser.FirstLineWith("x\nsee AB#1234", 1234));
    }

    // 381: RemoveReferences drops whole tokens only.
    [Theory]
    [InlineData("Sprint AB#1234 review", "Sprint  review")]
    [InlineData("#1 a #2", " a ")]
    [InlineData("xAB#12 #0 `#3`", "xAB#12 #0 `#3`")]
    public void RemoveReferences_DropsTokens(string line, string expected)
    {
        Assert.Equal(expected, WorkItemParser.RemoveReferences(line));
    }
}
