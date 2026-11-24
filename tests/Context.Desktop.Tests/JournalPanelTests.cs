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
        if (outlookFailsOnAppend)
        {
            AddOutlookAppointment(14);
        }
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
            Assert.Single(data.Outlook.AppointmentArguments, call => call.Day == DesktopFixture.Today.AddDays(-14));
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
    public async Task OlderLocalNote_OnlyRequestsOutlookDaysInConfirmedHistory(bool populated)
    {
        new SettingsStore(data.Root).SaveOutlook(true);
        data.WriteNote(Date(16), "Older local day");
        if (populated)
        {
            AddOutlookAppointment(2);
        }
        DesktopState state = data.CreateState();
        (Journal opened, JournalOutlook outlook) = await state.OpenJournal().WaitAsync(Wait);
        using Journal journal = opened;
        IReadOnlyList<JournalDay> first = journal.ReadBatch();
        Assert.Equal(populated ? new[] { DesktopFixture.Today.AddDays(-2) } : [], first.Select(day => day.Date));
        if (populated)
        {
            Assert.Equal("Outlook fixture", Assert.Single(first[0].Appointments).Title);
        }
        JournalDay second = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-16), second.Date);
        Assert.Equal("Older local day", second.Body);
        Assert.True(journal.Exhausted);
        Assert.Empty(journal.ReadBatch());
        Assert.Equal(OutlookStatus.Ok, outlook.Status);
        Assert.Equal(1, data.Outlook.OldestCalls);
        Assert.Equal(Enumerable.Range(0, populated ? 3 : 0).Select(i => DesktopFixture.Today.AddDays(-i)),
            data.Outlook.AppointmentArguments.Select(call => call.Day));
    }

    private void AddOutlookAppointment(int daysAgo)
    {
        DateOnly day = DesktopFixture.Today.AddDays(-daysAgo);
        DateTime start = day.ToDateTime(new TimeOnly(9, 0));
        data.Outlook.Days[day] = [new Appointment("Outlook fixture", null, start, start.AddHours(1), false)];
    }

    [Fact]
    public void ReplacementSettings_CannotEnableOutlookForTheBoundStore()
    {
        string replacement = data.Outside("replacement");
        new SettingsStore(data.Root).SaveOutlook(false);
        new SettingsStore(replacement).SaveOutlook(true);
        AddOutlookAppointment(0);
        DesktopState state = data.CreateState();
        using JournalFiles sources = new(data.Notes!, new CalendarStore(data.Root, DesktopFixture.Zone), null);
        Directory.Move(data.Root, data.Outside("original"));
        Directory.Move(replacement, data.Root);
        Assert.True(new SettingsStore(data.Root).ReadOutlook());

        (Journal opened, JournalOutlook outlook) = state.OpenJournal(sources);
        using Journal journal = opened;
        Assert.Empty(journal.ReadBatch());
        Assert.True(journal.Exhausted);
        Assert.Equal(OutlookStatus.Off, outlook.Status);
        Assert.Equal(0, data.Outlook.OldestCalls);
        Assert.Equal(0, data.Outlook.AppointmentCalls);
    }

    [Theory]
    [InlineData("limit", true)]
    [InlineData("oversized", false)]
    [InlineData("invalid", false)]
    [InlineData("alias", false)]
    public async Task JournalConsent_RequiresBoundedRegularSettings(string kind, bool enabled)
    {
        data.WriteNote(Date(0), "Local fixture");
        AddOutlookAppointment(0);
        string path = Path.Combine(data.Root, SettingsStore.FileName);
        const string OptIn = """{"outlook":{"enabled":true}}""";
        if (kind == "alias")
        {
            string fixture = data.Outside("fixture.json");
            File.WriteAllText(fixture, OptIn);
            try
            {
                File.CreateSymbolicLink(path, fixture);
                output.WriteLine("Settings fixture is a file link.");
            }
            catch (IOException e) when (OperatingSystem.IsWindows() && (e.HResult & 0xffff) == 1314)
            {
                Directory.CreateDirectory(path);
                output.WriteLine("Settings fixture is a directory. Windows denied file-link creation (1314).");
            }
        }
        else
        {
            File.WriteAllText(path, kind == "invalid" ? """{"outlook":{"enabled":"true"}}"""
                : OptIn.PadRight((int)SettingsStore.MaxBytes + (kind == "oversized" ? 1 : 0)));
        }
        (Journal opened, JournalOutlook outlook) = await data.CreateState().OpenJournal().WaitAsync(Wait);
        using Journal journal = opened;
        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal("Local fixture", day.Body);
        Assert.Equal(enabled ? new[] { "Outlook fixture" } : [], day.Appointments.Select(a => a.Title));
        Assert.Equal(enabled ? OutlookStatus.Ok : OutlookStatus.Off, outlook.Status);
        Assert.Equal(enabled ? 1 : 0, data.Outlook.OldestCalls);
        Assert.Equal(enabled ? 1 : 0, data.Outlook.AppointmentCalls);
        Assert.True(journal.Exhausted);
    }

    [Fact]
    public async Task OutlookTurnedOffBetweenBatches_StopsDayCallsAndKeepsLocalNotes()
    {
        SettingsStore settings = new(data.Root);
        settings.SaveOutlook(true);
        data.WriteNote(Date(0), "First batch");
        data.WriteNote(Date(14), "Older local note");
        AddOutlookAppointment(14);
        (Journal opened, JournalOutlook outlook) = await data.CreateState().OpenJournal().WaitAsync(Wait);
        using Journal journal = opened;
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);
        Assert.Equal(14, data.Outlook.AppointmentCalls);

        settings.SaveOutlook(false);
        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-14), day.Date);
        Assert.Equal("Older local note", day.Body);
        Assert.Empty(day.Appointments);
        Assert.Equal(OutlookStatus.Off, outlook.Status);
        Assert.Equal(1, data.Outlook.OldestCalls);
        Assert.Equal(14, data.Outlook.AppointmentCalls);
        Assert.True(journal.Exhausted);
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
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
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
    [InlineData("folder")]
    [InlineData("file")]
    [InlineData("cache")]
    public async Task ChangedCalendarSource_LeavesTheOtherEntriesOnTheDay(string changed)
    {
        static string Calendar(string title) => DesktopFixture.Calendar(DesktopFixture.Event(title, "20261015T090000", "20261015T093000"));
        data.WriteNote(Date(0), "First batch");
        data.WriteNote(Date(14), "Older daily");
        data.WriteCalendar("local.ics", Calendar("Local"));
        data.WriteCalendar("second.ICS", Calendar("Second"));
        data.WriteCache(Calendar("Cached"), data.UtcNow);
        data.SaveLink(FakeHttpHandler.Link);
        DesktopState state = data.CreateState();
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);

        string calendar = Path.Combine(data.Root, CalendarStore.FolderName);
        if (changed == "folder")
        {
            Directory.Move(calendar, data.Outside("previous-calendar"));
            data.WriteCalendar("local.ics", Calendar("Other folder"));
        }
        else
        {
            string path = changed == "file" ? Path.Combine(calendar, "local.ics") : Path.Combine(data.Root, CalendarFeed.CacheFileName);
            string fixture = Path.Combine(Path.GetDirectoryName(path)!, "fixture.txt");
            File.WriteAllText(fixture, Calendar("Alias"));
            File.Delete(path);
            try
            {
                File.CreateSymbolicLink(path, fixture);
                output.WriteLine("Excluded a local file alias.");
            }
            catch (IOException e) when (OperatingSystem.IsWindows() && (e.HResult & 0xffff) == 1314)
            {
                Directory.CreateDirectory(path);
                output.WriteLine("Excluded a directory at the file name. Windows file-link privilege was unavailable.");
            }
        }

        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-14), entry.Date);
        Assert.Equal("Older daily", entry.Body);
        string[] expected = changed switch { "folder" => ["Cached"], "file" => ["Cached", "Second"], _ => ["Local", "Second"] };
        Assert.Equal(expected, entry.Appointments.Select(item => item.Title));
        Assert.True(journal.Exhausted);
        Assert.Equal(0, data.Http.Requests);
    }

    [Fact]
    public async Task CacheSpelling_FollowsTheVolumesNameMatchingForHistoryAndDay()
    {
        Directory.CreateDirectory(data.Root);
        string calendar = DesktopFixture.Calendar(DesktopFixture.Event("Cached fixture", "20261027T090000", "20261027T093000"));
        File.WriteAllText(Path.Combine(data.Root, "Calendar-Link.ics"), calendar);
        bool matches = File.Exists(Path.Combine(data.Root, CalendarFeed.CacheFileName));
        CalendarFeed feed = new(data.Root, new CalendarLink(data.Root), data.Http, () => data.UtcNow);
        Assert.Equal(matches ? calendar : null, feed.ReadCache());
        DesktopState state = data.CreateState();
        List<DateOnly> reads = [];
        state.BeforeJournalDay = reads.Add;
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
        IReadOnlyList<JournalDay> batch = journal.ReadBatch();
        if (matches)
        {
            JournalDay day = Assert.Single(batch);
            Assert.Equal(DesktopFixture.Today.AddDays(-2), day.Date);
            Assert.Equal("Cached fixture", Assert.Single(day.Appointments).Title);
            Assert.Equal([DesktopFixture.Today, DesktopFixture.Today.AddDays(-1), day.Date], reads);
        }
        else
        {
            Assert.Empty(batch);
            Assert.Empty(reads);
        }
        output.WriteLine("The fixed cache name matched the mixed-case fixture: " + matches);
        Assert.True(journal.Exhausted);
        Assert.Equal(0, data.Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedStoreBetweenBatches_OnlyKeepsTheIndependentOutlookAppointment(bool turnOutlookOff)
    {
        string replacement = Directory.CreateDirectory(data.Outside("replacement")).FullName;
        foreach (string root in new[] { data.Root, replacement })
        {
            new SettingsStore(root).SaveOutlook(true);
            NoteStore notes = new(root);
            notes.CreateWithText(Date(0), "First batch");
            notes.CreateWithText(Date(14), "Older daily");
            notes.CreateWithText(Date(14) + " Standup", "#meeting");
            string calendar = DesktopFixture.Calendar(DesktopFixture.Event("File appointment", "20261015T090000", "20261015T093000"));
            string folder = Directory.CreateDirectory(Path.Combine(root, CalendarStore.FolderName)).FullName;
            File.WriteAllText(Path.Combine(folder, "local.ics"), calendar);
            File.WriteAllText(Path.Combine(root, CalendarFeed.CacheFileName), calendar);
        }
        AddOutlookAppointment(14);
        using Journal journal = (await data.CreateState().OpenJournal().WaitAsync(Wait)).Journal;
        Assert.Equal("First batch", Assert.Single(journal.ReadBatch()).Body);
        // Windows allows moving the open calendar directory itself before moving its parent.
        Directory.Move(Path.Combine(data.Root, CalendarStore.FolderName), data.Outside("previous-calendar"));
        Directory.Move(data.Root, data.Outside("previous-store"));
        Directory.Move(replacement, data.Root);

        if (turnOutlookOff)
        {
            int calls = data.Outlook.AppointmentCalls;
            ActionResult result = await data.Actions!.SetOutlookEnabled(false).WaitAsync(Wait);
            Assert.Equal(ActionKind.Done, result.Kind);
            Assert.True(new SettingsStore(data.Outside("previous-store")).ReadOutlook());
            Assert.Empty(journal.ReadBatch());
            Assert.True(journal.Exhausted);
            Assert.Equal(calls, data.Outlook.AppointmentCalls);
            Assert.Equal(0, data.Http.Requests);
            return;
        }

        JournalDay day = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-14), day.Date);
        Assert.Null(day.Daily);
        Assert.Null(day.Body);
        Assert.Empty(day.Meetings);
        Assert.Equal("Outlook fixture", Assert.Single(day.Appointments).Title);
        Assert.True(journal.Exhausted);
        Assert.Equal(0, data.Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalCalendarByteLimit_AppliesToHistoryAndKeepsTheIndependentSource(bool oversizedCache)
    {
        string text = DesktopFixture.Calendar(DesktopFixture.Event("At limit", "20261028T090000", "20261028T100000"));
        string exact = text.PadRight((int)CalendarStore.MaxFileBytes);
        string over = DesktopFixture.Calendar(DesktopFixture.Event("Too large", "20261001T090000", "20261001T100000"))
            .PadRight((int)CalendarStore.MaxFileBytes + 1);
        data.WriteCalendar("local.ics", oversizedCache ? exact : over);
        data.WriteCache(oversizedCache ? over : exact, data.UtcNow);
        DesktopState state = data.CreateState();
        List<DateOnly> reads = [];
        state.BeforeJournalDay = reads.Add;
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
        JournalDay entry = Assert.Single(journal.ReadBatch());
        Assert.Equal(DesktopFixture.Today.AddDays(-1), entry.Date);
        Assert.Equal("At limit", Assert.Single(entry.Appointments).Title);
        Assert.True(journal.Exhausted);
        Assert.Equal(new[] { DesktopFixture.Today, entry.Date }, reads);
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
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
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
            using Journal journal = opened.Journal;
            Assert.Equal(OutlookStatus.TimedOut, opened.Outlook.Status);
            Assert.Equal("Read locally", Assert.Single(journal.ReadBatch()).Body);
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
        (Journal opened, JournalOutlook outlook) = await state.OpenJournal().WaitAsync(Wait);
        using Journal journal = opened;
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
        AddOutlookAppointment(0);
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
            (Journal reopened, JournalOutlook outlook) = await state.OpenJournal().WaitAsync(Wait);
            using Journal journal = reopened;
            Assert.Equal(OutlookStatus.Ok, outlook.Status);
            Assert.Equal("First local day", Assert.Single(journal.ReadBatch()).Body);
            Assert.Equal(OutlookStatus.TimedOut, outlook.Status);
            Assert.Equal(DesktopFixture.Today, Assert.Single(data.Outlook.AppointmentArguments).Day);
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
        using Journal journal = (await state.OpenJournal().WaitAsync(Wait)).Journal;
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
        IElement close = cut.Find(".journal-panel button.close");
        Assert.Equal("Close", close.GetAttribute("aria-label"));
        Assert.Equal("button", close.GetAttribute("type"));
        Assert.NotNull(close.QuerySelector("svg[aria-hidden=true][focusable=false]"));
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
