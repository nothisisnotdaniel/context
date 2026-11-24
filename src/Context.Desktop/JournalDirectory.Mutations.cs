using Microsoft.Win32.SafeHandles;

namespace Context;

internal sealed partial class JournalDirectory
{
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

    private static void RemoveAt(SafeFileHandle parentHandle, string leaf, bool directory, SafeFileHandle created, bool plainHistory = false)
    {
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle file = WindowsOpenAt(parentHandle, leaf, directory, shareDelete: true, enumerate: false, deleteAccess: true);
            FileId id = Identity(file, directory);
            if (Identity(created, directory) != id)
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

        using SafeFileHandle current = OpenHandle(leaf, directory, parentHandle);
        if (Identity(current, directory) != Identity(created, directory))
        {
            throw Ineligible();
        }
        // Claim the entry, check the claimed object, and unlink only that object.
        string claimed = TempName(plainHistory);
        RenameNew(parentHandle, leaf, parentHandle, claimed, directory);
        int removeDirectory = OperatingSystem.IsMacOS() ? 0x80 : 0x200;
        try
        {
            using SafeFileHandle claimedFile = OpenHandle(claimed, directory, parentHandle);
            if (Identity(claimedFile, directory) != Identity(created, directory)
                || UnixUnlinkAt(parentHandle, claimed, directory ? removeDirectory : 0) != 0)
            {
                throw Ineligible();
            }
        }
        catch
        {
            RenameNew(parentHandle, claimed, parentHandle, leaf, directory);
            throw;
        }
    }

    // Conversion creates only new files. Keep each file and its parent open so cleanup can
    // address that creation even when the configured store has since been moved elsewhere.
    internal CreatedFile WriteSealedCopy(string path, byte[] bytes)
    {
        using SafeFileHandle directory = OpenCurrent();
        return InContainingFolder(directory, root, path, (folder, leaf) => WriteNew(folder, leaf, bytes));
    }

    private static CreatedFile WriteNew(SafeFileHandle folder, string name, byte[] bytes, Action? beforeCheck = null) =>
        WriteNew(folder, name, stream => stream.Write(bytes), beforeCheck);

    private static CreatedFile WriteNew(SafeFileHandle folder, string name, Action<FileStream> write, Action? beforeCheck = null,
        bool plainHistory = false)
    {
        string temp = TempName(plainHistory);
        string? windowsPath = OperatingSystem.IsWindows() ? CheckedPath(folder, true) : null;
        FileStream stream = windowsPath is null ? CreateUnixTemp(folder, temp) : PrivateFile.OpenNew(Path.Combine(windowsPath, temp), FileShare.Read | FileShare.Delete);
        SafeFileHandle? ownedParent = null;
        string ownedName = temp;
        try
        {
            write(stream);
            stream.Flush();
            ownedParent = ReopenFolder(folder);
            MoveOwned(folder, temp, folder, name, false, stream.SafeFileHandle, plainHistory);
            ownedName = name;
            beforeCheck?.Invoke();
            VerifyOwned(ownedParent, name, false, stream.SafeFileHandle);
            return new CreatedFile(ownedParent, name, stream);
        }
        catch
        {
            try
            {
                RemoveAt(folder, ownedName, false, stream.SafeFileHandle, plainHistory);
            }
            finally
            {
                ownedParent?.Dispose();
                stream.Dispose();
            }
            throw;
        }
    }

    private static string TempName(bool plainHistory = false) =>
        "." + Guid.NewGuid().ToString("N") + (plainHistory ? ".tmp.md" : ".tmp");

    private static void VerifyOwned(SafeFileHandle parent, string name, bool directory, SafeFileHandle expected)
    {
        using SafeFileHandle current = OpenHandle(name, directory, parent, shareDelete: true);
        if (Identity(current, directory) != Identity(expected, directory))
        {
            throw Ineligible();
        }
    }

    private static void MoveOwned(SafeFileHandle source, string from, SafeFileHandle destination, string to,
        bool directory, SafeFileHandle original, bool plainHistory = false)
    {
        // Claim and check the actual object before publishing it in another directory.
        // Every move, including recovery, requires a free destination name.
        FileId expected = Identity(original, directory);
        string claimed = TempName(plainHistory);
        RenameNew(source, from, source, claimed, directory);
        try
        {
            using SafeFileHandle item = OpenHandle(claimed, directory, source, shareDelete: true);
            if (Identity(item, directory) != expected)
            {
                throw Ineligible();
            }
            RenameNew(source, claimed, destination, to, directory);
        }
        catch
        {
            RenameNew(source, claimed, source, from, directory);
            throw;
        }
        // Publication transfers ownership to the caller. Its recovery record must
        // name the destination before any further I/O, including a verification read.
    }

    private static SafeFileHandle ReopenFolder(SafeFileHandle folder, bool shareDelete = true, bool enumerate = false)
    {
        SafeFileHandle reopened = OperatingSystem.IsWindows()
            ? OpenHandle(CheckedPath(folder, true), true, shareDelete: shareDelete, enumerate: enumerate)
            : OpenHandle(".", true, folder, enumerate: enumerate);
        try
        {
            return DirectoryId(reopened) == DirectoryId(folder) ? reopened : throw Ineligible();
        }
        catch
        {
            reopened.Dispose();
            throw;
        }
    }

    private static void RenameNew(SafeFileHandle source, string from, SafeFileHandle target, string to, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            // Deny directory renames for the duration of the managed no-overwrite move.
            using SafeFileHandle sourceFolder = ReopenFolder(source, shareDelete: false);
            using SafeFileHandle targetFolder = ReopenFolder(target, shareDelete: false);
            string oldPath = Path.Combine(CheckedPath(sourceFolder, true), from);
            string newPath = Path.Combine(CheckedPath(targetFolder, true), to);
            if (directory)
            {
                Directory.Move(oldPath, newPath);
            }
            else
            {
                File.Move(oldPath, newPath, overwrite: false);
            }
            return;
        }
        const uint MacRenameExclusive = 4, LinuxRenameNoReplace = 1;
        if ((OperatingSystem.IsMacOS() ? MacRenameAt(source, from, target, to, MacRenameExclusive)
            : LinuxRenameAt(source, from, target, to, LinuxRenameNoReplace)) != 0)
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
                RollbackChecked();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The name may already be gone or belong to another file.
            }
        }

        internal void RollbackChecked()
        {
            try
            {
                using SafeFileHandle current = OpenHandle(name, false, parentHandle, shareDelete: true);
                if (Matches(parentHandle, current))
                {
                    RemoveAt(parentHandle, name, false, current);
                }
            }
            catch (FileNotFoundException)
            {
                // This creation is already gone.
            }
        }

        internal bool Matches(SafeFileHandle folder, SafeFileHandle current) =>
            DirectoryId(folder) == DirectoryId(parentHandle) && Identity(current, false) == Identity(file.SafeFileHandle, false);

        public void Dispose()
        {
            file.Dispose();
            parentHandle.Dispose();
        }
    }
}
