using Bunit;

namespace Context.Tests;

public sealed class LockedPanelTests : IDisposable
{
    private readonly BunitContext context = new();

    public void Dispose() => context.Dispose();

    // AC-8: the fixed text, nothing else to click.
    [Fact]
    public void Render_FixedText()
    {
        IRenderedComponent<LockedPanel> cut = context.Render<LockedPanel>();

        Assert.Equal(
            "Your data is encrypted. The desktop app can't unlock it yet; use the console app.",
            cut.Find("p").TextContent);
        Assert.Empty(cut.FindAll("a, button, input, form"));
    }
}
