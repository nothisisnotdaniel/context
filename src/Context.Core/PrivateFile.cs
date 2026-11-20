namespace Context;

/// <summary>
/// The one place Context creates files and folders in its data folder (job 274). Off Windows a new file is mode
/// <c>0600</c> and a new folder (and every missing parent) <c>0700</c>, so other local accounts can't read the user's
/// notes; on Windows the plain calls are used. Umask can only remove bits, so these modes hold under any umask.
/// </summary>
internal static class PrivateFile
{
    /// <summary>Owner read and write: <c>0600</c>.</summary>
    internal const UnixFileMode FileBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Owner read, write and search: <c>0700</c>.</summary>
    internal const UnixFileMode FolderBits = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode GroupOrOther =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>
    /// Opens a new file at <paramref name="path"/> for writing (<see cref="FileMode.CreateNew"/>, no
    /// sharing, mode <c>0600</c> off Windows). Throws <see cref="IOException"/> when it exists.
    /// </summary>
    internal static FileStream OpenNew(string path) => OpenNew(path, FileMode.CreateNew, FileAccess.Write);

    // The one unshared open behind OpenNew and OpenLock: a file this creates is 0600 off Windows.
    private static FileStream OpenNew(string path, FileMode mode, FileAccess access)
    {
        FileStreamOptions options = new FileStreamOptions
        {
            Mode = mode,
            Access = access,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileBits;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Opens (or creates, mode <c>0600</c> off Windows) the lock file <paramref name="path"/> with no sharing, so a
    /// second open of it, in this or another process, fails with <see cref="IOException"/> while the returned stream
    /// is open (job 280: the desktop's <c>.desktop/running.lock</c>). Nothing is written to it.
    /// </summary>
    internal static FileStream OpenLock(string path) => OpenNew(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);

    /// <summary>Creates <paramref name="path"/> (it must not exist) holding <paramref name="bytes"/>, like <see cref="OpenNew"/>.</summary>
    internal static void Write(string path, byte[] bytes)
    {
        using FileStream stream = OpenNew(path);
        stream.Write(bytes);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> in place, creating it when missing or truncating it
    /// (no temp file, no move). Off Windows a new file is <c>0600</c> and an existing one is set to <c>0600</c> before
    /// any byte is written. The caller checks that the path is no link.
    /// </summary>
    internal static void Rewrite(string path, byte[] bytes)
    {
        FileStreamOptions options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileBits;
        }

        using FileStream stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stream.SafeFileHandle, FileBits);
        }

        stream.Write(bytes);
    }

    /// <summary>
    /// Creates the folder <paramref name="path"/> and any missing parents (mode <c>0700</c> off Windows). Folders that
    /// already exist keep their mode.
    /// </summary>
    internal static DirectoryInfo CreateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return Directory.CreateDirectory(path);
        }

        // Directory.CreateDirectory(path, mode) gives the mode only to the last folder, so each missing parent is
        // created on its own, outermost first.
        string full = Path.GetFullPath(path);
        Stack<string> missing = new Stack<string>();
        for (string? folder = full; folder is not null && !Directory.Exists(folder); folder = Path.GetDirectoryName(folder))
        {
            missing.Push(folder);
        }

        while (missing.Count > 0)
        {
            Directory.CreateDirectory(missing.Pop(), FolderBits);
        }

        return new DirectoryInfo(full);
    }

    /// <summary>
    /// Off Windows, sets an existing file that has any group or other bits to <c>0600</c>; nothing else changes. A
    /// missing file is left alone.
    /// </summary>
    internal static void Tighten(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        if ((File.GetUnixFileMode(path) & GroupOrOther) != 0)
        {
            File.SetUnixFileMode(path, FileBits);
        }
    }
}
