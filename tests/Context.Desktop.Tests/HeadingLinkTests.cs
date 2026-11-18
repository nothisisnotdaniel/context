using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 205 AC-6 to AC-8: [[Note#Heading]] links in the editor and through Main, with the page's editor mocked: headings
// returns canned JSON and every scrollToHeading call is recorded.
public sealed class HeadingLinkTests : IDisposable
{
    // Opening a note reads it on Task.Run; every wait passes this bounded timeout, a real failure still fails within it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // Plan's headings as the editor reports them: "Goals" matches the second one, after trimming, collapsing spaces and
    // ignoring case; the third has the same text and is never the one picked.
    private const string PlanHeadings =
        """[{"level":2,"text":"Intro"},{"level":2,"text":"  next   GOALS "},{"level":3,"text":"Next goals"}]""";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-6: a heading link to another note asks Main to open the resolved note at the heading.
    [Fact]
    public async Task Editor_HeadingLink_OpensNoteWithHeading()
    {
        data.WriteNote("Other", "See [[plan#Next goals]]\n");
        data.WriteNote("Plan", "## Intro\n");
        EditorJs js = new EditorJs(data.Bunit);
        List<(string Name, string Heading)> headings = [];
        List<string> opened = [];
        IRenderedComponent<NoteEditor> cut = await Render("Other", p => p
            .Add(e => e.OnOpenHeading, ((string Name, string Heading) link) => headings.Add(link))
            .Add(e => e.OnOpenNote, (string name) => opened.Add(name)));

        await cut.InvokeAsync(() => js.Page().OnLink("[[plan#Next goals]]"));

        Assert.Equal([("Plan", "Next goals")], headings);
        Assert.Empty(opened);
        Assert.Empty(js.Jumps());
    }

    // AC-4/6: a note whose name has a # opens as a note, with no heading.
    [Fact]
    public async Task Editor_NameWithHash_OpensWholeName()
    {
        data.WriteNote("Other", "See [[C# notes]]\n");
        data.WriteNote("C# notes", "x\n");
        data.WriteNote("C", "x\n");
        EditorJs js = new EditorJs(data.Bunit);
        List<(string Name, string Heading)> headings = [];
        List<string> opened = [];
        IRenderedComponent<NoteEditor> cut = await Render("Other", p => p
            .Add(e => e.OnOpenHeading, ((string Name, string Heading) link) => headings.Add(link))
            .Add(e => e.OnOpenNote, (string name) => opened.Add(name)));

        await cut.InvokeAsync(() => js.Page().OnLink("[[C# notes]]"));

        Assert.Equal(["C# notes"], opened);
        Assert.Empty(headings);
    }

    // AC-6: a note opened at a heading jumps once to the first heading that matches, after its mount.
    [Fact]
    public async Task Editor_OpenedAtHeading_JumpsToFirstMatch()
    {
        data.WriteNote("Plan", "## Intro\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);

        IRenderedComponent<NoteEditor> cut = await Render("Plan", p => p.Add(e => e.Heading, " Next Goals"));

        cut.WaitForAssertion(() => Assert.Equal([1], js.Jumps()), Wait);
        Assert.Equal(string.Empty, cut.Find(".status").TextContent.Trim());
    }

    // AC-7: a heading the note doesn't have: no jump, and the grey line says so.
    [Fact]
    public async Task Editor_OpenedAtMissingHeading_GreyLineNoJump()
    {
        data.WriteNote("Plan", "## Intro\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);

        IRenderedComponent<NoteEditor> cut = await Render("Plan", p => p.Add(e => e.Heading, "Goals\nnow"));

        cut.WaitForAssertion(() => Assert.Equal("Plan has no heading Goals now.", cut.Find(".status").TextContent), Wait);
        Assert.Equal(["status", "grey"], cut.Find(".status").ClassList.ToArray());
        Assert.Empty(js.Jumps());
    }

    // AC-7: a heading link to a missing note still names the whole target.
    [Fact]
    public async Task Editor_HeadingLinkToMissingNote_NoNoteNamed()
    {
        data.WriteNote("Plan", "See [[Ghost#Goals]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<NoteEditor> cut = await Render("Plan", _ => { });

        await cut.InvokeAsync(() => js.Page().OnLink("[[Ghost#Goals]]"));

        Assert.Equal("No note named Ghost#Goals.", cut.Find(".status").TextContent);
        Assert.Empty(js.Jumps());
    }

    // AC-8: [[#Heading]] and a heading link to the open note itself jump in place: no remount, unsaved text kept.
    [Theory]
    [InlineData("[[#next goals]]")]
    [InlineData("[[plan.md#Next  goals]]")]
    public async Task Editor_OwnHeading_JumpsInPlace(string raw)
    {
        data.WriteNote("Plan", "See " + raw + "\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);
        List<string> opened = [];
        List<(string Name, string Heading)> headings = [];
        IRenderedComponent<NoteEditor> cut = await Render("Plan", p => p
            .Add(e => e.OnOpenHeading, ((string Name, string Heading) link) => headings.Add(link))
            .Add(e => e.OnOpenNote, (string name) => opened.Add(name)));
        await js.Change(cut, "unsaved\n");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("span.dirty-dot")), Wait);

        await cut.InvokeAsync(() => js.Page().OnLink(raw));

        Assert.Equal([1], js.Jumps());
        Assert.Single(js.Calls("ContextEditor.mount"));
        Assert.Single(cut.FindAll("span.dirty-dot"));
        Assert.Equal("See " + raw + "\n", File.ReadAllText(Path.Combine(data.Root, "Plan.md")));
        Assert.Empty(opened);
        Assert.Empty(headings);
    }

    // AC-8: [[#Heading]] the open note doesn't have says so, and opens nothing.
    [Fact]
    public async Task Editor_OwnMissingHeading_GreyLine()
    {
        data.WriteNote("Plan", "See [[#Nope]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);
        IRenderedComponent<NoteEditor> cut = await Render("Plan", _ => { });

        await cut.InvokeAsync(() => js.Page().OnLink("[[#Nope]]"));

        Assert.Equal("Plan has no heading Nope.", cut.Find(".status").TextContent);
        Assert.Empty(js.Jumps());
    }

    // AC-6: through Main, a dirty note is saved first, then Plan opens and jumps to the heading.
    [Fact]
    public async Task Main_HeadingLink_SavesFirst_OpensAtHeading()
    {
        data.WriteNote("Other", "See [[Plan#Next goals]]\n");
        data.WriteNote("Plan", "## Intro\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Other");
        await js.Change(cut, "Edited [[Plan#Next goals]]\n");
        int mounts = js.Calls("ContextEditor.mount").Count;

        await cut.InvokeAsync(() => js.Page().OnLink("[[Plan#Next goals]]"));

        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.Equal([1], js.Jumps()), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Equal("Edited [[Plan#Next goals]]\n", File.ReadAllText(Path.Combine(data.Root, "Other.md")));
        Assert.Empty(cut.FindAll(".dialog"));
    }

    // AC-7: through Main, a missing heading opens Plan at the top with the grey line.
    [Fact]
    public async Task Main_MissingHeading_OpensAtTop_GreyLine()
    {
        data.WriteNote("Other", "See [[Plan#Goals]]\n");
        data.WriteNote("Plan", "## Intro\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings("""[{"level":2,"text":"Intro"}]""");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Other");

        await cut.InvokeAsync(() => js.Page().OnLink("[[Plan#Goals]]"));

        cut.WaitForAssertion(() => Assert.Equal("Plan has no heading Goals.", cut.Find(".note-editor .status").TextContent), Wait);
        Assert.Equal("Plan", cut.Find(".note-title").TextContent);
        Assert.Empty(js.Jumps());
    }

    // AC-6: a plain link after a heading link opens at the top: the heading is used once.
    [Fact]
    public async Task Main_PlainLinkAfterHeadingLink_NoJump()
    {
        data.WriteNote("Other", "See [[Plan#Next goals]]\n");
        data.WriteNote("Plan", "See [[Other]]\n");
        EditorJs js = new EditorJs(data.Bunit);
        js.SetHeadings(PlanHeadings);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await Open(cut, js, "Other");
        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.InvokeAsync(() => js.Page().OnLink("[[Plan#Next goals]]"));
        cut.WaitForAssertion(() => Assert.Equal([1], js.Jumps()), Wait);

        await cut.InvokeAsync(() => js.Page().OnLink("[[Other]]"));

        cut.WaitForAssertion(() => Assert.Equal(mounts + 2, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        Assert.Equal("Other", cut.Find(".note-title").TextContent);
        Assert.Equal([1], js.Jumps());
    }

    private async Task<IRenderedComponent<NoteEditor>> Render(
        string name, Action<ComponentParameterCollectionBuilder<NoteEditor>> parameters)
    {
        await data.RefreshAsync();
        IRenderedComponent<NoteEditor> cut = data.Bunit.Render<NoteEditor>(p =>
        {
            p.Add(e => e.Name, name);
            parameters(p);
        });
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        return cut;
    }

    private static async Task Open(IRenderedComponent<Main> cut, EditorJs js, string name)
    {
        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".notes-region button.name").Single(b => b.TextContent == name).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.save").HasAttribute("disabled")), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
    }
}
