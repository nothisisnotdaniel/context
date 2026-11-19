using System.Diagnostics;

namespace Context.Tests;

public sealed class GitCommitLogTests : IDisposable
{
    private readonly string parent;
    private readonly string repo;

    public GitCommitLogTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-gitlog-" + Guid.NewGuid().ToString("N"));
        repo = Path.Combine(parent, "repo");
        Directory.CreateDirectory(repo);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            foreach (string file in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(parent, recursive: true);
        }
    }

    // Runs git in the test repo with a fixed identity and dates, without the user's or system's config.
    private void Git(string date, params string[] arguments)
    {
        string config = Path.Combine(parent, "gitconfig");
        if (!File.Exists(config))
        {
            File.WriteAllText(config, string.Empty);
        }

        ProcessStartInfo start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repo,
        };
        foreach (string argument in (string[])["-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "-c", "commit.gpgsign=false", .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = config;
        start.Environment["GIT_COMMITTER_DATE"] = date;
        start.Environment["GIT_AUTHOR_DATE"] = date;
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error.Result);
    }

    private void Commit(string date, string message) =>
        Git(date, "commit", "--allow-empty", "-q", "-m", message);

    // 280 AC-6: a real repository with "Fix #1234", "Fix #12345" and "Other" gives exactly the first.
    [Fact]
    public void Read_RealRepo_OnlyTheMentioningCommit()
    {
        Git("2026-11-01T10:00:00+00:00", "init", "-q");
        Commit("2026-11-02T10:00:00+00:00", "Fix #1234");
        Commit("2026-11-03T10:00:00+00:00", "Fix #12345");
        Commit("2026-11-04T10:00:00+00:00", "Other");

        IReadOnlyList<LoggedCommit> commits = new GitCommitLog().Read(repo, 1234);

        LoggedCommit only = Assert.Single(commits);
        Assert.Equal("Fix #1234", only.Subject);
        Assert.Equal(new DateOnly(2026, 11, 2), only.Date);
        Assert.Equal(40, only.Hash.Length);
        Assert.All(only.Hash, c => Assert.True(Uri.IsHexDigit(c)));
    }

    // A mention in the body counts, newest first; a folder that is no repository throws.
    [Fact]
    public void Read_BodyMention_NewestFirst_NoRepoThrows()
    {
        Git("2026-11-01T10:00:00+00:00", "init", "-q");
        Commit("2026-11-02T10:00:00+00:00", "Start\n\nFor #1234.");
        Commit("2026-11-05T10:00:00+00:00", "Finish #1234");

        Assert.Equal(["Finish #1234", "Start"], new GitCommitLog().Read(repo, 1234).Select(c => c.Subject));

        string plain = Path.Combine(parent, "plain");
        Directory.CreateDirectory(plain);
        Assert.Throws<CommitLogException>(() => new GitCommitLog().Read(plain, 1234));
    }

    // 280 AC-6: the fixed argument list.
    [Fact]
    public void Arguments_AreTheFixedList()
    {
        Assert.Equal(
            ["--no-pager", "-c", "core.fsmonitor=false", "-C", "/x y/repo", "log", "--all", "--no-color", "-F",
                "--grep=#1234", "-n", "500", "--format=%H%x1f%cs%x1f%s%x1f%b%x1e"],
            GitCommitLog.Arguments("/x y/repo", 1234));
    }

    // 280 AC-5: a missing git is GitMissingException.
    [Fact]
    public void Read_MissingExecutable_IsGitMissing()
    {
        GitCommitLog log = new GitCommitLog("context-no-such-git-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<GitMissingException>(() => log.Read(repo, 1234));
    }

    // 280 AC-5: a message with #12345 only is dropped; only whole records count; at most 100 per repository.
    [Fact]
    public void Parse_RechecksTheMessage_WholeRecords_Capped()
    {
        string output = "h1\u001f2026-11-02\u001fFix #1234\u001f\u001e\n"
            + "h2\u001f2026-11-01\u001fFix #12345\u001f\u001e\n"
            + "h3\u001f2026-10-31\u001fOther\u001fsee `#1234`\u001e\n"
            + "h4\u001fnot a date\u001fFix #1234\u001f\u001e\n"
            + "h5\u001f2026-10-30\u001fCut #1234";

        Assert.Equal(["h1"], GitCommitLog.Parse(output, 1234).Select(c => c.Hash));

        string many = string.Concat(Enumerable.Range(0, 150).Select(i => $"h{i}\u001f2026-11-02\u001fFix #1234\u001f\u001e\n"));
        Assert.Equal(GitCommitLog.MaxCommitsPerRepo, GitCommitLog.Parse(many, 1234).Count);
        Assert.Equal(100, GitCommitLog.MaxCommitsPerRepo);
    }
}
