using System.Diagnostics;
using System.Runtime.Versioning;

namespace Context;

/// <summary>
/// The real <see cref="IScreenCapture"/> on macOS (job 260): runs <c>/usr/sbin/screencapture -i -x &lt;temp&gt;</c>
/// (interactive area picker, no sound) with fixed arguments and no shell, where <c>&lt;temp&gt;</c> is an app-made
/// <c>&lt;root&gt;/.tmp/screenshot-&lt;guid&gt;.png</c>. The file is read once (at most
/// <see cref="NoteAttachments.MaxBytes"/> plus one byte) and removed when it is closed. No file means cancelled.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacScreenCapture : IScreenCapture
{
    private const string Tool = "/usr/sbin/screencapture";

    private readonly string root;

    public MacScreenCapture(string root)
    {
        this.root = root;
    }

    public async Task<byte[]?> CaptureAreaAsync()
    {
        string folder = Path.Combine(root, ".tmp");
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, $"screenshot-{Guid.NewGuid():N}.png");
        try
        {
            ProcessStartInfo start = new ProcessStartInfo(Tool, ["-i", "-x", temp])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process? process = Process.Start(start))
            {
                if (process is null)
                {
                    return null;
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
            }

            return ReadAndRemove(temp);
        }
        finally
        {
            Remove(temp);
        }
    }

    // Reads the picked area and removes the file as it is closed; a file that is no regular file is not read.
    private static byte[]? ReadAndRemove(string path)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null)
        {
            return null;
        }

        using FileStream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Delete, 4096, FileOptions.DeleteOnClose);
        byte[] buffer = new byte[NoteAttachments.MaxBytes + 1];
        int total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return total == 0 ? null : buffer[..total];
    }

    // Removes a temp file left behind (an error before it was read); never throws.
    private static void Remove(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Delete, 1, FileOptions.DeleteOnClose);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Left in .tmp, which no note lists.
        }
    }
}
