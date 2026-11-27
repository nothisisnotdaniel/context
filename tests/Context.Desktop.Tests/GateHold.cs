namespace Context.Tests;

/// <summary>
/// Holds a lock (such as <see cref="DesktopFixture.IndexGate"/>) on a thread of its own until <see cref="Dispose"/>,
/// so a test can <c>await</c> while the lock is held (job 491). A <see cref="Monitor"/> is owned by the thread that
/// entered it: entered in an async test and left after an <c>await</c>, the exit may run on another thread, throw, and
/// leave the lock held for good, which hung the run when the fixture's dispose waited for the same lock. The hold
/// gives up after <see cref="Bound"/> even when nobody releases it, and every wait here is bounded and named.
/// </summary>
internal sealed class GateHold : IDisposable
{
    /// <summary>The longest the lock is held, and the longest each step of taking or leaving it may take.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly ManualResetEventSlim entered = new(initialState: false);
    private readonly ManualResetEventSlim release = new(initialState: false);
    private readonly Thread thread;

    private GateHold(object gate)
    {
        thread = new Thread(() =>
        {
            lock (gate)
            {
                entered.Set();
                release.Wait(Bound);
            }
        })
        {
            IsBackground = true,
            Name = nameof(GateHold),
        };
        thread.Start();
    }

    /// <summary>Takes <paramref name="gate"/> on a new thread and returns once it is held.</summary>
    public static GateHold Take(object gate)
    {
        GateHold hold = new GateHold(gate);
        if (!hold.entered.Wait(Bound))
        {
            hold.release.Set();
            throw new TimeoutException($"GateHold: the lock was not taken within {BoundedWait.Describe(Bound)}");
        }

        return hold;
    }

    /// <summary>Releases the lock and waits until its thread has left it.</summary>
    public void Dispose()
    {
        release.Set();
        if (!thread.Join(Bound))
        {
            throw new TimeoutException($"GateHold: the lock was not released within {BoundedWait.Describe(Bound)}");
        }
    }
}
