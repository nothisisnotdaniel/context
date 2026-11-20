using System.Globalization;

namespace Context.Tests;

// 315 AC-1 to AC-6: the desktop's start takes the running lock first, so a second copy never touches the crash session.
public sealed class DesktopStartupTests : IDisposable
{
    private static readonly DateTime FixedUtc = new(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime PreviousStart = new(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SessionWritten = new(2026, 11, 19, 8, 0, 5, DateTimeKind.Utc);

    private readonly string root;

    public DesktopStartupTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-startup-" + Guid.NewGuid().ToString("N"));
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

    private CrashLog CreateLog() => new CrashLog(root, () => FixedUtc, "1.2.3-test", "TestOS 1.0 Arm64", "/home/someone");

    private static string Stamp(DateTime time) => time.ToString("O", CultureInfo.InvariantCulture);

    // The session of a copy that is still running (or was killed): `running <previous start>`, with a fixed write time.
    private void WriteRunningSession()
    {
        Directory.CreateDirectory(Logs);
        File.WriteAllText(SessionPath, "running " + Stamp(PreviousStart));
        File.SetLastWriteTimeUtc(SessionPath, SessionWritten);
    }

    private string[] CrashLogs() =>
        Directory.GetFiles(Logs).Select(p => Path.GetFileName(p)!)
            .Where(n => n.StartsWith("crash-", StringComparison.Ordinal)).ToArray();

    private FileStream HoldLock()
    {
        Directory.CreateDirectory(DesktopRequest.Folder(root));
        return PrivateFile.OpenLock(DesktopRequest.LockPath(root));
    }

    // AC-1: a second copy (lock held) gets no hint, writes no crash log and leaves the session byte for byte.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockHeld_NoHint_NoLog_SessionUnchanged(bool isWindows)
    {
        WriteRunningSession();
        byte[] before = File.ReadAllBytes(SessionPath);
        using FileStream held = HoldLock();

        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows);

        Assert.Null(start.Lock);
        Assert.Null(start.Hint);
        Assert.Empty(CrashLogs());
        Assert.Equal(before, File.ReadAllBytes(SessionPath));
        Assert.Equal(SessionWritten, File.GetLastWriteTimeUtc(SessionPath));
    }

    // AC-2: a non-handover second copy that ends with End() still leaves the session alone.
    [Fact]
    public void LockHeld_EndAfterwards_SessionUnchanged()
    {
        WriteRunningSession();
        byte[] before = File.ReadAllBytes(SessionPath);
        using FileStream held = HoldLock();
        CrashLog crashLog = CreateLog();

        DesktopStart start = DesktopStartup.Begin(root, crashLog, isWindows: false);
        crashLog.End();

        Assert.False(start.HandOver);
        Assert.Empty(CrashLogs());
        Assert.Equal(before, File.ReadAllBytes(SessionPath));
        Assert.Equal(SessionWritten, File.GetLastWriteTimeUtc(SessionPath));
    }

    // AC-3: the first copy (lock free) still finds an unclean exit and starts a new session.
    [Fact]
    public void LockFree_RunningSession_UncleanExitHint_NewSession()
    {
        WriteRunningSession();

        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows: false);
        using FileStream? taken = start.Lock;

        Assert.NotNull(taken);
        Assert.NotNull(start.Hint);
        Assert.Single(CrashLogs());
        Assert.Contains("Source: unclean-exit", File.ReadAllText(start.Hint.LogPath), StringComparison.Ordinal);
        string session = File.ReadAllText(SessionPath);
        Assert.StartsWith("running ", session, StringComparison.Ordinal);
        DateTime started = DateTime.Parse(
            session["running ".Length..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.True(started >= FixedUtc, session);
    }

    // AC-4: the returned lock is the running lock: held while open, free once disposed.
    [Fact]
    public void LockFree_LockHeldUntilDisposed()
    {
        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows: true);

        Assert.NotNull(start.Lock);
        Assert.False(DesktopRequest.IsLockFree(root));
        start.Lock.Dispose();
        Assert.True(DesktopRequest.IsLockFree(root));
    }

    // AC-5: with the lock held, Windows writes exactly `show\n` and hands over; elsewhere nothing is written.
    [Fact]
    public void LockHeld_Windows_WritesShowAndHandsOver()
    {
        using FileStream held = HoldLock();

        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows: true);

        Assert.True(start.HandOver);
        Assert.Equal("show\n"u8.ToArray(), File.ReadAllBytes(DesktopRequest.RequestPath(root)));
    }

    [Fact]
    public void LockHeld_NotWindows_NoRequestNoHandOver()
    {
        using FileStream held = HoldLock();

        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows: false);

        Assert.False(start.HandOver);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // AC-6: the first copy never writes a request and never hands over.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockFree_NoRequestNoHandOver(bool isWindows)
    {
        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows);
        using FileStream? taken = start.Lock;

        Assert.False(start.HandOver);
        Assert.False(File.Exists(DesktopRequest.RequestPath(root)));
    }

    // Scope: when the lock can't be made (a file where the folder should be), Begin still runs the crash check.
    [Fact]
    public void LockCantBeMade_BeginStillCalled()
    {
        File.WriteAllText(Path.Combine(root, DesktopRequest.FolderName), "not a folder");
        WriteRunningSession();

        DesktopStart start = DesktopStartup.Begin(root, CreateLog(), isWindows: true);

        Assert.Null(start.Lock);
        Assert.False(start.HandOver);
        Assert.NotNull(start.Hint);
        Assert.NotEqual("running " + Stamp(PreviousStart), File.ReadAllText(SessionPath));
    }
}
