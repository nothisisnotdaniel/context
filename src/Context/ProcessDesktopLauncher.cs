using System.Diagnostics;

namespace Context;

/// <summary>
/// The real <see cref="IDesktopLauncher"/>: on macOS <c>/usr/bin/open -b com.nothisisnotdaniel.context</c>, on Windows
/// the installed <c>%LOCALAPPDATA%\ContextDesktop\current\Context.Desktop.exe</c>; anything else is not supported.
/// A fixed program and argument list, never a shell. The only command-line file that names
/// <c>System.Diagnostics</c>.
/// </summary>
internal sealed class ProcessDesktopLauncher : IDesktopLauncher
{
    /// <summary>The macOS bundle id of the desktop app.</summary>
    public const string BundleId = "com.nothisisnotdaniel.context";

    /// <summary>The Windows install's pack id (its folder under <c>%LOCALAPPDATA%</c>).</summary>
    public const string PackId = "ContextDesktop";

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);

    public void Launch()
    {
        if (OperatingSystem.IsMacOS())
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-b");
            start.ArgumentList.Add(BundleId);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("open did not start.");
            if (!process.WaitForExit(OpenTimeout) || process.ExitCode != 0)
            {
                throw new InvalidOperationException("open failed.");
            }

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            string exe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                PackId,
                "current",
                "Context.Desktop.exe");
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("The desktop app is not installed.");
            }

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
            };
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("The desktop app did not start.");
            return;
        }

        throw new PlatformNotSupportedException("The desktop app can't be started on this OS.");
    }
}
