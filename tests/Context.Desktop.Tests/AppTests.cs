using Bunit;

namespace Context.Tests;

public sealed class AppTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-7: rendering App over sample data and letting the state refresh changes no data file.
    [Fact]
    public async Task Render_SampleData_ChangesNoFile()
    {
        WriteSample();
        SortedDictionary<string, string> before = Files();

        IRenderedComponent<App> cut = await data.RenderAsync<App>();
        await data.State!.Refresh();
        data.UtcNow = data.UtcNow.Add(DesktopState.MaxAge);
        Assert.True(data.State.Tick());
        await data.State.Refresh();
        cut.WaitForAssertion(() => Assert.Contains("Sample todo", cut.Find(".todos").TextContent));

        Assert.NotEmpty(cut.FindAll("input.done[type=checkbox]"));
        Assert.Equal(before, Files());
    }

    private void WriteSample()
    {
        data.WriteNote("Plan", "# Plan\n#work notes [[Other]]\n");
        data.WriteNote("Other", "# Other\n#work\n");
        data.AddTodo("Sample todo #work", DesktopFixture.Today);
        data.AddTodo("Later todo", DesktopFixture.Today.AddDays(4));
        data.WriteCalendar("work.ics", DesktopFixture.Calendar(
            DesktopFixture.Event("Standup", "20261029T090000Z", "20261029T093000Z")));
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
    }

    // context.db* is the tag index a refresh keeps up to date; it is not user data.
    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName);
}
