using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 200: the top bar's More actions menu, and 213: its month calendar, rendered on their own (no files, no waits: every
// handler here is synchronous; Today and Selected are fixed parameters, never the clock).
public sealed class TopBarTests : IDisposable
{
    private readonly BunitContext context = new();
    private readonly List<string> chosen = [];
    private readonly List<DateOnly> picked = [];
    private readonly List<string> searched = [];

    public TopBarTests()
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose() => context.Dispose();

    private static IReadOnlyList<PageAction> Actions() =>
    [
        new PageAction("rename", "Rename"),
        new PageAction("history", "History", false),
        new PageAction("delete", "Delete"),
    ];

    private IRenderedComponent<TopBar> Render(IReadOnlyList<PageAction> actions, bool disabled = false) =>
        context.Render<TopBar>(p => p
            .Add(c => c.PageActions, actions)
            .Add(c => c.PageActionsDisabled, disabled)
            .Add(c => c.OnPageAction, EventCallback.Factory.Create<string>(this, id => chosen.Add(id)))
            .Add(c => c.OnSearch, EventCallback.Factory.Create<string>(this, text => searched.Add(text))));

    private IRenderedComponent<TopBar> RenderCalendar(DateOnly today, DateOnly? selected) =>
        context.Render<TopBar>(p => p
            .Add(c => c.Today, today)
            .Add(c => c.Selected, selected)
            .Add(c => c.OnPick, EventCallback.Factory.Create<DateOnly>(this, day => picked.Add(day))));

    private static IRenderedComponent<TopBar> OpenCalendar(IRenderedComponent<TopBar> cut)
    {
        cut.Find("button.day-pick-open").Click();
        return cut;
    }

    private static string GridLabel(IRenderedComponent<TopBar> cut) =>
        cut.Find(".day-pick table[role=grid]").GetAttribute("aria-label") ?? string.Empty;

    private static IElement FocusDay(IRenderedComponent<TopBar> cut)
    {
        IReadOnlyList<IElement> stops = cut.FindAll(".day-pick button.day[tabindex='0']");
        return Assert.Single(stops);
    }

    private static IElement Day(IRenderedComponent<TopBar> cut, DateOnly day) =>
        cut.Find($".day-pick button.day[aria-label='{day.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}']");

    private static void DayKey(IRenderedComponent<TopBar> cut, string key) =>
        FocusDay(cut).KeyDown(new KeyboardEventArgs { Key = key });

    private static void MenuKey(IRenderedComponent<TopBar> cut, string key) =>
        cut.Find(".page-menu").KeyDown(new KeyboardEventArgs { Key = key });

    private static string? Ref(IRenderedComponent<TopBar> cut, string selector)
    {
        string? id = cut.Find(selector).GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    private string? LastFocusId()
    {
        IReadOnlyList<JSRuntimeInvocation> calls = context.JSInterop.Invocations
            .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .ToList();
        if (calls.Count == 0)
        {
            return null;
        }

        return Assert.IsType<ElementReference>(calls[^1].Arguments[0]).Id;
    }

    // AC-1: the … button is last, after Pick a day, closed; a click opens the menu.
    [Fact]
    public void Render_MenuButtonLast_ClickOpensMenu()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        IReadOnlyList<IElement> buttons = cut.FindAll(".top-bar-buttons > button");
        Assert.Equal(
            ["capture-open", "search-open", "today-open", "day-pick-open", "page-menu-open"],
            buttons.Select(b => b.ClassName ?? string.Empty).ToArray());
        IElement open = buttons[^1];
        Assert.Equal("button", open.GetAttribute("type"));
        Assert.Equal("More actions", open.GetAttribute("aria-label"));
        Assert.Equal("More actions", open.GetAttribute("title"));
        Assert.Equal("menu", open.GetAttribute("aria-haspopup"));
        Assert.Equal("false", open.GetAttribute("aria-expanded"));
        Assert.False(open.HasAttribute("disabled"));
        Assert.Empty(cut.FindAll(".page-menu"));

        cut.Find("button.page-menu-open").Click();

        Assert.Equal("true", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        IElement menu = cut.Find("div.page-menu[role=menu]");
        Assert.Equal("More actions", menu.GetAttribute("aria-label"));
        IReadOnlyList<IElement> items = cut.FindAll(".page-menu button");
        Assert.Equal(["Rename", "History", "Delete"], items.Select(b => b.TextContent).ToArray());
        Assert.Equal(["rename", "history", "delete"], items.Select(b => b.ClassName ?? string.Empty).ToArray());
        Assert.Equal(["menuitem", "menuitemcheckbox", "menuitem"], items.Select(b => b.GetAttribute("role") ?? string.Empty).ToArray());
        Assert.Equal("false", items[1].GetAttribute("aria-checked"));
        Assert.All(items, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(items, b => Assert.Equal("-1", b.GetAttribute("tabindex")));

        cut.Find("button.page-menu-open").Click();

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // AC-7: no actions, no … button.
    [Fact]
    public void Render_NoActions_NoMenuButton()
    {
        IRenderedComponent<TopBar> cut = Render([]);

        Assert.Empty(cut.FindAll("button.page-menu-open"));
        Assert.Equal(
            ["capture-open", "search-open", "today-open", "day-pick-open"],
            cut.FindAll(".top-bar-buttons > button").Select(b => b.ClassName ?? string.Empty).ToArray());
    }

    // AC-8: disabled while busy, and an open menu closes when the parent disables it.
    [Fact]
    public void Disabled_ButtonDisabled_OpenMenuHidden()
    {
        IRenderedComponent<TopBar> cut = Render(Actions(), disabled: true);
        Assert.True(cut.Find("button.page-menu-open").HasAttribute("disabled"));

        IRenderedComponent<TopBar> other = Render(Actions());
        other.Find("button.page-menu-open").Click();
        Assert.Single(other.FindAll(".page-menu"));
        other.Render(p => p.Add(c => c.PageActionsDisabled, true));

        Assert.Empty(other.FindAll(".page-menu"));
        Assert.True(other.Find("button.page-menu-open").HasAttribute("disabled"));
        Assert.Equal("false", other.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        Assert.Empty(chosen);
    }

    // A menu hidden by an empty list or by disabling stays closed when the actions come back.
    [Fact]
    public void HiddenMenu_StaysClosed_WhenActionsReturn()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Render(p => p.Add(c => c.PageActions, []));
        Assert.Empty(cut.FindAll("button.page-menu-open"));
        cut.Render(p => p.Add(c => c.PageActions, Actions()));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));

        cut.Find("button.page-menu-open").Click();
        cut.Render(p => p.Add(c => c.PageActionsDisabled, true));
        cut.Render(p => p.Add(c => c.PageActionsDisabled, false));
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(chosen);
    }

    // AC-9: opening focuses the first item; arrows move and wrap; Return runs the focused item and closes the menu.
    [Fact]
    public void Keys_ArrowsWrap_EnterRunsFocusedItem()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        // bUnit writes an element's reference id only into the render that created it, so read all three now.
        string? first = Ref(cut, ".page-menu button.rename");
        string? second = Ref(cut, ".page-menu button.history");
        string? last = Ref(cut, ".page-menu button.delete");
        Assert.Equal(first, LastFocusId());

        MenuKey(cut, "ArrowDown");
        Assert.Equal(second, LastFocusId());

        MenuKey(cut, "ArrowDown");
        MenuKey(cut, "ArrowDown");
        Assert.Equal(first, LastFocusId());

        MenuKey(cut, "ArrowUp");
        Assert.Equal(last, LastFocusId());
        Assert.Empty(chosen);

        MenuKey(cut, "Enter");

        Assert.Equal(["delete"], chosen);
        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // AC-9: Esc closes the menu, runs nothing and gives the focus back to the … button.
    [Fact]
    public void Keys_Escape_ClosesAndFocusesMenuButton()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());
        string? button = Ref(cut, "button.page-menu-open");

        cut.Find("button.page-menu-open").Click();
        MenuKey(cut, "ArrowDown");
        MenuKey(cut, "Escape");

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(chosen);
        Assert.Equal(button, LastFocusId());
    }

    // OnPageAction receives the clicked item's id once; the menu closes.
    [Fact]
    public void Click_Item_RaisesItsIdOnce()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Find(".page-menu button.history").Click();

        Assert.Equal(["history"], chosen);
        Assert.Empty(cut.FindAll(".page-menu"));
    }

    // AC-10: the backdrop closes the menu and raises nothing.
    [Fact]
    public void Backdrop_ClosesWithoutRunning()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.page-menu-open").Click();
        cut.Find(".page-menu-backdrop").Click();

        Assert.Empty(cut.FindAll(".page-menu"));
        Assert.Empty(cut.FindAll(".page-menu-backdrop"));
        Assert.Empty(chosen);
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
    }

    // 213 AC-1: the calendar opens with its backdrop; no input, a grid labelled with the month, the same visible header.
    [Fact]
    public void Calendar_Opens_GridLabelledWithMonth_NoInput()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18)));

        Assert.Single(cut.FindAll(".day-pick-backdrop"));
        Assert.Empty(cut.FindAll(".day-pick input"));
        Assert.Equal("November 2026", GridLabel(cut));
        Assert.Equal("November 2026", cut.Find(".day-pick .day-pick-month").TextContent);
        Assert.Equal("true", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
    }

    // 213 AC-2: prev and next are icon-only, named, and show the month before or after.
    [Fact]
    public void Calendar_PrevNext_IconOnly_ChangeMonth()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        foreach ((string css, string label) in new[] { ("day-pick-prev", "Previous month"), ("day-pick-next", "Next month") })
        {
            IElement button = cut.Find($".day-pick button.{css}");
            Assert.Equal("button", button.GetAttribute("type"));
            Assert.Equal(label, button.GetAttribute("aria-label"));
            Assert.Equal(label, button.GetAttribute("title"));
            Assert.Equal(string.Empty, button.TextContent.Trim());
            Assert.Equal("true", button.QuerySelector("svg")?.GetAttribute("aria-hidden"));
            Assert.False(button.HasAttribute("disabled"));
        }

        cut.Find(".day-pick button.day-pick-next").Click();
        Assert.Equal("December 2026", GridLabel(cut));
        cut.Find(".day-pick button.day-pick-next").Click();
        Assert.Equal("January 2027", GridLabel(cut));
        cut.Find(".day-pick button.day-pick-prev").Click();
        cut.Find(".day-pick button.day-pick-prev").Click();
        cut.Find(".day-pick button.day-pick-prev").Click();
        Assert.Equal("October 2026", cut.Find(".day-pick .day-pick-month").TextContent);
        Assert.Single(cut.FindAll(".day-pick button.day[tabindex='0']"));
        Assert.Empty(picked);
    }

    // 213 AC-2: Previous month is disabled on January 1900, Next month on December 2100.
    [Fact]
    public void Calendar_PrevNext_DisabledAtTheRangeEnds()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(1900, 1, 15)));
        Assert.Equal("January 1900", GridLabel(cut));
        Assert.True(cut.Find(".day-pick button.day-pick-prev").HasAttribute("disabled"));
        Assert.False(cut.Find(".day-pick button.day-pick-next").HasAttribute("disabled"));

        IRenderedComponent<TopBar> late = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2100, 12, 2)));
        Assert.Equal("December 2100", GridLabel(late));
        Assert.True(late.Find(".day-pick button.day-pick-next").HasAttribute("disabled"));
        Assert.False(late.Find(".day-pick button.day-pick-prev").HasAttribute("disabled"));
    }

    // 213 AC-3: an empty week-number header, then Mo to Su.
    [Fact]
    public void Calendar_ColumnHeaders_WeekThenMondayFirst()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        Assert.Equal(
            ["", "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"],
            cut.FindAll(".day-pick th[role=columnheader]").Select(th => th.TextContent).ToArray());
    }

    // 213 AC-4: 6 rows of 7 days from the Monday on or before the 1st; each row starts with its ISO week number.
    [Fact]
    public void Calendar_SixWeeks_FromMonday_WithIsoWeeks()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        IReadOnlyList<IElement> rows = cut.FindAll(".day-pick tbody tr");
        Assert.Equal(6, rows.Count);
        IReadOnlyList<IElement> days = cut.FindAll(".day-pick td[role=gridcell] button.day");
        Assert.Equal(42, days.Count);
        Assert.Equal("Monday, 26 October 2026", days[0].GetAttribute("aria-label"));
        Assert.Equal("Sunday, 6 December 2026", days[41].GetAttribute("aria-label"));
        string[] weeks = cut.FindAll(".day-pick th[role=rowheader]").Select(th => th.TextContent).ToArray();
        Assert.Equal(["44", "45", "46", "47", "48", "49"], weeks);
        foreach (IElement row in rows)
        {
            IElement first = Assert.IsAssignableFrom<IElement>(row.FirstElementChild);
            Assert.Equal("th", first.LocalName);
            Assert.Equal("rowheader", first.GetAttribute("role"));
            Assert.Contains("week", first.ClassList);
            Assert.Equal($"Week {first.TextContent}", first.GetAttribute("aria-label"));
        }

        cut.Find(".day-pick button.day-pick-next").Click();
        cut.Find(".day-pick button.day-pick-next").Click();
        Assert.Equal("January 2027", GridLabel(cut));
        Assert.Equal("Monday, 28 December 2026", cut.FindAll(".day-pick button.day")[0].GetAttribute("aria-label"));
        Assert.Equal("53", cut.FindAll(".day-pick th[role=rowheader]")[0].TextContent);
    }

    // 213 AC-5: every day is a button in a gridcell with its full date; other months' days are outside and clickable;
    // days past 2100-12-31 are disabled.
    [Fact]
    public void Calendar_Days_FullDateLabels_OutsideAndDisabled()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        Assert.Equal(42, cut.FindAll(".day-pick td[role=gridcell] > button.day").Count);
        IElement wednesday = Day(cut, new DateOnly(2026, 11, 18));
        Assert.Equal("Wednesday, 18 November 2026", wednesday.GetAttribute("aria-label"));
        Assert.Equal("18", wednesday.TextContent);
        Assert.DoesNotContain("outside", wednesday.ClassList);
        IElement october = Day(cut, new DateOnly(2026, 10, 26));
        Assert.Contains("outside", october.ClassList);
        Assert.Contains("outside", Day(cut, new DateOnly(2026, 12, 6)).ClassList);
        Assert.Empty(cut.FindAll(".day-pick button.day[disabled]"));

        october.Click();
        Assert.Equal([new DateOnly(2026, 10, 26)], picked);

        IRenderedComponent<TopBar> late = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2100, 12, 31)));
        Assert.False(Day(late, new DateOnly(2100, 12, 31)).HasAttribute("disabled"));
        Assert.True(Day(late, new DateOnly(2101, 1, 1)).HasAttribute("disabled"));
        // December 2100 runs from Mon 29 Nov 2100 to Sun 9 Jan 2101: the nine January days are disabled.
        Assert.Equal(9, late.FindAll(".day-pick button.day[disabled]").Count);
        Assert.Equal("Sunday, 9 January 2101", late.FindAll(".day-pick button.day")[41].GetAttribute("aria-label"));
    }

    // 213 AC-6: today and the selected day carry their marks; nothing else does.
    [Fact]
    public void Calendar_TodayAndSelected_Marked()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 20)));

        IElement today = Assert.Single(cut.FindAll(".day-pick button.day.today"));
        Assert.Equal("Wednesday, 18 November 2026", today.GetAttribute("aria-label"));
        Assert.Equal("date", today.GetAttribute("aria-current"));
        Assert.Single(cut.FindAll(".day-pick [aria-current]"));
        IElement selected = Assert.Single(cut.FindAll(".day-pick button.day.selected"));
        Assert.Equal("Friday, 20 November 2026", selected.GetAttribute("aria-label"));
        IElement cell = Assert.Single(cut.FindAll(".day-pick td[aria-selected]"));
        Assert.Equal("true", cell.GetAttribute("aria-selected"));
        Assert.Same(selected, cell.QuerySelector("button.day"));
    }

    // 213 AC-6: Today == Selected puts both marks on one button; no Selected selects nothing.
    [Fact]
    public void Calendar_TodayIsSelected_BothMarks_NullSelectsNothing()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18)));
        IElement both = Assert.Single(cut.FindAll(".day-pick button.day.today.selected"));
        Assert.Equal("date", both.GetAttribute("aria-current"));
        Assert.Equal("true", both.ParentElement?.GetAttribute("aria-selected"));
        Assert.Single(cut.FindAll(".day-pick button.day.today"));
        Assert.Single(cut.FindAll(".day-pick button.day.selected"));
        Assert.Single(cut.FindAll(".day-pick td[aria-selected]"));

        IRenderedComponent<TopBar> none = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));
        Assert.Empty(none.FindAll(".day-pick button.day.selected"));
        Assert.Empty(none.FindAll(".day-pick td[aria-selected]"));
        Assert.Single(none.FindAll(".day-pick button.day.today"));
    }

    // 213 AC-7: the calendar opens on Selected's month with the focus day on it, and that button takes the focus.
    [Fact]
    public void Calendar_OpensOnSelected_FocusDayFocused()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2027, 3, 9)));

        Assert.Equal("March 2027", GridLabel(cut));
        IElement focus = FocusDay(cut);
        Assert.Equal("Tuesday, 9 March 2027", focus.GetAttribute("aria-label"));
        Assert.Equal(41, cut.FindAll(".day-pick button.day[tabindex='-1']").Count);
        Assert.Equal(Ref(cut, ".day-pick button.day[tabindex='0']"), LastFocusId());
    }

    // 213 AC-7: without Selected it opens on today; every opening starts again from that month.
    [Fact]
    public void Calendar_OpensOnToday_ForgetsBrowsedMonth()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));
        Assert.Equal("Wednesday, 18 November 2026", FocusDay(cut).GetAttribute("aria-label"));
        Assert.Equal(Ref(cut, ".day-pick button.day[tabindex='0']"), LastFocusId());

        cut.Find(".day-pick button.day-pick-next").Click();
        DayKey(cut, "PageDown");
        Assert.Equal("January 2027", GridLabel(cut));
        cut.Find("button.day-pick-open").Click();
        Assert.Empty(cut.FindAll(".day-pick"));
        cut.Find("button.day-pick-open").Click();

        Assert.Equal("November 2026", GridLabel(cut));
        Assert.Equal("Wednesday, 18 November 2026", FocusDay(cut).GetAttribute("aria-label"));
        Assert.Empty(picked);
    }

    // 213 AC-8: clicking a day raises OnPick once with that date and closes the calendar.
    [Fact]
    public void Calendar_ClickDay_PicksOnceAndCloses()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        Day(cut, new DateOnly(2026, 11, 20)).Click();

        Assert.Equal([new DateOnly(2026, 11, 20)], picked);
        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
        Assert.Equal("false", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
    }

    // 213 AC-9: arrows move by a day or a week, and crossing the month edge shows the next month.
    [Fact]
    public void Calendar_Arrows_MoveFocusDay_AcrossMonthEdge()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 30)));

        DayKey(cut, "ArrowRight");
        Assert.Equal("December 2026", GridLabel(cut));
        Assert.Equal("Tuesday, 1 December 2026", FocusDay(cut).GetAttribute("aria-label"));
        DayKey(cut, "ArrowLeft");
        Assert.Equal("November 2026", GridLabel(cut));
        Assert.Equal("Monday, 30 November 2026", FocusDay(cut).GetAttribute("aria-label"));
        DayKey(cut, "ArrowUp");
        Assert.Equal("Monday, 23 November 2026", FocusDay(cut).GetAttribute("aria-label"));
        DayKey(cut, "ArrowDown");
        DayKey(cut, "ArrowDown");
        Assert.Equal("December 2026", GridLabel(cut));
        Assert.Equal("Monday, 7 December 2026", FocusDay(cut).GetAttribute("aria-label"));
        Assert.Empty(picked);
    }

    // 213 AC-9: PageDown on 31 Jan 2028 gives 29 Feb 2028 (leap year); PageUp goes back to 29 Jan.
    [Fact]
    public void Calendar_PageKeys_MoveByMonth_ClampDay()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2028, 1, 31)));

        DayKey(cut, "PageDown");
        Assert.Equal("February 2028", GridLabel(cut));
        Assert.Equal("Tuesday, 29 February 2028", FocusDay(cut).GetAttribute("aria-label"));
        DayKey(cut, "PageUp");
        Assert.Equal("January 2028", GridLabel(cut));
        Assert.Equal("Saturday, 29 January 2028", FocusDay(cut).GetAttribute("aria-label"));
    }

    // 213 AC-9: no move leaves 1900-01-01..2100-12-31.
    [Theory]
    [InlineData(1900, 1, 1, "ArrowLeft")]
    [InlineData(1900, 1, 1, "ArrowUp")]
    [InlineData(1900, 1, 1, "PageUp")]
    [InlineData(2100, 12, 31, "ArrowRight")]
    [InlineData(2100, 12, 31, "ArrowDown")]
    [InlineData(2100, 12, 31, "PageDown")]
    public void Calendar_Moves_ClampedToRange(int year, int month, int day, string key)
    {
        DateOnly edge = new DateOnly(year, month, day);
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), edge));

        DayKey(cut, key);

        Assert.Equal(edge.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), GridLabel(cut));
        Assert.Equal(edge.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), FocusDay(cut).GetAttribute("aria-label"));
    }

    // 213 AC-9: Enter picks the focus day once and closes; the click the browser sends after it calls nothing more.
    [Fact]
    public void Calendar_Enter_PicksOnce_ClickAfterDoesNothing()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18)));
        DayKey(cut, "ArrowRight");
        IElement focus = FocusDay(cut);

        focus.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Empty(cut.FindAll(".day-pick"));
        // The day buttons are gone with the closed calendar, so the follow-up click reaches no handler (in the browser
        // a click already queued before that render finds the calendar closed and picks nothing).
        Assert.IsType<Bunit.Rendering.UnknownEventHandlerIdException>(Record.Exception(() => focus.Click()));

        Assert.Equal([new DateOnly(2026, 11, 19)], picked);
        Assert.Empty(cut.FindAll(".day-pick-backdrop"));
    }

    // 213 AC-9: Esc on a day closes the calendar and picks nothing.
    [Fact]
    public void Calendar_Escape_Closes()
    {
        IRenderedComponent<TopBar> cut = OpenCalendar(RenderCalendar(new DateOnly(2026, 11, 18), null));

        cut.Find(".top-bar").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".day-pick"));
        Assert.Empty(picked);
    }

    // 213 AC-11: every colour in the calendar rules is a theme variable, and today stays visible on the selected day.
    [Fact]
    public void Css_CalendarRules_UseThemeVariablesOnly()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        List<string> rules = [];
        int at = 0;
        while (at < css.Length)
        {
            int open = css.IndexOf('{', at);
            if (open < 0)
            {
                break;
            }

            int close = css.IndexOf('}', open);
            string selector = css[at..open];
            int comment = selector.LastIndexOf("*/", StringComparison.Ordinal);
            selector = (comment >= 0 ? selector[(comment + 2)..] : selector).Trim();
            if (selector.Split(',').Any(part => part.Trim().StartsWith(".day-pick", StringComparison.Ordinal)))
            {
                rules.Add(selector + " {" + css[(open + 1)..close] + "}");
            }

            at = close + 1;
        }

        Assert.Contains(rules, rule => rule.StartsWith(".day-pick .day.today.selected", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule.Contains(".day-pick .day.selected", StringComparison.Ordinal));
        foreach (string rule in rules)
        {
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", rule);
            Assert.DoesNotMatch(@"\b(rgb|rgba|hsl|hsla)\(", rule);
            foreach (string declaration in rule[(rule.IndexOf('{') + 1)..^1].Split(';'))
            {
                string[] pair = declaration.Split(':', 2);
                if (pair.Length == 2 && pair[0].Trim() is "color" or "background" or "background-color" or "border-color")
                {
                    string value = pair[1].Trim();
                    Assert.True(value == "transparent" || value.StartsWith("var(--", StringComparison.Ordinal), rule);
                }
            }
        }
    }

    // AC-10 (and 213 AC-10): at most one popover: each of the menu, Capture and the calendar closes the other two.
    [Fact]
    public void Popovers_MutuallyExclusive()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        cut.Find("button.page-menu-open").Click();
        Assert.Equal(["menu"], Open(cut));
        cut.Find("button.day-pick-open").Click();
        Assert.Equal(["day"], Open(cut));
        cut.Find("button.page-menu-open").Click();
        Assert.Equal(["menu"], Open(cut));
        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        // 213 AC-10: the calendar and Capture close each other too.
        cut.Find("button.day-pick-open").Click();
        Assert.Equal(["day"], Open(cut));
        Assert.Equal("false", cut.Find("button.capture-open").GetAttribute("aria-expanded"));
        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        Assert.Equal("false", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
        // 218 AC-7: Search closes the other three, and each of them closes Search.
        cut.Find("button.search-open").Click();
        Assert.Equal(["search"], Open(cut));
        Assert.Equal("false", cut.Find("button.capture-open").GetAttribute("aria-expanded"));
        cut.Find("button.capture-open").Click();
        Assert.Equal(["capture"], Open(cut));
        Assert.Equal("false", cut.Find("button.search-open").GetAttribute("aria-expanded"));
        cut.Find("button.search-open").Click();
        cut.Find("button.day-pick-open").Click();
        Assert.Equal(["day"], Open(cut));
        cut.Find("button.search-open").Click();
        Assert.Equal(["search"], Open(cut));
        Assert.Equal("false", cut.Find("button.day-pick-open").GetAttribute("aria-expanded"));
        cut.Find("button.page-menu-open").Click();
        Assert.Equal(["menu"], Open(cut));
        cut.Find("button.search-open").Click();
        Assert.Equal(["search"], Open(cut));
        Assert.Equal("false", cut.Find("button.page-menu-open").GetAttribute("aria-expanded"));
        Assert.Empty(chosen);
        Assert.Empty(searched);
    }

    private static IElement SearchField(IRenderedComponent<TopBar> cut) => Assert.Single(cut.FindAll(".search-pop input"));

    private static void SearchKey(IRenderedComponent<TopBar> cut, string key) =>
        SearchField(cut).KeyDown(new KeyboardEventArgs { Key = key });

    // 218 AC-1: Search sits after Capture: typed, named, closed, icon only, one safe decorative svg.
    [Fact]
    public void Search_Button_IconOnly_WithSafeSvg()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        IElement button = cut.Find(".top-bar-buttons > button.search-open");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Search", button.GetAttribute("aria-label"));
        Assert.Equal("Search", button.GetAttribute("title"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal(string.Empty, button.TextContent.Trim());
        IElement svg = Assert.Single(button.QuerySelectorAll("svg"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        string markup = svg.OuterHtml;
        foreach (string banned in new[] { "<script", "href", "src=", "url(", "style", "#" })
        {
            Assert.DoesNotContain(banned, markup, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("currentColor", markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".search-pop"));
        Assert.Empty(cut.FindAll(".search-pop-backdrop"));
    }

    // 218 AC-3: a click opens the popover with one empty, named text field that takes the focus; a second click closes it.
    [Fact]
    public void Search_Click_OpensPopover_FocusesField_SecondClickCloses()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.search-open").Click();

        Assert.Single(cut.FindAll(".search-pop-backdrop"));
        Assert.Single(cut.FindAll(".search-pop"));
        IElement input = SearchField(cut);
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("Search", input.GetAttribute("aria-label"));
        Assert.Equal("Search notes", input.GetAttribute("placeholder"));
        Assert.Equal("200", input.GetAttribute("maxlength"));
        Assert.Equal(string.Empty, input.GetAttribute("value") ?? string.Empty);
        Assert.Equal("true", cut.Find("button.search-open").GetAttribute("aria-expanded"));
        Assert.Equal(Ref(cut, ".search-pop input"), LastFocusId());

        cut.Find("button.search-open").Click();

        Assert.Empty(cut.FindAll(".search-pop"));
        Assert.Empty(cut.FindAll(".search-pop-backdrop"));
        Assert.Equal("false", cut.Find("button.search-open").GetAttribute("aria-expanded"));
        Assert.Empty(searched);
    }

    // 218 AC-4: Return hands the typed text over once and closes; other keys do nothing; reopening shows an empty field.
    [Fact]
    public void Search_Enter_RaisesTextOnce_Closes_ReopensEmpty()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.search-open").Click();
        SearchField(cut).Input("budget");
        SearchKey(cut, "a");
        SearchKey(cut, "ArrowDown");
        Assert.Empty(searched);

        SearchKey(cut, "Enter");

        Assert.Equal(["budget"], searched);
        Assert.Empty(cut.FindAll(".search-pop"));
        Assert.Equal("false", cut.Find("button.search-open").GetAttribute("aria-expanded"));

        cut.Find("button.search-open").Click();

        Assert.Equal(string.Empty, SearchField(cut).GetAttribute("value") ?? string.Empty);
        Assert.Equal(["budget"], searched);
    }

    // 218 AC-4 (moved from NotesPanelTests 207 AC-3): Return on an empty field asks for the panel with an empty text.
    [Fact]
    public void Search_EnterEmpty_RaisesEmptyText()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.search-open").Click();
        SearchKey(cut, "Enter");

        Assert.Equal([string.Empty], searched);
        Assert.Empty(cut.FindAll(".search-pop"));
    }

    // 218 AC-4: typed text that is closed away (a second click) is not kept for the next opening.
    [Fact]
    public void Search_ClosedWithText_ReopensEmpty()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.search-open").Click();
        SearchField(cut).Input("left over");
        cut.Find("button.search-open").Click();
        cut.Find("button.search-open").Click();

        Assert.Equal(string.Empty, SearchField(cut).GetAttribute("value") ?? string.Empty);
        Assert.Empty(searched);
    }

    // 218 AC-6: Esc closes the popover, raises nothing and gives the focus back to the Search icon.
    [Fact]
    public void Search_Escape_ClosesAndFocusesSearchButton()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());
        string? button = Ref(cut, "button.search-open");

        cut.Find("button.search-open").Click();
        SearchField(cut).Input("budget");
        SearchKey(cut, "Escape");

        Assert.Empty(cut.FindAll(".search-pop"));
        Assert.Empty(searched);
        Assert.Equal("false", cut.Find("button.search-open").GetAttribute("aria-expanded"));
        Assert.Equal(button, LastFocusId());
    }

    // 218 AC-6: the backdrop closes the popover and raises nothing.
    [Fact]
    public void Search_Backdrop_Closes()
    {
        IRenderedComponent<TopBar> cut = Render(Actions());

        cut.Find("button.search-open").Click();
        cut.Find(".search-pop-backdrop").Click();

        Assert.Empty(cut.FindAll(".search-pop"));
        Assert.Empty(cut.FindAll(".search-pop-backdrop"));
        Assert.Empty(searched);
        Assert.Equal("false", cut.Find("button.search-open").GetAttribute("aria-expanded"));
    }

    private static string[] Open(IRenderedComponent<TopBar> cut)
    {
        List<string> open = [];
        if (cut.FindAll(".capture-pop").Count > 0)
        {
            open.Add("capture");
        }

        if (cut.FindAll(".search-pop").Count > 0)
        {
            open.Add("search");
        }

        if (cut.FindAll(".day-pick").Count > 0)
        {
            open.Add("day");
        }

        if (cut.FindAll(".page-menu").Count > 0)
        {
            open.Add("menu");
        }

        return open.ToArray();
    }
}
