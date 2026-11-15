using AngleSharp.Dom;
using Bunit;

namespace Context.Tests;

// 169: the weekly review panel.
public sealed class ReviewPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // A note last written at utc (the fixture's zone is UTC+1).
    private void WriteNote(string name, DateTime utc)
    {
        data.WriteNote(name, "# " + name + "\n");
        File.SetLastWriteTimeUtc(Path.Combine(data.Root, name + ".md"), utc);
    }

    private void WriteTodos(string json) => data.WriteFile(TodoStore.FileName, json);

    private static string Todo(string text, string due, string? done = null)
    {
        string doneField = done is null ? string.Empty : $",\"done\":\"{done}\"";
        return $"{{\"text\":\"{text}\",\"due\":\"{due}\",\"created\":\"2026-10-01T08:00:00Z\"{doneField}}}";
    }

    private IRenderedComponent<ReviewPanel> Render(List<string>? opened = null, Action? closed = null)
    {
        data.CreateState();
        return data.Bunit.Render<ReviewPanel>(p => p
            .Add(r => r.OnOpen, (string name) => opened?.Add(name))
            .Add(r => r.OnClose, () => closed?.Invoke()));
    }

    // AC-7: heading, Close, the date range and the four sections in order.
    [Fact]
    public void Render_HeadingRangeAndSectionsInOrder()
    {
        IRenderedComponent<ReviewPanel> cut = Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-range")), Wait);
        Assert.Equal("Week review", cut.Find(".panel-bar h2").TextContent);
        Assert.Equal("Close", cut.Find(".panel-bar button.close").TextContent);
        Assert.Equal("2026-10-23 to 2026-10-29", cut.Find(".review-range").TextContent);
        Assert.Equal(
            ["Changed notes", "Done todos", "Overdue", "Next week"],
            cut.FindAll(".review-panel h3").Select(h => h.TextContent).ToArray());
    }

    // AC-8: every section filled; typed note buttons report the name; overdue class; appointments grouped by day.
    [Fact]
    public void Render_Filled_RowsAndButtons()
    {
        WriteNote("Plan", new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        WriteNote("Old", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        WriteTodos("["
            + Todo("Sent report", "2026-10-20", done: "2026-10-27T09:00:00Z") + ","
            + Todo("Pay bill", "2026-10-25") + ","
            + Todo("Later", "2026-11-03")
            + "]");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261030T090000", "20261030T091500", "Room 1"),
            DesktopFixture.Event("Lunch", "20261030T120000", "20261030T130000"),
            DesktopFixture.Event(string.Empty, "20261101T100000", "20261101T110000")));
        List<string> opened = [];
        int closed = 0;

        IRenderedComponent<ReviewPanel> cut = Render(opened, () => closed++);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-note")), Wait);
        IElement note = cut.Find(".review-note");
        Assert.Equal("2026-10-28 10:00", note.QuerySelector(".date")!.TextContent);
        IElement button = note.QuerySelector("button.note-button")!;
        Assert.Equal("Plan", button.TextContent);
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));

        IElement done = cut.Find(".done-todos .review-todo");
        Assert.Equal("2026-10-27", done.QuerySelector(".date")!.TextContent);
        Assert.Equal("Sent report", done.QuerySelector(".text")!.TextContent);
        IElement overdue = Assert.Single(cut.FindAll(".overdue-todos .review-todo"));
        Assert.True(overdue.QuerySelector(".date")!.ClassList.Contains("overdue"));
        Assert.Equal("Pay bill", overdue.QuerySelector(".text")!.TextContent);

        Assert.Equal(["2026-10-30", "2026-11-01"], cut.FindAll("h4.review-day").Select(h => h.TextContent).ToArray());
        IReadOnlyList<IElement> lists = cut.FindAll("ul.review-appointments");
        Assert.Equal(2, lists.Count);
        IElement standup = lists[0].QuerySelectorAll("li.review-appointment")[0];
        Assert.Equal("09:00-09:15", standup.QuerySelector(".time")!.TextContent);
        Assert.Equal("Standup", standup.QuerySelector(".title")!.TextContent);
        Assert.Equal("Room 1", standup.QuerySelector(".location")!.TextContent);
        Assert.Equal("Lunch", lists[0].QuerySelectorAll("li.review-appointment")[1].QuerySelector(".title")!.TextContent);
        Assert.Equal("(no title)", lists[1].QuerySelector(".title")!.TextContent);
        Assert.Empty(cut.FindAll(".review-panel .red"));

        button.Click();
        cut.Find(".panel-bar button.close").Click();

        Assert.Equal(["Plan"], opened);
        Assert.Equal(1, closed);
    }

    // AC-8: empty sections show the grey texts.
    [Fact]
    public void Render_Empty_GreyTexts()
    {
        IRenderedComponent<ReviewPanel> cut = Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-range")), Wait);
        Assert.Equal(
            ["No notes changed.", "No todos done.", "Nothing overdue.", "No appointments next week."],
            cut.FindAll(".review-panel p.grey:not(.review-range)").Select(p => p.TextContent).ToArray());
        Assert.Empty(cut.FindAll(".review-panel .red"));
    }

    // AC-8: an unreadable todos.json shows the red text in both todo sections; calendar errors use the current texts.
    [Fact]
    public void Render_Errors_RedTexts()
    {
        WriteTodos("not json");
        data.WriteCalendar("bad.ics", "not a calendar");
        data.SaveLink(FakeHttpHandler.Link);

        IRenderedComponent<ReviewPanel> cut = Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-range")), Wait);
        string[] red = cut.FindAll(".review-panel p.red").Select(p => p.TextContent).ToArray();
        Assert.Equal(4, red.Length);
        Assert.Equal("Can't read todos.json.", red[0]);
        Assert.Equal("Can't read todos.json.", red[1]);
        Assert.Equal("Can't read calendar/bad.ics", red[2]);
        Assert.StartsWith("Can't download the Exchange calendar (", red[3]);
        Assert.True(cut.Find(".review-panel p.red.feed").ClassList.Contains("feed"));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup);
    }

    // AC-8: an offline feed with a cached copy shows the grey stale line.
    [Fact]
    public void Render_StaleFeed_GreyLine()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(DesktopFixture.Event("Cached", "20261030T090000", "20261030T100000")),
            new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc));

        IRenderedComponent<ReviewPanel> cut = Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-panel p.grey.feed")), Wait);
        Assert.Equal(
            "Exchange calendar offline, showing the copy from 2026-10-29 08:00.",
            cut.Find(".review-panel p.grey.feed").TextContent);
        Assert.Equal("Cached", cut.Find(".review-appointment .title").TextContent);
    }

    // AC-9: showing the panel writes nothing and creates no context.db.
    [Fact]
    public void Render_WritesNothing()
    {
        WriteNote("Plan", new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        WriteTodos("[" + Todo("Pay bill", "2026-10-25") + "]");
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(DesktopFixture.Event("Standup", "20261030T090000", "20261030T091500")));
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<ReviewPanel> cut = Render();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-note")), Wait);
        Assert.Single(cut.FindAll(".review-appointment"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.False(File.Exists(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // AC-9: a state change reloads the review; the shown rows stay while it reloads.
    [Fact]
    public async Task StateChanged_Reloads_KeepingRows()
    {
        WriteNote("Plan", new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        IRenderedComponent<ReviewPanel> cut = Render();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".review-note")), Wait);

        WriteNote("Fresh", new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc));
        Task refresh = data.State!.Refresh();
        Assert.NotEmpty(cut.FindAll(".review-note"));
        Assert.Empty(cut.FindAll(".review-panel [role=status]"));
        await refresh;

        cut.WaitForAssertion(() => Assert.Equal(
            ["Fresh", "Plan"],
            cut.FindAll(".review-note button").Select(b => b.TextContent).ToArray()), Wait);
    }

    // AC-9: the panel reads through DesktopState.Review and never calls Actions.
    [Fact]
    public void Source_ReadsThroughReview_NoActions()
    {
        string source = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "ReviewPanel.razor"));

        Assert.Contains("State.Review()", source);
        Assert.DoesNotContain("Actions.", source);
    }
}
