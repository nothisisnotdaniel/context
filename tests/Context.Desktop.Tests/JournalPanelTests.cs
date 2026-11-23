using System.Collections.Concurrent;
using System.Globalization;
using Bunit;
using Jint;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

public sealed class JournalPanelTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private readonly DesktopFixture data = new();
    private readonly Engine browser = new();
    private readonly ITestOutputHelper output;
    private string? markerId;
    private readonly SemaphoreSlim watching = new(0);

    public JournalPanelTests(ITestOutputHelper output)
    {
        this.output = output;
        _ = new EditorJs(data.Bunit);
        browser.Execute("""
            const window = globalThis;
            const region = {scrollTop: 0, clientHeight: 120, scrollTo(x,y) { this.scrollTop = y; }};
            const marker = {closest: () => region};
            class IntersectionObserver {
                constructor(cb) { this.callback = cb; }
                observe() { globalThis.observer = this; }
                disconnect() {}
            }
            const bridge = {signal: null, invokeMethodAsync(method) { this.signal = method; }};
            """);
        browser.Execute(File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "journal.js")));
        data.Bunit.JSInterop.SetupVoid("ContextJournal.watch", invocation =>
        {
            SetRegion(((ElementReference)invocation.Arguments[0]!).Id);
            browser.Execute("ContextJournal.watch(marker, bridge, 1)");
            watching.Release();
            return true;
        }).SetVoidResult();
        data.Bunit.JSInterop.SetupVoid("ContextJournal.stop", _ => true).SetVoidResult();
    }

    public void Dispose() => data.Dispose();

    private void SetRegion(string? reference)
    {
        // A new scrolling region also replaces its descendant marker's Blazor reference.
        if (markerId != reference)
        {
            browser.Execute("region.scrollTop = 0");
            markerId = reference;
        }
    }

    private static string Date(int ago) => DesktopFixture.Today.AddDays(-ago).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task Open(IRenderedComponent<Main> cut)
    {
        await cut.Find(".layout").KeyDownAsync(new KeyboardEventArgs { Key = "K", CtrlKey = true });
        await cut.Find(".palette-input").InputAsync(new ChangeEventArgs { Value = "/journal" });
        await cut.Find(".palette-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Equal("Journal", cut.Find(".journal-panel h2").TextContent), Wait);
    }

    private static Task Bottom(IRenderedComponent<Main> cut) =>
        cut.InvokeAsync(cut.FindComponent<JournalPanel>().Instance.Bridge.OnJournalBottom);

    private int Measure(IRenderedComponent<Main> cut, string heading)
    {
        SetRegion(cut.Find(".current-region .journal-bottom").GetAttribute("blazor:elementReference"));
        List<string> dates = cut.FindAll(".current-region .journal-panel h3").Select(h => h.TextContent).ToList();
        int index = dates.IndexOf(heading);
        Assert.True(index >= 0);
        return index * 40 - (int)browser.Evaluate("region.scrollTop").AsNumber();
    }

    [Fact]
    public async Task AppendOnce_LeavesVisibleHeadingAtSameOffset()
    {
        for (int i = 0; i < 15; i++)
        {
            data.WriteNote(Date(i), "Text");
        }
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        ConcurrentQueue<DateOnly> reads = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        data.State.BeforeJournalDay = day =>
        {
            reads.Enqueue(day);
            if (day == DesktopFixture.Today.AddDays(-14))
            {
                started.TrySetResult();
                release.Task.WaitAsync(Wait).GetAwaiter().GetResult();
            }
        };
        await Open(cut);
        cut.WaitForAssertion(() => Assert.Equal(14, cut.FindAll(".journal-panel h3").Count), Wait);
        Assert.True(await watching.WaitAsync(Wait));
        await cut.InvokeAsync(() => { });
        SetRegion(cut.Find(".journal-bottom").GetAttribute("blazor:elementReference"));
        browser.Execute("region.scrollTop = 14 * 40 - region.clientHeight");
        string anchor = Date(12);
        int before = Measure(cut, anchor);
        Assert.InRange(before, 0, 119);
        browser.Execute("observer.callback([{isIntersecting:true}])");
        Assert.Equal("OnJournalBottom", browser.Evaluate("bridge.signal").AsString());
        Task append = Bottom(cut);
        try
        {
            await started.Task.WaitAsync(Wait);
            await Bottom(cut).WaitAsync(Wait);
        }
        finally
        {
            release.TrySetResult();
        }
        await append.WaitAsync(Wait);
        cut.WaitForAssertion(() => Assert.Equal(Enumerable.Range(0, 15).Select(Date),
            cut.FindAll(".journal-panel h3").Select(h => h.TextContent)), Wait);
        int after = Measure(cut, anchor);
        Assert.InRange(Math.Abs(after - before), 0, 1);
        browser.Execute("region.scrollTop += 2");
        int control = Measure(cut, anchor);
        Assert.Equal(after - 2, control);
        output.WriteLine($"Heading {anchor}: before {before}px, after {after}px, control {control}px.");
        await Bottom(cut);
        Assert.Equal(Enumerable.Range(0, 15).Select(i => DesktopFixture.Today.AddDays(-i)), reads.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutlookAlone_StopsAtItsOldestOrConfirmedEmpty(bool populated)
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        DateOnly oldest = DesktopFixture.Today.AddDays(-42);
        if (populated)
        {
            DateTime start = oldest.ToDateTime(new TimeOnly(9, 0));
            data.Outlook.Days[oldest] = [new Appointment("Outlook only", null, start, start.AddHours(1), false)];
        }
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await Open(cut);
        if (populated)
        {
            for (int page = 1; page <= 3; page++)
            {
                Assert.True(await watching.WaitAsync(Wait));
                Assert.DoesNotContain("No journal entries.", cut.Markup, StringComparison.Ordinal);
                await Bottom(cut);
            }
            cut.WaitForAssertion(() => Assert.Equal(Date(42), cut.Find(".journal-panel h3").TextContent), Wait);
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Contains("No journal entries.", cut.Find(".journal-panel").TextContent), Wait);
        }
        int calls = data.Outlook.AppointmentCalls;
        await Bottom(cut);
        Assert.Equal(calls, data.Outlook.AppointmentCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task CalendarHistory_UsesOnlyReadableOccurrencesAndLeavesSourcesUntouched(bool local, bool cache, bool outlook)
    {
        string calendar = DesktopFixture.Calendar(
            DesktopFixture.Event("Series", "20261026T080000", "20261026T090000").Replace("END:VEVENT", "RRULE:FREQ=DAILY;COUNT=3\r\nEND:VEVENT"),
            DesktopFixture.Event("Overnight", "20261027T230000", "20261028T000000"),
            DesktopFixture.Event("Private", "20261025T090000", "20261025T100000").Replace("END:VEVENT", "CLASS:PRIVATE\r\nEND:VEVENT"));
        if (local)
        {
            data.WriteCalendar("local.ics", calendar);
        }
        if (cache)
        {
            data.WriteCache(calendar.Replace("Series", "Cached series"), data.UtcNow);
        }
        if (outlook)
        {
            new SettingsStore(data.Root).SaveOutlook(true);
            DateTime start = DesktopFixture.Today.ToDateTime(new TimeOnly(7, 0));
            data.Outlook.Days[DesktopFixture.Today] = [new Appointment("Past today", null, start, start.AddMinutes(30), false)];
        }
        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        int requests = data.Http.Requests;
        (Journal journal, _) = await state.OpenJournal().WaitAsync(Wait);
        IReadOnlyList<JournalDay> entries = journal.ReadBatch();
        Assert.True(journal.Exhausted);
        Assert.Equal(outlook ? new[] { 29, 28, 27, 26 } : [28, 27, 26], entries.Select(e => e.Date.Day));
        foreach (JournalDay day in entries.Where(e => e.Date.Day != 29))
        {
            List<string> titles = [];
            if (cache)
            {
                titles.Add("Cached series");
            }
            if (local)
            {
                titles.Add("Series");
            }
            if (day.Date.Day == 27)
            {
                titles.AddRange(Enumerable.Repeat("Overnight", (local ? 1 : 0) + (cache ? 1 : 0)));
            }
            Assert.Equal(titles, day.Appointments.Select(a => a.Title));
        }
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(requests, data.Http.Requests);
    }

    [Fact]
    public async Task TimedOutHistory_LeavesLocalDayReadable()
    {
        data.OutlookTimeout = TimeSpan.FromMilliseconds(50);
        new SettingsStore(data.Root).SaveOutlook(true);
        data.WriteNote(Date(0), "Read locally");
        data.Outlook.Block();
        try
        {
            (Journal journal, OutlookStatus status) = await data.CreateState().OpenJournal().WaitAsync(Wait);
            Assert.Equal(OutlookStatus.TimedOut, status);
            Assert.Equal("Read locally", Assert.Single(journal.ReadBatch()).Body);
        }
        finally
        {
            data.Outlook.Release();
        }
    }

    [Fact]
    public async Task DeniedHistory_StillOpensLinksAndRestoresWorkspace()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        data.Outlook.AppointmentKind = OutlookReadKind.Denied;
        data.WriteNote(Date(0), "Daily text");
        data.WriteNote(Date(0) + " Standup", "#meeting");
        data.WriteCalendar("one.ics", DesktopFixture.Calendar(DesktopFixture.Event("Review", "20261029T090000", "20261029T093000")));
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await Open(cut);
        cut.WaitForAssertion(() => Assert.Contains("access denied", cut.Find(".journal-panel").TextContent), Wait);
        await StaleClick.ClickAsync(cut, ".journal-panel .close");
        cut.WaitForAssertion(() => Assert.False(cut.Find(".note-area").HasAttribute("hidden")), Wait);
        foreach ((string selector, string name) in new[] { (".journal-open", Date(0)), (".journal-meeting", Date(0) + " Standup") })
        {
            await Open(cut);
            cut.WaitForElement(selector, Wait);
            await StaleClick.ClickAsync(cut, selector);
            cut.WaitForAssertion(() => Assert.Equal(name, cut.Find(".note-title").GetAttribute("title")), Wait);
            cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Find(".links-panel").TextContent), Wait);
        }
        await Open(cut);
        cut.WaitForElement(".journal-appointment", Wait);
        await StaleClick.ClickAsync(cut, ".journal-panel .close");
        cut.WaitForAssertion(() => Assert.Equal(Date(0) + " Standup", cut.Find(".note-area:not([hidden]) .note-title").GetAttribute("title")), Wait);
        await Open(cut);
        cut.WaitForElement(".journal-appointment", Wait);
        await StaleClick.ClickAsync(cut, ".journal-appointment");
        cut.WaitForAssertion(() => Assert.Contains("Review", cut.Find(".appointment-page h2").TextContent), Wait);
    }
}
