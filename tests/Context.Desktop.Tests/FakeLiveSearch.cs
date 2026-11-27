namespace Context.Tests;

/// <summary>
/// The top bar's live search (job 492) without an index: it records each searched text and returns a result that the
/// test completes through <see cref="Complete"/>. Continuations run inline, so a completed call posts its rendering
/// to the renderer before <see cref="Complete"/> returns.
/// </summary>
internal sealed class FakeLiveSearch
{
    private readonly object gate = new();
    private readonly List<string> texts = [];
    private readonly List<TaskCompletionSource<SearchView>> results = [];
    private readonly SemaphoreSlim called = new(0);

    public FakeLiveSearch() => Search = new LiveSearch(Run);

    /// <summary>The parameter to hand to the top bar.</summary>
    public LiveSearch Search { get; }

    /// <summary>Every searched text, in call order.</summary>
    public IReadOnlyList<string> Texts
    {
        get
        {
            lock (gate)
            {
                return texts.ToList();
            }
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> calls were made, at most <paramref name="bound"/>.</summary>
    public async Task WaitForCalls(int count, TimeSpan bound)
    {
        while (Texts.Count < count)
        {
            Assert.True(await called.WaitAsync(bound), $"FakeLiveSearch: no call {count} within {bound}");
        }
    }

    /// <summary>Completes call <paramref name="index"/> (0-based) with <paramref name="view"/>.</summary>
    public void Complete(int index, SearchView view)
    {
        TaskCompletionSource<SearchView> result;
        lock (gate)
        {
            result = results[index];
        }

        result.SetResult(view);
    }

    private Task<SearchView> Run(string text)
    {
        TaskCompletionSource<SearchView> result = new TaskCompletionSource<SearchView>();
        lock (gate)
        {
            texts.Add(text);
            results.Add(result);
        }

        called.Release();
        return result.Task;
    }
}
