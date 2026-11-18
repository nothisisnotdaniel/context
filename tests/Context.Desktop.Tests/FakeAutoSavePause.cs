namespace Context.Tests;

/// <summary>
/// The editor's pause after the last edit, without a clock: every wait ends only when the test calls
/// <see cref="Elapse"/>, or when the editor cancels it.
/// </summary>
internal sealed class FakeAutoSavePause : IAutoSavePause
{
    private readonly object gate = new();
    private readonly List<TaskCompletionSource> waits = [];

    /// <summary>How many waits were started so far.</summary>
    public int Started
    {
        get
        {
            lock (gate)
            {
                return started;
            }
        }
    }

    /// <summary>How many waits are neither elapsed nor cancelled.</summary>
    public int Pending
    {
        get
        {
            lock (gate)
            {
                return waits.Count(w => !w.Task.IsCompleted);
            }
        }
    }

    private int started;

    public Task WaitAsync(CancellationToken cancel)
    {
        TaskCompletionSource wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            waits.Add(wait);
            started++;
        }

        cancel.Register(() => wait.TrySetCanceled(cancel));
        return wait.Task;
    }

    /// <summary>Ends every pending wait as elapsed.</summary>
    public void Elapse()
    {
        List<TaskCompletionSource> due;
        lock (gate)
        {
            due = waits.Where(w => !w.Task.IsCompleted).ToList();
            waits.Clear();
        }

        foreach (TaskCompletionSource wait in due)
        {
            wait.TrySetResult();
        }
    }
}
