using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 214 AC-1 to AC-4: the shown day's appointments at the top of the right sidebar, through Main.
public sealed class DayAppointmentsPanelTests : IDisposable
{
    // The sidebar list and an opened note load on Task.Run; every wait passes this bounded timeout.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // The fixture's clock is 09:00 UTC, 10:00 in its zone: Early is past, Standup is the last past one.
    private void WriteDays() =>
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Early", "20261029T070000", "20261029T080000"),
            DesktopFixture.Event("Standup", "20261029T083000", "20261029T093000"),
            DesktopFixture.Event("Plan", "20261029T110000", "20261029T120000"),
            DesktopFixture.Event("Retro", "20261030T100000", "20261030T110000"),
            DesktopFixture.Event("Old", "20261028T100000", "20261028T110000"),
            DesktopFixture.Event("Older", "20261028T120000", "20261028T130000")));

    // AC-1, AC-2: the first child of the sidebar, Today, icon-only arrows that step the day and come back to Today.
    [Fact]
    public async Task Arrows_StepTheDay_AndReturnToToday()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        IElement section = cut.Find("aside.todos-region").Children[0];
        Assert.Equal("SECTION", section.TagName);
        Assert.Contains("day-appointments", section.ClassList);
        Assert.Equal("Appointments", section.GetAttribute("aria-label"));
        Assert.Equal("Today", Heading(cut));
        IElement prev = cut.Find(".day-appointments button.day-prev");
        IElement next = cut.Find(".day-appointments button.day-next");
        Assert.Equal(("button", "Previous day", "Previous day"), (prev.GetAttribute("type"), prev.GetAttribute("aria-label"), prev.GetAttribute("title")));
        Assert.Equal(("button", "Next day", "Next day"), (next.GetAttribute("type"), next.GetAttribute("aria-label"), next.GetAttribute("title")));
        Assert.Equal(string.Empty, prev.TextContent.Trim());
        Assert.Equal(string.Empty, next.TextContent.Trim());
        Assert.Equal(
            ["day-prev", "day-appointments-day", "day-next"],
            cut.Find(".day-appointments .day-appointments-bar").Children.Select(c => c.ClassList[0]).ToArray());

        await next.ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 1);
        Assert.Equal("Friday, 30 October 2026", Heading(cut));
        Assert.Equal(["10:00-11:00 Retro"], Items(cut));

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 2);
        Assert.Equal("Today", Heading(cut));

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Wednesday, 28 October 2026", Heading(cut)), Wait);
        WaitForItems(cut, 2);
        Assert.Equal(["10:00-11:00 Old", "12:00-13:00 Older"], Items(cut));

        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Today", Heading(cut)), Wait);
        WaitForItems(cut, 2);
    }

    // AC-2: the arrows stop at 1900-01-01 and 2100-12-31.
    [Theory]
    [InlineData(1900, 1, 1, true, false)]
    [InlineData(2100, 12, 31, false, true)]
    [InlineData(2026, 10, 29, false, false)]
    public async Task Arrows_DisabledAtTheBounds(int year, int month, int day, bool prevDisabled, bool nextDisabled)
    {
        data.Day = new DateOnly(year, month, day);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);

        Assert.Equal("Today", Heading(cut));
        Assert.Equal(prevDisabled, cut.Find(".day-appointments button.day-prev").HasAttribute("disabled"));
        Assert.Equal(nextDisabled, cut.Find(".day-appointments button.day-next").HasAttribute("disabled"));
    }

    // AC-2: one step away from a bound reaches it and disables that arrow.
    [Fact]
    public async Task Arrows_StepOntoTheBound_Disables()
    {
        data.Day = new DateOnly(1900, 1, 2);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Monday, 1 January 1900", Heading(cut)), Wait);
        Assert.True(cut.Find(".day-appointments button.day-prev").HasAttribute("disabled"));
        Assert.False(cut.Find(".day-appointments button.day-next").HasAttribute("disabled"));
    }

    // AC-3: on today the last past appointment and the ones not yet past; the time text and a real button each.
    [Fact]
    public async Task Today_LastPastAndUpcoming()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
        IElement item = cut.FindAll(".day-appointments .appointment-item")[0];
        Assert.Equal("BUTTON", item.TagName);
        Assert.Equal("button", item.GetAttribute("type"));
    }

    // AC-3: the boundary End == now is past; a later clock leaves only the last past one.
    [Fact]
    public async Task Today_EndAtNow_IsPast_AllPast_OnlyLast()
    {
        WriteDays();
        data.UtcNow = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));

        data.UtcNow = new DateTime(2026, 10, 29, 11, 0, 0, DateTimeKind.Utc);
        await data.State!.Refresh();

        WaitForItems(cut, 1);
        Assert.Equal(["11:00-12:00 Plan"], Items(cut));
    }

    // AC-4: (no title), one-line text, a night appointment's outside side, and the cached feed copy (no download).
    [Fact]
    public async Task Items_NoTitle_OneLine_CachedFeed()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event(string.Empty, "20261029T110000", "20261029T120000"),
            DesktopFixture.Event("<b>Two\\nlines</b>", "20261029T130000", "20261030T010000")));
        data.WriteCache(
            DesktopFixture.Calendar(DesktopFixture.Event("From feed", "20261029T150000Z", "20261029T160000Z")),
            new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 3);

        Assert.Equal(["11:00-12:00 (no title)", "13:00-… <b>Two lines</b>", "16:00-17:00 From feed"], Items(cut));
        Assert.Empty(cut.FindAll(".day-appointments b"));
        Assert.Equal(0, data.Http.Requests);
    }

    // AC-4: an empty day shows grey "No appointments."; an unreadable file a red line.
    [Fact]
    public async Task EmptyDay_GreyLine_UnreadableFile_RedLine()
    {
        data.WriteCalendar("bad.ics", "not a calendar");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);

        IElement empty = cut.FindAll(".day-appointments p").Single(p => p.TextContent == "No appointments.");
        Assert.Contains("grey", empty.ClassList);
        IElement bad = cut.FindAll(".day-appointments p.red").Single();
        Assert.Equal("Can't read calendar/bad.ics", bad.TextContent);
        Assert.Empty(cut.FindAll(".day-appointments .appointment-item"));
    }

    // AC-1: shown with a note open and on the Settings page too, still first in the sidebar.
    [Fact]
    public async Task Shown_WithANoteOpen_AndOnSettings()
    {
        WriteDays();
        data.WriteNote("Plan", "# Plan\n");
        EditorJs js = new EditorJs(data.Bunit);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        int mounts = js.Calls("ContextEditor.mount").Count;
        await cut.FindAll(".notes-region button.name").Single(b => b.TextContent == "Plan").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        WaitForItems(cut, 2);
        Assert.Contains("day-appointments", cut.Find("aside.todos-region").Children[0].ClassList);
        Assert.Equal("Today", Heading(cut));

        await cut.Find(".settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".settings-panel")), Wait);
        WaitForItems(cut, 2);
        Assert.Contains("day-appointments", cut.Find("aside.todos-region").Children[0].ClassList);
    }

    // 216 AC-9: a Stale feed shows the grey offline line with the cache time in the view's zone, below the list, on
    // today and on another day alike.
    [Fact]
    public async Task Feed_Stale_GreyLineBelowTheList_OnEveryDay()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(
            DesktopFixture.Calendar(DesktopFixture.Event("Sync", "20261029T140000Z", "20261029T150000Z")),
            new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 1);

        IElement line = cut.Find(".day-appointments p.feed");
        Assert.Equal("Exchange calendar offline, showing the copy from 2026-10-29 08:30.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Equal(["15:00-16:00 Sync"], Items(cut));
        Assert.Equal(line, cut.Find(".day-appointments").Children[^1]);

        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Friday, 30 October 2026", Heading(cut)), Wait);
        WaitForEmpty(cut);
        Assert.Equal(
            "Exchange calendar offline, showing the copy from 2026-10-29 08:30.",
            cut.Find(".day-appointments p.feed.grey").TextContent);
    }

    // 216 AC-9: a Failed feed shows the red line with the fixed reason; the link never shows.
    [Fact]
    public async Task Feed_Failed_RedLineWithReason()
    {
        data.SaveLink(FakeHttpHandler.Link);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);

        IElement line = cut.Find(".day-appointments p.feed");
        Assert.Equal("Can't download the Exchange calendar (HTTP 500).", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.Host, cut.Markup, StringComparison.Ordinal);
    }

    // 216 AC-9: no line when no feed is set up, or when the download worked.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Feed_NotConfiguredOrFresh_NoLine(bool fresh)
    {
        if (fresh)
        {
            data.SaveLink(FakeHttpHandler.Link);
            data.Http = FakeHttpHandler.Ok(
                DesktopFixture.Calendar(DesktopFixture.Event("Sync", "20261029T140000Z", "20261029T150000Z")));
        }

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, fresh ? 1 : 0);

        Assert.Empty(cut.FindAll("p.feed"));
        Assert.Equal(fresh ? CalendarFeedStatus.Fresh : CalendarFeedStatus.NotConfigured, data.State!.Snapshot!.Current!.Status);
    }

    private static string Heading(IRenderedComponent<Main> cut) => cut.Find(".day-appointments h2.day-appointments-day").TextContent;

    private static string[] Items(IRenderedComponent<Main> cut) =>
        cut.FindAll(".day-appointments .appointment-item")
            .Select(b => b.QuerySelector(".time")!.TextContent + " " + b.QuerySelector(".appointment-title")!.TextContent)
            .ToArray();

    // ".day-appointments shows no Loading… and has N .appointment-item".
    private static void WaitForItems(IRenderedComponent<Main> cut, int count) =>
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".day-appointments").TextContent, StringComparison.Ordinal);
            Assert.Equal(count, cut.FindAll(".day-appointments .appointment-item").Count);
        }, Wait);

    // ".day-appointments shows no Loading… and no .appointment-item".
    private static void WaitForEmpty(IRenderedComponent<Main> cut) => WaitForItems(cut, 0);
}
