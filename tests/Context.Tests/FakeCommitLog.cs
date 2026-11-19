namespace Context.Tests;

/// <summary>
/// An <see cref="ICommitLog"/> in memory (job 280): answers the commits set per folder, records every call, and can
/// throw per folder or report that git is missing. It never runs git.
/// </summary>
internal sealed class FakeCommitLog : ICommitLog
{
    private readonly object gate = new();
    private readonly Dictionary<string, IReadOnlyList<LoggedCommit>> commits = new(StringComparer.Ordinal);
    private readonly HashSet<string> failing = new(StringComparer.Ordinal);
    private readonly List<(string Folder, int Item)> calls = [];

    /// <summary>When true every call throws <see cref="GitMissingException"/>.</summary>
    public bool GitMissing { get; set; }

    /// <summary>The calls so far, in order.</summary>
    public IReadOnlyList<(string Folder, int Item)> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    /// <summary>Sets what <paramref name="folder"/> answers.</summary>
    public void Set(string folder, params LoggedCommit[] logged)
    {
        lock (gate)
        {
            commits[folder] = logged;
        }
    }

    /// <summary>Makes every call for <paramref name="folder"/> throw <see cref="CommitLogException"/>.</summary>
    public void Fail(string folder)
    {
        lock (gate)
        {
            failing.Add(folder);
        }
    }

    public IReadOnlyList<LoggedCommit> Read(string folder, int item)
    {
        lock (gate)
        {
            calls.Add((folder, item));
            if (GitMissing)
            {
                throw new GitMissingException("git did not start.");
            }

            if (failing.Contains(folder))
            {
                throw new CommitLogException("git failed.");
            }

            return commits.TryGetValue(folder, out IReadOnlyList<LoggedCommit>? found) ? found : [];
        }
    }
}
