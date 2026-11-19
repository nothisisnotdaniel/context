using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Context;

/// <summary>One commit whose message mentions a work item (raw text from <c>git</c>).</summary>
internal sealed record LoggedCommit
{
    public LoggedCommit(string hash, DateOnly date, string subject)
    {
        Hash = hash;
        Date = date;
        Subject = subject;
    }

    /// <summary>The full commit hash.</summary>
    public string Hash { get; }

    /// <summary>The committer date (<c>%cs</c>).</summary>
    public DateOnly Date { get; }

    /// <summary>The subject line (raw).</summary>
    public string Subject { get; }

    /// <summary>Only the type name, like the view records.</summary>
    public override string ToString() => nameof(LoggedCommit);
}

/// <summary>Reading a repository's commits failed (no repository, git failed, timed out).</summary>
internal class CommitLogException : IOException
{
    public CommitLogException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary><c>git</c> could not be started at all.</summary>
internal sealed class GitMissingException : CommitLogException
{
    public GitMissingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Reads the commits of one repository folder that mention a work item.</summary>
internal interface ICommitLog
{
    /// <summary>
    /// The commits of every branch in <paramref name="folder"/> whose message mentions <c>#<paramref name="item"/></c>
    /// (<see cref="WorkItemParser"/>), newest first, at most <see cref="GitCommitLog.MaxCommitsPerRepo"/>. Throws
    /// <see cref="GitMissingException"/> when git can't be started and <see cref="CommitLogException"/> for any other
    /// failure.
    /// </summary>
    public IReadOnlyList<LoggedCommit> Read(string folder, int item);
}

/// <summary>
/// The real <see cref="ICommitLog"/>: runs <c>git log</c> with a fixed argument list (no shell), a timeout after which
/// the process is killed, and a cap on what is read from its output. The only Core file that names
/// <c>System.Diagnostics</c>.
/// </summary>
internal sealed class GitCommitLog : ICommitLog
{
    /// <summary>The most commits kept per repository.</summary>
    public const int MaxCommitsPerRepo = 100;

    /// <summary>The most commits git is asked for (<c>-n</c>), before the message is re-checked.</summary>
    public const int MaxLogged = 500;

    /// <summary>The most bytes read from git's output.</summary>
    public const int MaxOutputBytes = 1024 * 1024;

    /// <summary>How long git may run before it is killed.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    private readonly string executable;

    public GitCommitLog()
        : this("git")
    {
    }

    /// <summary>Like the public constructor with another program name (tests use a missing one).</summary>
    internal GitCommitLog(string executable)
    {
        this.executable = executable;
    }

    /// <summary>The fixed argument list for <paramref name="folder"/> and <paramref name="item"/>.</summary>
    internal static IReadOnlyList<string> Arguments(string folder, int item) =>
    [
        "--no-pager",
        "-c",
        "core.fsmonitor=false",
        "-C",
        folder,
        "log",
        "--all",
        "--no-color",
        "-F",
        "--grep=#" + item.ToString(CultureInfo.InvariantCulture),
        "-n",
        MaxLogged.ToString(CultureInfo.InvariantCulture),
        "--format=%H%x1f%cs%x1f%s%x1f%b%x1e",
    ];

    public IReadOnlyList<LoggedCommit> Read(string folder, int item)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in Arguments(folder, item))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new GitMissingException("git did not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or PlatformNotSupportedException)
        {
            throw new GitMissingException("git did not start.", ex);
        }

        using (process)
        {
            process.StandardInput.Close();
            // stderr is drained into nothing, so it is never buffered and git never blocks on a full pipe.
            _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            Task<byte[]> reading = ReadCappedAsync(process.StandardOutput.BaseStream);
            bool finished;
            try
            {
                finished = reading.Wait(Timeout);
            }
            catch (AggregateException ex)
            {
                Kill(process);
                throw new CommitLogException("Can't read git's output.", ex);
            }

            if (!finished)
            {
                Kill(process);
                throw new CommitLogException("git timed out.");
            }

            byte[] output = reading.Result;
            if (output.Length > MaxOutputBytes)
            {
                // Over the cap: keep the records that arrived whole and stop git.
                Kill(process);
                output = output[..MaxOutputBytes];
            }
            else if (!process.WaitForExit(Timeout))
            {
                Kill(process);
                throw new CommitLogException("git timed out.");
            }
            else if (process.ExitCode != 0)
            {
                throw new CommitLogException("git failed.");
            }

            return Parse(Encoding.UTF8.GetString(output), item);
        }
    }

    /// <summary>
    /// The commits in git's output (<c>%H 1f %cs 1f %s 1f %b 1e</c> records): only whole records, only those whose
    /// subject and body mention the item, at most <see cref="MaxCommitsPerRepo"/>, in git's order.
    /// </summary>
    internal static IReadOnlyList<LoggedCommit> Parse(string output, int item)
    {
        List<LoggedCommit> commits = [];
        string[] records = output.Split(RecordSeparator);
        for (int r = 0; r < records.Length - 1 && commits.Count < MaxCommitsPerRepo; r++)
        {
            string record = records[r].TrimStart('\n', '\r');
            string[] fields = record.Split(FieldSeparator, 4);
            if (fields.Length < 3
                || !DateOnly.TryParseExact(fields[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                continue;
            }

            string body = fields.Length > 3 ? fields[3] : string.Empty;
            if (!WorkItemParser.Mentions(fields[2] + "\n" + body, item))
            {
                continue;
            }

            commits.Add(new LoggedCommit(fields[0], date, fields[2]));
        }

        return commits;
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream)
    {
        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (buffer.Length <= MaxOutputBytes)
        {
            int read = await stream.ReadAsync(chunk).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            // Already gone.
        }
    }
}
