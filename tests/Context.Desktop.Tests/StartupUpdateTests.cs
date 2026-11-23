namespace Context.Tests;

/// <summary>
/// The silent start-up update, against <see cref="FakeUpdateSource"/> only: no network, no installer. Every flow also
/// proves, with <see cref="FileSnapshot"/>, that the data folder is byte- and time-identical afterwards.
/// </summary>
public sealed class StartupUpdateTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    // Job 396: a fixed fake clock; nothing here reads the real time.
    private static readonly DateTime Now = new(2026, 11, 23, 8, 15, 30, DateTimeKind.Utc);

    private readonly UpdateStatus status = new();
    private readonly List<UpdateOutcome> recorded = [];

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

        Task run = Begin(source);
        await source.CheckEntered.Task.WaitAsync(Limit);

        Assert.False(run.IsCompleted, "Start must not wait for the check");
        Assert.Equal(["check"], source.Calls);
        // Job 396 AC-1: installed is known before the check ends; no outcome yet.
        Assert.True(status.Installed);
        Assert.Null(status.Last);

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

        Task run = Begin(source);
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

        Task run = Begin(source);
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(1, source.InstalledReads);
        Assert.Empty(source.Calls);
        // Job 396 AC-1: one NotInstalled outcome, and the status knows the copy doesn't update itself.
        UpdateOutcome outcome = SingleOutcome();
        Assert.Equal(UpdateResultKind.NotInstalled, outcome.Kind);
        Assert.Equal("not installed", outcome.Text);
        Assert.False(status.Installed);
        AssertDataFolderUnchanged();
    }

    // AC-9: a newer version is downloaded once, then applied on exit once, in that order.
    [Fact]
    public async Task InstalledAndNewer_DownloadsOnce_ThenAppliesOnExitOnce()
    {
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, Newer = true, FoundVersion = "1.2.3" };

        Task run = Begin(source);
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(["check", "download", "apply"], source.Calls);
        // Job 396 AC-2: one Downloaded outcome naming the new version.
        UpdateOutcome outcome = SingleOutcome();
        Assert.Equal(UpdateResultKind.Downloaded, outcome.Kind);
        Assert.Equal("1.2.3", outcome.NewVersion);
        Assert.Equal("downloaded 1.2.3, applies on exit", outcome.Text);
        Assert.Null(outcome.Error);
        Assert.True(status.Installed);
        AssertDataFolderUnchanged();
    }

    // AC-9: the only way to apply is "after exit, without a restart": the source has no restart member at all.
    [Fact]
    public void UpdateSource_HasNoRestartMember()
    {
        string[] members = [.. typeof(IUpdateSource).GetMembers().Select(member => member.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["ApplyOnExit", "CheckAsync", "DownloadAsync", "FoundVersion", "IsInstalled", "get_FoundVersion", "get_IsInstalled"], members);
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

        Task run = Begin(source);
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(["check"], source.Calls);
        // Job 396 AC-2: one NoNewer outcome.
        UpdateOutcome outcome = SingleOutcome();
        Assert.Equal(UpdateResultKind.NoNewer, outcome.Kind);
        Assert.Equal("no newer version", outcome.Text);
        Assert.Null(outcome.NewVersion);
        Assert.True(status.Installed);
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
        Task run = Begin(source);
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully, "the update task must never fail or be cancelled");
        Assert.Equal(calls.Length == 0 ? [] : calls.Split(','), source.Calls);

        // Job 396 AC-3: exactly one Failed outcome, "failed: <Type.Name>: <message>" on one line. A cancelled task
        // surfaces as the task's own cancellation.
        UpdateOutcome outcome = SingleOutcome();
        Assert.Equal(UpdateResultKind.Failed, outcome.Kind);
        string expected = kind == "cancelled" && inTask
            ? "failed: TaskCanceledException: " + new TaskCanceledException().Message
            : $"failed: {exception.GetType().Name}: {exception.Message}";
        Assert.Equal(expected, outcome.Text);
        Assert.DoesNotContain('\n', outcome.Text);
        if (step == "installed")
        {
            // Unknown, not false: the read itself failed.
            Assert.Null(status.Installed);
        }
        else
        {
            Assert.True(status.Installed);
        }

        AssertDataFolderUnchanged();
    }

    // Job 396 AC-3: a failure keeps one line of at most 200 characters of message and never the stack trace.
    [Fact]
    public async Task Failure_LongMultiLineMessage_OneLineCutWithoutStackTrace()
    {
        string message = "first line\r\nsecond\tline " + new string('x', 400);
        Exception thrown;
        try
        {
            throw new IOException(message);
        }
        catch (IOException e)
        {
            thrown = e;
        }

        FakeUpdateSource source = new FakeUpdateSource { Installed = true, CheckThrows = thrown };

        await Begin(source).WaitAsync(Limit);

        UpdateOutcome outcome = SingleOutcome();
        string expectedMessage = ("first line  second line " + new string('x', 400))[..UpdateOutcome.MaxMessage];
        Assert.Equal("failed: IOException: " + expectedMessage, outcome.Text);
        Assert.DoesNotContain(" at ", outcome.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Failure_LongMultiLineMessage_OneLineCutWithoutStackTrace), outcome.Text, StringComparison.Ordinal);
    }

    // Job 396 AC-3: a record callback that throws is swallowed; the outcome is still the status's last one.
    [Fact]
    public async Task RecordThrows_IsSwallowed_AndLastIsStillSet()
    {
        FakeUpdateSource source = new FakeUpdateSource { Installed = true, Newer = false };
        int calls = 0;

        Task run = Begin(source, _ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("log broken");
        });
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(1, calls);
        Assert.NotNull(status.Last);
        Assert.Equal(UpdateResultKind.NoNewer, status.Last.Kind);
        AssertDataFolderUnchanged();
    }

    // Job 396 AC-3: a throwing status listener never reaches the update either.
    [Fact]
    public async Task ThrowingStatusListener_IsIgnored()
    {
        status.Changed += () => throw new InvalidOperationException("listener broken");
        FakeUpdateSource source = new FakeUpdateSource { Installed = false };

        Task run = Begin(source);
        await run.WaitAsync(Limit);

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(UpdateResultKind.NotInstalled, SingleOutcome().Kind);
    }

    // Job 396: the outcome texts of each kind; a download without a known version still reads as one.
    [Fact]
    public void OutcomeText_ForEachKind()
    {
        Assert.Equal("not installed", new UpdateOutcome(Now, "1.0.0", UpdateResultKind.NotInstalled, null, null).Text);
        Assert.Equal("no newer version", new UpdateOutcome(Now, "1.0.0", UpdateResultKind.NoNewer, null, null).Text);
        Assert.Equal(
            "downloaded 2.0.0, applies on exit",
            new UpdateOutcome(Now, "1.0.0", UpdateResultKind.Downloaded, "2.0.0", null).Text);
        Assert.Equal(
            "downloaded a newer version, applies on exit",
            new UpdateOutcome(Now, "1.0.0", UpdateResultKind.Downloaded, null, null).Text);
        Assert.Equal(
            "failed: HttpRequestException: offline https://example.invalid/feed",
            UpdateOutcome.Failure(Now, "1.0.0", new HttpRequestException("offline https://example.invalid/feed?token=abc#x")).Text);
    }

    // Job 396 AC-7: the shown version drops the +commit suffix; an empty one is a development build.
    [Theory]
    [InlineData("0.3.1-develop.12+0123abc", "0.3.1-develop.12")]
    [InlineData("1.0.0+0123abc", "1.0.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("", "development build")]
    [InlineData(" ", "development build")]
    [InlineData(null, "development build")]
    [InlineData("+0123abc", "development build")]
    public void ShownVersion_DropsTheCommitSuffix(string? version, string expected)
    {
        Assert.Equal(expected, StartupUpdate.ShownVersion(version));
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

    // Job 396: the update with this test's status, a recording callback (which writes nothing) and the fake clock.
    private Task Begin(FakeUpdateSource source, Action<UpdateOutcome>? record = null) =>
        new StartupUpdate(source, status, record ?? Record, () => Now).Start();

    private void Record(UpdateOutcome outcome)
    {
        lock (recorded)
        {
            recorded.Add(outcome);
        }
    }

    // Job 396: exactly one outcome was recorded, and it is the status's last one.
    private UpdateOutcome SingleOutcome()
    {
        UpdateOutcome outcome;
        lock (recorded)
        {
            outcome = Assert.Single(recorded);
        }

        Assert.Same(outcome, status.Last);
        Assert.Equal(Now, outcome.Utc);
        Assert.Equal(StartupUpdate.CurrentVersion, outcome.Version);
        return outcome;
    }

    private void AssertDataFolderUnchanged()
    {
        Assert.Equal(before, FileSnapshot.Take(root));
    }
}
