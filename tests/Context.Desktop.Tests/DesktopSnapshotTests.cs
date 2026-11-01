namespace Context.Tests;

public sealed class DesktopSnapshotTests
{
    // The raw text never leaks through formatting.
    [Fact]
    public void ToString_OnlyTypeName()
    {
        DesktopSnapshot snapshot = new DesktopSnapshot(
            false, false, DesktopFixture.Today, null, [new TodoItem("secret", DesktopFixture.Today, DateTime.UtcNow)],
            false, [], false, "/secret/folder");

        Assert.Equal("DesktopSnapshot", snapshot.ToString());
        Assert.Equal("DesktopSnapshot", $"{snapshot}");
    }
}
