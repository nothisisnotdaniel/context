using System.Diagnostics;

namespace Context.Tests;

/// <summary>
/// The real Times runner against temp shell scripts named <c>tm</c> (job 282, AC-2, AC-4). Unix only: each test
/// returns at once on Windows, where no shell script runs. Each test has its own temp folder.
/// </summary>
public sealed class TimesProcessRunnerTests : IDisposable
{
    // The bound on a script that should answer at once; only a broken runner ever reaches it.
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(30);

    // The runner's timeout in the sleeping-script test.
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);

    private readonly string folder = Path.Combine(Path.GetTempPath(), "context-tm-" + Guid.NewGuid().ToString("N"));

    public TimesProcessRunnerTests()
    {
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string Script(string body)
    {
        string path = Path.Combine(folder, "tm");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    [Fact]
    public async Task Run_PassesEachArgumentLiterally()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string marker = Path.Combine(folder, "ran");
        string tm = Script("for a in \"$@\"; do printf '%s\\n' \"$a\"; done");
        TimesProcessRunner runner = new TimesProcessRunner(() => tm, ScriptTimeout);

        TimesRunResult result = await runner.Run(["a;b", "$(touch " + marker + ")", "x y", "|z", "'q\"", "`id`"]);

        Assert.Equal(TimesRunKind.Done, result.Kind);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a;b\n$(touch " + marker + ")\nx y\n|z\n'q\"\n`id`\n", result.Stdout);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Run_KeepsExitCodeAndStderr_SetsNoColor_ClosesStdin()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tm = Script("cat\nprintf 'NO_COLOR=%s\\n' \"$NO_COLOR\"\nprintf 'busy\\n' >&2\nexit 3");
        TimesProcessRunner runner = new TimesProcessRunner(() => tm, ScriptTimeout);

        TimesRunResult result = await runner.Run(["status"]);

        Assert.Equal(TimesRunKind.Done, result.Kind);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("NO_COLOR=1\n", result.Stdout);
        Assert.Equal("busy\n", result.Stderr);
    }

    [Fact]
    public async Task Run_CapsOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tm = Script("head -c 200000 /dev/zero | tr '\\000' x");
        TimesProcessRunner runner = new TimesProcessRunner(() => tm, ScriptTimeout);

        TimesRunResult result = await runner.Run([]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TimesProcessRunner.MaxOutputBytes, result.Stdout.Length);
    }

    [Fact]
    public async Task Run_TooSlow_TimesOutAndKillsTheProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tm = Script("sleep 60");
        TimesProcessRunner runner = new TimesProcessRunner(() => tm, ShortTimeout);

        TimesRunResult result = await runner.Run(["a"]).WaitAsync(ScriptTimeout);

        Assert.Equal(TimesRunKind.TimedOut, result.Kind);
        Assert.NotEqual(0, runner.LastProcessId);
        Assert.False(IsRunning(runner.LastProcessId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Run_NoExecutable_IsNotFound(string? path)
    {
        TimesProcessRunner runner = new TimesProcessRunner(() => path, ScriptTimeout);

        Assert.Equal(TimesRunKind.NotFound, (await runner.Run(["a"])).Kind);
    }

    [Fact]
    public async Task Run_MissingFile_IsNotFound()
    {
        string missing = Path.Combine(folder, "missing", "tm");
        TimesProcessRunner runner = new TimesProcessRunner(() => missing, ScriptTimeout);

        Assert.Equal(TimesRunKind.NotFound, (await runner.Run(["a"])).Kind);
    }

    [Fact]
    public async Task Run_ThrowingLocator_IsNotFound()
    {
        TimesProcessRunner runner = new TimesProcessRunner(() => throw new IOException("x"), ScriptTimeout);

        Assert.Equal(TimesRunKind.NotFound, (await runner.Run(["a"])).Kind);
    }

    private static bool IsRunning(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
