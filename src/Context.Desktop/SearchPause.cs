namespace Context;

/// <summary>
/// The pause after the last keystroke in the top bar's Search popover before it searches as you type (job 492). Tests
/// replace it with a pause that ends only when they say so.
/// </summary>
internal interface ISearchPause
{
    /// <summary>Completes when the pause has elapsed; cancelled through <paramref name="cancel"/>.</summary>
    public Task WaitAsync(CancellationToken cancel);
}

/// <summary>The real pause: <see cref="Pause"/> on the clock.</summary>
internal sealed class SearchPause : ISearchPause
{
    /// <summary>How long the popover waits after the last keystroke before it searches (250 ms).</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    public Task WaitAsync(CancellationToken cancel) => Task.Delay(Pause, cancel);
}
