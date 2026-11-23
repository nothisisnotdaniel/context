namespace Context;

/// <summary>
/// What this start's update check knows so far (job 396), in memory only: whether this copy updates itself
/// (<c>null</c> until it was read, and when the read failed) and the last outcome. Thread-safe; <see cref="Changed"/>
/// is raised after each change, on the thread that made it, and a throwing handler is ignored.
/// </summary>
internal sealed class UpdateStatus
{
    private readonly object gate = new();
    private bool? installed;
    private UpdateOutcome? last;

    /// <summary>Raised after <see cref="Installed"/> or <see cref="Last"/> changed.</summary>
    public event Action? Changed;

    /// <summary>True when this copy was installed by <c>Setup.exe</c>; <c>null</c> while unknown.</summary>
    public bool? Installed
    {
        get
        {
            lock (gate)
            {
                return installed;
            }
        }

        set
        {
            lock (gate)
            {
                installed = value;
            }

            Raise();
        }
    }

    /// <summary>The outcome of this start's check, or <c>null</c> before it ended.</summary>
    public UpdateOutcome? Last
    {
        get
        {
            lock (gate)
            {
                return last;
            }
        }

        set
        {
            lock (gate)
            {
                last = value;
            }

            Raise();
        }
    }

    private void Raise()
    {
        if (Changed is not { } changed)
        {
            return;
        }

        foreach (Delegate listener in changed.GetInvocationList())
        {
            try
            {
                ((Action)listener)();
            }
            catch (Exception)
            {
                // A listener's failure never reaches the update or the other listeners.
            }
        }
    }
}
