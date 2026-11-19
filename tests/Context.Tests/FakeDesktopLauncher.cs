namespace Context.Tests;

/// <summary>An <see cref="IDesktopLauncher"/> that only counts calls and can fail (job 280); it starts no app.</summary>
internal sealed class FakeDesktopLauncher : IDesktopLauncher
{
    private int calls;

    /// <summary>When true <see cref="Launch"/> throws, as when the app is missing or the OS is not supported.</summary>
    public bool Fails { get; set; }

    /// <summary>How often <see cref="Launch"/> was called.</summary>
    public int Calls => Volatile.Read(ref calls);

    public void Launch()
    {
        Interlocked.Increment(ref calls);
        if (Fails)
        {
            throw new PlatformNotSupportedException("Not on this OS.");
        }
    }
}
