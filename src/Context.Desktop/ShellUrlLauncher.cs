using System.Diagnostics;

namespace Context;

/// <summary>
/// The real <see cref="IUrlLauncher"/> (job 354): hands the checked URL to the OS shell (<c>UseShellExecute</c>), so it
/// opens in the default browser or mail app. <see cref="Uri.AbsoluteUri"/> is the only input: it is the
/// <c>FileName</c>, and no arguments are ever passed.
/// </summary>
internal sealed class ShellUrlLauncher : IUrlLauncher
{
    public void Open(Uri uri)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        };
        using Process? process = Process.Start(start);
    }
}
