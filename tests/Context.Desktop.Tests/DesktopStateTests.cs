using System.Net;
using System.Reflection;

namespace Context.Tests;

public sealed class DesktopStateTests : IDisposable
{
    private const BindingFlags All =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // The data is read (the feed is asked, the index is built).
    [Fact]
    public async Task Refresh_Plain_ReadsEverything()
    {
        data.WriteNote("Alpha", "#work");
        data.AddTodo("Due #work", DesktopFixture.Today);
        data.SaveLink(FakeHttpHandler.Link);

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.NotNull(snapshot.Current);
        Assert.Single(snapshot.Notes);
        Assert.Single(snapshot.Todos);
        Assert.Equal(1, data.Http.Requests);
        Assert.True(File.Exists(Path.Combine(data.Root, TagIndex.FileName)));
        Assert.Single(snapshot.Current.LinkedNotes);
    }

    // AC-7: neither the snapshot nor the state has a member that is or holds the calendar link.
    [Fact]
    public async Task Members_NeverHoldTheLink()
    {
        const string Link = "https://secret-host.example/abc-TOKEN123.ics";
        data.SaveLink(Link);
        data.WriteCache(
            DesktopFixture.Calendar(DesktopFixture.Event("Sync", "20261029T140000Z", "20261029T150000Z")),
            new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc));
        DesktopSnapshot snapshot = await data.RefreshAsync();
        DesktopState state = data.State!;

        foreach (Type type in new[] { typeof(DesktopSnapshot), typeof(DesktopState) })
        {
            foreach (FieldInfo field in type.GetFields(All))
            {
                // 138: the state holds the CalendarLink reader for the settings page (it knows the folder, not
                // the link; the feed held one all along). The link itself, a Uri, is still in no member.
                bool reader = type == typeof(DesktopState) && field.FieldType == typeof(CalendarLink);
                Assert.False(!reader && IsLinkType(field.FieldType), $"{type.Name}.{field.Name} is a {field.FieldType.Name}");
            }

            foreach (PropertyInfo property in type.GetProperties(All))
            {
                Assert.False(
                    IsLinkType(property.PropertyType), $"{type.Name}.{property.Name} is a {property.PropertyType.Name}");
            }
        }

        foreach ((object owner, FieldInfo field) in new object[] { snapshot, state }
            .SelectMany(o => o.GetType().GetFields(All).Select(f => (o, f))))
        {
            object? value = field.GetValue(field.IsStatic ? null : owner);
            if (value is string text)
            {
                Assert.DoesNotContain("TOKEN123", text);
                Assert.DoesNotContain("secret-host", text);
            }
        }

        Assert.Equal(CalendarFeedStatus.Stale, snapshot.Current!.Status);
    }

    // 171 AC-10: with a Times day that has left after 17:00 and todos due, refresh and tick write nothing: no
    // settings.json, no daily note, no moved todo; only the day-end timer (through DesktopActions) closes the day.
    [Fact]
    public async Task RefreshAndTick_AfterLeaving_WriteNothing()
    {
        Directory.CreateDirectory(data.TimesFolder);
        File.WriteAllText(
            Path.Combine(data.TimesFolder, "2026-10-29.json"),
            "{\"entries\":[{\"kind\":\"arrival\",\"time\":\"08:00\"},{\"kind\":\"leaving\",\"time\":\"17:30\"}]}");
        data.LocalNow = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Local);
        data.AddTodo("call Anna", DesktopFixture.Today);
        DesktopState state = data.CreateState();
        await state.Refresh();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        SortedDictionary<string, string> times = FileSnapshot.Take(data.TimesFolder);

        await state.Refresh();
        data.UtcNow = data.UtcNow.AddMinutes(10);
        Assert.True(state.Tick());
        await WaitForBuilds(state, 3);

        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(times, FileSnapshot.Take(data.TimesFolder));
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
        Assert.False(File.Exists(Path.Combine(data.Root, "2026-10-29.md")));
    }

    // AC-10: Tick refreshes only when the day changed or 5 minutes passed.
    [Fact]
    public async Task Tick_RefreshesOnDayChangeOrAge()
    {
        DesktopState state = data.CreateState();

        Assert.True(state.Tick());
        await WaitForBuilds(state, 1);

        data.UtcNow = data.UtcNow.AddMinutes(4).AddSeconds(59);
        Assert.False(state.Tick());

        data.UtcNow = data.UtcNow.AddSeconds(1);
        Assert.True(state.Tick());
        await WaitForBuilds(state, 2);

        Assert.False(state.Tick());

        data.Day = data.Day.AddDays(1);
        Assert.True(state.Tick());
        await WaitForBuilds(state, 3);
        Assert.Equal(data.Day, state.Snapshot!.Today);

        Assert.False(state.Tick());
        await Task.Delay(100);
        Assert.Equal(3, state.Builds);
    }

    // AC-10: an exception from a refresh gives the fixed failure snapshot, and the next refresh recovers.
    [Fact]
    public async Task Refresh_Throws_FailedSnapshotThenRecovers()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("boom");

        DesktopSnapshot failed = await data.RefreshAsync();

        Assert.True(failed.Failed);
        Assert.Null(failed.Current);

        data.FeedClockThrows = null;
        DesktopSnapshot recovered = await data.State!.Refresh();

        Assert.False(recovered.Failed);
        Assert.NotNull(recovered.Current);
    }

    // AC-10: a listener that throws doesn't stop the refresh or the other listeners.
    [Fact]
    public async Task Refresh_ListenerThrows_OthersStillCalled()
    {
        DesktopState state = data.CreateState();
        int called = 0;
        state.Changed += () => throw new InvalidOperationException("listener");
        state.Changed += () => Interlocked.Increment(ref called);

        await state.Refresh();
        await state.Refresh();

        Assert.Equal(2, called);
        Assert.Equal(2, state.Builds);
    }

    // Refresh runs off the caller's thread, one at a time; requests during a run coalesce into one more run.
    [Fact]
    public async Task Refresh_Overlapping_CoalesceIntoOneMoreRun()
    {
        using ManualResetEventSlim gate = new ManualResetEventSlim(false);
        using ManualResetEventSlim entered = new ManualResetEventSlim(false);
        data.Http = new FakeHttpHandler((request, _) =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                RequestMessage = request,
            });
        });
        data.SaveLink(FakeHttpHandler.Link);
        DesktopState state = data.CreateState();

        Task<DesktopSnapshot> first = state.Refresh();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Task<DesktopSnapshot> second = state.Refresh();
        Task<DesktopSnapshot> third = state.Refresh();
        Task<DesktopSnapshot> fourth = state.Refresh();
        Assert.Equal(0, state.Builds);
        gate.Set();
        await Task.WhenAll(first, second, third, fourth);

        Assert.Equal(2, state.Builds);
        Assert.Equal(2, data.Http.Requests);
    }

    // 138 AC-2: the calendar link as the settings page shows it: read only, no download, only the masked link.
    [Fact]
    public async Task CalendarLinkSetting_NotSetShownUnreadable_ReadsOnly()
    {
        DesktopState state = data.CreateState();

        CalendarLinkView none = await state.CalendarLinkSetting();

        Assert.Equal(CalendarLinkViewKind.NotSet, none.Kind);
        Assert.False(Directory.Exists(data.Root));

        data.SaveLink(FakeHttpHandler.Link);
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        CalendarLinkView fresh = await state.CalendarLinkSetting();

        Assert.Equal(CalendarLinkViewKind.Shown, fresh.Kind);
        Assert.Equal("https://" + FakeHttpHandler.Host + "/…", fresh.Masked);
        Assert.Null(fresh.CachedAtUtc);

        DateTime at = new(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc);
        data.WriteCache(DesktopFixture.Calendar(), at);
        CalendarLinkView cached = await state.CalendarLinkSetting();

        Assert.Equal(at, cached.CachedAtUtc);
        Assert.Equal(DesktopFixture.Zone, state.Zone);
        foreach (CalendarLinkView view in new[] { fresh, cached })
        {
            Assert.Equal("CalendarLinkView", view.ToString());
            Assert.DoesNotContain(FakeHttpHandler.PathToken, view.Masked!, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeHttpHandler.QueryToken, view.Masked!, StringComparison.Ordinal);
        }

        File.Delete(Path.Combine(data.Root, CalendarFeed.CacheFileName));
        Assert.Equal(before, FileSnapshot.Take(data.Root));

        data.WriteFile(CalendarLink.FileName, "not a link\n");
        CalendarLinkView damaged = await state.CalendarLinkSetting();

        Assert.Equal(CalendarLinkViewKind.Unreadable, damaged.Kind);
        Assert.Null(damaged.Masked);
        Assert.Equal("not a link\n", File.ReadAllText(Path.Combine(data.Root, CalendarLink.FileName)));
        Assert.Equal(0, data.Http.Requests);
        Assert.Equal(0, state.Builds);
    }

    // 138 AC-10: the machine key's details come from the key store without a private-key call or a new key.
    [Fact]
    public async Task SensitiveKey_UnsupportedNotSetUpShownUnreadable_NoAgreeNoNewKey()
    {
        FakeKeyStore keys = new FakeKeyStore { Supported = false };
        data.Keys = keys;
        DesktopState state = data.CreateState();

        SensitiveKeyView unsupported = await state.SensitiveKey();

        Assert.Equal(SensitiveKeyKind.Unsupported, unsupported.Kind);
        Assert.Equal(0, keys.DetailsReads);

        keys.Supported = true;
        SensitiveKeyView notSetUp = await state.SensitiveKey();

        Assert.Equal(SensitiveKeyKind.NotSetUp, notSetUp.Kind);
        Assert.Null(notSetUp.Details);

        keys.CreateMachineKey();
        int created = keys.MachineKeysCreated;
        SensitiveKeyView shown = await state.SensitiveKey();

        Assert.Equal(SensitiveKeyKind.Shown, shown.Kind);
        Assert.Equal("Context sensitive notes", shown.Details!.Name);
        Assert.Equal(SealedNote.KeyId(keys.MachinePublicKey()!), shown.Details.Fingerprint, ignoreCase: true);
        Assert.Null(shown.Details.ExpiresUtc);

        keys.Details = new KeyDetails("Context sensitive notes", "0A1B", new DateTime(2076, 11, 13, 0, 0, 0, DateTimeKind.Utc));
        SensitiveKeyView certificate = await state.SensitiveKey();

        Assert.Same(keys.Details, certificate.Details);

        keys.DetailsThrow = true;
        SensitiveKeyView unreadable = await state.SensitiveKey();

        Assert.Equal(SensitiveKeyKind.Unreadable, unreadable.Kind);
        Assert.Null(unreadable.Details);
        Assert.Equal(0, keys.Agrees);
        Assert.Equal(created, keys.MachineKeysCreated);
        Assert.Null(keys.RecoveryPublicKey());
        Assert.False(Directory.Exists(data.Root));
        Assert.Equal(0, state.Builds);
    }

    private static bool IsLinkType(Type type) =>
        type == typeof(CalendarLink) || type == typeof(Uri) || type == typeof(CalendarLink[]) || type == typeof(Uri[]);

    private static async Task WaitForBuilds(DesktopState state, int count)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(10);
        while (state.Builds < count && DateTime.UtcNow < until)
        {
            await Task.Delay(10);
        }

        Assert.Equal(count, state.Builds);
    }

    // 132 AC-5: a leftover key file of the removed feature changes nothing: notes, todos and appointments are read.
    [Fact]
    public async Task Refresh_WithALeftoverKeyFile_ReadsNotesTodosAndAppointments()
    {
        string key = Path.Combine(data.Root, RemovedFeature.KeyFileName);
        data.WriteFile(RemovedFeature.KeyFileName, "{\"left\":\"over\"}");
        data.WriteNote("Alpha", "#work");
        data.AddTodo("Due #work", DesktopFixture.Today);
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000")));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.False(snapshot.Failed);
        Assert.NotNull(snapshot.Current);
        Assert.Equal(["Standup"], snapshot.Current.Appointments.Select(a => a.Title).ToArray());
        Assert.Single(snapshot.Notes);
        Assert.Equal(["Due #work"], snapshot.Todos.Select(t => t.Text).ToArray());
        Assert.True(File.Exists(Path.Combine(data.Root, TagIndex.FileName)));
        Assert.Equal("{\"left\":\"over\"}", File.ReadAllText(key));
    }

    // 137: Tags() gives what the Core view gives, every tag with its count.
    [Fact]
    public async Task Tags_SameAsTheCoreView()
    {
        data.WriteNote("Plan", "#work #[big project]\n");
        data.WriteNote("Other", "#Work\n");
        DesktopState state = data.CreateState();

        TagsView view = await state.Tags();

        TagIndex index = new TagIndex(data.Root);
        TagsView expected = TagsView.Build(index, data.Notes!, string.Empty);
        Assert.Equal(TagsViewKind.AllTags, view.Kind);
        Assert.Equal(expected.Tags.Select(t => (t.Tag, t.Count)).ToArray(), view.Tags.Select(t => (t.Tag, t.Count)).ToArray());
        Assert.Contains(("work", 2), view.Tags.Select(t => (t.Tag, t.Count)));
        Assert.Contains(("big project", 1), view.Tags.Select(t => (t.Tag, t.Count)));
    }

    // 137: Links() gives what the Core view gives for that note.
    [Fact]
    public async Task Links_SameAsTheCoreView()
    {
        data.WriteNote("Plan", "#work [[Other]] [[Ghost]]\n");
        data.WriteNote("Other", "#work [[Plan]]\n");
        data.WriteNote("Alone", "x\n");
        DesktopState state = data.CreateState();

        LinksView view = await state.Links("Plan");
        LinksView alone = await state.Links("Alone");
        LinksView none = await state.Links("Nowhere");

        TagIndex index = new TagIndex(data.Root);
        LinksView expected = LinksView.Build(index, data.Notes!, "Plan");
        Assert.Equal(LinksViewKind.Links, view.Kind);
        Assert.Equal("Plan", view.Note);
        Assert.Equal(
            expected.Links.Select(l => (l.Note, l.Direction, l.Missing, string.Join(",", l.SharedTags))).ToArray(),
            view.Links.Select(l => (l.Note, l.Direction, l.Missing, string.Join(",", l.SharedTags))).ToArray());
        Assert.Equal(
            [("Ghost", NoteLinkDirection.To, true, string.Empty), ("Other", NoteLinkDirection.Both, false, "work")],
            view.Links.Select(l => (l.Note, l.Direction, l.Missing, string.Join(",", l.SharedTags))).Order().ToArray());
        Assert.Equal(LinksViewKind.NoLinks, alone.Kind);
        Assert.Equal(LinksViewKind.NoSuchNote, none.Kind);
    }

    // 137: a context.db that is not a database gives IndexUnreadable from both, and the file is left alone.
    [Fact]
    public async Task TagsAndLinks_CorruptIndex_IndexUnreadable()
    {
        string text = "this is not a database, just some text that fills the header....";
        data.WriteNote("Plan", "#work\n");
        data.WriteFile(TagIndex.FileName, text);
        DesktopState state = data.CreateState();

        TagsView tags = await state.Tags();
        LinksView links = await state.Links("Plan");

        Assert.Equal(TagsViewKind.IndexUnreadable, tags.Kind);
        Assert.Empty(tags.Tags);
        Assert.Equal(LinksViewKind.IndexUnreadable, links.Kind);
        Assert.Empty(links.Links);
        Assert.Equal(text, File.ReadAllText(Path.Combine(data.Root, TagIndex.FileName)));
    }

    // 217: an Outlook appointment on the test day, in the calendar's zone.
    private static Appointment OutlookAppointment(string title, int hour, int minute = 0) =>
        new(title, "Room 1", new DateTime(2026, 10, 29, hour, minute, 0), new DateTime(2026, 10, 29, hour + 1, minute, 0), false);

    private void OutlookOn() => new SettingsStore(data.Root).SaveOutlook(true);

    // 217 AC-1: with no outlook section, with enabled false, or with Outlook not available, nothing is asked across
    // Refresh and DayAppointments.
    [Theory]
    [InlineData(null, "Available")]
    [InlineData("{\"outlook\":{\"enabled\":false}}", "Available")]
    [InlineData("{\"outlook\":{\"enabled\":\"yes\"}}", "Available")]
    [InlineData("not json", "Available")]
    [InlineData("{\"outlook\":{\"enabled\":true}}", "NotInstalled")]
    [InlineData("{\"outlook\":{\"enabled\":true}}", "Unsupported")]
    public async Task Outlook_Off_AsksTheSourceNothing(string? settings, string availability)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        data.Outlook.Available = Enum.Parse<OutlookAvailability>(availability);
        data.Outlook.Days[DesktopFixture.Today] = [OutlookAppointment("From Outlook", 11)];

        DesktopSnapshot snapshot = await data.RefreshAsync();
        DayAppointmentsView day = await data.State!.DayAppointments(DesktopFixture.Today);

        Assert.Equal(0, data.Outlook.AppointmentCalls);
        Assert.Empty(snapshot.Current!.Appointments);
        Assert.Empty(day.Appointments);
        Assert.Equal(OutlookStatus.Off, day.Outlook);
    }

    // 217 AC-3: Outlook's appointments are merged with the .ics ones in CalendarStore.Day order, the same in
    // DayAppointments and in the snapshot's current view (so /meeting numbers them like the sidebar).
    [Fact]
    public async Task Outlook_Ok_MergesAppointments_InDayOrder_InTheSidebarAndTheSnapshot()
    {
        OutlookOn();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000"),
            DesktopFixture.Event("Lunch", "20261029T120000", "20261029T130000")));
        data.Outlook.Days[DesktopFixture.Today] = [OutlookAppointment("Review", 14), OutlookAppointment("Planning", 10, 30)];
        CalendarStore store = new CalendarStore(data.Root, DesktopFixture.Zone);
        string[] expected = store.Day(DesktopFixture.Today, data.Outlook.Days[DesktopFixture.Today]).Appointments
            .Select(a => a.Title).ToArray();

        DesktopSnapshot snapshot = await data.RefreshAsync();
        DayAppointmentsView day = await data.State!.DayAppointments(DesktopFixture.Today);

        Assert.Equal(["Standup", "Planning", "Lunch", "Review"], expected);
        Assert.Equal(expected, day.Appointments.Select(a => a.Title).ToArray());
        Assert.Equal(expected, snapshot.Current!.Appointments.Select(a => a.Title).ToArray());
        Assert.Equal(OutlookStatus.Ok, day.Outlook);
        Assert.Equal(DesktopFixture.Today, data.Outlook.AppointmentArguments[0].Day);
        Assert.Same(DesktopFixture.Zone, data.Outlook.AppointmentArguments[0].Zone);
    }

    // 217 AC-3: the appointment page finds an Outlook appointment by its title.
    [Fact]
    public async Task Outlook_Ok_AppointmentPage_FindsTheOutlookAppointment()
    {
        OutlookOn();
        data.Outlook.Days[DesktopFixture.Today] = [OutlookAppointment("Review", 14)];
        DesktopState state = data.CreateState();

        AppointmentPageView page = await state.AppointmentPage("Review", DesktopFixture.Today);

        Assert.NotNull(page.First);
        Assert.Equal(("Review", "Room 1", new DateTime(2026, 10, 29, 14, 0, 0)), (page.First.Title, page.First.Location, page.First.Start));
        Assert.False(page.IndexUnreadable);
    }

    // 217 AC-4: every failure kind (and a call that throws) gives its status; the .ics appointments still show and
    // nothing throws.
    [Theory]
    [InlineData("NotRunning", false, "NotRunning")]
    [InlineData("Denied", false, "Denied")]
    [InlineData("Failed", false, "Failed")]
    [InlineData("Ok", true, "Failed")]
    public async Task Outlook_Failure_GivesItsStatus_AndKeepsTheIcsAppointments(
        string kindName, bool throws, string expectedName)
    {
        OutlookReadKind kind = Enum.Parse<OutlookReadKind>(kindName);
        OutlookStatus expected = Enum.Parse<OutlookStatus>(expectedName);
        OutlookOn();
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000")));
        data.Outlook.AppointmentKind = kind;
        data.Outlook.Days[DesktopFixture.Today] = [OutlookAppointment("Review", 14)];
        data.Outlook.Throws = throws ? new InvalidOperationException("secret text") : null;

        DesktopSnapshot snapshot = await data.RefreshAsync();
        DayAppointmentsView day = await data.State!.DayAppointments(DesktopFixture.Today);

        Assert.False(snapshot.Failed);
        Assert.Equal(["Standup"], snapshot.Current!.Appointments.Select(a => a.Title).ToArray());
        Assert.Equal(["Standup"], day.Appointments.Select(a => a.Title).ToArray());
        Assert.False(day.Failed);
        Assert.Equal(expected, day.Outlook);
    }

    // 217 AC-5: a source call that doesn't return within OutlookTimeout gives TimedOut; the .ics appointments show.
    [Fact]
    public async Task Outlook_Blocked_TimesOut()
    {
        OutlookOn();
        data.OutlookTimeout = TimeSpan.FromMilliseconds(100);
        data.WriteCalendar("a.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T093000")));
        data.Outlook.Days[DesktopFixture.Today] = [OutlookAppointment("Review", 14)];
        data.Outlook.Block();
        DesktopState state = data.CreateState();

        DayAppointmentsView day = await state.DayAppointments(DesktopFixture.Today);

        Assert.Equal(TimeSpan.FromMilliseconds(100), state.OutlookTimeout);
        Assert.Equal(OutlookStatus.TimedOut, day.Outlook);
        Assert.Equal(["Standup"], day.Appointments.Select(a => a.Title).ToArray());
    }

    // 217 AC-5: the default timeout is ten seconds.
    [Fact]
    public void Outlook_DefaultTimeout_IsTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), data.CreateState().OutlookTimeout);
    }

    // 217 AC-6: two reads within five minutes (by the state's UTC clock) make one source call; one after five
    // minutes makes a second; each day has its own entry.
    [Fact]
    public async Task Outlook_Cache_FiveMinutesPerDay()
    {
        OutlookOn();
        DesktopState state = data.CreateState();
        DateOnly tomorrow = DesktopFixture.Today.AddDays(1);

        await state.DayAppointments(DesktopFixture.Today);
        data.UtcNow = data.UtcNow.AddMinutes(4).AddSeconds(59);
        await state.DayAppointments(DesktopFixture.Today);
        Assert.Equal(1, data.Outlook.AppointmentCalls);

        await state.DayAppointments(tomorrow);
        Assert.Equal(2, data.Outlook.AppointmentCalls);
        Assert.Equal(tomorrow, data.Outlook.AppointmentArguments[1].Day);

        data.UtcNow = data.UtcNow.AddSeconds(1);
        await state.DayAppointments(DesktopFixture.Today);
        Assert.Equal(3, data.Outlook.AppointmentCalls);
    }

    // 217 AC-6: a clock that went back makes the cached read stale.
    [Fact]
    public async Task Outlook_Cache_ClockBack_ReadsAgain()
    {
        OutlookOn();
        DesktopState state = data.CreateState();

        await state.DayAppointments(DesktopFixture.Today);
        data.UtcNow = data.UtcNow.AddMinutes(-1);
        await state.DayAppointments(DesktopFixture.Today);

        Assert.Equal(2, data.Outlook.AppointmentCalls);
    }
}
