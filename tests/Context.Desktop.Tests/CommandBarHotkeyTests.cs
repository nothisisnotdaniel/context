namespace Context.Tests;

/// <summary>The command bar's hotkey with a fake hotkey and window (job 282, AC-8).</summary>
public sealed class CommandBarHotkeyTests
{
    [Fact]
    public void Press_NoBarOpen_ShowsOnce()
    {
        FakeGlobalHotkey hotkey = new FakeGlobalHotkey();
        FakeCommandBarWindow window = new FakeCommandBarWindow();
        using CommandBarHotkey bar = new CommandBarHotkey(hotkey, window);

        hotkey.Press();

        Assert.Equal(1, window.Shows);
        Assert.Equal(0, window.Focuses);
    }

    [Fact]
    public void Press_BarOpen_Focuses()
    {
        FakeGlobalHotkey hotkey = new FakeGlobalHotkey();
        FakeCommandBarWindow window = new FakeCommandBarWindow();
        using CommandBarHotkey bar = new CommandBarHotkey(hotkey, window);

        hotkey.Press();
        hotkey.Press();
        hotkey.Press();

        Assert.Equal(1, window.Shows);
        Assert.Equal(2, window.Focuses);

        window.IsOpen = false;
        hotkey.Press();
        Assert.Equal(2, window.Shows);
    }

    [Fact]
    public void OtherHotkey_NeverOpensTheBar()
    {
        FakeGlobalHotkey screenshot = new FakeGlobalHotkey();
        FakeGlobalHotkey barKey = new FakeGlobalHotkey();
        FakeCommandBarWindow window = new FakeCommandBarWindow();
        using CommandBarHotkey bar = new CommandBarHotkey(barKey, window);

        screenshot.Press();
        screenshot.Press();

        Assert.Equal(0, window.Shows);
        Assert.Equal(0, window.Focuses);
    }

    [Fact]
    public void Register_And_Dispose_PassThrough()
    {
        FakeGlobalHotkey hotkey = new FakeGlobalHotkey();
        FakeCommandBarWindow window = new FakeCommandBarWindow();
        CommandBarHotkey bar = new CommandBarHotkey(hotkey, window);

        Assert.True(bar.Register());
        Assert.True(hotkey.Registered);
        bar.Dispose();
        Assert.True(hotkey.Disposed);

        hotkey.Press();
        Assert.Equal(0, window.Shows);
    }

    [Fact]
    public void Ids_And_Keys_Differ()
    {
        Assert.NotEqual(GlobalHotkeys.ScreenshotId, GlobalHotkeys.CommandBarId);
        Assert.NotEqual(
            (GlobalHotkeys.KeyS, GlobalHotkeys.ScreenshotModifiers),
            (GlobalHotkeys.KeySpace, GlobalHotkeys.CommandBarModifiers));
        Assert.Equal(GlobalHotkeys.Control | GlobalHotkeys.Option, GlobalHotkeys.CommandBarModifiers);
    }

    [Fact]
    public void Press_ThrowingWindow_NeverEscapes()
    {
        FakeGlobalHotkey hotkey = new FakeGlobalHotkey();
        using CommandBarHotkey bar = new CommandBarHotkey(hotkey, new ThrowingWindow());

        hotkey.Press();
    }

    private sealed class ThrowingWindow : ICommandBarWindow
    {
        public bool IsOpen => false;

        public void Show() => throw new InvalidOperationException("no window");

        public void Focus() => throw new InvalidOperationException("no window");
    }
}
