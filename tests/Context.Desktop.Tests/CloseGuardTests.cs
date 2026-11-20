namespace Context.Tests;

// 203 AC-9: the window close guard with a fake close action and a fake save-first result; no window, no page.
public sealed class CloseGuardTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private int closes;

    // Nothing attached, nothing unsaved: the close goes on and the close action is not needed.
    [Fact]
    public void Closing_NothingUnsaved_Proceeds()
    {
        CloseGuard guard = new CloseGuard(() => closes++);
        Assert.False(guard.Closing());

        int saves = 0;
        guard.Attach(() => false, () => { saves++; return Task.FromResult(true); }, () => Task.CompletedTask);

        Assert.False(guard.Closing());
        Assert.Equal((0, 0), (saves, closes));
    }

    // Unsaved changes: the close is cancelled, the save runs, and on success the close action is called once.
    [Fact]
    public async Task Closing_Unsaved_SaveSucceeds_ClosesOnce()
    {
        TaskCompletionSource<bool> save = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int saves = 0;
        int asks = 0;
        CloseGuard guard = new CloseGuard(() =>
        {
            Interlocked.Increment(ref closes);
            closed.TrySetResult();
        });
        guard.Attach(() => true, () => { saves++; return save.Task; }, () => { asks++; return Task.CompletedTask; });

        Assert.True(guard.Closing());
        Assert.True(guard.Closing());
        Assert.Equal(1, saves);
        Assert.Equal(0, closes);
        save.SetResult(true);
        await closed.Task.WaitAsync(Wait);

        Assert.Equal((1, 0), (closes, asks));
        Assert.False(guard.Closing());
        Assert.Equal(1, closes);
    }

    // A failed save asks; Discard closes once, and nothing closes without it.
    [Fact]
    public async Task Closing_Unsaved_SaveFails_AsksThenDiscardCloses()
    {
        TaskCompletionSource asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CloseGuard guard = new CloseGuard(() => Interlocked.Increment(ref closes));
        guard.Attach(() => true, () => Task.FromResult(false), () =>
        {
            asked.TrySetResult();
            return Task.CompletedTask;
        });

        Assert.True(guard.Closing());
        await asked.Task.WaitAsync(Wait);
        Assert.Equal(0, closes);

        guard.Discard();
        guard.Discard();

        Assert.Equal(1, closes);
        Assert.False(guard.Closing());
    }

    // A save that throws leaves the window open; a later close asks again.
    [Fact]
    public async Task Closing_SaveThrows_StaysOpen_NextCloseTriesAgain()
    {
        int saves = 0;
        TaskCompletionSource<bool> second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CloseGuard guard = new CloseGuard(() => Interlocked.Increment(ref closes));
        guard.Attach(
            () => true,
            () => ++saves == 1 ? Task.FromException<bool>(new InvalidOperationException("page gone")) : second.Task,
            () => Task.CompletedTask);

        Assert.True(guard.Closing());
        Assert.True(guard.Closing());

        Assert.Equal(2, saves);
        Assert.Equal(0, closes);
        second.SetResult(false);
    }

    // After Detach (the page is gone) a close goes on.
    [Fact]
    public void Detach_ThenClosing_Proceeds()
    {
        CloseGuard guard = new CloseGuard(() => closes++);
        guard.Attach(() => true, () => Task.FromResult(true), () => Task.CompletedTask);

        guard.Detach();

        Assert.False(guard.Closing());
        Assert.Equal(0, closes);
    }
}
