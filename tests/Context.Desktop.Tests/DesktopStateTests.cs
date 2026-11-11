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
}
