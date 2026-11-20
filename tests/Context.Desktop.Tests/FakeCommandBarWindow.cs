namespace Context.Tests;

/// <summary>A command bar window that opens nothing (job 282): it counts <see cref="Show"/> and <see cref="Focus"/>.</summary>
internal sealed class FakeCommandBarWindow : ICommandBarWindow
{
    private int shows;
    private int focuses;

    /// <summary>Whether a bar is open; <see cref="Show"/> sets it, tests may clear it (the bar closed).</summary>
    public bool IsOpen { get; set; }

    public int Shows => Volatile.Read(ref shows);

    public int Focuses => Volatile.Read(ref focuses);

    public void Show()
    {
        Interlocked.Increment(ref shows);
        IsOpen = true;
    }

    public void Focus() => Interlocked.Increment(ref focuses);
}
