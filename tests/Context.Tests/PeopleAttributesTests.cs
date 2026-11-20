using System.Text;

namespace Context.Tests;

// 291: the affiliations and roles of the person pages for the People panel's filters.
public sealed class PeopleAttributesTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public PeopleAttributesTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-peopleattributes-" + Guid.NewGuid().ToString("N"));
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

    private PeopleAttributes Build() => PeopleAttributes.Build(new TagIndex(root), new NoteStore(root));

    // AC-2: only direct person pages count; [[ACME]] and acme are one option labelled from the first page in ordinal
    // name order; a page can have several values; options sorted by label ignoring case, then ordinal.
    [Fact]
    public void Build_DirectPersonPagesOnly_MergedByValueKey_Sorted()
    {
        Write("people", "# people\naffiliation:: Root Corp\nrole:: Root\n");
        Write("people/Anna", "# Anna\naffiliation:: [[ACME]]\nrole:: Lead\n");
        Write("people/bob", "# bob\naffiliation:: Zeta Corp\naffiliation:: acme\naffiliation:: beta\n");
        Write("people/Carl", "# Carl\nrole:: Lead\n");
        Write("people/Dora", "# Dora\n");
        Write("people/Anna/cv", "# cv\naffiliation:: Sub Corp\nrole:: Sub\n");
        Write("peoplex", "# peoplex\n");
        Write("peoplex/Carl", "# Carl\naffiliation:: X Corp\n");
        Write("Work", "affiliation:: Work Corp\nrole:: Worker\n");

        PeopleAttributes view = Build();

        Assert.Equal(PeopleAttributesKind.Found, view.Kind);
        Assert.Equal([("acme", "ACME"), ("beta", "beta"), ("zeta corp", "Zeta Corp")], view.Affiliations.Select(o => (o.Key, o.Label)));
        Assert.Equal([("lead", "Lead")], view.Roles.Select(o => (o.Key, o.Label)));
        Assert.Equal(["people/Anna", "people/Carl", "people/bob"], view.People.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["ACME"], view.People["people/Anna"].Affiliations);
        Assert.Equal(["Lead"], view.People["people/Anna"].Roles);
        Assert.Equal(["Zeta Corp", "acme", "beta"], view.People["people/bob"].Affiliations);
        Assert.Empty(view.People["people/bob"].Roles);
        Assert.True(view.People["people/bob"].HasAffiliation("acme"));
        Assert.True(view.People["people/Anna"].HasAffiliation("acme"));
        Assert.False(view.People["people/Carl"].HasAffiliation("acme"));
        Assert.True(view.People["people/Carl"].HasRole("lead"));
    }

    // 332 AC-5: a comma-separated affiliation gives the person each part (shown, so [[ACME]] is ACME) and each part is
    // its own option; the whole line is never an option.
    [Fact]
    public void Build_AffiliationList_PartsAndOptions()
    {
        Write("people", "# people\n");
        Write("people/Anna", "# Anna\naffiliation:: [[ACME]], Beta GmbH\n");

        PeopleAttributes view = Build();

        Assert.Equal(["ACME", "Beta GmbH"], view.People["people/Anna"].Affiliations);
        Assert.True(view.People["people/Anna"].HasAffiliation("acme"));
        Assert.True(view.People["people/Anna"].HasAffiliation("beta gmbh"));
        Assert.Equal([("acme", "ACME"), ("beta gmbh", "Beta GmbH")], view.Affiliations.Select(o => (o.Key, o.Label)));
        Assert.DoesNotContain(view.Affiliations, o => o.Label.Contains(',', StringComparison.Ordinal));
    }

    // AC-2: the label comes from the first person page in ordinal name order, whichever spelling that is.
    [Fact]
    public void Build_LabelFromFirstPageInOrdinalOrder()
    {
        Write("people", "# people\n");
        Write("people/anna", "affiliation:: acme\n");
        Write("people/Zed", "affiliation:: [[ACME]]\n");

        PeopleAttributes view = Build();

        // "people/Zed" sorts before "people/anna" by ordinal.
        Assert.Equal([("acme", "ACME")], view.Affiliations.Select(o => (o.Key, o.Label)));
        Assert.Equal(["acme"], view.People["people/anna"].Affiliations);
    }

    // AC-2: no person page with the attributes gives empty lists.
    [Fact]
    public void Build_NoAttributes_Empty()
    {
        Write("people", "# people\n");
        Write("people/Anna", "# Anna\n");

        PeopleAttributes view = Build();

        Assert.Equal(PeopleAttributesKind.Found, view.Kind);
        Assert.Empty(view.Affiliations);
        Assert.Empty(view.Roles);
        Assert.Empty(view.People);
    }

    // A context.db that is not a database is a kind, never an exception, and the file is left alone.
    [Fact]
    public void Build_BadDb_IndexUnreadable()
    {
        Write("people", "# people\n");
        Write("people/Anna", "affiliation:: ACME\n");
        byte[] garbage = Encoding.ASCII.GetBytes(new string('x', 2048));
        File.WriteAllBytes(DbPath, garbage);

        PeopleAttributes view = Build();

        Assert.Equal(PeopleAttributesKind.IndexUnreadable, view.Kind);
        Assert.Empty(view.Affiliations);
        Assert.Empty(view.People);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    // AC-7: exactly one [[X]] shows as the trimmed X; anything else as is.
    [Theory]
    [InlineData("[[ACME]]", "ACME")]
    [InlineData("[[  ACME Corp ]]", "ACME Corp")]
    [InlineData("ACME", "ACME")]
    [InlineData("[[a]] and [[b]]", "[[a]] and [[b]]")]
    [InlineData("[[]]", "[[]]")]
    [InlineData("[[ ]]", "[[ ]]")]
    [InlineData("[[a]b]]", "[[a]b]]")]
    public void Shown_OneLinkShowsItsTarget(string value, string shown) => Assert.Equal(shown, PeopleAttributes.Shown(value));

    [Fact]
    public void ToString_IsTypeNameOnly()
    {
        Assert.Equal(nameof(PeopleAttributes), PeopleAttributes.Unreadable.ToString());
        Assert.Equal(nameof(AttributeOption), new AttributeOption("k", "secret").ToString());
        Assert.Equal(nameof(PersonAttributes), new PersonAttributes([], []).ToString());
    }
}
