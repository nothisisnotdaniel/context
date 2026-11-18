using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>The crash hint shown once on the next start: the newest crash log and the fixed folder that holds it.</summary>
internal sealed class CrashHint
{
    public CrashHint(string logPath, string folder)
    {
        LogPath = logPath;
        Folder = folder;
    }

    /// <summary>The newest crash log written since the previous start.</summary>
    public string LogPath { get; }

    /// <summary>The crash folder, <c>&lt;root&gt;/.logs</c>; the only folder the launcher is ever given.</summary>
    public string Folder { get; }
}

/// <summary>
/// The desktop crash log (job 237). <see cref="Write"/> puts one <c>crash-yyyyMMdd-HHmmss-fff.log</c> (UTC) into
/// <c>&lt;root&gt;/.logs</c> with the time, the version, the OS, the source and each exception's type, message and
/// stack trace (cleaned, cut and with the home folder shown as <c>~</c>); nothing else. Only the newest
/// <see cref="MaxLogs"/> logs are kept. The session file <c>.logs/session</c> (<c>running &lt;start&gt;</c> or
/// <c>clean &lt;start&gt;</c>) lets <see cref="Begin"/> notice an exit that left no managed exception. No member ever
/// throws.
/// </summary>
internal sealed class CrashLog
{
    /// <summary>The name of the crash folder inside the data folder; it starts with a dot, so the note list skips it.</summary>
    public const string FolderName = ".logs";

    /// <summary>The session file inside the crash folder.</summary>
    public const string SessionFileName = "session";

    /// <summary>How many crash logs are kept; older ones are deleted after each write.</summary>
    public const int MaxLogs = 10;

    /// <summary>Each message and each stack trace is cut to this many characters.</summary>
    public const int MaxText = 2000;

    /// <summary>How deep inner exceptions are followed (the outer exception is level 1).</summary>
    public const int MaxDepth = 10;

    /// <summary>A log file stays below this many bytes (64 KiB).</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The source of the log <see cref="Begin"/> writes after an exit that left no exception.</summary>
    public const string UncleanSource = "unclean-exit";

    private const string StampFormat = "yyyyMMdd-HHmmss-fff";
    private const string Running = "running";
    private const string Clean = "clean";
    private const int MaxSessionLength = 200;
    private const string LeftOut = "(further exceptions left out: the log is full)\n";

    private static readonly Regex LogName = new(
        @"^crash-(\d{8}-\d{6}-\d{3})(?:-(\d{1,2}))?\.log$", RegexOptions.CultureInvariant);

    private readonly Func<DateTime> utcNow;
    private readonly string version;
    private readonly string os;
    private readonly string? home;
    private readonly object gate = new();
    private DateTime? started;

    public CrashLog(string root, Func<DateTime> utcNow, string version, string os, string? home)
    {
        Folder = Path.Combine(root, FolderName);
        this.utcNow = utcNow;
        this.version = version;
        this.os = os;
        this.home = string.IsNullOrEmpty(home) ? null : home.TrimEnd('/', '\\');
    }

    /// <summary>The crash folder, <c>&lt;root&gt;/.logs</c>.</summary>
    public string Folder { get; }

    private string SessionPath => Path.Combine(Folder, SessionFileName);

    /// <summary>The text of the log <see cref="Begin"/> writes when the previous session started at <paramref name="start"/> never ended.</summary>
    public static string UncleanText(string start) =>
        $"No exception was caught; the app was killed or the web view stopped. Previous start: {start}.";

    /// <summary>
    /// Writes one crash log for <paramref name="e"/> from <paramref name="source"/> and prunes old logs. Returns the
    /// log's path, or <c>null</c> when it can't be written. Never throws.
    /// </summary>
    public string? Write(string source, Exception e)
    {
        try
        {
            return WriteLog(source, body => AppendExceptions(body, e));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs once at start: reads the previous session, writes <c>running &lt;now&gt;</c> and returns the hint for the
    /// newest log written since the previous start (after an unclean exit with no log, a new <see cref="UncleanSource"/>
    /// log), or <c>null</c>. A missing, unreadable or malformed session counts as none. Never throws.
    /// </summary>
    public CrashHint? Begin()
    {
        try
        {
            CrashHint? hint = null;
            if (ReadSession() is (bool running, DateTime start))
            {
                string? newest = NewestLogSince(start);
                if (newest is null && running)
                {
                    string startText = Stamp(start);
                    newest = WriteLog(UncleanSource, body => body.Append(UncleanText(startText)).Append('\n'));
                }

                if (newest is not null)
                {
                    hint = new CrashHint(newest, Folder);
                }
            }

            // The new start is taken after any unclean-exit log and is always later than the newest log's name time,
            // so a log that was already shown never counts as newer than this session on the next start.
            DateTime now = Utc(utcNow());
            if (NewestLogTime() is { } newestTime && newestTime.AddMilliseconds(1) > now)
            {
                now = newestTime.AddMilliseconds(1);
            }

            lock (gate)
            {
                started = now;
            }

            WriteSession(Running, now);
            return hint;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Runs on a clean exit: rewrites the session as <c>clean &lt;start&gt;</c>. Never throws.</summary>
    public void End()
    {
        try
        {
            DateTime? start;
            lock (gate)
            {
                start = started;
            }

            if (start is { } value)
            {
                WriteSession(Clean, value);
            }
        }
        catch (Exception)
        {
            // Nothing to report: the next start counts it as an unclean exit.
        }
    }

    private static DateTime Utc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    };

    private static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private void WriteSession(string kind, DateTime start)
    {
        try
        {
            // Never through a link: a linked .logs folder or session file (or anything but a regular file there) is
            // left alone, so no other file is ever overwritten.
            if (!EnsureFolder())
            {
                return;
            }

            FileInfo session = new FileInfo(SessionPath);
            if (session.LinkTarget is not null || Directory.Exists(SessionPath)
                || (session.Exists && session.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                return;
            }

            File.WriteAllText(SessionPath, kind + " " + Stamp(start));
        }
        catch (Exception)
        {
            // The session marker is best effort.
        }
    }

    // (running, start) of the previous session, or null when the file is missing, unreadable or malformed.
    private (bool Running, DateTime Start)? ReadSession()
    {
        string text;
        try
        {
            FileInfo info = new FileInfo(SessionPath);
            if (!info.Exists || info.Length > MaxSessionLength || info.LinkTarget is not null)
            {
                return null;
            }

            text = File.ReadAllText(SessionPath).Trim();
        }
        catch (Exception)
        {
            return null;
        }

        string[] parts = text.Split(' ');
        if (parts.Length != 2 || (parts[0] != Running && parts[0] != Clean))
        {
            return null;
        }

        if (!DateTime.TryParseExact(parts[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime start)
            || start.Kind != DateTimeKind.Utc)
        {
            return null;
        }

        return (parts[0] == Running, start);
    }

    private string? WriteLog(string source, Action<StringBuilder> fill)
    {
        DateTime now = Utc(utcNow());
        StringBuilder body = new StringBuilder();
        body.Append("Context crash log\n")
            .Append("Time (UTC): ").Append(Stamp(now)).Append('\n')
            .Append("Version: ").Append(Line(version)).Append('\n')
            .Append("OS: ").Append(Line(os)).Append('\n')
            .Append("Source: ").Append(Line(source)).Append('\n');
        fill(body);
        byte[] bytes = Encoding.UTF8.GetBytes(body.ToString());

        if (!EnsureFolder())
        {
            return null;
        }

        string stem = "crash-" + now.ToString(StampFormat, CultureInfo.InvariantCulture);
        for (int n = 1; n <= 99; n++)
        {
            string path = Path.Combine(Folder, n == 1 ? stem + ".log" : $"{stem}-{n}.log");
            if (File.Exists(path) || Directory.Exists(path))
            {
                continue;
            }

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                // Taken between the check and the create: try the next name.
                continue;
            }

            Prune();
            return path;
        }

        return null;
    }

    // Creates the crash folder when missing; false when it is (or became) a link, a reparse point or not a folder.
    private bool EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        return IsPlainFolder(Folder);
    }

    /// <summary>True when <paramref name="path"/> is a folder itself: not a link, not a reparse point, not a file.</summary>
    public static bool IsPlainFolder(string path)
    {
        try
        {
            DirectoryInfo info = new DirectoryInfo(path);
            return info.Exists && info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Each exception, depth first: the outer one, then its InnerException or every AggregateException entry, at most
    // MaxDepth levels; the file stays below MaxBytes.
    private void AppendExceptions(StringBuilder body, Exception outer)
    {
        int bytes = Encoding.UTF8.GetByteCount(body.ToString());
        int budget = MaxBytes - 1024 - Encoding.UTF8.GetByteCount(LeftOut) - bytes;
        Stack<(Exception Error, int Level)> pending = new Stack<(Exception Error, int Level)>();
        pending.Push((outer, 1));
        int number = 0;
        while (pending.Count > 0)
        {
            (Exception error, int level) = pending.Pop();
            number++;
            string entry = Entry(error, number, level);
            int size = Encoding.UTF8.GetByteCount(entry);
            if (size > budget)
            {
                body.Append(LeftOut);
                return;
            }

            budget -= size;
            body.Append(entry);
            if (level >= MaxDepth)
            {
                continue;
            }

            IReadOnlyList<Exception> inner = Inner(error);
            for (int i = inner.Count - 1; i >= 0; i--)
            {
                pending.Push((inner[i], level + 1));
            }
        }
    }

    private static IReadOnlyList<Exception> Inner(Exception error)
    {
        try
        {
            if (error is AggregateException aggregate)
            {
                return aggregate.InnerExceptions.Where(e => e is not null).ToArray();
            }

            return error.InnerException is { } inner ? [inner] : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private string Entry(Exception error, int number, int level)
    {
        string type = Read(() => error.GetType().FullName) ?? "(unknown type)";
        string message = Text(Read(() => error.Message) ?? string.Empty);
        string stack = Text(Read(() => error.StackTrace) ?? string.Empty);
        return new StringBuilder()
            .Append('\n')
            .Append("Exception ").Append(number.ToString(CultureInfo.InvariantCulture))
            .Append(" (level ").Append(level.ToString(CultureInfo.InvariantCulture)).Append("): ")
            .Append(Line(type)).Append('\n')
            .Append("Message: ").Append(message).Append('\n')
            .Append("Stack trace:\n").Append(stack.Length == 0 ? "(none)" : stack).Append('\n')
            .ToString();
    }

    private static string? Read(Func<string?> get)
    {
        try
        {
            return get();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // A message or stack trace: cleaned, the home folder shown as ~, cut to MaxText characters.
    private string Text(string raw)
    {
        string text = SafeText.Clean(raw);
        if (home is not null)
        {
            text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }

        return text.Length <= MaxText ? text : text[..MaxText];
    }

    private string Line(string raw) => Text(raw).Replace('\n', ' ');

    // Regular crash-*.log files only (never a link), each with its name's time and number.
    private List<(FileInfo File, string Stamp, int Number)> Logs()
    {
        List<(FileInfo File, string Stamp, int Number)> logs = [];
        if (!IsPlainFolder(Folder))
        {
            return logs;
        }

        foreach (FileInfo file in new DirectoryInfo(Folder).EnumerateFiles())
        {
            Match match = LogName.Match(file.Name);
            if (!match.Success || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            int number = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
            logs.Insert(logs.Count, (file, match.Groups[1].Value, number));
        }

        logs.Sort((a, b) =>
        {
            int byStamp = string.CompareOrdinal(b.Stamp, a.Stamp);
            return byStamp != 0 ? byStamp : b.Number.CompareTo(a.Number);
        });
        return logs;
    }

    // The UTC time in the newest log's name, or null.
    private DateTime? NewestLogTime()
    {
        foreach ((FileInfo _, string stamp, int _) in Logs())
        {
            return DateTime.SpecifyKind(
                DateTime.ParseExact(stamp, StampFormat, CultureInfo.InvariantCulture), DateTimeKind.Utc);
        }

        return null;
    }

    // The newest log written at or after start (to the millisecond of its name), or null.
    private string? NewestLogSince(DateTime start)
    {
        string since = start.ToString(StampFormat, CultureInfo.InvariantCulture);
        foreach ((FileInfo file, string stamp, int _) in Logs())
        {
            if (string.CompareOrdinal(stamp, since) >= 0)
            {
                return file.FullName;
            }

            break;
        }

        return null;
    }

    private void Prune()
    {
        try
        {
            foreach ((FileInfo file, string _, int _) in Logs().Skip(MaxLogs))
            {
                try
                {
                    File.Delete(file.FullName);
                }
                catch (Exception)
                {
                    // Left for the next write.
                }
            }
        }
        catch (Exception)
        {
            // Pruning is best effort.
        }
    }
}
