using System.Globalization;
using System.Text;

namespace Context.Tests;

// 237 AC-1 to AC-8: the crash log and the session marker, in a temp root with a fake clock.
public sealed class CrashLogTests : IDisposable
{
    private const string Version = "1.2.3-test";
    private const string Os = "TestOS 1.0 Arm64";
    private const string Home = "/home/someone";

    private readonly string root;
    private DateTime now = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    public CrashLogTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    private string Logs => Path.Combine(root, CrashLog.FolderName);

    private string SessionPath => Path.Combine(Logs, CrashLog.SessionFileName);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private CrashLog Create() => new CrashLog(root, () => now, Version, Os, Home);

    private static string Stamp(DateTime time) => time.ToString("O", CultureInfo.InvariantCulture);

    private string[] LogFiles() =>
        Directory.GetFiles(Logs).Select(Path.GetFileName).Where(n => n!.StartsWith("crash-", StringComparison.Ordinal))
            .Select(n => n!).Order(StringComparer.Ordinal).ToArray();

    private static Exception Thrown(Exception e)
    {
        try
        {
            throw e;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    // AC-1: one file in .logs with the time, version, OS, source and each exception's type, message and stack.
    [Fact]
    public void Write_OneFile_WithHeaderAndNestedExceptions()
    {
        Exception inner = Thrown(new ArgumentException("inner problem"));
        Exception outer = Thrown(new InvalidOperationException("outer problem", inner));

        string? path = Create().Write("component", outer);

        Assert.NotNull(path);
        Assert.Equal(Logs, Path.GetDirectoryName(path));
        Assert.Equal(["crash-20261118-100000-000.log"], LogFiles());
        string text = File.ReadAllText(path);
        Assert.Contains("Time (UTC): " + Stamp(now), text, StringComparison.Ordinal);
        Assert.Contains("Version: " + Version, text, StringComparison.Ordinal);
        Assert.Contains("OS: " + Os, text, StringComparison.Ordinal);
        Assert.Contains("Source: component", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("Message: outer problem", text, StringComparison.Ordinal);
        Assert.Contains("System.ArgumentException", text, StringComparison.Ordinal);
        Assert.Contains("Message: inner problem", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("outer problem", StringComparison.Ordinal) < text.IndexOf("inner problem", StringComparison.Ordinal));
    }

    // AC-1: every AggregateException entry is followed, and nesting stops at 10 levels.
    [Fact]
    public void Write_FollowsAggregateEntries_AtMostTenLevels()
    {
        AggregateException aggregate = new AggregateException(
            new InvalidOperationException("first entry"), new FormatException("second entry"));
        Exception deep = new Exception("level 11");
        for (int level = 10; level >= 1; level--)
        {
            deep = new Exception("level " + level.ToString(CultureInfo.InvariantCulture), deep);
        }

        CrashLog log = Create();
        string aggregateText = File.ReadAllText(log.Write("unobserved-task", aggregate)!);
        now = now.AddSeconds(1);
        string deepText = File.ReadAllText(log.Write("unhandled", deep)!);

        Assert.Contains("System.AggregateException", aggregateText, StringComparison.Ordinal);
        Assert.Contains("Message: first entry", aggregateText, StringComparison.Ordinal);
        Assert.Contains("Message: second entry", aggregateText, StringComparison.Ordinal);
        Assert.Contains("Message: level 10\n", deepText, StringComparison.Ordinal);
        Assert.DoesNotContain("level 11", deepText, StringComparison.Ordinal);
    }

    // AC-2: messages are cleaned, cut to 2,000 characters, the home folder shown as ~; no Data; under 64 KiB.
    [Fact]
    public void Write_CleansCutsAndHidesHome_WritesNoData()
    {
        Exception withHome = new InvalidOperationException("Can't read " + Home + "/notes/a.md\u0007 or " + Home + "/b");
        withHome.Data["secret"] = "data-value-must-not-appear";
        Exception big = new Exception(new string('x', 5000), withHome);

        string path = Create().Write("component", big)!;
        string text = File.ReadAllText(path);

        Assert.DoesNotContain(Home, text, StringComparison.Ordinal);
        Assert.Contains("Can't read ~/notes/a.md� or ~/b", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", text, StringComparison.Ordinal);
        Assert.Contains("Message: " + new string('x', CrashLog.MaxText) + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', CrashLog.MaxText + 1), text, StringComparison.Ordinal);
        Assert.DoesNotContain("data-value-must-not-appear", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.Ordinal);
        Assert.True(new FileInfo(path).Length < CrashLog.MaxBytes);
    }

    // AC-2: only the listed items: the header lines and, per exception, type, message and stack trace.
    [Fact]
    public void Write_HoldsOnlyTheListedItems()
    {
        string text = File.ReadAllText(Create().Write("component", new InvalidOperationException("boom"))!);

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            ["Context crash log", "Time (UTC): ", "Version: ", "OS: ", "Source: ", "Exception 1 (level 1): ", "Message: ", "Stack trace:", "(none)"],
            lines.Select(l => l.Contains(':', StringComparison.Ordinal) && !l.EndsWith(':') ? l[..(l.IndexOf(':', StringComparison.Ordinal) + 2)] : l));
    }

    // AC-2: a huge aggregate stays below 64 KiB.
    [Fact]
    public void Write_ManyLargeEntries_StaysUnder64KiB()
    {
        Exception[] entries = Enumerable.Range(0, 100).Select(i => new Exception(new string('é', 5000))).ToArray();

        string path = Create().Write("component", new AggregateException(entries))!;

        Assert.True(new FileInfo(path).Length < CrashLog.MaxBytes);
        Assert.Contains("left out", File.ReadAllText(path), StringComparison.Ordinal);
    }

    // AC-1: a name clash adds -2 and keeps both files.
    [Fact]
    public void Write_SameMillisecond_AddsASuffix()
    {
        CrashLog log = Create();

        string first = log.Write("component", new Exception("a"))!;
        string second = log.Write("component", new Exception("b"))!;

        Assert.NotEqual(first, second);
        Assert.Equal(["crash-20261118-100000-000-2.log", "crash-20261118-100000-000.log"], LogFiles());
    }

    // AC-3: after 12 writes only the newest 10 remain; other files are kept.
    [Fact]
    public void Write_Twelve_KeepsTheNewestTen_AndOtherFiles()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(Path.Combine(Logs, "notes.txt"), "keep");
        File.WriteAllText(Path.Combine(Logs, "crash-old.log"), "keep");
        CrashLog log = Create();
        List<string> written = [];
        for (int i = 0; i < 12; i++)
        {
            written.Add(log.Write("component", new Exception("n" + i.ToString(CultureInfo.InvariantCulture)))!);
            now = now.AddSeconds(1);
        }

        string[] remaining = Directory.GetFiles(Logs).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray();
        string[] expected = written.Skip(2).Select(p => Path.GetFileName(p)!)
            .Concat(["crash-old.log", "notes.txt"]).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, remaining);
    }

    // AC-3: a symlink named like a log is neither deleted nor counted.
    [Fact]
    public void Write_SymlinkNamedLikeALog_IsNotDeletedOrCounted()
    {
        Directory.CreateDirectory(Logs);
        string target = Path.Combine(root, "target.txt");
        File.WriteAllText(target, "target");
        string link = Path.Combine(Logs, "crash-20000101-000000-000.log");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // This OS refuses symlinks here; there is nothing to check.
            return;
        }

        CrashLog log = Create();
        for (int i = 0; i < 11; i++)
        {
            log.Write("component", new Exception("n"));
            now = now.AddSeconds(1);
        }

        Assert.True(new FileInfo(link).LinkTarget is not null);
        Assert.Equal("target", File.ReadAllText(target));
        Assert.Equal(CrashLog.MaxLogs + 1, LogFiles().Length);
    }

    // AC-4: .logs as a file: Write returns null and doesn't throw.
    [Fact]
    public void Write_FolderIsAFile_ReturnsNull()
    {
        File.WriteAllText(Logs, "not a folder");

        Assert.Null(Create().Write("component", new Exception("x")));
        Assert.Equal("not a folder", File.ReadAllText(Logs));
    }

    // AC-4: Begin and End don't throw either when .logs is a file.
    [Fact]
    public void BeginEnd_FolderIsAFile_DoNotThrow()
    {
        File.WriteAllText(Logs, "not a folder");
        CrashLog log = Create();

        Assert.Null(log.Begin());
        log.End();
    }

    // AC-5: no session file: no hint, then running <now>; End writes clean <same start>.
    [Fact]
    public void Begin_NoSession_ReturnsNull_ThenEndWritesClean()
    {
        CrashLog log = Create();
        DateTime start = now;

        Assert.Null(log.Begin());
        Assert.Equal("running " + Stamp(start), File.ReadAllText(SessionPath));

        now = now.AddHours(1);
        log.End();

        Assert.Equal("clean " + Stamp(start), File.ReadAllText(SessionPath));
        Assert.Empty(LogFiles());
    }

    // AC-6: a clean previous session with no newer log gives no hint.
    [Fact]
    public void Begin_CleanSession_NoNewerLog_ReturnsNull()
    {
        CrashLog first = Create();
        first.Write("component", new Exception("before"));
        now = now.AddMinutes(1);
        first.Begin();
        now = now.AddMinutes(1);
        first.End();
        now = now.AddMinutes(1);

        Assert.Null(Create().Begin());
        Assert.Single(LogFiles());
    }

    // AC-6: a newer log gives the hint with the newest log, after a clean or a running session.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Begin_NewerLog_ReturnsTheNewest(bool clean)
    {
        CrashLog first = Create();
        first.Begin();
        now = now.AddMinutes(1);
        first.Write("component", new Exception("a"));
        now = now.AddMinutes(1);
        string newest = first.Write("component", new Exception("b"))!;
        if (clean)
        {
            first.End();
        }

        now = now.AddMinutes(1);
        CrashHint? hint = Create().Begin();

        Assert.NotNull(hint);
        Assert.Equal(newest, hint.LogPath);
        Assert.Equal(Logs, hint.Folder);
        Assert.Equal(2, LogFiles().Length);
        Assert.Equal("running " + Stamp(now), File.ReadAllText(SessionPath));
    }

    // AC-7: a running session with no newer log writes an unclean-exit log and returns it.
    [Fact]
    public void Begin_RunningSession_NoLog_WritesUncleanExitLog()
    {
        DateTime start = now;
        Create().Begin();
        now = now.AddHours(2);

        CrashHint? hint = Create().Begin();

        Assert.NotNull(hint);
        Assert.Equal(Logs, hint.Folder);
        string text = File.ReadAllText(hint.LogPath);
        Assert.Contains("Source: unclean-exit", text, StringComparison.Ordinal);
        Assert.Contains(
            "No exception was caught; the app was killed or the web view stopped. Previous start: " + Stamp(start) + ".",
            text,
            StringComparison.Ordinal);
        Assert.Single(LogFiles());
    }

    // AC-8: a malformed session file counts as no session.
    [Theory]
    [InlineData("garbage")]
    [InlineData("running")]
    [InlineData("running not-a-date")]
    [InlineData("stopped 2026-11-18T09:00:00.0000000Z")]
    [InlineData("running 2026-11-18T09:00:00.0000000+01:00")]
    [InlineData("")]
    public void Begin_MalformedSession_CountsAsNone(string content)
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(SessionPath, content);

        Assert.Null(Create().Begin());
        Assert.Equal("running " + Stamp(now), File.ReadAllText(SessionPath));
        Assert.Empty(LogFiles());
    }

    // AC-8: an unreadable session (a folder in its place) counts as none and never throws.
    [Fact]
    public void Begin_UnreadableSession_CountsAsNone()
    {
        Directory.CreateDirectory(SessionPath);
        CrashLog log = Create();

        Assert.Null(log.Begin());
        log.End();
        Assert.True(Directory.Exists(SessionPath));
        Assert.Empty(LogFiles());
    }

    // AC-8: a session file with bytes that aren't text still counts as none.
    [Fact]
    public void Begin_BinarySession_CountsAsNone()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllBytes(SessionPath, [0xFF, 0xFE, 0x00, 0x01, 0x80]);

        Assert.Null(Create().Begin());
        Assert.StartsWith("running ", File.ReadAllText(SessionPath, Encoding.UTF8), StringComparison.Ordinal);
    }

    // AC-7 (round 2): with a frozen clock, the unclean-exit log is shown once; the start after it shows nothing.
    [Fact]
    public void Begin_UncleanExit_FrozenClock_ShowsTheHintOnce()
    {
        Create().Begin();

        CrashLog second = Create();
        Assert.NotNull(second.Begin());
        second.End();

        Assert.Null(Create().Begin());
        Assert.Single(LogFiles());
    }

    // AC-7 (round 2): a crash in the same millisecond is still shown; the next start after it is not.
    [Fact]
    public void Begin_LogInTheStartMillisecond_ShownOnce()
    {
        CrashLog first = Create();
        first.Begin();
        first.Write("component", new Exception("same millisecond"));

        CrashLog second = Create();
        Assert.NotNull(second.Begin());
        second.End();

        Assert.Null(Create().Begin());
    }

    // AC-4 (round 2): a linked .logs folder is never written through.
    [Fact]
    public void BeginWriteEnd_FolderIsASymlink_WriteNothingThere()
    {
        string target = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(Logs, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // This OS refuses symlinks here; there is nothing to check.
            return;
        }

        CrashLog log = Create();
        Assert.Null(log.Begin());
        Assert.Null(log.Write("component", new Exception("n")));
        log.End();

        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    // AC-5 (round 2): a session file that is a symlink is never written through; its target stays as it was.
    [Fact]
    public void BeginEnd_SessionIsASymlink_TargetUnchanged()
    {
        Directory.CreateDirectory(Logs);
        string target = Path.Combine(root, "target.txt");
        File.WriteAllText(target, "target");
        try
        {
            File.CreateSymbolicLink(SessionPath, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // This OS refuses symlinks here; there is nothing to check.
            return;
        }

        CrashLog log = Create();
        log.Begin();
        log.End();

        Assert.Equal("target", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(SessionPath).LinkTarget);
    }

    // AC-5 (round 2): a folder named like the session file is left alone and nothing throws.
    [Fact]
    public void BeginEnd_SessionIsAFolder_DoNotThrow()
    {
        Directory.CreateDirectory(SessionPath);

        CrashLog log = Create();
        Assert.Null(log.Begin());
        log.End();

        Assert.True(Directory.Exists(SessionPath));
    }

    // Job 274 AC-6: a crash log and the session marker are 0600, the crash folder 0700, and a 0644 marker is 0600 once
    // it is rewritten; no temp file is left behind.
    [Fact]
    public void Write_AndSession_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        CrashLog log = Create();
        Assert.Null(log.Begin());
        string? path = log.Write("test", Thrown(new InvalidOperationException("boom")));

        Assert.NotNull(path);
        Assert.Equal(PrivateMode, File.GetUnixFileMode(path));
        Assert.Equal(PrivateMode, File.GetUnixFileMode(SessionPath));
        Assert.Equal(PrivateMode | UnixFileMode.UserExecute, File.GetUnixFileMode(Logs));

        File.SetUnixFileMode(
            SessionPath, PrivateMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        log.End();

        Assert.Equal("clean " + Stamp(now), File.ReadAllText(SessionPath));
        Assert.Equal(PrivateMode, File.GetUnixFileMode(SessionPath));
        Assert.Equal(
            [Path.GetFileName(path)!, CrashLog.SessionFileName],
            Directory.GetFiles(Logs).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray());
    }
}
