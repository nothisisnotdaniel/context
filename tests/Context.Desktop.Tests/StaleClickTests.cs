using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 363: StaleClick finds an element again when a render dropped the handler its click names, and clicks it only once.
public sealed class StaleClickTests : IDisposable
{
    // The settings load awaits two Task.Run hops, so no wait relies on bUnit's 1 s default.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    private IRenderedComponent<StatusBarView>? bar;

    // 455: feedback goes to the status bar, which Main renders; tests render the bar next to the panel.
    private IRenderedComponent<StatusBarView> Bar => bar ??= data.Bunit.Render<StatusBarView>();

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

        Bar.WaitForAssertion(() => Assert.Equal("Theme: Light.", data.StatusBar.Text), Wait);
        Assert.Equal(AppTheme.Light, new SettingsStore(data.Root).ReadTheme());
        Assert.Contains("\"light\"", File.ReadAllText(Path.Combine(data.Root, SettingsStore.FileName)), StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref writes));
    }

    // 491: FireAsync takes a finder (a row of a FindAll, a helper's query) and asks it again while the event names a
    // dropped handler, so the theme is still saved exactly once.
    [Fact]
    public async Task StaleElementFromAFinder_FiresOnce()
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
        IElement stale = (IElement)cut.Find("button.theme-dark").Clone();
        cut.Render();
        int finds = 0;

        await StaleClick.FireAsync(
            cut,
            () => Interlocked.Increment(ref finds) == 1 ? stale : cut.FindAll("button.theme-dark")[0],
            e => e.ClickAsync(new MouseEventArgs()));

        Bar.WaitForAssertion(() => Assert.Equal("Theme: Dark.", data.StatusBar.Text), Wait);
        Assert.Equal(AppTheme.Dark, new SettingsStore(data.Root).ReadTheme());
        Assert.Equal(2, Volatile.Read(ref finds));
        Assert.Equal(1, Volatile.Read(ref writes));
    }

    // 491: an exception other than a dropped handler is not retried.
    [Fact]
    public async Task OtherFailure_IsNotRetried()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        int finds = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => StaleClick.FireAsync(
            cut,
            () =>
            {
                Interlocked.Increment(ref finds);
                return cut.Find("div");
            },
            _ => throw new InvalidOperationException("not a stale handler")));

        Assert.Equal(1, Volatile.Read(ref finds));
    }

    // 491: Fire, for a test that does not await, finds the element again too and returns once the click was handled.
    [Fact]
    public void StaleElement_SyncFireClicksOnce()
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
        IElement stale = (IElement)cut.Find("button.theme-dark").Clone();
        cut.Render();
        int finds = 0;

        StaleClick.Fire(() => Interlocked.Increment(ref finds) == 1 ? stale : cut.Find("button.theme-dark"), e => e.ClickAsync(new MouseEventArgs()));

        Bar.WaitForAssertion(() => Assert.Equal("Theme: Dark.", data.StatusBar.Text), Wait);
        Assert.Equal(AppTheme.Dark, new SettingsStore(data.Root).ReadTheme());
        Assert.Equal(2, Volatile.Read(ref finds));
        Assert.Equal(1, Volatile.Read(ref writes));
    }
}
