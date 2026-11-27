using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
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

        IElement section = cut.Find("aside.todos-region > .sidebar-panels").Children[0];
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
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
        Assert.Equal(["10:00-11:00 Retro"], Items(cut));

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 2);
        Assert.Equal("Today", Heading(cut));

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Wednesday, 28 Oct 2026", Heading(cut)), Wait);
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

        cut.WaitForAssertion(() => Assert.Equal("Monday, 1 Jan 1900", Heading(cut)), Wait);
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
        await NoteOpener.OpenAsync(cut, "Plan");
        cut.WaitForAssertion(() => Assert.Equal(mounts + 1, js.Calls("ContextEditor.mount").Count), Wait);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Find(".links-panel").TextContent, StringComparison.Ordinal), Wait);
        WaitForItems(cut, 2);
        Assert.Contains("day-appointments", cut.Find("aside.todos-region > .sidebar-panels").Children[0].ClassList);
        Assert.Equal("Today", Heading(cut));

        await cut.Find(".settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".settings-panel")), Wait);
        WaitForItems(cut, 2);
        Assert.Contains("day-appointments", cut.Find("aside.todos-region > .sidebar-panels").Children[0].ClassList);
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
        cut.WaitForAssertion(() => Assert.Equal("Friday, 30 Oct 2026", Heading(cut)), Wait);
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

    // 217 AC-3: Outlook's appointments show merged with the .ics ones, and a click opens the appointment page.
    [Fact]
    public async Task Outlook_Ok_MergedRows_ClickOpensThePage()
    {
        WriteDays();
        new SettingsStore(data.Root).SaveOutlook(true);
        data.Outlook.Days[DesktopFixture.Today] =
        [
            new Appointment("From Outlook", null, new DateTime(2026, 10, 29, 10, 30, 0), new DateTime(2026, 10, 29, 11, 0, 0), false),
        ];
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 3);

        Assert.Equal(["08:30-09:30 Standup", "10:30-11:00 From Outlook", "11:00-12:00 Plan"], Items(cut));
        Assert.Empty(cut.FindAll(".day-appointments .outlook-status"));

        await cut.FindAll(".day-appointments .appointment-item")[1].ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(
            () => Assert.Equal("From Outlook", cut.Find(".current-region section.appointment-page h2").TextContent),
            Wait);
    }

    // 217 AC-4: each failure shows its status line under the list; the .ics appointments still show.
    [Theory]
    [InlineData("NotRunning", false, "grey", "Outlook isn't running. Start it to see its appointments.")]
    [InlineData("Denied", false, "red", "Can't read Outlook (access denied).")]
    [InlineData("Failed", false, "red", "Can't read Outlook (error).")]
    [InlineData("Ok", true, "red", "Can't read Outlook (error).")]
    public async Task Outlook_Failure_StatusLine_KeepsTheIcsRows(string kind, bool throws, string css, string text)
    {
        WriteDays();
        new SettingsStore(data.Root).SaveOutlook(true);
        data.Outlook.AppointmentKind = Enum.Parse<OutlookReadKind>(kind);
        data.Outlook.Throws = throws ? new InvalidOperationException("MARKER-217") : null;
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        IElement line = cut.Find(".day-appointments p.outlook-status");
        Assert.Equal(text, line.TextContent);
        Assert.Contains(css, line.ClassList);
        Assert.Equal("status", line.GetAttribute("role"));
        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
        Assert.DoesNotContain("MARKER-217", cut.Markup, StringComparison.Ordinal);
    }

    // 217 AC-5: a source call that doesn't return within the timeout shows "timed out", and the panel leaves Loading….
    [Fact]
    public async Task Outlook_Blocked_TimedOut_LeavesLoading()
    {
        WriteDays();
        new SettingsStore(data.Root).SaveOutlook(true);
        data.OutlookTimeout = TimeSpan.FromMilliseconds(100);
        data.Outlook.Block();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading…", cut.Find(".day-appointments").TextContent, StringComparison.Ordinal);
            Assert.Equal("Can't read Outlook (timed out).", cut.Find(".day-appointments p.outlook-status").TextContent);
        }, Wait);
        Assert.Contains("red", cut.Find(".day-appointments p.outlook-status").ClassList);
        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
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

    // 227 AC-7: a private appointment never shows in the sidebar, and the pages of the shown ones don't list it.
    [Fact]
    public async Task PrivateAppointment_NotShown_NorOnThePages()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T083000", "20261029T093000"),
            DesktopFixture.Event("Doctor", "20261029T100000", "20261029T110000")
                .Replace("END:VEVENT", "CLASS:PRIVATE\r\nEND:VEVENT", StringComparison.Ordinal),
            DesktopFixture.Event("Plan", "20261029T110000", "20261029T120000")));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
        Assert.DoesNotContain("Doctor", cut.Markup, StringComparison.Ordinal);

        foreach ((int index, string title) in new[] { (0, "Standup"), (1, "Plan") })
        {
            await cut.FindAll(".day-appointments .appointment-item")[index].ClickAsync(new MouseEventArgs());
            cut.WaitForAssertion(() =>
            {
                Assert.Equal(title, cut.Find(".current-region section.appointment-page h2").TextContent);
                Assert.DoesNotContain("Loading…", cut.Find(".current-region section.appointment-page").TextContent, StringComparison.Ordinal);
            }, Wait);
            Assert.DoesNotContain("Doctor", cut.Markup, StringComparison.Ordinal);
        }
    }

    // 227 AC-7: a day with only private appointments looks like a day with none.
    [Fact]
    public async Task OnlyPrivateAppointments_LooksEmpty()
    {
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Doctor", "20261029T100000", "20261029T110000")
                .Replace("END:VEVENT", "CLASS:CONFIDENTIAL\r\nEND:VEVENT", StringComparison.Ordinal)));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);

        Assert.DoesNotContain("Doctor", cut.Markup, StringComparison.Ordinal);
    }

    // 289: the sidebar's day picker (heading button, month calendar popover, Today buttons).
    private static IElement OpenButton(IRenderedComponent<Main> cut) => cut.Find(".day-appointments h2.day-appointments-day button.day-appointments-open");

    private static async Task OpenPickAsync(IRenderedComponent<Main> cut)
    {
        await OpenButton(cut).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".day-appointments .day-pick")), Wait);
    }

    private static IElement PickFocusDay(IRenderedComponent<Main> cut) =>
        Assert.Single(cut.FindAll(".day-appointments .day-pick button.day[tabindex='0']"));

    private static string? PickFocusLabel(IRenderedComponent<Main> cut) => PickFocusDay(cut).GetAttribute("aria-label");

    private static string? PickMonth(IRenderedComponent<Main> cut) => cut.Find(".day-appointments .day-pick .day-pick-month").TextContent;

    private static IElement PickDay(IRenderedComponent<Main> cut, string label) =>
        cut.Find($".day-appointments .day-pick button.day[aria-label='{label}']");

    private static Task PickKeyAsync(IRenderedComponent<Main> cut, string key) =>
        PickFocusDay(cut).KeyDownAsync(new KeyboardEventArgs { Key = key });

    private static string HeadingTitle(IRenderedComponent<Main> cut) =>
        cut.Find(".day-appointments h2.day-appointments-day").GetAttribute("title") ?? string.Empty;

    private static string[] BarChildren(IRenderedComponent<Main> cut) =>
        cut.Find(".day-appointments .day-appointments-bar").Children.Select(c => c.ClassList[0]).ToArray();

    private static string? RefOf(IElement element)
    {

        string? id = element.GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    // The heading button's element reference (bUnit leaves its markup attribute empty once the panel re-rendered).
    private static string OpenButtonId(IRenderedComponent<Main> cut)
    {
        string id = cut.FindComponent<DayAppointmentsPanel>().Instance.OpenButtonReference.Id;
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    private string? LastFocusId()
    {
        JSRuntimeInvocation? last = data.Bunit.JSInterop.Invocations
            .LastOrDefault(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        return last is { } call ? Assert.IsType<ElementReference>(call.Arguments[0]).Id : null;
    }

    private static void WaitForHeading(IRenderedComponent<Main> cut, string text) =>
        cut.WaitForAssertion(() => Assert.Equal(text, Heading(cut)), Wait);

    // AC-1: off today the short month name, on today "Today"; the title is always the full day.
    [Fact]
    public async Task Heading_ShortMonth_TitleFullDay()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        Assert.Equal("Today", Heading(cut));
        Assert.Equal("Thursday, 29 October 2026", HeadingTitle(cut));

        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 1);
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
        Assert.Equal("Friday, 30 October 2026", HeadingTitle(cut));

        // Five months back from 30 October through the calendar: 30 May, then four on: 30 September.
        await OpenPickAsync(cut);
        for (int i = 0; i < 5; i++)
        {
            await PickKeyAsync(cut, "PageUp");
        }

        await PickKeyAsync(cut, "ArrowLeft");
        Assert.Equal("Friday, 29 May 2026", PickFocusLabel(cut));
        await PickKeyAsync(cut, "Enter");
        WaitForHeading(cut, "Friday, 29 May 2026");
        WaitForEmpty(cut);
        Assert.Equal("Friday, 29 May 2026", HeadingTitle(cut));

        await OpenPickAsync(cut);
        for (int i = 0; i < 4; i++)
        {
            await PickKeyAsync(cut, "PageDown");
        }

        await PickKeyAsync(cut, "Enter");
        WaitForHeading(cut, "Tuesday, 29 Sep 2026");
        WaitForEmpty(cut);
        Assert.Equal("Tuesday, 29 September 2026", HeadingTitle(cut));
    }

    // AC-2, AC-3: the heading is one named button that toggles the popover; the popover opens on the shown day, marks
    // it and today, and that day is the one tab stop and takes the focus.
    [Fact]
    public async Task HeadingButton_TogglesPopover_OnTheShownDay()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        IElement h2 = cut.Find(".day-appointments h2.day-appointments-day");
        IElement button = Assert.Single(h2.Children);
        Assert.Equal("BUTTON", button.TagName);
        Assert.Contains("day-appointments-open", button.ClassList);
        Assert.Equal(("button", "dialog", "false"), (button.GetAttribute("type"), button.GetAttribute("aria-haspopup"), button.GetAttribute("aria-expanded")));
        Assert.Equal("Today", button.TextContent);
        Assert.False(button.HasAttribute("blazor:onkeydown"));
        Assert.Empty(cut.FindAll(".day-pick"));

        await OpenPickAsync(cut);
        Assert.Equal("true", OpenButton(cut).GetAttribute("aria-expanded"));
        Assert.Single(cut.FindAll("section.day-appointments .day-pick-backdrop"));
        Assert.Equal("October 2026", PickMonth(cut));
        Assert.Equal("October 2026", cut.Find(".day-appointments .day-pick table[role=grid]").GetAttribute("aria-label"));
        IElement focus = PickFocusDay(cut);
        Assert.Equal("Thursday, 29 October 2026", focus.GetAttribute("aria-label"));
        Assert.Contains("today", focus.ClassList);
        Assert.Contains("selected", focus.ClassList);
        Assert.Equal("date", focus.GetAttribute("aria-current"));
        Assert.Equal("true", Assert.Single(cut.FindAll(".day-appointments .day-pick td[aria-selected]")).GetAttribute("aria-selected"));
        Assert.Equal(41, cut.FindAll(".day-appointments .day-pick button.day[tabindex='-1']").Count);
        Assert.Equal(RefOf(focus), LastFocusId());

        await OpenButton(cut).ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Equal("false", OpenButton(cut).GetAttribute("aria-expanded"));

        // Off today: the shown day is selected, today only marked; browsing another month is forgotten on reopening.
        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 1);
        await OpenPickAsync(cut);
        focus = PickFocusDay(cut);
        Assert.Equal("Friday, 30 October 2026", focus.GetAttribute("aria-label"));
        Assert.Contains("selected", focus.ClassList);
        Assert.DoesNotContain("today", focus.ClassList);
        IElement today = Assert.Single(cut.FindAll(".day-appointments .day-pick button.day.today"));
        Assert.Equal("Thursday, 29 October 2026", today.GetAttribute("aria-label"));
        Assert.DoesNotContain("selected", today.ClassList);
        Assert.Equal(RefOf(focus), LastFocusId());

        await cut.Find(".day-appointments .day-pick button.day-pick-next").ClickAsync(new MouseEventArgs());
        Assert.Equal("November 2026", PickMonth(cut));
        await OpenButton(cut).ClickAsync(new MouseEventArgs());
        await OpenPickAsync(cut);
        Assert.Equal("October 2026", PickMonth(cut));
        Assert.Equal("Friday, 30 October 2026", PickFocusLabel(cut));
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
    }

    // AC-4: arrows move by a day or a week, PageUp/PageDown by a month; the month arrows step the shown month.
    [Fact]
    public async Task Popover_Keys_MoveTheFocusDay()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);
        await OpenPickAsync(cut);

        await PickKeyAsync(cut, "ArrowRight");
        await PickKeyAsync(cut, "ArrowRight");
        Assert.Equal("Saturday, 31 October 2026", PickFocusLabel(cut));
        await PickKeyAsync(cut, "ArrowDown");
        Assert.Equal("November 2026", PickMonth(cut));
        Assert.Equal("Saturday, 7 November 2026", PickFocusLabel(cut));
        await PickKeyAsync(cut, "ArrowLeft");
        await PickKeyAsync(cut, "ArrowUp");
        Assert.Equal("Friday, 30 October 2026", PickFocusLabel(cut));
        await PickKeyAsync(cut, "PageDown");
        Assert.Equal("Monday, 30 November 2026", PickFocusLabel(cut));
        await PickKeyAsync(cut, "PageUp");
        Assert.Equal("October 2026", PickMonth(cut));
        await cut.Find(".day-appointments .day-pick button.day-pick-prev").ClickAsync(new MouseEventArgs());
        Assert.Equal("September 2026", PickMonth(cut));
        Assert.Equal("Today", Heading(cut));
    }

    // AC-4: at the bounds the month arrows are disabled and no key leaves 1900-01-01..2100-12-31.
    [Theory]
    [InlineData(1900, 1, 2, "day-prev", "Monday, 1 January 1900", "January 1900", "day-pick-prev")]
    [InlineData(2100, 12, 30, "day-next", "Friday, 31 December 2100", "December 2100", "day-pick-next")]
    public async Task Popover_AtTheBounds_StaysInRange(int year, int month, int day, string arrow, string edge, string monthLabel, string disabled)
    {
        data.Day = new DateOnly(year, month, day);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForEmpty(cut);
        await cut.Find($".day-appointments button.{arrow}").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotEqual("Today", Heading(cut)), Wait);
        WaitForEmpty(cut);

        await OpenPickAsync(cut);
        Assert.True(cut.Find($".day-appointments .day-pick button.{disabled}").HasAttribute("disabled"));
        Assert.Equal(edge, PickFocusLabel(cut));
        string[] keys = year == 1900 ? ["ArrowLeft", "ArrowUp", "PageUp"] : ["ArrowRight", "ArrowDown", "PageDown"];
        foreach (string key in keys)
        {
            await PickKeyAsync(cut, key);
            Assert.Equal(monthLabel, PickMonth(cut));
            Assert.Equal(edge, PickFocusLabel(cut));
        }
    }

    // AC-5: a click on a day shows it, closes the popover and loads that day once; picking today follows today again.
    [Fact]
    public async Task Popover_ClickDay_ShowsIt_LoadsOnce()
    {
        WriteDays();
        new SettingsStore(data.Root).SaveOutlook(true);
        DateOnly friday = new DateOnly(2026, 10, 30);
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);

        await OpenPickAsync(cut);
        await PickDay(cut, "Friday, 30 October 2026").ClickAsync(new MouseEventArgs());

        WaitForItems(cut, 1);
        Assert.Equal(["10:00-11:00 Retro"], Items(cut));
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
        Assert.Equal(1, data.Outlook.AppointmentArguments.Count(a => a.Day == friday));

        await OpenPickAsync(cut);
        await PickDay(cut, "Thursday, 29 October 2026").ClickAsync(new MouseEventArgs());
        WaitForHeading(cut, "Today");
        WaitForItems(cut, 2);
        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
        Assert.Empty(cut.FindAll(".day-appointments button.day-today"));
    }

    // AC-5: Enter picks the focus day once; the click the browser sends after it finds the popover closed.
    [Fact]
    public async Task Popover_Enter_PicksOnce_ClickAfterDoesNothing()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        await OpenPickAsync(cut);
        await PickKeyAsync(cut, "ArrowRight");
        IElement focus = PickFocusDay(cut);

        await focus.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        // The day buttons are gone with the closed popover, so the browser's follow-up click reaches no handler (one
        // already queued finds the popover closed: DayPicker and the panel both ignore a second pick).
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Empty(cut.FindAll(".day-appointments button.day"));

        WaitForItems(cut, 1);
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
        Assert.Equal(OpenButtonId(cut), LastFocusId());
    }

    // AC-6, AC-8: the popover's Today shows today and closes it, focus back on the heading; only the sidebar has it.
    [Fact]
    public async Task PopoverToday_ShowsToday_OnlyInTheSidebar()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 1);

        await OpenPickAsync(cut);
        IElement today = cut.Find(".day-appointments .day-pick button.day-pick-today");
        Assert.Equal(("button", "Today"), (today.GetAttribute("type"), today.TextContent));
        Assert.Empty(cut.FindAll(".top-bar .day-pick"));
        await today.ClickAsync(new MouseEventArgs());

        WaitForHeading(cut, "Today");
        WaitForItems(cut, 2);
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Equal(OpenButtonId(cut), LastFocusId());

        // AC-9: the top bar's calendar has no Today button and opens without the sidebar's.
        await cut.Find(".top-bar button.day-pick-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".top-bar .day-pick")), Wait);
        Assert.Empty(cut.FindAll(".day-pick-today"));
        Assert.Empty(cut.FindAll(".day-appointments .day-pick"));
        Assert.Equal("false", OpenButton(cut).GetAttribute("aria-expanded"));
    }

    // AC-7: off today the bar ends in Today (Back to today); a click shows today and the button goes.
    [Fact]
    public async Task BarToday_OffToday_BackToToday()
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        Assert.Equal(["day-prev", "day-appointments-day", "day-next"], BarChildren(cut));
        Assert.Empty(cut.FindAll(".day-appointments button.day-today"));

        await cut.Find(".day-appointments button.day-prev").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 2);
        Assert.Equal(["day-prev", "day-appointments-day", "day-next", "day-today"], BarChildren(cut));
        IElement back = cut.Find(".day-appointments button.day-today");
        Assert.Equal(("button", "Today", "Back to today"), (back.GetAttribute("type"), back.TextContent, back.GetAttribute("title")));

        await back.ClickAsync(new MouseEventArgs());
        WaitForHeading(cut, "Today");
        WaitForItems(cut, 2);
        Assert.Equal(["08:30-09:30 Standup", "11:00-12:00 Plan"], Items(cut));
        Assert.Equal(["day-prev", "day-appointments-day", "day-next"], BarChildren(cut));
    }

    // AC-8: Escape and the backdrop close the popover, change no day and give the focus back to the heading button.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Popover_EscapeOrBackdrop_Closes_FocusBack(bool escape)
    {
        WriteDays();
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        WaitForItems(cut, 2);
        await cut.Find(".day-appointments button.day-next").ClickAsync(new MouseEventArgs());
        WaitForItems(cut, 1);
        await OpenPickAsync(cut);
        await PickKeyAsync(cut, "ArrowRight");

        if (escape)
        {
            await PickKeyAsync(cut, "Escape");
        }
        else
        {
            await cut.Find(".day-appointments .day-pick-backdrop").ClickAsync(new MouseEventArgs());
        }

        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Equal("false", OpenButton(cut).GetAttribute("aria-expanded"));
        Assert.Equal("Friday, 30 Oct 2026", Heading(cut));
        Assert.Equal(["10:00-11:00 Retro"], Items(cut));
        Assert.Equal(OpenButtonId(cut), LastFocusId());
    }
}
