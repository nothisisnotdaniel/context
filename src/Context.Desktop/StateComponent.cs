using Microsoft.AspNetCore.Components;

namespace Context;

/// <summary>
/// Base of the desktop components: injects the <see cref="DesktopState"/> and <see cref="DesktopActions"/> (internal
/// types can't be public component parameters) and re-renders whenever the state changes. Components call
/// <see cref="Actions"/> only from event handlers.
/// </summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject]
    internal DesktopState State { get; set; } = null!;

    /// <summary>The only way to change data; call it only from an event handler.</summary>
    [Inject]
    internal DesktopActions Actions { get; set; } = null!;

    /// <summary>The window's status bar (job 455): every feedback message goes there.</summary>
    [Inject]
    internal StatusBar Bar { get; set; } = null!;

    /// <summary>The latest snapshot, or <c>null</c> before the first refresh.</summary>
    internal DesktopSnapshot? Snapshot => State.Snapshot;

    public void Dispose()
    {
        State.Changed -= OnChanged;
        OnDisposing();
        GC.SuppressFinalize(this);
    }

    /// <summary>Called once from <see cref="Dispose"/>; a component that subscribed to more state events unsubscribes here.</summary>
    protected virtual void OnDisposing()
    {
    }

    protected override void OnInitialized() => State.Changed += OnChanged;

    /// <summary>
    /// Called on the renderer's thread after every state change, right after the re-render was requested. A
    /// component that loads its own data reloads it here (read-only) and then calls <c>StateHasChanged</c>.
    /// </summary>
    protected virtual Task OnStateChangedAsync() => Task.CompletedTask;

    // Counts the reload on the state before it is dispatched; it is ended exactly once, whether the reload runs (its
    // finally), the dispatch throws, or the dispatched task ends without running it.
    private void OnChanged()
    {
        State.BeginReload();
        ReloadEnd end = new(State);
        Task dispatched;
        try
        {
            dispatched = InvokeAsync(() => Reload(end));
        }
        catch
        {
            end.Run();
            throw;
        }

        _ = dispatched.ContinueWith(
            static (_, e) => ((ReloadEnd)e!).Run(),
            end,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task Reload(ReloadEnd end)
    {
        try
        {
            StateHasChanged();
            await OnStateChangedAsync();
        }
        finally
        {
            end.Run();
        }
    }

    // Ends one reload's count on the state at most once.
    private sealed class ReloadEnd(DesktopState state)
    {
        private int ended;

        public void Run()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0)
            {
                state.EndReload();
            }
        }
    }
}
