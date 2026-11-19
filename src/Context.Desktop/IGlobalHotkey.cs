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
