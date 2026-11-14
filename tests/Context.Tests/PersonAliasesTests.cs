namespace Context.Tests;

// 158 AC-1 to AC-3: the aliases of person pages and the one rule that resolves a mention's name.
public sealed class PersonAliasesTests : IDisposable
{
    private readonly string root;
    private readonly NoteStore store;

    public PersonAliasesTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-aliases-" + Guid.NewGuid().ToString("N"));
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Write(string name, string text, string extension = ".md")
    {
        string path = Path.Combine([root, .. name.Split('/')]) + extension;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static IReadOnlyList<string> Parse(string text, string? title = null) =>
        PersonAliases.Parse(AttributeParser.Parse(text), title);

    private static PersonAliases Rule(params (string Title, string[] Aliases)[] pages) =>
        new(pages.Select(p => (p.Title, (IReadOnlyList<string>)p.Aliases)));

    private static string Describe(AliasResolution resolution) =>
        $"{resolution.Kind}|{resolution.Person}|{string.Join(",", resolution.Titles)}";

    // AC-1: split at commas, normalized, distinct by key with the first spelling kept.
    [Fact]
    public void Parse_SplitsNormalizesAndKeepsFirstSpelling()
    {
        Assert.Equal(["Anna", "AS"], Parse("aliases:: Anna,  AS ,anna"));
        Assert.Equal(["Anna Maria", "as"], Parse("aliases:: Anna   Maria\nAliases:: as, AS\naliases:: anna maria"));
    }

    // AC-1: empty and invalid parts are skipped, and the page's own title is dropped.
    [Fact]
    public void Parse_SkipsEmptyInvalidAndOwnTitle()
    {
        Assert.Equal(["Anna", "x"], Parse("aliases:: , a?b,Anna,, a/b , [x], a*b, x", "Bob"));
        Assert.Equal(["AS"], Parse("aliases:: anna  SCHMIDT, AS", "Anna Schmidt"));
        Assert.Empty(Parse("aliases::\nrole:: Anna, AS"));
        Assert.Empty(Parse("```\naliases:: Anna\n```"));
    }

    // AC-3: a page title wins, ignoring case; then the alias of exactly one page; else unresolved.
    [Fact]
    public void Resolve_TitleThenSingleAliasThenNone()
    {
        PersonAliases rule = Rule(
            ("Anna Schmidt", ["Anna", "AS", "Bob"]),
            ("Anna Berg", ["Anna"]),
            ("Bob", []));

        Assert.Equal("Person|Anna Schmidt|", Describe(rule.Resolve("anna  SCHMIDT")));
        Assert.Equal("Person|Anna Schmidt|", Describe(rule.Resolve("as")));
        Assert.Equal("Person|Bob|", Describe(rule.Resolve("BOB")));
        Assert.Equal("Ambiguous||Anna Berg,Anna Schmidt", Describe(rule.Resolve("Anna")));
        Assert.Equal("None||", Describe(rule.Resolve("Carl")));
        Assert.Equal("anna schmidt", rule.PersonKey("AS"));
        Assert.Equal("anna", rule.PersonKey("Anna"));
        Assert.Equal("carl", rule.PersonKey(" Carl "));
        Assert.Equal(nameof(AliasResolution), rule.Resolve("AS").ToString());
    }

    // AC-3: two pages of one key are one person: the first title wins and their aliases merge.
    [Fact]
    public void Resolve_SameTitleKey_OnePerson()
    {
        PersonAliases rule = Rule(("Anna", ["A1"]), ("anna", ["A2"]));

        Assert.Equal("Person|Anna|", Describe(rule.Resolve("a1")));
        Assert.Equal("Person|Anna|", Describe(rule.Resolve("A2")));
        Assert.Empty(rule.Clashes("anna"));
    }

    // AC-9 (core): the clashes of a page, in its order, with the warning text.
    [Fact]
    public void Clashes_SharedAndOtherName()
    {
        PersonAliases rule = Rule(
            ("Anna Schmidt", ["Anna", "AS", "Anna Berg"]),
            ("Anna Berg", ["Anna"]),
            ("Carl", ["anna"]));

        Assert.Equal(
            [
                "Alias Anna is also used by Anna Berg, Carl, so @[Anna] links to no one.",
                "Alias Anna Berg is another person's name, so @[Anna Berg] links to them.",
            ],
            rule.Clashes("anna schmidt").Select(c => c.Text));
        Assert.Equal(["Alias Anna is also used by Anna Schmidt, Carl, so @[Anna] links to no one."], rule.Clashes("Anna Berg").Select(c => c.Text));
        Assert.Empty(rule.Clashes("Nobody"));
        Assert.Equal(nameof(AliasClash), rule.Clashes("Carl")[0].ToString());
        Assert.Equal(
            "@[Anna] is an alias of more than one person: Anna Berg, Anna Schmidt.",
            PersonAliases.AmbiguousText("Anna", ["Anna Berg", "Anna Schmidt"]));
    }

    // AC-2: only direct person pages supply aliases, whatever their type; other notes, subpages and sealed files don't.
    [Fact]
    public void Load_OnlyDirectPersonPages()
    {
        Write("people", "# people");
        Write("people/Anna Schmidt", "# Anna Schmidt\ntype:: organization\naliases:: AS, Anna");
        Write("people/Anna Schmidt/Notes", "aliases:: Sub");
        Write("people/Sealed", "aliases:: Hidden", NoteStore.SensitiveExtension);
        Write("Other", "aliases:: Other alias");
        Write("types", "# types");
        Write("types/person", "aliases:: TypeAlias");

        PersonAliases rule = PersonAliases.Load(store);

        Assert.Equal("Person|Anna Schmidt|", Describe(rule.Resolve("AS")));
        Assert.Equal("Person|Anna Schmidt|", Describe(rule.Resolve("anna")));
        Assert.Equal("None||", Describe(rule.Resolve("Sub")));
        Assert.Equal("None||", Describe(rule.Resolve("Hidden")));
        Assert.Equal("None||", Describe(rule.Resolve("Other alias")));
        Assert.Equal("None||", Describe(rule.Resolve("TypeAlias")));
    }

    // AC-2: no store, no person pages: nothing resolves and nothing throws.
    [Fact]
    public void Load_MissingRoot_Empty()
    {
        Assert.Equal("None||", Describe(PersonAliases.Load(store).Resolve("Anna")));
        Assert.False(Directory.Exists(root));
        Assert.Equal("None||", Describe(PersonAliases.Empty.Resolve("Anna")));
    }
}
