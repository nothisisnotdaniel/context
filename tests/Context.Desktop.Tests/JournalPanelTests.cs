using System.Collections.Concurrent;
using System.Globalization;
using AngleSharp.Dom;
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
        int top = 0;
        foreach (IElement row in cut.FindAll(".current-region .journal-panel > :is(article, [role=status])"))
        {
            if (row.QuerySelector("h3") is { } date && date.TextContent == heading)
            {
                return top - (int)browser.Evaluate("region.scrollTop").AsNumber();
            }
            top += row.LocalName == "article" ? 40 : 20;
        }
        throw new InvalidOperationException("Missing journal heading.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendOnce_LeavesVisibleHeadingAtSameOffset(bool outlookFailsOnAppend)
    {
        new SettingsStore(data.Root).SaveOutlook(outlookFailsOnAppend);
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
        if (outlookFailsOnAppend)
        {
            data.Outlook.AppointmentKind = OutlookReadKind.Denied;
        }
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
        if (outlookFailsOnAppend)
        {
            Assert.Contains("access denied", cut.Find(".journal-panel [role=status]").TextContent, StringComparison.Ordinal);
        }
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
        ConcurrentQueue<DateOnly> reads = new();
        data.State.BeforeJournalDay = reads.Enqueue;
        await Open(cut);
        if (populated)
        {
            for (int page = 1; page <= 3; page++)
            {
                Assert.True(await watching.WaitAsync(Wait));
                Assert.Contains("Loading…", cut.Find(".journal-panel").TextContent, StringComparison.Ordinal);
                Assert.DoesNotContain("No journal entries.", cut.Markup, StringComparison.Ordinal);
                await Bottom(cut);
            }
            cut.WaitForAssertion(() => Assert.Equal(Date(42), cut.Find(".journal-panel h3").TextContent), Wait);
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Contains("No journal entries.", cut.Find(".journal-panel").TextContent), Wait);
        }
        DateOnly[] expected = Enumerable.Range(0, populated ? 43 : 0).Select(i => DesktopFixture.Today.AddDays(-i)).ToArray();
        Assert.Equal(expected, reads.ToArray());
        await Bottom(cut);
        Assert.Equal(expected, reads.ToArray());
    }

    [Fact]
    public async Task FinalBatch_StaysLoadingThroughRenderUntilTheEntriesArrive()
    {
        data.WriteNote(Date(14), "Last day");
        data.CreateState();
        IRenderedComponent<JournalPanel> cut = data.Bunit.Render<JournalPanel>();
        Assert.True(await watching.WaitAsync(Wait));
        TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Journal, Task<IReadOnlyList<JournalDay>>> read = cut.Instance.ReadBatchAsync;
        cut.Instance.ReadBatchAsync = async journal =>
        {
            IReadOnlyList<JournalDay> batch = await read(journal);
            finished.TrySetResult(journal.Exhausted);
            await release.Task.WaitAsync(Wait);
            return batch;
        };

        Task append = cut.InvokeAsync(cut.Instance.Bridge.OnJournalBottom);
        try
        {
            Assert.True(await finished.Task.WaitAsync(Wait));
            cut.Render();
            Assert.Empty(cut.FindAll("article"));
            Assert.Equal("Loading…", cut.Find("[role=status]").TextContent);
        }
        finally
        {
            release.TrySetResult();
        }
        await append.WaitAsync(Wait);
        cut.WaitForAssertion(() => Assert.Equal(Date(14), cut.Find("h3").TextContent), Wait);
        Assert.Equal("Last day", cut.Find(".journal-body").TextContent);
        Assert.Equal("Open " + Date(14), cut.Find(".journal-open").GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll("[role=status]"));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task CalendarHistory_UsesOnlyReadableOccurrencesAndLeavesSourcesUntouched(bool local, bool cache, bool outlook)
    {
        data.SaveLink(FakeHttpHandler.Link);
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
            data.WriteCache(calendar.Replace("Series", "Cached series"), data.UtcNow - CalendarFeed.CacheLifetime - TimeSpan.FromMinutes(1));
        }
        if (outlook)
        {
            data.WriteNote(Date(0), "Journal text");
            new SettingsStore(data.Root).SaveOutlook(true);
            DateTime start = DesktopFixture.Today.ToDateTime(new TimeOnly(7, 0));
            data.Outlook.Days[DesktopFixture.Today] = [new Appointment("Past today", null, start, start.AddMinutes(30), false)];
        }
        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        (Journal journal, _) = await state.OpenJournal().WaitAsync(Wait);
        IReadOnlyList<JournalDay> entries = journal.ReadBatch();
        Assert.True(journal.Exhausted);
        Assert.Equal(outlook ? new[] { 29, 28, 27, 26 } : [28, 27, 26], entries.Select(e => e.Date.Day));
        if (outlook)
        {
            Assert.Equal("Journal text", entries[0].Body);
            Appointment past = Assert.Single(entries[0].Appointments);
            Assert.Equal("Past today", past.Title);
            Assert.Equal(DesktopFixture.Today.ToDateTime(new TimeOnly(7, 0)), past.Start);
            Assert.Equal(DesktopFixture.Today.ToDateTime(new TimeOnly(7, 30)), past.End);
        }
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
        Assert.Equal(0, data.Http.Requests);
    }

    [Theory]
    [InlineData("EXDATE", false)]
    [InlineData("CLASS:PRIVATE", false)]
    [InlineData("STATUS:CANCELLED", false)]
    [InlineData("CLASS:PRIVATE", true)]
    public async Task ExcludedHistory_StartsOnlyAtVisibleOccurrence(string exclusion, bool repeats)
    {
        string master = DesktopFixture.Event("Old", "20261026T080000", "20261026T090000")
            .Replace("END:VEVENT", "UID:series\r\n"
                + (exclusion == "EXDATE" ? "RDATE:20261025T080000\r\nEXDATE:20261025T080000,20261026T080000\r\n" : string.Empty)
                + (repeats ? "RRULE:FREQ=DAILY;COUNT=2\r\n" : string.Empty) + "END:VEVENT");
        string replacement = exclusion == "EXDATE" ? string.Empty
            : DesktopFixture.Event("Hidden", "20261026T080000", "20261026T090000")
                .Replace("END:VEVENT", "UID:series\r\nRECURRENCE-ID:20261026T080000\r\n" + exclusion + "\r\nEND:VEVENT");
        data.WriteCalendar("series.ics", DesktopFixture.Calendar(master, replacement));
        DesktopState state = data.CreateState();
        List<DateOnly> reads = [];
        state.BeforeJournalDay = reads.Add;
        (Journal journal, _) = await state.OpenJournal().WaitAsync(Wait);
        IReadOnlyList<JournalDay> entries = journal.ReadBatch();
        Assert.True(journal.Exhausted);
        Assert.Equal(repeats ? new[] { DesktopFixture.Today.AddDays(-2) } : [], entries.Select(entry => entry.Date));
        Assert.Equal(Enumerable.Range(0, repeats ? 3 : 0).Select(i => DesktopFixture.Today.AddDays(-i)), reads);
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
            DesktopState state = data.CreateState();
            Task<(Journal Journal, JournalOutlook Outlook)> first = state.OpenJournal();
            await data.Outlook.OldestEntered.Task.WaitAsync(Wait);
            AssertLocalDay(await first.WaitAsync(Wait));
            AssertLocalDay(await state.OpenJournal().WaitAsync(Wait));
            data.Day = DesktopFixture.Today.AddDays(1);
            AssertLocalDay(await state.OpenJournal().WaitAsync(Wait));
            Assert.Equal(1, data.Outlook.OldestCalls);
        }
        finally
        {
            data.Outlook.Release();
        }

        static void AssertLocalDay((Journal Journal, JournalOutlook Outlook) opened)
        {
            Assert.Equal(OutlookStatus.TimedOut, opened.Outlook.Status);
            Assert.Equal("Read locally", Assert.Single(opened.Journal.ReadBatch()).Body);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeniedOutlookDay_StopsAtTheRemainingLocalHistory(bool local)
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        DateOnly oldest = DesktopFixture.Today.AddDays(-42);
        DateTime start = oldest.ToDateTime(new TimeOnly(9, 0));
        data.Outlook.Days[oldest] = [new Appointment("Older Outlook item", null, start, start.AddHours(1), false)];
        if (local)
        {
            data.WriteNote(Date(1), "Local note");
            data.WriteCalendar("local.ics", DesktopFixture.Calendar(DesktopFixture.Event("Local meeting", "20261027T090000", "20261027T100000")));
        }
        DesktopState state = data.CreateState();
        List<DateOnly> reads = [];
        state.BeforeJournalDay = reads.Add;
        (Journal journal, JournalOutlook outlook) = await state.OpenJournal().WaitAsync(Wait);
        Assert.Equal(OutlookStatus.Ok, outlook.Status);
        data.Outlook.AppointmentKind = OutlookReadKind.Denied;

        IReadOnlyList<JournalDay> entries = journal.ReadBatch();
        Assert.Equal(OutlookStatus.Denied, outlook.Status);
        Assert.Equal(1, data.Outlook.AppointmentCalls);
        Assert.Equal(local ? new[] { DesktopFixture.Today.AddDays(-1), DesktopFixture.Today.AddDays(-2) } : [], entries.Select(e => e.Date));
        if (local)
        {
            Assert.Equal("Local note", entries[0].Body);
            Assert.Equal("Local meeting", Assert.Single(entries[1].Appointments).Title);
        }
        Assert.True(journal.Exhausted);
        Assert.Empty(journal.ReadBatch());
        Assert.Equal(Enumerable.Range(0, local ? 3 : 1).Select(i => DesktopFixture.Today.AddDays(-i)), reads);
    }

    [Fact]
    public async Task TimedOutDay_ShowsStatusAndKeepsReadingLocalHistoryWithOneCall()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        data.OutlookTimeout = TimeSpan.FromSeconds(2);
        data.WriteNote(Date(0), "First local day");
        data.WriteCalendar("older.ics", DesktopFixture.Calendar(DesktopFixture.Event("", "20261015T090000", "20261015T093000")));
        DesktopState state = data.CreateState();
        data.Outlook.BlockAppointments();
        try
        {
            (string Title, DateOnly Day)? opened = null;
            IRenderedComponent<JournalPanel> cut = data.Bunit.Render<JournalPanel>(p =>
                p.Add(c => c.OnAppointment, target => { opened = target; }));
            await data.Outlook.AppointmentsEntered.Task.WaitAsync(Wait);
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("Outlook (timed out)", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
                Assert.Equal("First local day", cut.Find(".journal-body").TextContent);
            }, Wait);
            Assert.Equal(1, data.Outlook.OldestCalls);
            Assert.True(await watching.WaitAsync(Wait));
            await cut.InvokeAsync(cut.Instance.Bridge.OnJournalBottom);
            cut.WaitForAssertion(() => Assert.Equal("09:00-09:30 (no title)", cut.Find(".journal-appointment").TextContent.Trim()), Wait);
            await cut.Find(".journal-appointment").ClickAsync(new MouseEventArgs());
            Assert.Equal((string.Empty, DesktopFixture.Today.AddDays(-14)), opened);
            Assert.Contains("timed out", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);

            // A new local date bypasses the per-day cache. A still-blocked journal read must also cover this open.
            data.Day = DesktopFixture.Today.AddDays(1);
            (Journal journal, JournalOutlook outlook) = await state.OpenJournal().WaitAsync(Wait);
            Assert.Equal(OutlookStatus.Ok, outlook.Status);
            Assert.Equal("First local day", Assert.Single(journal.ReadBatch()).Body);
            Assert.Equal(OutlookStatus.TimedOut, outlook.Status);
            Assert.Equal(1, data.Outlook.AppointmentCalls);
        }
        finally
        {
            data.Outlook.Release();
        }
    }

    [Fact]
    public async Task SealedMeetingBetweenBatches_LeavesOnlyTheAppointment()
    {
        data.Keys = FakeKeyStore.Ready();
        string meeting = Date(14) + " Tagged";
        data.WriteNote(Date(0), "First batch");
        data.WriteNote(meeting, "#meeting");
        data.WriteCalendar("older.ics", DesktopFixture.Calendar(DesktopFixture.Event("Scheduled", "20261015T090000", "20261015T093000")));
        DesktopState state = data.CreateState();
        (Journal journal, _) = await state.OpenJournal().WaitAsync(Wait);
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);

        data.WriteSensitive(meeting, "#meeting");
        string plain = Path.Combine(data.Root, meeting + ".md");
        File.Delete(plain);
        Assert.False(File.Exists(plain));
        Assert.True(File.Exists(Path.Combine(data.Root, meeting + NoteStore.SensitiveExtension)));
        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-14), day.Date);
        Assert.Empty(day.Meetings);
        Assert.Equal("Scheduled", Assert.Single(day.Appointments).Title);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public async Task DeniedHistory_StillOpensLinksAndRestoresWorkspace()
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        data.Outlook.AppointmentKind = OutlookReadKind.Denied;
        data.WriteNote(Date(0), "Daily text");
        data.WriteNote(Date(0) + " Standup", "#meeting");
        data.WriteCalendar("one.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Review", "20261029T090000", "20261029T093000"),
            DesktopFixture.Event("Review", "20261028T140000", "20261028T143000")));
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
        const string Older = ".journal-panel article:nth-of-type(2)";
        Assert.Equal(Date(1), cut.Find(Older + " h3").TextContent);
        Assert.Equal("14:00-14:30 Review", cut.Find(Older + " .journal-appointment").TextContent.Trim());
        await StaleClick.ClickAsync(cut, Older + " .journal-appointment");
        cut.WaitForAssertion(() => Assert.Equal("Review", cut.Find(".appointment-page h2").TextContent), Wait);
        Assert.Equal(DesktopFixture.Today.AddDays(-1), cut.FindComponent<AppointmentPanel>().Instance.Day);
    }
}
