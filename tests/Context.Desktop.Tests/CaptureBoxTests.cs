using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 140 AC-6 / AC-7: the capture box on its own.
public sealed class CaptureBoxTests : IDisposable
{
    private const string Line = "- 2026-10-29 10:05 ";

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-6: a text field with its label, placeholder and limit, a Capture button and an empty status line.
    [Fact]
    public async Task Render_FieldButtonAndStatusLine()
    {
        IRenderedComponent<CaptureBox> cut = await data.RenderAsync<CaptureBox>();

        IElement field = cut.Find(".capture-box input");
        Assert.Equal("text", field.GetAttribute("type"));
        Assert.Equal("Capture", field.GetAttribute("aria-label"));
        Assert.Equal("Capture to inbox", field.GetAttribute("placeholder"));
        Assert.Equal("1000", field.GetAttribute("maxlength"));
        IElement button = Assert.Single(cut.FindAll(".capture-box button"));
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Capture", button.TextContent);
        IElement status = cut.Find(".capture-box .capture-status");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal(string.Empty, status.TextContent);
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-7: Enter captures the typed text once, empties the field, shows the accent line and gives the focus back;
    // other keys do nothing.
    [Fact]
    public async Task Enter_CapturesClearsAndRefocuses()
    {
        IRenderedComponent<CaptureBox> cut = await data.RenderAsync<CaptureBox>();
        string? fieldId = cut.Find("input.capture-text").GetAttribute("blazor:elementReference");
        Assert.False(string.IsNullOrEmpty(fieldId));
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = "call Anna" });

        await cut.Find("input.capture-text").KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        Assert.False(Directory.Exists(data.Root));
        await cut.Find("input.capture-text").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal($"# inbox\n{Line}call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal(string.Empty, cut.Find("input.capture-text").GetAttribute("value"));
        IElement status = cut.Find(".capture-status");
        Assert.Equal("Captured.", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        JSRuntimeInvocation focus = data.Bunit.JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal(fieldId, ((ElementReference)focus.Arguments[0]!).Id);
    }

    // AC-7: the button captures as well and leaves the focus where it is; a second capture appends a second line.
    [Fact]
    public async Task Button_CapturesWithoutMovingFocus()
    {
        IRenderedComponent<CaptureBox> cut = await data.RenderAsync<CaptureBox>();
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = "one" });
        await cut.Find("button.capture").ClickAsync(new MouseEventArgs());
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = "two" });

        await cut.Find("button.capture").ClickAsync(new MouseEventArgs());

        Assert.Equal($"# inbox\n{Line}one\n{Line}two\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal(string.Empty, cut.Find("input.capture-text").GetAttribute("value"));
        Assert.DoesNotContain(data.Bunit.JSInterop.Invocations, i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
    }

    // AC-7: a refusal shows its text in red, keeps what was typed and writes nothing.
    [Theory]
    [InlineData("   ", "Type something to capture.")]
    [InlineData(null, "A capture can be at most 1000 characters.")]
    public async Task Refused_RedTextAndTheTypedTextStays(string? text, string expected)
    {
        string typed = text ?? new string('x', 1001);
        data.WriteNote("inbox", "# inbox\n");
        IRenderedComponent<CaptureBox> cut = await data.RenderAsync<CaptureBox>();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root, TagIndex.FileName);
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = typed });

        await cut.Find("button.capture").ClickAsync(new MouseEventArgs());

        IElement status = cut.Find(".capture-status");
        Assert.Equal(expected, status.TextContent);
        Assert.Equal(expected, status.GetAttribute("title"));
        Assert.True(status.ClassList.Contains("red"));
        Assert.Equal(typed, cut.Find("input.capture-text").GetAttribute("value"));
        Assert.Equal(before, FileSnapshot.Take(data.Root, TagIndex.FileName));
    }

    // AC-7: while the capture runs the field and the button are disabled; a second Enter starts nothing.
    [Fact]
    public async Task Capture_Running_FieldAndButtonDisabled()
    {
        IRenderedComponent<CaptureBox> cut = await data.RenderAsync<CaptureBox>();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () => release.Wait(TimeSpan.FromSeconds(10));
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = "call Anna" });

        Task capture = cut.Find("button.capture").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".capture-box input, .capture-box button"),
            e => Assert.True(e.HasAttribute("disabled"), e.OuterHtml)));
        Assert.Equal(2, cut.FindAll(".capture-box input, .capture-box button").Count);
        release.Set();
        await capture;
        cut.WaitForAssertion(() => Assert.All(
            cut.FindAll(".capture-box input, .capture-box button"),
            e => Assert.False(e.HasAttribute("disabled"), e.OuterHtml)));
        Assert.Equal($"# inbox\n{Line}call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
    }

    // AC-8 (the box's side): a capture the parent holds back shows the parent's text in red, keeps the typed text,
    // writes nothing and doesn't report a capture; without a hold the parent hears about the capture once.
    [Fact]
    public async Task Guard_HoldsTheCapture_OnCapturedOnlyAfterAWrite()
    {
        await data.RefreshAsync();
        string? held = "Save inbox first.";
        int captured = 0;
        IRenderedComponent<CaptureBox> cut = data.Bunit.Render<CaptureBox>(p => p
            .Add(b => b.Guard, () => Task.FromResult<string?>(held))
            .Add(b => b.OnCaptured, () => captured++));
        await cut.Find("input.capture-text").InputAsync(new ChangeEventArgs { Value = "call Anna" });

        await cut.Find("button.capture").ClickAsync(new MouseEventArgs());

        Assert.Equal("Save inbox first.", cut.Find(".capture-status").TextContent);
        Assert.True(cut.Find(".capture-status").ClassList.Contains("red"));
        Assert.Equal("call Anna", cut.Find("input.capture-text").GetAttribute("value"));
        Assert.False(Directory.Exists(data.Root));
        Assert.Equal(0, captured);

        held = null;
        await cut.Find("button.capture").ClickAsync(new MouseEventArgs());

        Assert.Equal("Captured.", cut.Find(".capture-status").TextContent);
        Assert.Equal($"# inbox\n{Line}call Anna\n", File.ReadAllText(Path.Combine(data.Root, "inbox.md")));
        Assert.Equal(1, captured);
    }
}
