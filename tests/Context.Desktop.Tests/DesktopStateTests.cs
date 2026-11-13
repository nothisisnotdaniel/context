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
                Assert.False(IsLinkType(field.FieldType), $"{type.Name}.{field.Name} is a {field.FieldType.Name}");
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
}
