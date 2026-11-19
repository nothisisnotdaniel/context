namespace Context;

/// <summary>
/// Opens the command bar on each press of its hotkey (job 282, Control+Option+Space on macOS): a press with no bar
/// open shows one, a press while one is open focuses it, so one press never opens a second bar.
/// </summary>
internal sealed class CommandBarHotkey : IDisposable
{
    private readonly IGlobalHotkey hotkey;
    private readonly ICommandBarWindow window;
    private readonly object gate = new object();

    public CommandBarHotkey(IGlobalHotkey hotkey, ICommandBarWindow window)
    {
        this.hotkey = hotkey;
        this.window = window;
        hotkey.Pressed += OnPressed;
    }

    /// <summary>Registers the hotkey; false when the system refused it.</summary>
    public bool Register() => hotkey.Register();

    public void Dispose()
    {
        hotkey.Pressed -= OnPressed;
        hotkey.Dispose();
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        lock (gate)
        {
            try
            {
                if (window.IsOpen)
                {
                    window.Focus();
                }
                else
                {
                    window.Show();
                }
            }
            catch (Exception)
            {
                // Never let an exception reach the system's callback.
            }
        }
    }
}
