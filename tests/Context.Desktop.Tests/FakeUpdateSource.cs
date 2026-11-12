namespace Context.Tests;

/// <summary>
/// An <see cref="IUpdateSource"/> that only records what was called, in order. Tests set what it answers, what it
/// throws and whether the check stays open; it never touches the network or a file.
/// </summary>
internal sealed class FakeUpdateSource : IUpdateSource
{
    private readonly List<string> calls = [];

    /// <summary>What <see cref="IsInstalled"/> answers.</summary>
    public bool Installed { get; set; }

    /// <summary>What the check answers: is there a newer version?</summary>
    public bool Newer { get; set; }

    public Exception? InstalledThrows { get; set; }

    public Exception? CheckThrows { get; set; }

    public Exception? DownloadThrows { get; set; }

    public Exception? ApplyThrows { get; set; }

    /// <summary>True: the failure comes out of the returned task; false: the call itself throws.</summary>
    public bool ThrowInTask { get; set; }

    /// <summary>When set, the check's task stays open until this is completed (with the answer).</summary>
    public TaskCompletionSource<bool>? HeldCheck { get; set; }

    /// <summary>When set, the check blocks its thread (not a task) until this is set.</summary>
    public ManualResetEventSlim? BlockedCheck { get; set; }

    /// <summary>Completed when the check was entered.</summary>
    public TaskCompletionSource CheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How often <see cref="IsInstalled"/> was read.</summary>
    public int InstalledReads { get; private set; }

    /// <summary>The calls so far: <c>check</c>, <c>download</c>, <c>apply</c>.</summary>
    public List<string> Calls
    {
        get
        {
            lock (calls)
            {
                return [.. calls];
            }
        }
    }

    public bool IsInstalled
    {
        get
        {
            InstalledReads++;
            if (InstalledThrows is not null)
            {
                throw InstalledThrows;
            }

            return Installed;
        }
    }

    public Task<bool> CheckAsync()
    {
        Record("check");
        CheckEntered.TrySetResult();
        BlockedCheck?.Wait(TimeSpan.FromSeconds(30));
        if (CheckThrows is not null)
        {
            return Fail<bool>(CheckThrows);
        }

        return HeldCheck is not null ? HeldCheck.Task : Task.FromResult(Newer);
    }

    public Task DownloadAsync()
    {
        Record("download");
        return DownloadThrows is not null ? Fail<bool>(DownloadThrows) : Task.CompletedTask;
    }

    public void ApplyOnExit()
    {
        Record("apply");
        if (ApplyThrows is not null)
        {
            throw ApplyThrows;
        }
    }

    private Task<T> Fail<T>(Exception exception)
    {
        if (!ThrowInTask)
        {
            throw exception;
        }

        return exception is OperationCanceledException
            ? Task.FromCanceled<T>(new CancellationToken(canceled: true))
            : Task.FromException<T>(exception);
    }

    private void Record(string call)
    {
        lock (calls)
        {
            calls.Add(call);
        }
    }
}
