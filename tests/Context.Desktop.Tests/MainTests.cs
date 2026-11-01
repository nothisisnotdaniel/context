using Bunit;

namespace Context.Tests;

public sealed class MainTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-4 layout: notes on the left, /current in the middle, todos on the right.
    [Fact]
    public async Task Render_Unlocked_ThreeRegions()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal(
            ["notes-region", "current-region", "todos-region"],
            cut.Find(".layout").Children.Select(c => c.ClassName ?? string.Empty).ToArray());
    }

    // AC-8: an encryption.json shows only the locked panel.
    [Fact]
    public async Task Render_Encrypted_OnlyLockedPanel()
    {
        Vault.Create(data.Root, "a long enough passphrase", 1000).Dispose();
        data.WriteNote("Alpha", "a");

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal(
            "Your data is encrypted. The desktop app can't unlock it yet; use the console app.",
            cut.Find(".locked p").TextContent);
        Assert.Empty(cut.FindAll(".layout"));
        Assert.DoesNotContain("Alpha", cut.Markup);
    }

    // AC-10: a refresh that throws shows the fixed red line, never the exception message.
    [Fact]
    public async Task Render_RefreshThrows_FixedRedLine()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("secret detail " + FakeHttpHandler.PathToken);

        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();

        Assert.Equal("Can't read the data folder.", cut.Find("p.red").TextContent);
        Assert.DoesNotContain("secret detail", cut.Markup);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup);
        Assert.Empty(cut.FindAll(".layout"));
    }

    // Main re-renders when the state changes (the failure clears on the next good refresh).
    [Fact]
    public async Task Render_StateChanges_ReRenders()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.FeedClockThrows = new InvalidOperationException("boom");
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Single(cut.FindAll("p.red"));

        data.FeedClockThrows = null;
        data.AddTodo("Fresh todo", DesktopFixture.Today);
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Contains("Fresh todo", cut.Find(".todos").TextContent));
    }
}
