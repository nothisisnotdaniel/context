using Microsoft.Win32.SafeHandles;

namespace Context;

internal sealed partial class JournalDirectory
{
    // Resolve the selected page's parent chain first. Every removal, including a kept version
    // or an attachment, then uses a single component in the actual directory we opened.
    public void Remove(NoteInfo note, string path, bool directory) =>
        WithNoteParent(note, (parentHandle, _) => InContainingFolder(parentHandle, Path.GetDirectoryName(note.Path)!, path,
            (folder, leaf) =>
            {
                RemoveAt(folder, leaf, directory);
                return true;
            }));

    private static T InContainingFolder<T>(SafeFileHandle parentHandle, string parentPath, string path, Func<SafeFileHandle, string, T> action)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            parentPath = NoteHistory.ExtendedWindowsPath(parentPath);
        }
        string relative = Path.GetRelativePath(parentPath, path);
        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || parts.Any(part => part is "" or "." or ".." || part != Path.GetFileName(part)))
        {
            throw Ineligible();
        }
        return Descend(parentHandle, 0);

        T Descend(SafeFileHandle folder, int index)
        {
            if (index == parts.Length - 1)
            {
                return action(folder, parts[index]);
            }
            using SafeFileHandle next = OpenHandle(parts[index], true, folder);
            _ = DirectoryId(next);
            return Descend(next, index + 1);
        }
    }

    private static void RemoveAt(SafeFileHandle parentHandle, string leaf, bool directory, SafeFileHandle? created = null)
    {
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle file = WindowsOpenAt(parentHandle, leaf, directory, shareDelete: true, enumerate: false, deleteAccess: true);
            FileId id = Identity(file, directory);
            if (created is not null && Identity(created, directory) != id)
            {
                throw Ineligible();
            }
            const int FileDispositionInfo = 4;
            int delete = 1;
            if (!SetFileInformationByHandle(file, FileDispositionInfo, ref delete, sizeof(int)))
            {
                throw Ineligible();
            }
            return;
        }

        if (created is not null)
        {
            using SafeFileHandle current = OpenHandle(leaf, false, parentHandle);
            if (Identity(current, false) != Identity(created, false))
            {
                throw Ineligible();
            }
            // Unix unlinks by name. Claim the entry with a no-replace rename and check the
            // claimed file before unlinking it. If the name changed hands, put it back.
            string claimed = TempName();
            RenameNew(parentHandle, leaf, claimed);
            try
            {
                using SafeFileHandle claimedFile = OpenHandle(claimed, false, parentHandle);
                if (Identity(claimedFile, false) != Identity(created, false) || UnixUnlinkAt(parentHandle, claimed, 0) != 0)
                {
                    throw Ineligible();
                }
            }
            catch
            {
                RenameNew(parentHandle, claimed, leaf);
                throw;
            }
            return;
        }
        int removeDirectory = OperatingSystem.IsMacOS() ? 0x80 : 0x200;
        if (UnixUnlinkAt(parentHandle, leaf, directory ? removeDirectory : 0) != 0)
        {
            throw Ineligible();
        }
    }

    // Conversion creates only new files. Keep each file and its parent open so cleanup can
    // address that creation even when the configured store has since been moved elsewhere.
    internal CreatedFile WriteSealedCopy(string path, byte[] bytes)
    {
        using SafeFileHandle directory = OpenCurrent();
        return InContainingFolder(directory, root, path, (folder, leaf) => WriteNew(folder, leaf, bytes));
    }

    internal FileStream OpenVersion(string path)
    {
        using SafeFileHandle directory = OpenCurrent();
        return InContainingFolder(directory, root, path, (folder, leaf) => OpenChildFile(folder, leaf, CheckedPath(folder, true)));
    }

    private static CreatedFile WriteNew(SafeFileHandle folder, string name, byte[] bytes)
    {
        string temp = TempName();
        string? windowsPath = OperatingSystem.IsWindows() ? CheckedPath(folder, true) : null;
        FileStream stream = windowsPath is null ? CreateUnixTemp(folder, temp) : PrivateFile.OpenNew(Path.Combine(windowsPath, temp), FileShare.Delete);
        SafeFileHandle? ownedParent = null;
        try
        {
            stream.Write(bytes);
            stream.Flush();
            ownedParent = windowsPath is not null ? OpenHandle(windowsPath, true, shareDelete: true) : OpenHandle(".", true, folder);
            if (DirectoryId(ownedParent) != DirectoryId(folder))
            {
                throw Ineligible();
            }
            // The retained parent shares delete. The working Windows folder denies renames
            // through this move; the Unix calls take both names relative to the same fd.
            if (windowsPath is not null)
            {
                File.Move(Path.Combine(windowsPath, temp), Path.Combine(windowsPath, name), overwrite: false);
            }
            else
            {
                RenameNew(folder, temp, name);
            }
            return new CreatedFile(ownedParent, name, stream);
        }
        catch
        {
            try
            {
                RemoveAt(folder, temp, false, stream.SafeFileHandle);
            }
            finally
            {
                ownedParent?.Dispose();
                stream.Dispose();
            }
            throw;
        }
    }

    private static string TempName() => "." + Guid.NewGuid().ToString("N") + ".tmp";

    private static void RenameNew(SafeFileHandle folder, string from, string to)
    {
        const uint MacRenameExclusive = 4, LinuxRenameNoReplace = 1;
        if ((OperatingSystem.IsMacOS() ? MacRenameAt(folder, from, folder, to, MacRenameExclusive)
            : LinuxRenameAt(folder, from, folder, to, LinuxRenameNoReplace)) != 0)
        {
            throw Ineligible();
        }
    }

    private static FileStream CreateUnixTemp(SafeFileHandle folder, string name)
    {
        int flags = OperatingSystem.IsMacOS() ? 0x01000000 | 0x0200 | 0x0800 : 0x80000 | 0x40 | 0x80;
        return PrivateFile.OpenNew(mode => new SafeFileHandle((IntPtr)CreateAt(folder, name, flags | 1, (uint)mode), ownsHandle: true));
    }

    internal sealed class CreatedFile : IDisposable
    {
        private readonly SafeFileHandle parentHandle;
        private readonly string name;
        private readonly FileStream file;

        internal CreatedFile(SafeFileHandle parentHandle, string name, FileStream file)
        {
            this.parentHandle = parentHandle;
            this.name = name;
            this.file = file;
        }

        internal void Rollback()
        {
            try
            {
                RemoveAt(parentHandle, name, false, file.SafeFileHandle);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The name may already be gone or belong to another file.
            }
        }

        public void Dispose()
        {
            file.Dispose();
            parentHandle.Dispose();
        }
    }
}
