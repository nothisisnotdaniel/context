namespace Context.Tests;

// 491 AC-4, AC-5: a blocked FakeOutlookSource read gives up after its release bound and says why, so a test that fails
// before Release() never blocks a pool thread forever.
public sealed class FakeOutlookSourceTests
{
    // The test awaits the read's task with this bound; the fake's own bound (50 ms) runs out long before it.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShortBound = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Appointments_BlockedPastTheBound_ThrowsNamingRelease()
    {
        using FakeOutlookSource outlook = new FakeOutlookSource(ShortBound);
        outlook.Block();

        Task read = Task.Run(() => outlook.Appointments(new DateOnly(2026, 10, 29), TimeZoneInfo.Utc));

        TimeoutException e = await Assert.ThrowsAsync<TimeoutException>(() => read.WaitAsync(Wait));
        Assert.Equal("FakeOutlookSource: Release() not called within 50 ms", e.Message);
        Assert.Equal(1, outlook.AppointmentCalls);
    }

    [Fact]
    public async Task RecentMail_BlockedPastTheBound_ThrowsNamingRelease()
    {
        using FakeOutlookSource outlook = new FakeOutlookSource(ShortBound);
        outlook.Block();

        Task read = Task.Run(() => outlook.RecentMail(new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), 10));

        TimeoutException e = await Assert.ThrowsAsync<TimeoutException>(() => read.WaitAsync(Wait));
        Assert.Equal("FakeOutlookSource: Release() not called within 50 ms", e.Message);
    }

    [Fact]
    public async Task Released_ReadAnswers()
    {
        using FakeOutlookSource outlook = new FakeOutlookSource();
        outlook.Block();
        outlook.Release();

        OutlookRead<MailMessage> read = await Task.Run(() => outlook.RecentMail(DateTime.UnixEpoch, 10)).WaitAsync(Wait);

        Assert.Equal(OutlookReadKind.Ok, read.Kind);
    }

    [Fact]
    public void DefaultBound_IsThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), FakeOutlookSource.DefaultReleaseBound);
    }
}
