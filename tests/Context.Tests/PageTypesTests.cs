namespace Context.Tests;

// 157 AC-5 / AC-6: built-in and note-defined page types, and a page's types.
public sealed class PageTypesTests : IDisposable
{
    private readonly string root;
    private readonly NoteStore store;

    public PageTypesTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-pagetypes-" + Guid.NewGuid().ToString("N"));
        store = new NoteStore(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static string Describe(IReadOnlyList<PageType> types) =>
        string.Join(" | ", types.Select(t => $"{t.Name}:{string.Join(",", t.Attributes)}"));

    private void Write(string name, string text)
    {
        string path = Path.Combine([root, .. name.Split('/')]) + ".md";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // AC-5: with no types note (or no folder at all) only the built-ins.
    [Fact]
    public void Load_NoTypesNote_BuiltIns()
    {
        Assert.Equal("organization: | person:affiliation,role", Describe(PageTypes.Load(store)));
        Write("Other", "# Other");
        Assert.Equal("organization: | person:affiliation,role", Describe(PageTypes.Load(store)));
    }

    // AC-5: each direct subpage of a top-level types note (any case) is a type; it replaces a built-in of that name.
    [Fact]
    public void Load_TypePages_AddAndReplace_InOrder_Distinct()
    {
        Write("Types", "# Types");
        Write("Types/Project", "# Project\nattribute:: Owner\nattribute:: due\nattribute:: owner\nattribute:: bad key\nattribute:: 1x\n```\nattribute:: hidden\n```\n");
        Write("Types/Person", "# Person\nattribute:: email\n");
        Write("Types/Empty", "# Empty\n");
        Write("Types/Project/Sub", "attribute:: nested\n");
        Write("Other", "# Other");
        Write("Other/Fake", "attribute:: x\n");

        Assert.Equal(
            "empty: | organization: | person:email | project:owner,due",
            Describe(PageTypes.Load(store)));
    }

    // AC-5: a types/person page with no attribute lines gives person no attributes.
    [Fact]
    public void Load_PersonPageWithoutAttributes_ReplacesTheBuiltIn()
    {
        Write("types", "# types");
        Write("types/person", "# person\n");

        Assert.Empty(PageTypes.Find(PageTypes.Load(store), PageTypes.Person)!.Attributes);
    }

    // AC-5: an unreadable type page is skipped and Load never throws.
    [Fact]
    public void Load_UnreadablePage_Skipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write("types", "# types");
        Write("types/person", "attribute:: email\n");
        Write("types/project", "attribute:: owner\n");
        File.SetUnixFileMode(Path.Combine(root, "types", "person.md"), UnixFileMode.None);
        if (CanRead(Path.Combine(root, "types", "person.md")))
        {
            // Running as root: the file stays readable, nothing to check.
            return;
        }

        Assert.Equal("organization: | person:affiliation,role | project:owner", Describe(PageTypes.Load(store)));
    }

    // AC-5: a sensitive top-level types note reads no type page.
    [Fact]
    public void Load_SensitiveTypes_OnlyBuiltIns()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "types.ctxs"), "sealed");
        Write("types/person", "attribute:: email\n");

        Assert.Equal("organization: | person:affiliation,role", Describe(PageTypes.Load(store)));
    }

    // AC-6: a page's types are its type:: values (lower-cased); a person page without one is a person.
    [Theory]
    [InlineData("Plan", "type:: Project\ntype:: [[Event]]\ntype:: project", "project,event")]
    [InlineData("Plan", "role:: x", "")]
    [InlineData("people/Anna", "role:: x", "person")]
    [InlineData("People/Anna", "", "person")]
    [InlineData("people/Anna", "type:: Organization", "organization")]
    [InlineData("people/Anna/Notes", "", "")]
    [InlineData("people", "", "")]
    public void TypesOf_Cases(string name, string text, string expected)
    {
        Assert.Equal(expected, string.Join(",", PageTypes.TypesOf(name, AttributeParser.Parse(text))));
    }

    // AC-9: the person template: a blank line and one empty attribute line each; none without attributes.
    [Fact]
    public void Template_Lines()
    {
        Assert.Equal("\naffiliation::\nrole::\n", PageTypes.Template(PageTypes.Find(PageTypes.BuiltIn, PageTypes.Person)));
        Assert.Equal(string.Empty, PageTypes.Template(new PageType("x", [])));
        Assert.Equal(string.Empty, PageTypes.Template(null));
        Assert.Equal(nameof(PageType), new PageType("x", []).ToString());
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.ReadAllBytes(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
