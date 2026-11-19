using System.Diagnostics;
using System.Text;

namespace Context;

/// <summary>
/// The real <see cref="ITimesRunner"/> (job 282): starts the located <c>tm</c> with the arguments only through
/// <see cref="ProcessStartInfo.ArgumentList"/> (no shell), stdin closed, <c>NO_COLOR=1</c>, and no window. Each output
/// stream is kept up to <see cref="MaxOutputBytes"/> and the rest drained; after the timeout the process tree is killed.
/// </summary>
internal sealed class TimesProcessRunner : ITimesRunner
{
    /// <summary>The most bytes kept of stdout and of stderr each.</summary>
    public const int MaxOutputBytes = 64 * 1024;

    private readonly Func<string?> executable;
    private readonly TimeSpan timeout;

    public TimesProcessRunner(Func<string?> executable, TimeSpan timeout)
    {
        this.executable = executable;
        this.timeout = timeout;
    }

    /// <summary>The process id of the last start (for the runner's own test to check it exited).</summary>
    public int LastProcessId { get; private set; }

    public async Task<TimesRunResult> Run(IReadOnlyList<string> arguments)
    {
        string? path;
        try
        {
            path = executable();
        }
        catch (Exception)
        {
            path = null;
        }

        if (string.IsNullOrEmpty(path))
        {
            return TimesRunResult.NotFound();
        }

        // Each word is one ArgumentList entry (this constructor fills it); no shell ever sees the line.
        ProcessStartInfo start = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        start.Environment["NO_COLOR"] = "1";

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception)
        {
            return TimesRunResult.NotFound();
        }

        if (process is null)
        {
            return TimesRunResult.NotFound();
        }

        using (process)
        {
            LastProcessId = process.Id;
            try
            {
                process.StandardInput.Close();
                Task<string> stdout = ReadCappedAsync(process.StandardOutput.BaseStream);
                Task<string> stderr = ReadCappedAsync(process.StandardError.BaseStream);
                using CancellationTokenSource cancel = new CancellationTokenSource(timeout);
                try
                {
                    await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
                    await Task.WhenAll(stdout, stderr).WaitAsync(cancel.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    return TimesRunResult.TimedOut();
                }

                return new TimesRunResult(TimesRunKind.Done, process.ExitCode, stdout.Result, stderr.Result);
            }
            catch (Exception)
            {
                Kill(process);
                return new TimesRunResult(TimesRunKind.Done, -1, string.Empty, string.Empty);
            }
        }
    }

    private static async Task<string> ReadCappedAsync(Stream stream)
    {
        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(chunk).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            int keep = (int)Math.Min(read, MaxOutputBytes - buffer.Length);
            if (keep > 0)
            {
                buffer.Write(chunk, 0, keep);
            }
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}
