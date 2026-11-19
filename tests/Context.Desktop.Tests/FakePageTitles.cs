namespace Context.Tests;

/// <summary>
/// A page-title source in memory (job 244): it records every URL it is asked for and answers <see cref="Title"/> at
/// once, or, with <see cref="Hold"/>, waits until <see cref="Answer"/> or a cancelled token. <see cref="Asked"/>
/// completes at the first call and <see cref="SawCancel"/> once a call's token was cancelled. It never touches the
/// network.
/// </summary>
internal sealed class FakePageTitles : IPageTitleSource
{
    private readonly object gate = new();
    private readonly List<string> urls = [];
    private readonly TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What a call answers when not held.</summary>
    public string? Title { get; set; } = "Example page";

    /// <summary>When true, a call waits for <see cref="Answer"/> or its token.</summary>
    public bool Hold { get; set; }

    public int Calls
    {
        get
        {
            lock (gate)
            {
                return urls.Count;
            }
        }
    }

    public IReadOnlyList<string> Urls
    {
        get
        {
            lock (gate)
            {
                return [.. urls];
            }
        }
    }

    public Task Asked => asked.Task;

    public Task SawCancel => cancelled.Task;

    /// <summary>Lets every held call answer <paramref name="title"/>.</summary>
    public void Answer(string? title) => answer.TrySetResult(title);

    public async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            urls.Add(url);
        }

        cancellationToken.Register(() => cancelled.TrySetResult());
        asked.TrySetResult();
        if (!Hold)
        {
            return Title;
        }

        try
        {
            return await answer.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
