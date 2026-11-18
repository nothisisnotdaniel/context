namespace Context.Tests;

public sealed class DesktopSnapshotTests
{
    // The raw text never leaks through formatting.
    [Fact]
    public void ToString_OnlyTypeName()
    {
        DesktopSnapshot snapshot = new DesktopSnapshot(
            false, DesktopFixture.Today, null, [new TodoItem(Guid.NewGuid(), "secret", DesktopFixture.Today, DateTime.UtcNow)],
            false, [], false, "/secret/folder");

        Assert.Equal("DesktopSnapshot", snapshot.ToString());
        Assert.Equal("DesktopSnapshot", $"{snapshot}");
    }

    // 227 AC-7: a private event in a calendar file never reaches Snapshot.Current; the two public ones are 1 and 2.
    [Fact]
    public async Task Current_LeavesOutPrivateAppointments()
    {
        using DesktopFixture data = new DesktopFixture();
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000", "20261029T091500"),
            DesktopFixture.Event("Doctor", "20261029T100000", "20261029T110000")
                .Replace("END:VEVENT", "CLASS:PRIVATE\r\nEND:VEVENT", StringComparison.Ordinal),
            DesktopFixture.Event("Review", "20261029T120000", "20261029T130000")));

        DesktopSnapshot snapshot = await data.RefreshAsync();

        Assert.Equal(["Standup", "Review"], snapshot.Current!.Appointments.Select(a => a.Title).ToArray());
        Assert.Equal(2, snapshot.Current.Related.Count);
    }
}
