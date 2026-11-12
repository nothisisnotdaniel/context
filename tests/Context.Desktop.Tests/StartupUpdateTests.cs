namespace Context.Tests;

/// <summary>
/// The silent start-up update, against <see cref="FakeUpdateSource"/> only: no network, no installer. Every flow also
/// proves, with <see cref="FileSnapshot"/>, that the data folder is byte- and time-identical afterwards.
/// </summary>
public sealed class StartupUpdateTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly string root;
    private readonly SortedDictionary<string, string> before;

    public StartupUpdateTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "calendar"));
        File.WriteAllText(Path.Combine(root, "inbox.md"), "# inbox\n- 2026-10-29 09:00 call #work\n");
        File.WriteAllText(Path.Combine(root, "todos.json"), "[]");
        File.WriteAllText(Path.Combine(root, "calendar-link.txt"), "https://example.invalid/calendar.ics\n");
        File.WriteAllText(Path.Combine(root, "calendar", "team.ics"), "BEGIN:VCALENDAR\nEND:VCALENDAR\n");
        File.WriteAllBytes(Path.Combine(root, "context.db"), [1, 2, 3]);
        before = FileSnapshot.Take(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    // AC-7: Start returns while the check's task is still open, and the rest runs once the check answers.
    [Fact]
    public async Task Start_ReturnsBeforeTheCheckFinishes()
    {
        FakeUpdateSource source = new FakeUpdateSource
        {
            Installed = true,
            HeldCheck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        Task run = new StartupUpdate(source).Start();
        await source.CheckEntered.Task.WaitAsync(Limit);

        Assert.False(run.IsCompleted, "Start must not wait for the check");
        Assert.Equal(["check"], source.Calls);

        source.HeldCheck.SetResult(true);
        await run.WaitAsync(Limit);
        Assert.Equal(["check", "download", "apply"], source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-7: even a check that blocks its thread does not hold up Start (the caller is the UI thread).
    [Fact]
    public async Task Start_ReturnsEvenWhenTheCheckBlocksItsThread()
    {
        using ManualResetEventSlim gate = new ManualResetEventSlim(false);
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, BlockedCheck = gate };

        Task run = new StartupUpdate(source).Start();
        await source.CheckEntered.Task.WaitAsync(Limit);

        Assert.False(run.IsCompleted, "Start must not wait for the check");
        gate.Set();
        await run.WaitAsync(Limit);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(["check"], source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-8: a copy that was not installed never asks, downloads or applies.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotInstalled_CallsNothing(bool newer)
    {
        FakeUpdateSource source = new FakeUpdateSource { Installed = false, Newer = newer };

        Task run = new StartupUpdate(source).Start();
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(1, source.InstalledReads);
        Assert.Empty(source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-9: a newer version is downloaded once, then applied on exit once, in that order.
    [Fact]
    public async Task InstalledAndNewer_DownloadsOnce_ThenAppliesOnExitOnce()
    {
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, Newer = true };

        Task run = new StartupUpdate(source).Start();
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(["check", "download", "apply"], source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-9: the only way to apply is "after exit, without a restart": the source has no restart member at all.
    [Fact]
    public void UpdateSource_HasNoRestartMember()
    {
        string[] members = [.. typeof(IUpdateSource).GetMembers().Select(member => member.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["ApplyOnExit", "CheckAsync", "DownloadAsync", "IsInstalled", "get_IsInstalled"], members);
        Assert.Empty(typeof(IUpdateSource).GetMethod("ApplyOnExit")!.GetParameters());
        Assert.DoesNotContain(
            typeof(StartupUpdate).GetMembers(),
            member => member.Name.Contains("Restart", StringComparison.OrdinalIgnoreCase));
    }

    // AC-9: nothing newer means no download and no apply.
    [Fact]
    public async Task InstalledAndNothingNewer_DownloadsAndAppliesNothing()
    {
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, Newer = false };

        Task run = new StartupUpdate(source).Start();
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(["check"], source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-7, AC-10: an exception or a cancellation, thrown by the call or out of its task, is swallowed: Start does not
    // throw, its task does not fail, and nothing after the failing step runs (never an apply after a failed download).
    [Theory]
    [InlineData("installed", "io", false, "")]
    [InlineData("check", "io", false, "check")]
    [InlineData("check", "io", true, "check")]
    [InlineData("check", "http", true, "check")]
    [InlineData("check", "cancelled", false, "check")]
    [InlineData("check", "cancelled", true, "check")]
    [InlineData("check", "invalid", true, "check")]
    [InlineData("download", "io", false, "check,download")]
    [InlineData("download", "io", true, "check,download")]
    [InlineData("download", "http", true, "check,download")]
    [InlineData("download", "cancelled", false, "check,download")]
    [InlineData("download", "cancelled", true, "check,download")]
    [InlineData("apply", "invalid", false, "check,download,apply")]
    public async Task Failure_IsSwallowed_AndNothingFurtherRuns(string step, string kind, bool inTask, string calls)
    {
        Exception exception = kind switch
        {
            "io" => new IOException("disk"),
            "http" => new HttpRequestException("offline"),
            "cancelled" => new OperationCanceledException("cancelled"),
            _ => new InvalidOperationException("broken"),
        };
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, Newer = true, ThrowInTask = inTask };
        switch (step)
        {
            case "installed":
                source.InstalledThrows = exception;
                break;
            case "check":
                source.CheckThrows = exception;
                break;
            case "download":
                source.DownloadThrows = exception;
                break;
            default:
                source.ApplyThrows = exception;
                break;
        }

        // A throw from Start itself would fail the test right here.
        Task run = new StartupUpdate(source).Start();
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully, "the update task must never fail or be cancelled");
        Assert.Equal(calls.Length == 0 ? [] : calls.Split(','), source.Calls);
        AssertDataFolderUnchanged();
    }

    // AC-11: only a SemVer version with a prerelease label follows the prereleases.
    [Theory]
    [InlineData("0.3.1-develop.12", true)]
    [InlineData("1.2.4-develop.7", true)]
    [InlineData("0.3.1-develop.12+0123abc", true)]
    [InlineData("1.0.0-beta", true)]
    [InlineData("1.0.0-rc-1.x", true)]
    [InlineData("0.3.0", false)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0+0123abc", false)]
    [InlineData("1.0.0+build-7", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData("-develop.1", false)]
    [InlineData("1.0-develop.1", false)]
    [InlineData("1.0.0-", false)]
    [InlineData("1.0.0-develop..1", false)]
    [InlineData("v1.0.0-develop.1", false)]
    [InlineData("1.0.0-develop.1\n", false)]
    [InlineData("1.0.0-béta", false)]
    public void IncludePrereleases_OnlyForAPrereleaseLabel(string? version, bool expected)
    {
        Assert.Equal(expected, StartupUpdate.IncludePrereleases(version));
    }

    // AC-11: the version comes from the desktop assembly; a plain local build is no prerelease.
    [Fact]
    public void CurrentVersion_IsTheDesktopAssemblysVersion()
    {
        string version = StartupUpdate.CurrentVersion;

        Assert.Matches(@"^\d+\.\d+\.\d+", version);
        Assert.Equal(version.Split('+')[0].Contains('-', StringComparison.Ordinal), StartupUpdate.IncludePrereleases(version));
    }

    private void AssertDataFolderUnchanged()
    {
        Assert.Equal(before, FileSnapshot.Take(root));
    }
}
