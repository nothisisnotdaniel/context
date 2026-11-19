namespace Context;

/// <summary>How a Times run ended (job 282).</summary>
internal enum TimesRunKind
{
    /// <summary>Times ran and exited; see <see cref="TimesRunResult.ExitCode"/>.</summary>
    Done,

    /// <summary>No <c>tm</c> executable was found, or it could not be started.</summary>
    NotFound,

    /// <summary>Times did not exit within the timeout and was stopped.</summary>
    TimedOut,
}

/// <summary>The outcome of one Times run: raw output, never shown before the host cleans it.</summary>
internal sealed class TimesRunResult
{
    public TimesRunResult(TimesRunKind kind, int exitCode, string stdout, string stderr)
    {
        Kind = kind;
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }

    public TimesRunKind Kind { get; }

    public int ExitCode { get; }

    public string Stdout { get; }

    public string Stderr { get; }

    public static TimesRunResult NotFound() => new TimesRunResult(TimesRunKind.NotFound, -1, string.Empty, string.Empty);

    public static TimesRunResult TimedOut() => new TimesRunResult(TimesRunKind.TimedOut, -1, string.Empty, string.Empty);

    public override string ToString() => nameof(TimesRunResult);
}

/// <summary>
/// Runs the Times tool with a list of arguments (job 282). <c>Program.cs</c> passes the real
/// process runner next to it; tests use <c>FakeTimesRunner</c>, so no test but the runner's own starts a process.
/// </summary>
internal interface ITimesRunner
{
    /// <summary>Runs <c>tm</c> with exactly these arguments; never throws.</summary>
    public Task<TimesRunResult> Run(IReadOnlyList<string> arguments);
}
