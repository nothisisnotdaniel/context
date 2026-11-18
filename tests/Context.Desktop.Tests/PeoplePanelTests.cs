using AngleSharp.Dom;
using Bunit;
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
