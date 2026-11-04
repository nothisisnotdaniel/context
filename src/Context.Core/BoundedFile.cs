using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Context;

/// <summary>
/// Reads a file only when it is a regular file of at most a given size. On Unix the file is opened without blocking,
/// so a FIFO or a terminal named like a note is refused instead of hanging the reader.
/// </summary>
internal static class BoundedFile
{
    private const int MacNonBlock = 0x0004;
    private const int MacCloseOnExec = 0x0100_0000;
    private const int LinuxNonBlock = 0x0800;
    private const int LinuxCloseOnExec = 0x0008_0000;
    private const int NoSuchFile = 2;
    private const int NotADirectory = 20;
    private const int AccessDenied = 13;

    /// <summary>
    /// The bytes of <paramref name="path"/>. Throws <see cref="FileNotFoundException"/> when it is missing,
    /// <see cref="UnauthorizedAccessException"/> when access is denied, and <see cref="IOException"/> when it is not a
    /// regular file, is larger than <paramref name="maxBytes"/> or can't be read. Exception messages are fixed texts.
    /// </summary>
    public static byte[] Read(string path, long maxBytes)
    {
        using FileStream stream = Open(path);
        if (!stream.CanSeek)
        {
            throw new IOException("Not a regular file.");
        }

        if (stream.Length > maxBytes)
        {
            throw new IOException("Too large.");
        }

        using MemoryStream copy = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (copy.Length + read > maxBytes)
            {
                throw new IOException("Too large.");
            }

            copy.Write(buffer, 0, read);
        }

        return copy.ToArray();
    }

    private static FileStream Open(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.Device) != 0)
            {
                throw new IOException("Not a regular file.");
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        int flags = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
            ? MacNonBlock | MacCloseOnExec
            : LinuxNonBlock | LinuxCloseOnExec;
        int handle = OpenFile(path, flags);
        if (handle < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw error switch
            {
                NoSuchFile or NotADirectory => new FileNotFoundException("No such file."),
                AccessDenied => new UnauthorizedAccessException("Access denied."),
                _ => new IOException("Can't open the file."),
            };
        }

        SafeFileHandle safe = new SafeFileHandle(handle, ownsHandle: true);
        try
        {
            return new FileStream(safe, FileAccess.Read);
        }
        catch
        {
            safe.Dispose();
            throw;
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
