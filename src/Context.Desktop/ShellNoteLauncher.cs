using System.Diagnostics;

namespace Context;

/// <summary>
/// The real <see cref="INoteLauncher"/>: hands the path to the OS shell (<c>UseShellExecute</c>), so the file opens in
/// its default app. The path is the only input: it is the <c>FileName</c>, and no arguments are ever passed.
/// </summary>
internal sealed class ShellNoteLauncher : INoteLauncher
{
    public void Launch(string path)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        };
        using Process? process = Process.Start(start);
    }
}
