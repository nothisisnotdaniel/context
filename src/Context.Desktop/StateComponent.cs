using Microsoft.AspNetCore.Components;

namespace Context;

/// <summary>
/// Base of the desktop components: injects the <see cref="DesktopState"/> (internal Core types can't be public
/// component parameters) and re-renders whenever it changes.
/// </summary>
public abstract class StateComponent : ComponentBase, IDisposable
{
    [Inject]
    internal DesktopState State { get; set; } = null!;

    /// <summary>The latest snapshot, or <c>null</c> before the first refresh.</summary>
    internal DesktopSnapshot? Snapshot => State.Snapshot;

    public void Dispose()
    {
        State.Changed -= OnChanged;
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized() => State.Changed += OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
