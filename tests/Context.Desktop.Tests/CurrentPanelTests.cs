using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class CurrentPanelTests : IDisposable
{
    private const string Xss = "<script>alert(1)</script>";
    private const string XssImage = "<img src=x onerror=alert(1)>";

    // Opening a note from Main reads it on Task.Run; every wait for that passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-2: title, then the appointments table (time, one-line title or "(no title)", location), Due, Linked notes.
    [Fact]
    public async Task Render_FullDay_ShowsSectionsInOrder()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000", "Room\\n1"),
            DesktopFixture.Event(string.Empty, "20261029T110000", "20261029T120000"),
            DesktopFixture.Event("Night", "20261028T230000", "20261029T010000")));
        data.WriteNote("Alpha", "# Alpha\n#work #[big project]\n");
        data.WriteNote("Beta", "# Beta\n#home\n");
        data.AddTodo("Report\nfor #work", DesktopFixture.Today.AddDays(-1));
        data.AddTodo("Plan #[big project]", DesktopFixture.Today);
        data.AddTodo("Later", DesktopFixture.Today.AddDays(3));

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal("Today, 2026-10-29", cut.Find("h1.title").TextContent);
        string[] headings = cut.FindAll("h2").Select(h => h.TextContent).ToArray();
        Assert.Equal(["Appointments", "Due", "Linked notes"], headings);

        IReadOnlyList<IElement> rows = cut.FindAll("table.appointments tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["#", "Time", "Appointment", "Where"], cut.FindAll("table.appointments thead th").Select(h => h.TextContent).ToArray());
        Assert.Equal(["1", "…-01:00", "Night", string.Empty], Cells(rows[0]));
        Assert.Equal(["2", "09:00-09:30", "Standup", "Room 1"], Cells(rows[1]));
        Assert.Equal(["3", "11:00-12:00", "(no title)", string.Empty], Cells(rows[2]));
        Assert.Equal("Standup", rows[1].Children[2].GetAttribute("title"));
        Assert.True(rows[1].Children[0].ClassList.Contains("number"));

        IReadOnlyList<IElement> due = cut.FindAll("table.due tbody tr");
        Assert.Equal(2, due.Count);
        Assert.Equal(["2026-10-28", "Report for #work"], Cells(due[0]));
        Assert.Equal(["2026-10-29", "Plan #[big project]"], Cells(due[1]));
        Assert.True(due[0].Children[0].ClassList.Contains("overdue"));
        Assert.True(due[1].Children[0].ClassList.Contains("today"));

        IElement linked = Assert.Single(cut.FindAll("ul.linked li"));
        Assert.Equal("Alpha", linked.QuerySelector(".name")!.TextContent);
        Assert.Equal("#[big project], #work", linked.QuerySelector(".tags")!.TextContent);
    }

    // AC-2: at most 15 rows, the last reads "… N more".
    [Fact]
    public async Task Render_TwentyAppointments_FifteenRowsWithMore()
    {
        string[] events = Enumerable.Range(0, 20)
            .Select(i => DesktopFixture.Event($"M{i:00}", $"20261029T{8 + i / 4:00}{i % 4 * 15:00}00",
                $"20261029T{8 + i / 4:00}{i % 4 * 15 + 10:00}00"))
            .ToArray();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(events));

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IReadOnlyList<IElement> rows = cut.FindAll("table.appointments tbody tr");
        Assert.Equal(15, rows.Count);
        Assert.Equal("M13", rows[13].Children[2].TextContent);
        Assert.Equal("14", rows[13].QuerySelector("td.number")!.TextContent);
        Assert.Equal("… 6 more", rows[14].TextContent.Trim());
        Assert.Equal(4, rows[14].Children.Length);
        Assert.True(rows[14].Children[2].ClassList.Contains("grey"));
    }

    // AC-2: the grey empty lines and no tables when there is nothing.
    [Fact]
    public async Task Render_EmptyFolder_ShowsEmptyLines()
    {
        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IReadOnlyList<IElement> lines = cut.FindAll("p");
        string folder = Path.Combine(data.Root, CalendarStore.FolderName);
        Assert.Equal(
            [
                $"No appointments today. Put .ics files in {folder} to see them here.",
                "Nothing due today. Add one with /todo today <text>.",
            ],
            lines.Select(p => p.TextContent).ToArray());
        Assert.All(lines, p => Assert.True(p.ClassList.Contains("grey")));
        Assert.Empty(cut.FindAll("table"));
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-2: a red line per unreadable calendar file.
    [Fact]
    public async Task Render_UnreadableCalendarFile_RedLine()
    {
        data.WriteCalendar("bad.ics", "not a calendar");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement line = cut.FindAll("p.red").Single();
        Assert.Equal("Can't read calendar/bad.ics", line.TextContent);
    }

    // AC-2: Stale shows the grey offline line with the cache time in the view's zone.
    [Fact]
    public async Task Render_StaleFeed_GreyLineInZone()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(
            DesktopFixture.Calendar(DesktopFixture.Event("Sync", "20261029T140000Z", "20261029T150000Z")),
            new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement line = cut.Find("p.feed");
        Assert.Equal("Exchange calendar offline, showing the copy from 2026-10-29 08:30.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Equal("15:00-16:00", cut.Find("table.appointments td.time").TextContent);
        Assert.Equal(1, data.Http.Requests);
    }

    // AC-2: Failed shows the red line with the fixed reason.
    [Fact]
    public async Task Render_FailedFeed_RedLineWithReason()
    {
        data.SaveLink(FakeHttpHandler.Link);

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement line = cut.Find("p.feed");
        Assert.Equal("Can't download the Exchange calendar (HTTP 500).", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup);
        Assert.DoesNotContain(FakeHttpHandler.Host, cut.Markup);
    }

    // AC-2: a bad todos.json shows the red line in place of Due; the title and appointments still show.
    [Fact]
    public async Task Render_BadTodos_RedLine()
    {
        data.WriteFile(TodoStore.FileName, "{ not json");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000")));

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal("Can't read todos.json. Fix or move the file and try again.", cut.Find("p.red").TextContent);
        Assert.Single(cut.FindAll("table.appointments tbody tr"));
        Assert.Empty(cut.FindAll("table.due"));
    }

    // AC-2: due todos with no tag match show the grey linked-notes line.
    [Fact]
    public async Task Render_NothingLinked_GreyLine()
    {
        data.AddTodo("Call #nobody", DesktopFixture.Today);

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement line = cut.FindAll("p.grey").Last();
        Assert.Equal("No linked notes. Add the same #tag to a todo and a note to link them.", line.TextContent);
        Assert.Empty(cut.FindAll("ul.linked"));
    }

    // AC-2: a bad context.db shows the red line in place of the linked notes.
    [Fact]
    public async Task Render_BadIndex_RedLine()
    {
        data.AddTodo("Call #work", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal(
            "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.",
            cut.Find("p.red").TextContent);
        Assert.Single(cut.FindAll("table.due tbody tr"));
    }

    // AC-5: appointment title and location, todo text and linked note name are encoded text, never elements.
    [Fact]
    public async Task Render_HostileText_EncodedAndCleaned()
    {
        string hostile = Xss + " " + XssImage + " \u0007";
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event(hostile, "20261029T090000", "20261029T093000", hostile)));
        data.AddTodo(hostile + " #work", DesktopFixture.Today);
        if (!OperatingSystem.IsWindows())
        {
            // Windows file names can't hold < > or control chars; the notes panel test covers names elsewhere.
            data.WriteNote(XssImage + " \u0007", "#work\n");
        }

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        string shown = Xss + " " + XssImage + " �";
        IReadOnlyList<IElement> cells = cut.FindAll("table.appointments td");
        Assert.Equal("1", cells[0].TextContent);
        Assert.Equal(shown, cells[2].TextContent);
        Assert.Equal(shown, cells[3].TextContent);
        Assert.Equal(shown, cells[3].GetAttribute("title"));
        Assert.Equal(shown + " #work", cut.FindAll("table.due td")[1].TextContent);
        Assert.Contains("&lt;script&gt;", cut.Markup);
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(XssImage + " �", cut.Find("ul.linked .name").TextContent);
        }
    }

    // AC-7: the calendar link never reaches the page, whether the feed is stale or failed.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Render_CalendarLink_NeverInMarkup(bool cached)
    {
        data.SaveLink("https://secret-host.example/abc-TOKEN123.ics");
        if (cached)
        {
            data.WriteCache(
                DesktopFixture.Calendar(DesktopFixture.Event("Sync", "20261029T140000Z", "20261029T150000Z")),
                new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));
        }

        data.AddTodo("Due", DesktopFixture.Today);
        data.WriteNote("Alpha", "x");

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.NotEmpty(cut.FindAll("p.feed"));
        Assert.NotEmpty(cut.FindAll(".todo"));
        Assert.NotEmpty(cut.FindAll(".note"));
        Assert.DoesNotContain("TOKEN123", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IRenderedComponent<CurrentPanel>> RenderWithOpen(List<string> opened)
    {
        await data.RefreshAsync();
        return data.Bunit.Render<CurrentPanel>(p => p.Add(c => c.OnOpen, (string name) => opened.Add(name)));
    }

    // 160 AC-8: a tr.related row under each appointment with related notes: one button per note (Title shown, full
    // name in title), grey matches; a click raises OnOpen with the full name.
    [Fact]
    public async Task Render_RelatedRows_ButtonsAndOpen()
    {
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("ACME kickoff", "20261029T090000", "20261029T100000"),
            DesktopFixture.Event("Lunch", "20261029T120000", "20261029T130000")));
        data.WriteNote("Clients", "#acme #[acme kickoff]\n");
        data.WriteNote("projects", "# projects\n");
        data.WriteFile(Path.Combine("projects", "ACME Rollout.md"), "# ACME Rollout\n");
        List<string> opened = [];

        IRenderedComponent<CurrentPanel> cut = await RenderWithOpen(opened);

        IReadOnlyList<IElement> rows = cut.FindAll("table.appointments tbody tr");
        Assert.Equal(["appointment", "related", "appointment"], rows.Select(r => r.ClassName));
        IReadOnlyList<IElement> buttons = rows[1].QuerySelectorAll("button").ToList();
        Assert.Equal(["Clients", "ACME Rollout"], buttons.Select(b => b.TextContent));
        Assert.Equal(["Clients", "projects/ACME Rollout"], buttons.Select(b => b.GetAttribute("title")));
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        IReadOnlyList<IElement> matches = rows[1].QuerySelectorAll(".matches").ToList();
        Assert.Equal(["#acme #[acme kickoff]", "acme"], matches.Select(m => m.TextContent));
        Assert.All(matches, m => Assert.True(m.ClassList.Contains("grey")));

        await cut.FindAll("tr.related button")[1].ClickAsync(new MouseEventArgs());

        Assert.Equal(["projects/ACME Rollout"], opened);
        Assert.Empty(cut.FindAll("p.related-failed"));
    }

    // 160 AC-8: in Main, a related button opens the note through RequestOpen.
    [Fact]
    public async Task Main_RelatedButton_OpensTheNote()
    {
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Plan review", "20261029T090000", "20261029T100000")));
        data.WriteNote("Plan", "# Plan\n");
        _ = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        await cut.Find("tr.related button").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Plan", cut.Find(".note-title").TextContent), Wait);
    }

    // 160 AC-8: related rows don't count toward the 15 rows, and hidden appointments show none.
    [Fact]
    public async Task Render_SixteenAppointments_RelatedRowsOutsideTheCap()
    {
        string[] events = Enumerable.Range(0, 16)
            .Select(i => DesktopFixture.Event($"Topic{i:00} acme", $"20261029T{8 + i / 4:00}{i % 4 * 15:00}00",
                $"20261029T{8 + i / 4:00}{i % 4 * 15 + 10:00}00"))
            .ToArray();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(events));
        data.WriteNote("ACME", "x");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal(14, cut.FindAll("tr.appointment").Count);
        Assert.Equal(14, cut.FindAll("tr.related").Count);
        Assert.Equal("… 2 more", Assert.Single(cut.FindAll("tr.more")).TextContent.Trim());
        Assert.Equal(29, cut.FindAll("table.appointments tbody tr").Count);
    }

    // 167 AC-1: each shown appointment row starts with its 1-based number; related and "more" rows have four cells
    // with empty number cells, and the numbers run on past the related rows.
    [Fact]
    public async Task Render_NumberCells_WithRelatedAndMoreRows()
    {
        string[] events = Enumerable.Range(0, 16)
            .Select(i => DesktopFixture.Event($"Topic{i:00} acme", $"20261029T{8 + i / 4:00}{i % 4 * 15:00}00",
                $"20261029T{8 + i / 4:00}{i % 4 * 15 + 10:00}00"))
            .ToArray();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(events));
        data.WriteNote("ACME", "x");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal(
            Enumerable.Range(1, 14).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            cut.FindAll("tr.appointment td.number").Select(c => c.TextContent).ToArray());
        Assert.All(cut.FindAll("tr.appointment"), r => Assert.Equal(4, r.Children.Length));
        IElement related = cut.FindAll("tr.related")[0];
        Assert.Equal(3, related.Children.Length);
        Assert.Equal(string.Empty, related.Children[0].TextContent);
        Assert.Equal(string.Empty, related.Children[1].TextContent);
        Assert.Equal("2", related.Children[2].GetAttribute("colspan"));
        IElement more = cut.Find("tr.more");
        Assert.Equal(["", "", "… 2 more", ""], Cells(more));
    }

    // 160 AC-8: a bad context.db with appointments shows the red related line after the table and no related rows.
    [Fact]
    public async Task Render_BadIndex_WithAppointments_RelatedRedLine()
    {
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Plan", "20261029T090000", "20261029T100000")));
        data.WriteNote("Plan", "#plan\n");
        data.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement line = Assert.Single(cut.FindAll("p.red"));
        Assert.Equal("Can't read the related notes.", line.TextContent);
        Assert.Equal("TABLE", line.PreviousElementSibling!.TagName);
        Assert.Empty(cut.FindAll("tr.related"));
    }

    // 160 AC-8: with appointments and due todos both, both red lines show, each after its own table.
    [Fact]
    public async Task Render_BadIndex_AppointmentsAndDue_BothRedLines()
    {
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Plan", "20261029T090000", "20261029T100000")));
        data.AddTodo("Call #work", DesktopFixture.Today);
        data.WriteFile(TagIndex.FileName, "this is not a database, just some text that fills the header....");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        Assert.Equal(
            [
                "Can't read the related notes.",
                "Can't read context.db. Move it out of the Context folder and it is rebuilt from your notes.",
            ],
            cut.FindAll("p.red").Select(p => p.TextContent));
    }

    // 160 AC-8: control characters in a note name are cleaned in the button text and its title.
    [Fact]
    public async Task Render_RelatedName_ControlCharsCleaned()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows file names can't hold control chars.
            return;
        }

        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("ACME", "20261029T090000", "20261029T100000")));
        data.WriteNote("ACME \u0007" + XssImage, "x");

        IRenderedComponent<CurrentPanel> cut = await data.RenderAsync<CurrentPanel>();

        IElement button = cut.Find("tr.related button");
        Assert.Equal("ACME �" + XssImage, button.TextContent);
        Assert.Equal("ACME �" + XssImage, button.GetAttribute("title"));
        Assert.Empty(cut.FindAll("img"));
        Assert.DoesNotContain("\u0007", cut.Markup, StringComparison.Ordinal);
    }

    private static string[] Cells(IElement row) => row.Children.Select(c => c.TextContent).ToArray();
}
