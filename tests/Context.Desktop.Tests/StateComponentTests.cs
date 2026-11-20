using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Context.Tests;

// 221: every Changed handler's reload is counted on the state, so a test can await DesktopState.ReloadsSettled()
// before it compares render counts.
public sealed class StateComponentTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    // AC-1: a fresh state has no reload running, and none is left once every reload has finished.
    [Fact]
    public async Task ReloadsSettled_NoReloadRunning_IsCompleted()
    {
        DesktopState state = data.CreateState();
        Assert.True(state.ReloadsSettled().IsCompleted);

        IRenderedComponent<Loader> cut = await data.RenderAsync<Loader>();
        await data.State!.Refresh();
        await data.State.ReloadsSettled().WaitAsync(Wait);

        Assert.True(data.State.ReloadsSettled().IsCompleted);
        Assert.Equal(1, cut.Instance.Loads);
    }

    // AC-2: while the component's OnStateChangedAsync awaits, the reloads are not settled; they settle once it
    // completes and the component has rendered.
    [Fact]
    public async Task ReloadsSettled_LoadHeld_CompletesAfterLoadAndRender()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await data.RefreshAsync();
        IRenderedComponent<Loader> cut = data.Bunit.Render<Loader>(p => p.Add(c => c.Load, () => held.Task));

        await data.State!.Refresh();
        Task settled = data.State.ReloadsSettled();

        Assert.False(settled.IsCompleted);
        held.SetResult();
        await settled.WaitAsync(Wait);
        Assert.Equal(1, cut.Instance.Loaded);
        Assert.Equal("loaded 1", cut.Find("p").TextContent);
        Assert.True(data.State.ReloadsSettled().IsCompleted);
    }

    // AC-3: a reload whose OnStateChangedAsync throws still ends its count.
    [Fact]
    public async Task ReloadsSettled_LoadThrows_StillCompletes()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await data.RefreshAsync();
        IRenderedComponent<Loader> cut = data.Bunit.Render<Loader>(p => p.Add(c => c.Load, () => held.Task));

        await data.State!.Refresh();
        Task settled = data.State.ReloadsSettled();

        Assert.False(settled.IsCompleted);
        held.SetException(new InvalidOperationException("load failed"));
        await settled.WaitAsync(Wait);
        Assert.Equal(1, cut.Instance.Loads);
        Assert.Equal(0, cut.Instance.Loaded);
        Assert.True(data.State.ReloadsSettled().IsCompleted);
    }

    // AC-4: the count goes up in OnChanged, before the reload is dispatched: with the renderer busy, the reload is
    // still queued when Refresh() completes, and the reloads are not settled until it has run.
    [Fact]
    public async Task ReloadsSettled_ReloadStillQueued_IsNotCompleted()
    {
        await data.RefreshAsync();
        IRenderedComponent<Loader> cut = data.Bunit.Render<Loader>();
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        Task busy = Task.Run(() => cut.InvokeAsync(() =>
        {
            entered.Set();
            release.Wait(Wait);
        }));
        Assert.True(entered.Wait(Wait));

        await data.State!.Refresh();
        Task settled = data.State.ReloadsSettled();

        Assert.Equal(0, cut.Instance.Loads);
        Assert.False(settled.IsCompleted);
        release.Set();
        await busy.WaitAsync(Wait);
        await settled.WaitAsync(Wait);
        Assert.Equal(1, cut.Instance.Loads);
    }

    // Counts its reloads; each one awaits Load (completed when not set) and then renders the count.
    internal sealed class Loader : StateComponent
    {
        private int loads;
        private int loaded;

        [Parameter]
        public Func<Task>? Load { get; set; }

        public int Loads => Volatile.Read(ref loads);

        public int Loaded => Volatile.Read(ref loaded);

        protected override async Task OnStateChangedAsync()
        {
            Interlocked.Increment(ref loads);
            if (Load is not null)
            {
                await Load();
            }

            Interlocked.Increment(ref loaded);
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, "loaded " + Loaded);
            builder.CloseElement();
        }
    }
}
