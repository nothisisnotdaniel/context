using System.Text;

namespace Context.Tests;

// 396 AC-4 to AC-6: the update log, in a temp root with a fixed fake clock and home folder.
public sealed class UpdateLogTests : IDisposable
{
    private const string Home = "/home/someone";
    private const string Stamp = "2026-11-23T08:15:30Z";

    private static readonly DateTime Now = new(2026, 11, 23, 8, 15, 30, DateTimeKind.Utc);

    private readonly string root;

    public UpdateLogTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-updatelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    private string Logs => Path.Combine(root, CrashLog.FolderName);

    private string LogPath => Path.Combine(Logs, UpdateLog.FileName);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private UpdateLog Create() => new UpdateLog(root, () => Now, Home);

    private static UpdateOutcome Outcome(UpdateResultKind kind, string version = "1.2.3-develop.4+0123abc", string? found = null) =>
        new UpdateOutcome(Now, version, kind, found, null);

    private string[] Lines() => File.ReadAllText(LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // AC-4: one line per write, "<stamp> <shown version> <outcome text>", in a .logs folder created when missing.
    [Fact]
    public void Write_AppendsOneLinePerOutcome_FolderCreated()
    {
        UpdateLog log = Create();

        log.Write(Outcome(UpdateResultKind.NotInstalled));
        log.Write(Outcome(UpdateResultKind.Downloaded, found: "1.2.4"));
        log.Write(Outcome(UpdateResultKind.NoNewer, version: string.Empty));

        Assert.Equal(
            [
                Stamp + " 1.2.3-develop.4 not installed",
                Stamp + " 1.2.3-develop.4 downloaded 1.2.4, applies on exit",
                Stamp + " development build no newer version",
            ],
            Lines());
        Assert.EndsWith("\n", File.ReadAllText(LogPath), StringComparison.Ordinal);
        Assert.Equal(Path.Combine(Logs, "update.log"), log.LogPath);
    }

    // AC-4: the message is cleaned, the home folder becomes ~ (any case), a URL loses its query and fragment, and
    // CR, LF and tab become spaces; the line holds no stack trace.
    [Fact]
    public void Write_CleansTheMessage()
    {
        Exception thrown;
        try
        {
            throw new HttpRequestException(
                "bad\r\nfeed\tat /HOME/Someone/AppData/x https://example.invalid/r?token=SECRET#frag and HTTP://h.invalid/p#f \u0007end");
        }
        catch (HttpRequestException e)
        {
            thrown = e;
        }

        Create().Write(UpdateOutcome.Failure(Now, "1.0.0", thrown));

        string line = Assert.Single(Lines());
        Assert.Equal(
            Stamp + " 1.0.0 failed: HttpRequestException: bad  feed at ~/AppData/x https://example.invalid/r and HTTP://h.invalid/p "
                + SafeText.Placeholder + "end",
            line);
        Assert.DoesNotContain("SECRET", line, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Write_CleansTheMessage), line, StringComparison.Ordinal);
    }

    // AC-4: an outcome's own text is cleaned too, also when it was built elsewhere with a raw error.
    [Fact]
    public void Write_RawError_StillOneCleanLine()
    {
        UpdateOutcome raw = new UpdateOutcome(Now, "1.0.0", UpdateResultKind.Failed, null, "X: a\nb\rc\td /home/SOMEONE/f");

        Create().Write(raw);

        Assert.Equal([Stamp + " 1.0.0 failed: X: a b c d ~/f"], Lines());
    }

    // AC-4 (nits): a home folder cut off at the end of a long message, or written with the other separator, is hidden
    // too, and so is the user part of a URL.
    [Fact]
    public void Write_HidesACutHome_SwappedSeparators_AndUrlUser()
    {
        string message = new string('x', UpdateOutcome.MaxMessage - 8) + " /home/someone/AppData";
        UpdateOutcome cut = UpdateOutcome.Failure(Now, "1.0.0", new InvalidOperationException(message));
        UpdateOutcome swapped = new UpdateOutcome(
            Now, "1.0.0", UpdateResultKind.Failed, null, @"X: at \home\Someone\f via https://user:token@h.invalid/p");

        UpdateLog log = Create();
        log.Write(cut);
        log.Write(swapped);

        string[] lines = Lines();
        Assert.EndsWith(" ~", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("/hom", lines[0], StringComparison.Ordinal);
        Assert.Equal(Stamp + " 1.0.0 failed: X: at ~\\f via https://h.invalid/p", lines[1]);
    }

    // AC-6 (nit): a home folder of only separators is no home folder, so the line is still written.
    [Fact]
    public void Write_HomeIsRoot_StillWritesTheLine()
    {
        new UpdateLog(root, () => Now, "/").Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Equal([Stamp + " 1.2.3-develop.4 no newer version"], Lines());
    }

    // AC-5: only the newest MaxLines lines are kept.
    [Fact]
    public void Write_KeepsTheNewestMaxLines()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(LogPath, string.Concat(Enumerable.Range(1, UpdateLog.MaxLines).Select(i => $"old {i}\n")));

        Create().Write(Outcome(UpdateResultKind.NoNewer));

        string[] lines = Lines();
        Assert.Equal(UpdateLog.MaxLines, lines.Length);
        Assert.Equal("old 2", lines[0]);
        Assert.Equal(Stamp + " 1.2.3-develop.4 no newer version", lines[^1]);
    }

    // AC-5: long lines are dropped oldest first, so the file stays below CrashLog.MaxBytes.
    [Fact]
    public void Write_StaysBelowMaxBytes()
    {
        Directory.CreateDirectory(Logs);
        string longLine = new string('y', 900);
        File.WriteAllText(LogPath, string.Concat(Enumerable.Range(1, 70).Select(i => $"{i} {longLine}\n")));
        Assert.True(new FileInfo(LogPath).Length < CrashLog.MaxBytes);

        UpdateLog log = Create();
        for (int i = 0; i < 10; i++)
        {
            log.Write(UpdateOutcome.Failure(Now, "1.0.0", new IOException(new string('z', 500))));
        }

        Assert.True(new FileInfo(LogPath).Length < CrashLog.MaxBytes);
        string[] lines = Lines();
        Assert.Equal(Stamp + " 1.0.0 failed: IOException: " + new string('z', UpdateOutcome.MaxMessage), lines[^1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("1 ", StringComparison.Ordinal));
    }

    // AC-5: an oversized existing file is replaced by the new line only.
    [Fact]
    public void Write_OversizedFile_ReplacedByTheNewLine()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(LogPath, new string('o', CrashLog.MaxBytes + 10) + "\n");

        Create().Write(Outcome(UpdateResultKind.NotInstalled));

        Assert.Equal([Stamp + " 1.2.3-develop.4 not installed"], Lines());
    }

    // AC-5: an existing file that isn't UTF-8 counts as unreadable and is replaced by the new line only.
    [Fact]
    public void Write_UnreadableContent_ReplacedByTheNewLine()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllBytes(LogPath, [0x6f, 0x6c, 0x64, 0xff, 0xfe, 0x0a]);

        Create().Write(Outcome(UpdateResultKind.NotInstalled));

        Assert.Equal([Stamp + " 1.2.3-develop.4 not installed"], Lines());
    }

    // AC-5: a file that can be written but not read (Unix only) is replaced by the new line only, owner-only.
    [Fact]
    public void Write_FileWithoutReadPermission_ReplacedByTheNewLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Logs);
        File.WriteAllText(LogPath, "old line\n");
        File.SetUnixFileMode(LogPath, UnixFileMode.UserWrite);
        try
        {
            File.ReadAllBytes(LogPath);

            // Readable anyway (running as root): there is nothing to check.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            // Expected: the write below can't read the old lines.
        }

        Create().Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(LogPath));
        Assert.Equal([Stamp + " 1.2.3-develop.4 no newer version"], Lines());
    }

    // AC-6: nothing else in the data folder changes.
    [Fact]
    public void Write_ChangesNoOtherFile()
    {
        File.WriteAllText(Path.Combine(root, "inbox.md"), "# inbox\n");
        Directory.CreateDirectory(Logs);
        File.WriteAllText(Path.Combine(Logs, CrashLog.SessionFileName), "clean 2026-11-23T08:00:00.0000000Z");
        File.WriteAllText(Path.Combine(Logs, "crash-20261123-080000-000.log"), "crash");
        SortedDictionary<string, string> before = FileSnapshot.Take(root, ".logs/update.log");

        Create().Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Equal(before, FileSnapshot.Take(root, ".logs/update.log"));
        Assert.Single(Lines());
    }

    // AC-6: a linked .logs folder is never written through.
    [Fact]
    public void Write_FolderIsASymlink_WritesNothingThere()
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

        Create().Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    // AC-6: a linked update.log is never written through.
    [Fact]
    public void Write_LogIsASymlink_WritesNothingThere()
    {
        Directory.CreateDirectory(Logs);
        string target = Path.Combine(root, "target.txt");
        File.WriteAllText(target, "target");
        try
        {
            File.CreateSymbolicLink(LogPath, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Create().Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Equal("target", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(LogPath).LinkTarget);
    }

    // AC-6: .logs as a file or update.log as a folder: nothing is written and nothing throws.
    [Fact]
    public void Write_FolderIsAFile_OrLogIsAFolder_WritesNothing()
    {
        File.WriteAllText(Logs, "not a folder");
        Create().Write(Outcome(UpdateResultKind.NoNewer));
        Assert.Equal("not a folder", File.ReadAllText(Logs));

        File.Delete(Logs);
        Directory.CreateDirectory(LogPath);
        Create().Write(Outcome(UpdateResultKind.NoNewer));
        Assert.True(Directory.Exists(LogPath));
        Assert.Empty(Directory.GetFileSystemEntries(LogPath));
    }

    // AC-6: a root that can't hold a folder never makes Write throw.
    [Fact]
    public void Write_RootIsAFile_DoesNotThrow()
    {
        string file = Path.Combine(root, "plain");
        File.WriteAllText(file, "x");

        new UpdateLog(file, () => Now, Home).Write(Outcome(UpdateResultKind.NoNewer));

        Assert.Equal("x", File.ReadAllText(file));
    }

    // AC-6: the crash log's rotation keeps only its own crash-*.log files; update.log survives 12 crash logs.
    [Fact]
    public void CrashLogRotation_NeverDeletesTheUpdateLog()
    {
        Create().Write(Outcome(UpdateResultKind.NoNewer));
        byte[] before = File.ReadAllBytes(LogPath);
        DateTime crashNow = Now;
        CrashLog crashLog = new CrashLog(root, () => crashNow, "1.0.0", "TestOS", Home);

        for (int i = 0; i < 12; i++)
        {
            crashNow = Now.AddSeconds(i + 1);
            Assert.NotNull(crashLog.Write("test", new InvalidOperationException("boom " + i)));
        }

        Assert.Equal(CrashLog.MaxLogs, Directory.GetFiles(Logs, "crash-*.log").Length);
        Assert.Equal(before, File.ReadAllBytes(LogPath));
        Assert.Equal(Encoding.UTF8.GetBytes(Stamp + " 1.2.3-develop.4 no newer version\n"), before);
    }
}
