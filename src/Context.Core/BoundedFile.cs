namespace Context;

/// <summary>
/// Reads a file only when it is a seekable file of at most a given size, in managed code only. A file that reports
/// length 0 is returned as empty without being opened: a FIFO always reports 0 when opening it could block (no writer),
/// so a FIFO named like a note never hangs the reader. Anything opened that is not seekable is refused.
/// </summary>
internal static class BoundedFile
{
    /// <summary>
    /// The bytes of <paramref name="path"/>. Throws <see cref="FileNotFoundException"/> when it is missing,
    /// <see cref="UnauthorizedAccessException"/> when access is denied, and <see cref="IOException"/> when it is not
    /// seekable, is larger than <paramref name="maxBytes"/> or can't be read. Exception messages are fixed texts.
    /// </summary>
    public static byte[] Read(string path, long maxBytes)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("No such file.");
        }

        if (info.Length == 0)
        {
            return [];
        }

        if (info.Length > maxBytes)
        {
            throw new IOException("Too large.");
        }

        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
}
