using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 363: StaleClick finds an element again when a render dropped the handler its click names, and clicks it only once.
public sealed class StaleClickTests : IDisposable
{
    // The settings load awaits two Task.Run hops, so no wait relies on bUnit's 1 s default.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a click on an element from before a render throws UnknownEventHandlerIdException; the helper finds the
    // element again, so the theme is saved and applied, and SetTheme writes exactly once.
    [Fact]
    public async Task StaleElement_HelperClicksOnce()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("div.theme-choice"));
            Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal);
        }, Wait);
        int writes = 0;
        data.Actions!.BeforeWrite = () => Interlocked.Increment(ref writes);

        // bUnit updates a node that stays on the page in place, so a copy keeps the handler ID from before the render,
        // just as an element found right before a render that lands ahead of the dispatch does.
        IElement stale = (IElement)cut.Find("button.theme-light").Clone();
        string before = stale.GetAttribute("blazor:onclick")!;
        cut.Render();
        Assert.NotEqual(before, cut.Find("button.theme-light").GetAttribute("blazor:onclick"));
        await Assert.ThrowsAsync<UnknownEventHandlerIdException>(() => stale.ClickAsync(new MouseEventArgs()));
        Assert.Equal(0, Volatile.Read(ref writes));

        await StaleClick.ClickAsync(cut, "button.theme-light");

        cut.WaitForAssertion(() => Assert.Equal("Theme: Light.", cut.Find("p.theme-result").TextContent), Wait);
        Assert.Equal(AppTheme.Light, new SettingsStore(data.Root).ReadTheme());
        Assert.Contains("\"light\"", File.ReadAllText(Path.Combine(data.Root, SettingsStore.FileName)), StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref writes));
    }
}
