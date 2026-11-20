using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 239: the People page, every person page by name from the snapshot's note list.
public sealed class PeoplePanelTests : IDisposable
{
    private const string EmptyText = "No people yet. Mention someone as @[Name] and open the mention to add their page.";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4: only direct children of people are listed, sorted ignoring case; the people note, a subpage and peoplex
    // are not. Clicking a row reports its full name; Close reports too.
    [Fact]
    public async Task Render_ListsPersonPagesSorted_ClickReportsFullName()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "zoe.md"), "# zoe\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        data.WriteFile(Path.Combine("people", "bob.md"), "# bob\n");
        data.WriteFile(Path.Combine("people", "Anna", "cv.md"), "# cv\n");
        data.WriteNote("peoplex", "# peoplex\n");
        data.WriteFile(Path.Combine("peoplex", "Carl.md"), "# Carl\n");
        data.WriteNote("work", "# work\n");
        await data.RefreshAsync();
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>(p => p
            .Add(c => c.OnOpen, (string name) => opened.Add(name))
            .Add(c => c.OnClose, () => closed++));

        Assert.Equal(["Anna", "bob", "zoe"], Names(cut));
        Assert.Equal("People", cut.Find("section.page.people-panel").GetAttribute("aria-label"));
        Assert.Equal("People", cut.Find("ul.people-list").GetAttribute("aria-label"));
        Assert.Equal("People", cut.Find(".panel-bar h2").TextContent);
        IElement close = cut.Find(".page > .panel-bar button.close");
        Assert.Equal("Close", close.GetAttribute("aria-label"));
        Assert.Equal("Close", close.GetAttribute("title"));
        Assert.Equal(string.Empty, close.TextContent);
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(cut.FindAll("ul.people-list > li > button.person-open"), b => Assert.True(b.ClassList.Contains("cut")));
        Assert.Empty(cut.FindAll("p[role=status]"));

        // The attribute load re-renders the rows and Close with new handlers, so click only once it is done.
        cut.WaitForState(() => cut.Instance.Loaded, Wait);
        await cut.FindAll("button.person-open").Single(b => b.TextContent == "bob").ClickAsync(new MouseEventArgs());
        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(["people/bob"], opened);
        Assert.Equal(1, closed);
    }

    // AC-4: the rule over the spec's names, including People/bob (a parent spelled in another case can't sit next to
    // people/ on a case-insensitive disk), and ignoring case first, then ordinal.
    [Fact]
    public void PeopleIn_SpecNames_DirectChildrenOfPeopleIgnoringCase_Sorted()
    {
        string[] names = ["people", "people/zoe", "people/Anna", "People/bob", "people/Anna/cv", "peoplex/Carl", "work"];

        Assert.Equal(["Anna", "bob", "zoe"], PeoplePanel.PeopleIn(names.Select(Info)).Select(n => n.Title).ToArray());
        Assert.Equal(
            ["people/Anna", "People/bob", "people/zoe"],
            PeoplePanel.PeopleIn(names.Select(Info)).Select(n => n.Name).ToArray());
        Assert.Equal(
            ["anna", "Bob", "bob", "Zed"],
            PeoplePanel.PeopleIn(new[] { "people/Zed", "people/bob", "people/Bob", "people/anna" }.Select(Info))
                .Select(n => n.Title).ToArray());
    }

    // AC-5: no people note at all, and a people note without children: the grey status line and no list.
    [Fact]
    public async Task Render_NoPersonPage_EmptyText()
    {
        data.WriteNote("work", "# work\n");
        await data.RefreshAsync();

        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();

        AssertEmpty(cut);

        data.WriteNote("people", "# people\n");
        DesktopSnapshot snapshot = await data.State!.Refresh();
        Assert.Contains(snapshot.Notes, n => n.Name == "people");
        IRenderedComponent<PeoplePanel> again = data.Bunit.Render<PeoplePanel>();

        AssertEmpty(again);
    }

    // AC-6: a bidi override in a title is shown as the placeholder, the title attribute is the one-line title, and
    // the markup holds no raw control character.
    [Fact]
    public async Task Render_BidiTitle_ShownCleaned()
    {
        string title = "Ann‮a";
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", title + ".md"), "# x\n");
        await data.RefreshAsync();

        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();

        IElement button = cut.Find("button.person-open");
        Assert.Equal("Ann" + SafeText.Placeholder + "a", button.TextContent);
        Assert.Equal(DesktopText.OneLine(title), button.GetAttribute("title"));
        Assert.DoesNotContain(button.TextContent, c => c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩');
        Assert.DoesNotContain(cut.Markup, c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');
    }

    // AC-6: control characters can't reach a note title on disk (ValidateTitle), so the shown text and title rule
    // the panel uses are checked directly.
    [Fact]
    public void ShownNameAndTitle_ControlCharacters_Replaced()
    {
        string raw = "An\u0001na\u0085‮";

        Assert.Equal("An" + SafeText.Placeholder + "na" + SafeText.Placeholder + SafeText.Placeholder, PeoplePanel.ShownName(raw));
        Assert.Equal("An" + SafeText.Placeholder + "na" + SafeText.Placeholder + "‮", PeoplePanel.ShownTitle(raw));
        Assert.DoesNotContain(PeoplePanel.ShownName(raw), char.IsControl);
        Assert.DoesNotContain(PeoplePanel.ShownTitle(raw), char.IsControl);
    }

    // AC-10: a new person page written on disk shows up after the state reloads.
    [Fact]
    public async Task StateChanged_NewPersonPage_Listed()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        Assert.Equal(["Anna"], Names(cut));

        data.WriteFile(Path.Combine("people", "Dan.md"), "# Dan\n");
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Anna", "Dan"], Names(cut)), Wait);
    }

    // 291 AC-3 / AC-7: once loaded, the two selects sit above the list with All first and one option per value; each
    // row with a role or affiliation has its grey details line, [[X]] shown as X, a control character replaced.
    [Fact]
    public async Task Loaded_FiltersAboveList_DetailsPerRow()
    {
        WritePeople();
        await data.RefreshAsync();

        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);

        IElement affiliation = cut.Find("select.people-affiliation");
        IElement role = cut.Find("select.people-role");
        Assert.Equal("Affiliation", affiliation.GetAttribute("aria-label"));
        Assert.Equal("Role", role.GetAttribute("aria-label"));
        // The value is the raw value key; the text is cleaned.
        Assert.Equal([("", "All"), ("acme", "ACME"), ("beta\u0007co", "Beta" + SafeText.Placeholder + "Co")], Options(affiliation));
        Assert.Equal([("", "All"), ("lead", "Lead"), ("tester", "Tester")], Options(role));
        Assert.Equal(string.Empty, Selected(affiliation));
        Assert.Equal(string.Empty, Selected(role));
        Assert.Empty(cut.FindAll("button.people-clear"));
        Assert.Empty(cut.FindAll("p[role=status]"));
        // The filters come before the list in document order.
        string markup = cut.Markup;
        Assert.True(markup.IndexOf("people-affiliation", StringComparison.Ordinal) < markup.IndexOf("people-list", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("people-role", StringComparison.Ordinal) < markup.IndexOf("people-list", StringComparison.Ordinal));

        Assert.Equal(["Anna", "Bea", "Carl", "Dan"], RowNames(cut));
        Assert.Equal("Lead · ACME", Details(cut, "Anna"));
        Assert.Equal("Tester", Details(cut, "Bea"));
        Assert.Equal("Beta" + SafeText.Placeholder + "Co", Details(cut, "Carl"));
        Assert.Null(Details(cut, "Dan"));
        Assert.All(cut.FindAll("span.person-details"), d => Assert.True(d.ClassList.Contains("grey")));
        Assert.All(cut.FindAll("ul.people-list > li"), li => Assert.Single(li.Children));
        Assert.All(cut.FindAll("button, option, span"), e => Assert.DoesNotContain(e.TextContent, char.IsControl));
        Assert.All(cut.FindAll("[title]"), e => Assert.DoesNotContain(e.GetAttribute("title")!, char.IsControl));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
    }

    // 291 AC-7: a row with roles and affiliations joins each part with ", " and the parts with " · ".
    [Fact]
    public void DetailsOf_JoinsParts_LeavesOutEmptyOnes()
    {
        Assert.Equal("Lead, Chair · ACME, Beta", PeoplePanel.DetailsOf(new PersonAttributes(
            [new AttributeOption("acme", "ACME"), new AttributeOption("beta", "Beta")],
            [new AttributeOption("lead", "Lead"), new AttributeOption("chair", "Chair")])));
        Assert.Equal("ACME", PeoplePanel.DetailsOf(new PersonAttributes([new AttributeOption("acme", "ACME")], [])));
        Assert.Equal("Lead", PeoplePanel.DetailsOf(new PersonAttributes([], [new AttributeOption("lead", "Lead")])));
        Assert.Null(PeoplePanel.DetailsOf(new PersonAttributes([], [])));
        Assert.Equal("A" + SafeText.Placeholder + "B", PeoplePanel.DetailsOf(new PersonAttributes([], [new AttributeOption("a", "A\u0001B")])));
    }

    // 291 AC-4 / AC-5: an affiliation keeps only its people, a role too, both only those with both, in the PeopleIn order;
    // Clear filters resets both, and All in one select clears only that filter.
    [Fact]
    public async Task Filters_NarrowTheList_ClearAndAllReset()
    {
        WritePeople();
        data.WriteFile(Path.Combine("people", "Eve.md"), "# Eve\nrole:: lead\naffiliation:: acme\n");
        data.WriteFile(Path.Combine("people", "fay.md"), "# fay\nrole:: Lead\n");
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);

        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "acme" });
        Assert.Equal(["Anna", "Eve"], RowNames(cut));
        Assert.Equal("acme", Selected(cut.Find("select.people-affiliation")));
        Assert.Equal("Clear filters", cut.Find("button.people-clear").TextContent);

        await cut.Find("select.people-role").ChangeAsync(new ChangeEventArgs { Value = "lead" });
        Assert.Equal(["Anna", "Eve"], RowNames(cut));

        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = string.Empty });
        Assert.Equal(["Anna", "Eve", "fay"], RowNames(cut));
        Assert.Equal(string.Empty, Selected(cut.Find("select.people-affiliation")));
        Assert.Equal("lead", Selected(cut.Find("select.people-role")));
        Assert.NotNull(cut.Find("button.people-clear"));

        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "beta\u0007co" });
        Assert.Equal("Lead", Assert.Single(cut.FindAll("select.people-role option[selected]")).TextContent);

        await cut.Find("button.people-clear").ClickAsync(new MouseEventArgs());
        Assert.Equal(["Anna", "Bea", "Carl", "Dan", "Eve", "fay"], RowNames(cut));
        Assert.Equal(string.Empty, Selected(cut.Find("select.people-affiliation")));
        Assert.Equal(string.Empty, Selected(cut.Find("select.people-role")));
        Assert.Empty(cut.FindAll("button.people-clear"));
    }

    // 332 AC-6: a comma-separated affiliation shows its parts in the details, gives one option per part, and either
    // option keeps the person in the list.
    [Fact]
    public async Task AffiliationList_DetailsAndOneOptionPerPart()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\naffiliation:: [[ACME]], Beta GmbH\n");
        data.WriteFile(Path.Combine("people", "Bob.md"), "# Bob\naffiliation:: Gamma\n");
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);

        Assert.Equal("ACME, Beta GmbH", Details(cut, "Anna"));
        Assert.Equal(
            [("", "All"), ("acme", "ACME"), ("beta gmbh", "Beta GmbH"), ("gamma", "Gamma")],
            Options(cut.Find("select.people-affiliation")));

        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "acme" });
        Assert.Equal(["Anna"], RowNames(cut));
        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "beta gmbh" });
        Assert.Equal(["Anna"], RowNames(cut));
    }

    // 291 AC-6: filters that match no one show the grey line in place of the list and stay visible.
    [Fact]
    public async Task Filters_NoMatch_GreyLine_FiltersStay()
    {
        WritePeople();
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);

        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "acme" });
        await cut.Find("select.people-role").ChangeAsync(new ChangeEventArgs { Value = "tester" });

        Assert.Empty(cut.FindAll("ul.people-list"));
        IElement status = cut.Find("p[role=status]");
        Assert.Equal("No people match these filters.", status.TextContent);
        Assert.True(status.ClassList.Contains("grey"));
        Assert.Equal("acme", Selected(cut.Find("select.people-affiliation")));
        Assert.Equal("tester", Selected(cut.Find("select.people-role")));
        Assert.NotNull(cut.Find("button.people-clear"));
    }

    // 291 AC-3: a filter with no values is not rendered; with no values at all there are no filter controls.
    [Fact]
    public async Task Loaded_OnlyRoles_NoAffiliationSelect_NoValues_NoControls()
    {
        data.WriteNote("people", "# people\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\nrole:: Lead\n");
        data.WriteFile(Path.Combine("people", "Bob.md"), "# Bob\n");
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-role")), Wait);
        Assert.Empty(cut.FindAll("select.people-affiliation"));
        Assert.Equal("Lead", Details(cut, "Anna"));

        using DesktopFixture other = new();
        other.WriteNote("people", "# people\n");
        other.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n");
        await other.RefreshAsync();
        IRenderedComponent<PeoplePanel> none = other.Bunit.Render<PeoplePanel>();
        // Nothing on the page changes once these attributes load, so wait for the panel's own loaded flag.
        none.WaitForState(() => none.Instance.Loaded, Wait);
        Assert.Empty(none.FindAll("select"));
        Assert.Empty(none.FindAll(".people-filters"));
        Assert.Empty(none.FindAll("button.people-clear"));
        Assert.Equal(["Anna"], Names(none));
    }

    // 291 AC-8: until the attributes load the list shows by name with no filters and no details; a bad index keeps
    // everyone listed with no filters and the red line.
    [Fact]
    public async Task BeforeLoad_NamesOnly_BadIndex_RedLine()
    {
        WritePeople();
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<PeoplePanel> cut;
        lock (gate)
        {
            cut = data.Bunit.Render<PeoplePanel>();
            Assert.Equal(["Anna", "Bea", "Carl", "Dan"], Names(cut));
            Assert.Empty(cut.FindAll("select"));
            Assert.Empty(cut.FindAll("span.person-details"));
            Assert.Empty(cut.FindAll("p[role=status]"));
        }

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);

        using DesktopFixture bad = new();
        bad.WriteNote("people", "# people\n");
        bad.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\nrole:: Lead\n");
        bad.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");
        await bad.RefreshAsync();
        IRenderedComponent<PeoplePanel> red = bad.Bunit.Render<PeoplePanel>();

        red.WaitForAssertion(() => Assert.Equal("Can't read the attributes.", red.Find("p.red[role=status]").TextContent), Wait);
        Assert.Equal(["Anna"], Names(red));
        Assert.Empty(red.FindAll("select"));
        Assert.Empty(red.FindAll("span.person-details"));
    }

    // 291 AC-9: on State.Changed the attributes reload; a selected value that still exists stays, one that is gone
    // goes back to All.
    [Fact]
    public async Task StateChanged_Reloads_KeepsExistingSelection_ResetsGoneOne()
    {
        WritePeople();
        await data.RefreshAsync();
        IRenderedComponent<PeoplePanel> cut = data.Bunit.Render<PeoplePanel>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("select.people-affiliation")), Wait);
        await cut.Find("select.people-affiliation").ChangeAsync(new ChangeEventArgs { Value = "acme" });
        await cut.Find("select.people-role").ChangeAsync(new ChangeEventArgs { Value = "tester" });

        // Bea is no longer a tester; Anna gains a second affiliation.
        Edit(Path.Combine("people", "Bea.md"), "# Bea\nrole:: Writer\n");
        Edit(Path.Combine("people", "Anna.md"), "# Anna\nrole:: Lead\naffiliation:: [[ACME]]\naffiliation:: Gamma\n");
        await data.RefreshAsync();

        cut.WaitForAssertion(() => Assert.Contains(("writer", "Writer"), Options(cut.Find("select.people-role"))), Wait);
        Assert.Equal("acme", Selected(cut.Find("select.people-affiliation")));
        Assert.Equal(string.Empty, Selected(cut.Find("select.people-role")));
        Assert.Contains(("gamma", "Gamma"), Options(cut.Find("select.people-affiliation")));
        Assert.Equal(["Anna"], RowNames(cut));
        Assert.Equal("Lead · ACME, Gamma", Details(cut, "Anna"));
    }

    // Anna: role and [[ACME]]; Bea: only a role; Carl: only an affiliation with a control character; Dan: neither.
    private void WritePeople()
    {
        data.WriteNote("people", "# people\naffiliation:: Not A Person\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\nrole:: Lead\naffiliation:: [[ACME]]\n");
        data.WriteFile(Path.Combine("people", "Bea.md"), "# Bea\nrole:: Tester\n");
        data.WriteFile(Path.Combine("people", "Carl.md"), "# Carl\naffiliation:: Beta\u0007Co\n");
        data.WriteFile(Path.Combine("people", "Dan.md"), "# Dan\n");
    }

    // Rewrites a file and moves its time on, so the index re-parses it whatever the file system's time resolution.
    private void Edit(string relative, string text)
    {
        string path = Path.Combine(data.Root, relative);
        DateTime before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    private static (string Value, string Text)[] Options(IElement select) =>
        select.QuerySelectorAll("option").Select(o => (o.GetAttribute("value") ?? string.Empty, o.TextContent)).ToArray();

    private static string Selected(IElement select) =>
        select.QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).GetAttribute("value") ?? string.Empty;

    // The row names without their details line.
    private static string[] RowNames(IRenderedComponent<PeoplePanel> cut) =>
        cut.FindAll("ul.people-list > li > button.person-open")
            .Select(b => string.Concat(b.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent)))
            .ToArray();

    private static string? Details(IRenderedComponent<PeoplePanel> cut, string name) =>
        cut.FindAll("ul.people-list > li > button.person-open")
            .Single(b => string.Concat(b.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent)) == name)
            .QuerySelector("span.person-details")?.TextContent;

    private static void AssertEmpty(IRenderedComponent<PeoplePanel> cut)
    {
        Assert.Empty(cut.FindAll("ul.people-list"));
        IElement status = cut.Find("p[role=status]");
        Assert.True(status.ClassList.Contains("grey"));
        Assert.Equal(EmptyText, status.TextContent);
    }

    private static string[] Names(IRenderedComponent<PeoplePanel> cut) =>
        cut.FindAll("ul.people-list > li > button.person-open").Select(b => b.TextContent).ToArray();

    private static NoteInfo Info(string name) => new(name, new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Local), name + ".md");
}
