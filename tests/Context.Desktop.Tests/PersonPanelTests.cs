using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 156 AC-11: the person page's panel: the notes that mention the person and their appointments.
public sealed class PersonPanelTests : IDisposable
{
    private const string NotADatabase = "this is not a database, just some text that fills the header....";

    // The load runs DesktopState.Person on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-11: the mentioning notes are typed buttons that report the note; appointments show date, time text, one-line
    // title and location, latest first; nothing is written but context.db.
    [Fact]
    public async Task Render_NotesAndAppointments()
    {
        WriteAnna();
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        List<string> opened = [];

        IRenderedComponent<PersonPanel> cut = data.Bunit.Render<PersonPanel>(p => p
            .Add(c => c.Person, "Anna")
            .Add(c => c.OnOpen, (string name) => opened.Add(name)));

        cut.WaitForAssertion(() => Assert.Equal(["Plan", "Review"], NoteNames(cut)), Wait);
        Assert.Equal(["Mentioned in", "Appointments"], cut.FindAll("h2").Select(h => h.TextContent).ToArray());
        Assert.Equal("Person", cut.Find("section.person-panel").GetAttribute("aria-label"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(
            ["2026-10-30|all day|Offsite with @[Anna]|", "2026-10-29|11:00-12:00|Sync|Room 1 east"],
            cut.FindAll(".person-appointment").Select(Row).ToArray());
        Assert.Equal("Sync", cut.Find(".person-appointment:nth-child(2) .title").GetAttribute("title"));
        Assert.Empty(cut.FindAll("p[role=status]"));

        await cut.FindAll(".person-notes button")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(["Review"], opened);
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-11: nothing found shows the grey line.
    [Fact]
    public async Task Render_NothingFound_GreyLine()
    {
        data.WriteNote("Plan", "@[Bob]\n");
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(
            "No notes or appointments mention Anna.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-11: a context.db that is not a database shows the red line (the appointments still show) and is left alone.
    [Fact]
    public async Task Render_BadIndex_RedLine_AppointmentsStill()
    {
        WriteAnna();
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the mentions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.True(cut.Find("p[role=status]").ClassList.Contains("red"));
        Assert.Empty(cut.FindAll(".person-notes"));
        Assert.Equal(2, cut.FindAll(".person-appointment").Count);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-11: a bad index with no appointments shows only the red line.
    [Fact]
    public async Task Render_BadIndex_NoAppointments_OnlyRedLine()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteFile(TagIndex.FileName, NotADatabase);
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal("Can't read the mentions.", cut.Find("p[role=status]").TextContent), Wait);
        Assert.Single(cut.FindAll("p"));
        Assert.Empty(cut.FindAll("ul"));
    }

    // AC-11: after the state changed the panel shows the new mention without reopening.
    [Fact]
    public async Task StateChanged_ShowsNewMentions()
    {
        data.WriteNote("Plan", "plain\n");
        await data.RefreshAsync();
        IRenderedComponent<PersonPanel> cut = Render("Anna");
        cut.WaitForAssertion(() => Assert.Equal(
            "No notes or appointments mention Anna.", cut.Find("p[role=status]").TextContent), Wait);

        data.WriteNote("Plan", "now @[anna]\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, "Plan.md"), DateTime.UtcNow.AddSeconds(5));
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
    }

    // AC-11: Loading… before the first result; a result for a person who is no longer shown is dropped; the load
    // runs under indexGate.
    [Fact]
    public async Task PersonChanges_StaleResultDropped_UnderIndexGate()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteNote("Review", "@[Bob]\n");
        await data.RefreshAsync();
        object gate = DesktopFixture.IndexGate(data.State!);
        IRenderedComponent<PersonPanel> cut;

        Monitor.Enter(gate);
        try
        {
            cut = Render("Anna");
            cut.Render(p => p.Add(c => c.Person, "Bob"));
            Assert.Equal("Loading…", cut.Find("p[role=status]").TextContent);
            Assert.True(cut.Find("p[role=status]").ClassList.Contains("grey"));
        }
        finally
        {
            Monitor.Exit(gate);
        }

        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
        typeof(DesktopState).GetMethod("RaiseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(data.State, null);
        cut.WaitForAssertion(() => Assert.Equal(["Review"], NoteNames(cut)), Wait);
    }

    // AC-11: titles and locations from disk are text, never markup; controls become U+FFFD; the full text is in the
    // title attribute.
    [Fact]
    public async Task Render_HostileText_AsText()
    {
        data.WriteNote("Plan", "@[Anna]\n");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("<b>x</b> \u0007 @[Anna]", "20261029T100000Z", "20261029T110000Z", "<img src=x>")));
        await data.RefreshAsync();

        IRenderedComponent<PersonPanel> cut = Render("Anna");

        cut.WaitForAssertion(() => Assert.Equal(["Plan"], NoteNames(cut)), Wait);
        Assert.Empty(cut.FindAll("b, img"));
        Assert.Equal("<b>x</b> \uFFFD @[Anna]", cut.Find(".person-appointment .title").TextContent);
        Assert.Equal("<b>x</b> \uFFFD @[Anna]", cut.Find(".person-appointment .title").GetAttribute("title"));
        Assert.Equal("<img src=x>", cut.Find(".person-appointment .location").GetAttribute("title"));
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<PersonPanel> Render(string person) =>
        data.Bunit.Render<PersonPanel>(p => p.Add(c => c.Person, person));

    private static string[] NoteNames(IRenderedComponent<PersonPanel> cut) =>
        cut.FindAll(".person-notes button").Select(b => b.TextContent).ToArray();

    private static string Row(IElement row) =>
        string.Join("|", new[] { ".date", ".time", ".title", ".location" }
            .Select(s => row.QuerySelector(s)?.TextContent ?? string.Empty));

    // Two notes and the person page mention Anna; a calendar file holds an all-day event mentioning her, a timed
    // event listing her as an attendee (with an address) and one that does not concern her.
    private void WriteAnna()
    {
        data.WriteNote("Plan", "Ask @[Anna]\n");
        data.WriteNote("Review", "With @[ANNA]\n");
        data.WriteFile(Path.Combine("people", "Anna.md"), "# Anna\n\n@[Anna] herself\n");
        data.WriteNote("people", "# people\n");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            "BEGIN:VEVENT\r\nSUMMARY:Offsite with @[Anna]\r\nDTSTART;VALUE=DATE:20261030\r\nEND:VEVENT\r\n",
            "BEGIN:VEVENT\r\nSUMMARY:Sync\r\nLOCATION:Room 1\\neast\r\nATTENDEE;CN=\"anna\":mailto:anna@example.com\r\n"
                + "DTSTART:20261029T100000Z\r\nDTEND:20261029T110000Z\r\nEND:VEVENT\r\n",
            DesktopFixture.Event("Other", "20261029T120000Z", "20261029T130000Z")));
    }
}
