namespace Context;

/// <summary>
/// The pause after the last edit before the note editor saves on its own. Tests replace it with a pause that ends
/// only when they say so.
/// </summary>
internal interface IAutoSavePause
{
    /// <summary>Completes when the pause has elapsed; cancelled through <paramref name="cancel"/>.</summary>
    public Task WaitAsync(CancellationToken cancel);
}

/// <summary>The real pause: <see cref="Pause"/> on the clock.</summary>
internal sealed class AutoSavePause : IAutoSavePause
{
    /// <summary>How long the editor waits after the last edit before it saves (1 second).</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);

    public Task WaitAsync(CancellationToken cancel) => Task.Delay(Pause, cancel);
}
