namespace Context;

/// <summary>
/// A system-wide hotkey (job 260: Control+Option+Shift+S on macOS). <c>Program.cs</c> passes the real one on macOS
/// only; tests use a fake with a <c>Press()</c> method, so no test registers a real hotkey.
/// </summary>
internal interface IGlobalHotkey : IDisposable
{
    /// <summary>Raised each time the hotkey is pressed, on whatever thread the system calls back on.</summary>
    public event EventHandler? Pressed;

    /// <summary>Registers the hotkey; false when the system refused it (for example another app holds it).</summary>
    public bool Register();
}

/// <summary>
/// The hotkeys Context registers (jobs 260 and 282), as Carbon key codes, modifier bits and ids. Each hotkey has its
/// own id, so its handler raises <see cref="IGlobalHotkey.Pressed"/> only for its own presses.
/// </summary>
internal static class GlobalHotkeys
{
    /// <summary>kVK_ANSI_S.</summary>
    public const uint KeyS = 0x01;

    /// <summary>kVK_Space.</summary>
    public const uint KeySpace = 0x31;

    /// <summary>Carbon's controlKey, optionKey and shiftKey bits.</summary>
    public const uint Control = 0x1000;
    public const uint Option = 0x0800;
    public const uint Shift = 0x0200;

    /// <summary>Control+Option+Shift+S: a screen area into the inbox.</summary>
    public const uint ScreenshotModifiers = Control | Option | Shift;
    public const uint ScreenshotId = 1;

    /// <summary>Control+Option+Space: the command bar.</summary>
    public const uint CommandBarModifiers = Control | Option;
    public const uint CommandBarId = 2;
}
