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

    /// <summary>The latest snapshot, or <c>null</c> before the first refresh.</summary>
    internal DesktopSnapshot? Snapshot => State.Snapshot;

    public void Dispose()
    {
        State.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized() => State.Changed += OnChanged;

    /// <summary>
    /// Called on the renderer's thread after every state change, right after the re-render was requested. A
    /// component that loads its own data reloads it here (read-only) and then calls <c>StateHasChanged</c>.
    /// </summary>
    protected virtual Task OnStateChangedAsync() => Task.CompletedTask;

    private void OnChanged() => _ = InvokeAsync(Reload);

    private Task Reload()
    {
        StateHasChanged();
        return OnStateChangedAsync();
    }
}
