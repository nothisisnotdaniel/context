namespace Context.Tests;

/// <summary>A Times runner that starts nothing (job 282): it records each argument list and returns a set result.</summary>
internal sealed class FakeTimesRunner : ITimesRunner
{
    private readonly object gate = new object();
    private readonly List<IReadOnlyList<string>> calls = [];

    /// <summary>The result every run returns.</summary>
    public TimesRunResult Result { get; set; } = new TimesRunResult(TimesRunKind.Done, 0, string.Empty, string.Empty);

    /// <summary>When set, a run returns only once this task completes (then <see cref="Result"/>).</summary>
    public Task? Hold { get; set; }

    /// <summary>When set, a run throws this.</summary>
    public Exception? Throws { get; set; }

    /// <summary>The argument lists, in run order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Calls
    {
        get
        {
            lock (gate)
            {
                return calls.ToList();
            }
        }
    }

    public async Task<TimesRunResult> Run(IReadOnlyList<string> arguments)
    {
        lock (gate)
        {
            calls.Add(arguments.ToList());
        }

        if (Throws is not null)
        {
            throw Throws;
        }

        if (Hold is not null)
        {
            await Hold.ConfigureAwait(false);
        }

        return Result;
    }
}
