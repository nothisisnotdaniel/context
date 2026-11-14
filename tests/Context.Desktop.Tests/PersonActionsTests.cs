using System.Text;

namespace Context.Tests;

// 156 AC-10: DesktopActions.OpenPerson makes the people note and the person page on demand, against real files.
public sealed class PersonActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-10: with no people note both notes are created, each with only its heading, and the page is opened.
    [Fact]
    public async Task OpenPerson_NoPeople_CreatesPeopleAndThePage()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenPerson("Anna Schmidt");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(ActionReason.None, result.Reason);
        Assert.Equal("people/Anna Schmidt", result.Text);
        Assert.Equal("# people\n", Read("people.md"));
        Assert.Equal("# Anna Schmidt\n\naffiliation::\nrole::\n", Read(Path.Combine("people", "Anna Schmidt.md")));
    }

    // AC-10: an existing People (any case) is reused, and the typed @[...] form and extra spaces are accepted.
    [Fact]
    public async Task OpenPerson_ExistingPeople_IsReused()
    {
        data.WriteNote("People", "# People\n\nMy contacts.\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenPerson("@[  Bob   Jones ]");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("People/Bob Jones", result.Text);
        Assert.Equal("# People\n\nMy contacts.\n", Read("People.md"));
        Assert.Equal(["People.md"], Directory.EnumerateFiles(data.Root, "*.md").Select(f => Path.GetFileName(f)).ToArray());
        Assert.Equal("# Bob Jones\n\naffiliation::\nrole::\n", Read(Path.Combine("People", "Bob Jones.md")));
    }

    // AC-10: opening a person page a second time (in another case too) writes nothing.
    [Fact]
    public async Task OpenPerson_SecondTime_WritesNothing()
    {
        await data.RefreshAsync();
        await data.Actions!.OpenPerson("Anna");
        File.WriteAllText(Path.Combine(data.Root, "people", "Anna.md"), "# Anna\n\nNotes about Anna.\n");
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        ActionResult again = await data.Actions.OpenPerson("anna");

        Assert.Equal(ActionKind.Done, again.Kind);
        Assert.Equal(ActionReason.Unchanged, again.Reason);
        Assert.Equal("people/Anna", again.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-10: a sensitive top-level people note is refused with the fixed text: nothing written, no key-store call.
    [Fact]
    public async Task OpenPerson_SensitivePeople_RefusedNoAgree()
    {
        data.Keys = FakeKeyStore.Ready();
        data.WriteSensitive("people", "# people\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        ActionResult result = await data.Actions!.OpenPerson("Anna");

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Can't make a page for Anna. Nothing was done.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-1, AC-10: a name that is no mention is refused and nothing is written; it never becomes a path.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("x:y")]
    [InlineData("<b>")]
    public async Task OpenPerson_InvalidName_RefusedNothingWritten(string name)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        ActionResult result = await data.Actions!.OpenPerson(name);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.EndsWith(". Nothing was done.", result.Text, StringComparison.Ordinal);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // 157 AC-9: a types/person page sets the template's attribute lines, in its order.
    [Fact]
    public async Task OpenPerson_TypesPersonPage_SetsTheTemplate()
    {
        data.WriteNote("types", "# types\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "types"));
        File.WriteAllText(
            Path.Combine(data.Root, "types", "Person.md"),
            "# Person\nattribute:: Email\nattribute:: role\nattribute:: bad key\nattribute:: email\n");
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenPerson("Anna");

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("# Anna\n\nemail::\nrole::\n", Read(Path.Combine("people", "Anna.md")));
    }

    // 157 AC-9: a types/person page without attribute lines gives the plain heading as before.
    [Fact]
    public async Task OpenPerson_TypesPersonWithoutAttributes_PlainHeading()
    {
        data.WriteNote("types", "# types\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "types"));
        File.WriteAllText(Path.Combine(data.Root, "types", "person.md"), "# person\nA person.\n");
        await data.RefreshAsync();

        await data.Actions!.OpenPerson("Anna");

        Assert.Equal("# Anna\n", Read(Path.Combine("people", "Anna.md")));
    }

    // 157 AC-9: an existing person page is never changed by the template.
    [Fact]
    public async Task OpenPerson_ExistingPage_NotChanged()
    {
        data.WriteNote("people", "# people\n");
        Directory.CreateDirectory(Path.Combine(data.Root, "people"));
        File.WriteAllText(Path.Combine(data.Root, "people", "Anna.md"), "# Anna\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);

        ActionResult result = await data.Actions!.OpenPerson("Anna");

        Assert.Equal(ActionReason.Unchanged, result.Reason);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    private string Read(string relative) =>
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(data.Root, relative)));
}
