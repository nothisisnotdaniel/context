namespace Context;

/// <summary>
/// Runs a screenshot capture each time the hotkey is pressed (job 260). A press while a capture is still running (the
/// area picker is open, or the image is being stored) is ignored, so one press is one capture at most. Exceptions of
/// the capture are swallowed; the capture itself notifies.
/// </summary>
internal sealed class ScreenshotHotkey : IDisposable
{
    private readonly IGlobalHotkey hotkey;
    private readonly Func<Task> capture;
    private int running;
    private int captures;

    public ScreenshotHotkey(IGlobalHotkey hotkey, Func<Task> capture)
    {
        this.hotkey = hotkey;
        this.capture = capture;
        hotkey.Pressed += OnPressed;
    }

    /// <summary>How many captures have finished (for tests to wait on).</summary>
    public int Captures => Volatile.Read(ref captures);

    /// <summary>Registers the hotkey; false when the system refused it.</summary>
    public bool Register() => hotkey.Register();

    public void Dispose()
    {
        hotkey.Pressed -= OnPressed;
        hotkey.Dispose();
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await capture().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The capture notifies on its own; a throwing one is skipped.
            }
            finally
            {
                Volatile.Write(ref running, 0);
                Interlocked.Increment(ref captures);
            }
        });
    }
}
