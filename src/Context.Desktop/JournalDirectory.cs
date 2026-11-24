using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Context;

/// <summary>A journal source directory. Its original handle keeps the directory identity alive through paging.</summary>
internal sealed partial class JournalDirectory : INoteFileSource, IDisposable
{
    private readonly string root;
    private readonly JournalDirectory? parent;
    private readonly SafeFileHandle original;
    private readonly FileId id;
    private readonly string resolvedRoot;
    private readonly Dictionary<string, EntryIdentity> observed = new(StringComparer.Ordinal);
    // Functional storage seams. The observer sees the actual folder handle and its lifetime.
    internal Action<SafeFileHandle>? FolderOpened { get; set; }
    internal Func<string, bool>? EntryAvailable { get; set; }
    internal Func<string, bool>? RemovalAvailable { get; set; }
    internal Func<string, bool>? RecoveryAvailable { get; set; }
    internal Func<SafeFileHandle, long>? FileChangeTime { get; set; }
    internal Func<SafeFileHandle, long>? FolderChangeTime { get; set; }

    private JournalDirectory(string root, JournalDirectory? parent, SafeFileHandle original)
    {
        this.root = root;
        this.parent = parent;
        this.original = original;
        id = DirectoryId(original);
        resolvedRoot = CheckedPath(original, directory: true);
    }

    public static JournalDirectory? Bind(string root) => Bind(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), null);

    public JournalDirectory? BindCalendar() => Bind(Path.Combine(root, CalendarStore.FolderName), this);

    private static JournalDirectory? Bind(string root, JournalDirectory? parent)
    {
        try
        {
            using SafeFileHandle? above = parent?.OpenCurrent();
            return Own(root, parent, OpenHandle(above is null ? root : Path.GetFileName(root), directory: true, above, shareDelete: true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // An editor may outlive its journal panel. Take its own handle to the same verified directory.
    public JournalDirectory Retain(NoteInfo note)
    {
        JournalDirectory retained = Own(root, parent, OpenCurrent(shareDelete: true));
        try
        {
            using FileStream file = OpenNote(note);
            retained.EntryAvailable = EntryAvailable;
            retained.RemovalAvailable = RemovalAvailable;
            retained.RecoveryAvailable = RecoveryAvailable;
            retained.FileChangeTime = FileChangeTime;
            retained.FolderChangeTime = FolderChangeTime;
            WithNoteParent(note, (_, _) =>
            {
                retained.Remember(note.Path, Stamp(file.SafeFileHandle, false));
                return true;
            }, retain: retained);
            return retained;
        }
        catch
        {
            retained.Dispose();
            throw;
        }
    }

    private static JournalDirectory Own(string root, JournalDirectory? parent, SafeFileHandle handle)
    {
        try
        {
            return new JournalDirectory(root, parent, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (original)
        {
            original.Dispose();
        }
    }

    private SafeFileHandle OpenCurrent(bool enumerate = false, bool shareDelete = false)
    {
        // Compare live directories, then traverse from the very handle that passed the comparison.
        using SafeFileHandle? above = parent?.OpenCurrent();
        lock (original)
        {
            if (original.IsClosed)
            {
                throw Ineligible();
            }
            SafeFileHandle current = OpenHandle(above is null ? root : Path.GetFileName(root), directory: true, above,
                shareDelete: shareDelete, enumerate: enumerate);
            try
            {
                return DirectoryId(current) == id ? current : throw Ineligible();
            }
            catch
            {
                current.Dispose();
                throw;
            }
        }
    }

    public IReadOnlyList<NoteInfo> ListNotes()
    {
        using SafeFileHandle directory = OpenCurrent(enumerate: true, shareDelete: true);
        return NotesAtLevel(directory, root, null).ToArray();
    }

    private IEnumerable<NoteInfo> NotesAtLevel(SafeFileHandle directory, string path, string? parentName)
    {
        Listing listing = ListDirectory(directory, path);
        foreach ((string name, string file, string? child) in listing.Pages(parentName))
        {
            yield return new NoteInfo(name, listing.Modified(file), Path.Combine(path, file));
            // Only Meetings/<series>/<date> can contribute a nested journal entry.
            if (child is null || !(name.Equals(MeetingNote.FolderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parentName, MeetingNote.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            NoteInfo[] subpages;
            try
            {
                using SafeFileHandle folder = OpenHandle(child, directory: true, directory, shareDelete: true, enumerate: true);
                FolderOpened?.Invoke(folder);
                Remember(Path.Combine(path, child), Stamp(folder, true));
                subpages = NotesAtLevel(folder, Path.Combine(path, child), name).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unavailable subpage folder leaves its siblings eligible.
                continue;
            }
            foreach (NoteInfo subpage in subpages)
            {
                yield return subpage;
            }
        }
    }

    public IEnumerable<(string Name, string? Text)> ReadCalendars() =>
        CalendarNames().Select(name => (name, ReadCalendar(name)));

    private IReadOnlyList<string> CalendarNames()
    {
        try
        {
            using SafeFileHandle directory = OpenCurrent(enumerate: true, shareDelete: true);
            return ListDirectory(directory, root).Calendars.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Consent belongs to the store to which this journal was bound. Reopen its settings for every
    // decision so an atomic settings save (including turning Outlook off) takes effect during paging.
    public bool ReadOutlook()
    {
        lock (original)
        {
            if (original.IsClosed)
            {
                return false;
            }
            using FileStream settings = OpenChildFile(original, SettingsStore.FileName, CheckedPath(original, directory: true));
            return SettingsStore.ReadOutlook(settings);
        }
    }

    public FileStream OpenNote(NoteInfo note) => OpenNote(note, null);

    internal FileStream OpenNote(NoteInfo note, Func<SafeFileHandle>? openFile,
        Func<SafeFileHandle, FileAttributes>? readDirectoryAttributes = null) =>
        WithNoteParent(note, (folder, component) =>
        {
            FileStream file = CheckedStream(openFile?.Invoke() ?? OpenHandle(component, false, folder),
                Path.Join(resolvedRoot, Path.GetRelativePath(root, note.Path)));
            try
            {
                Remember(note.Path, Stamp(file.SafeFileHandle, false));
                return file;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }, readDirectoryAttributes);

    private T WithNoteParent<T>(NoteInfo note, Func<SafeFileHandle, string, T> action,
        Func<SafeFileHandle, FileAttributes>? readDirectoryAttributes = null, JournalDirectory? retain = null)
    {
        string relative = Path.GetRelativePath(root, note.Path);
        if (Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !Path.GetExtension(relative).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw Ineligible();
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        using SafeFileHandle directory = OpenCurrent(enumerate: parts.Length > 1);
        return OpenBelow(directory, root, null, 0);

        T OpenBelow(SafeFileHandle folder, string path, string? parentName, int depth)
        {
            string component = parts[depth];
            if (depth == parts.Length - 1)
            {
                return action(folder, component);
            }

            // A dated file can outlive its series page. Recheck who owns each folder using
            // the editor's pairing rules and descend from the same directory we enumerated.
            (string name, _, string? childFolder) = ListDirectory(folder, path).Pages(parentName).FirstOrDefault(page =>
                page.Child == component && note.Name.StartsWith(page.Name + "/", StringComparison.Ordinal));
            if (childFolder is null)
            {
                throw Ineligible();
            }
            using SafeFileHandle child = OpenHandle(component, directory: true, folder, enumerate: depth + 2 < parts.Length);
            if (child.IsInvalid)
            {
                throw Ineligible();
            }
            if (OperatingSystem.IsWindows())
            {
                CheckWindowsType(child, (readDirectoryAttributes ?? File.GetAttributes)(child), directory: true);
            }
            string childPath = Path.Combine(path, component);
            FolderOpened?.Invoke(child);
            EntryIdentity identity = Stamp(child, true);
            Remember(childPath, identity);
            retain?.Remember(childPath, identity);
            return OpenBelow(child, childPath, name, depth + 1);
        }
    }

    // Pair the volume/file ID with the object's creation time so series can be closed
    // immediately after listing. Edits only advance identities for their own publications.
    private void Remember(string path, EntryIdentity identity)
    {
        lock (original)
        {
            if (original.IsClosed || EntryAvailable?.Invoke(path) == false
                || (observed.TryGetValue(path, out EntryIdentity known) && known != identity))
            {
                throw Ineligible();
            }
            observed[path] = identity;
        }
    }

    public string? ReadCalendar(string name)
    {
        if (name != Path.GetFileName(name) || !Path.GetExtension(name).Equals(".ics", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            using SafeFileHandle folder = OpenCurrent();
            using FileStream file = OpenChildFile(folder, name, resolvedRoot);
            return CalendarStore.ReadText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The filesystem resolves this one name in the verified parent, with its own case rules.
    // No intermediate component or file link is followed. Check the type of the returned handle.
    private static FileStream OpenChildFile(SafeFileHandle folder, string name, string expectedDirectory) =>
        CheckedStream(OpenHandle(name, directory: false, folder), expectedDirectory, compareParent: true);

    private static FileStream CheckedStream(SafeFileHandle file, string expectedPath, bool compareParent = false)
    {
        try
        {
            string path = CheckedPath(file, directory: false);
            if (!string.Equals(compareParent ? Path.GetDirectoryName(path) : path, expectedPath, StringComparison.Ordinal))
            {
                throw Ineligible();
            }
            return new FileStream(file, FileAccess.Read);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static IOException Ineligible() => new IOException("Journal source is unavailable.");

    private static SafeFileHandle OpenHandle(string path, bool directory, SafeFileHandle? parent = null,
        bool shareDelete = false, bool enumerate = false)
    {
        if (OperatingSystem.IsWindows())
        {
            if (parent is not null)
            {
                return WindowsOpenAt(parent, path, directory, shareDelete, enumerate);
            }
            const uint Traverse = 0x20, ReadAttributes = 0x80, OpenExisting = 3, BackupSemantics = 0x02000000;
            const uint OpenReparsePoint = 0x00200000;
            const uint ListDirectory = 1;
            return CreateFileW(path, Traverse | ReadAttributes | (enumerate ? ListDirectory : 0), Share(shareDelete), IntPtr.Zero, OpenExisting,
                BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        }

        // Read-only, nonblocking (including FIFOs), no controlling terminal, close-on-exec.
        int flags, directoryFlag, noFollow;
        if (OperatingSystem.IsMacOS())
        {
            flags = 0x0004 | 0x20000 | 0x01000000;
            directoryFlag = 0x100000;
            noFollow = 0x0100;
        }
        else if (OperatingSystem.IsLinux())
        {
            bool arm = RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64;
            directoryFlag = arm ? 1 << 14 : 1 << 16;
            noFollow = arm ? 1 << 15 : 1 << 17;
            flags = 0x0800 | 0x0100 | 0x80000;
        }
        else
        {
            throw Ineligible();
        }
        flags |= noFollow | (directory ? directoryFlag : 0);
        int fd = parent is null ? UnixOpen(path, flags) : UnixOpenAt(parent, path, flags);
        if (fd == -1 && Marshal.GetLastPInvokeError() == 2)
        {
            throw new FileNotFoundException();
        }
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    private static FileShare Share(bool delete) => delete ? FileShare.ReadWrite | FileShare.Delete : FileShare.ReadWrite;

    private static SafeFileHandle WindowsOpenAt(SafeFileHandle parent, string component, bool directory, bool shareDelete, bool enumerate,
        bool deleteAccess = false)
    {
        const uint ReadData = 1, Traverse = 0x20, ReadAttributes = 0x80, Synchronize = 0x100000;
        const uint DirectoryFile = 1, NonDirectoryFile = 0x40, SynchronousIo = 0x20, OpenReparsePoint = 0x00200000;
        const uint DontReparse = 0x1000, CaseInsensitive = 0x40, ListDirectory = 1;
        ushort bytes = checked((ushort)(component.Length * sizeof(char)));
        GCHandle text = GCHandle.Alloc(component, GCHandleType.Pinned);
        GCHandle name = default;
        try
        {
            name = GCHandle.Alloc(new UnicodeString { Length = bytes, MaximumLength = bytes, Buffer = text.AddrOfPinnedObject() },
                GCHandleType.Pinned);
            ObjectAttributes attributes = new()
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = name.AddrOfPinnedObject(),
                Attributes = DontReparse | CaseInsensitive,
            };
            const uint Delete = 0x10000;
            uint access = deleteAccess ? Delete : directory ? Traverse | (enumerate ? ListDirectory : 0) : ReadData;
            int status = NtOpenFile(out SafeFileHandle child, ReadAttributes | Synchronize | access,
                ref attributes, out _, Share(shareDelete), SynchronousIo | OpenReparsePoint | (directory ? DirectoryFile : NonDirectoryFile));
            if (status != 0)
            {
                child.Dispose();
                if (status is unchecked((int)0xc0000034) or unchecked((int)0xc000003a))
                {
                    throw new FileNotFoundException();
                }
                throw Ineligible();
            }
            return child;
        }
        finally
        {
            if (name.IsAllocated)
            {
                name.Free();
            }
            text.Free();
            GC.KeepAlive(parent);
        }
    }

    private static void CheckWindowsType(SafeFileHandle handle, FileAttributes attributes, bool directory)
    {
        const uint Disk = 1;
        FileAttributes type = attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device);
        if (GetFileType(handle) != Disk || type != (directory ? FileAttributes.Directory : 0))
        {
            throw Ineligible();
        }
    }

    private static string CheckedPath(SafeFileHandle handle, bool directory)
    {
        if (handle.IsInvalid)
        {
            throw Ineligible();
        }
        if (OperatingSystem.IsWindows())
        {
            CheckWindowsType(handle, File.GetAttributes(handle), directory);
            StringBuilder path = new(512);
            uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            if (length > path.Capacity && length <= 32768)
            {
                path.Capacity = (int)length;
                length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            }
            return length > 0 && length < path.Capacity ? path.ToString() : throw Ineligible();
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacPath(handle, directory);
        }
        _ = LinuxInfo(handle, directory);
        return new FileInfo(DescriptorPath(handle)).LinkTarget ?? throw Ineligible();
    }

    private static string DescriptorPath(SafeFileHandle handle) =>
        "/proc/self/fd/" + handle.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);

    private static LinuxStat LinuxInfo(SafeFileHandle handle, bool directory)
    {
        const int AtEmptyPath = 0x1000, TypeAndInode = 0x101;
        if (Statx(handle, "", AtEmptyPath, TypeAndInode, out LinuxStat stat) != 0 || (stat.Mask & TypeAndInode) != TypeAndInode
            || (stat.Mode & 0xf000) != (directory ? 0x4000 : 0x8000))
        {
            throw Ineligible();
        }
        return stat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileId
    {
        private readonly ulong volume;
        private readonly ulong low;
        private readonly ulong high;

        public FileId(ulong volume, ulong low, ulong high)
        {
            this.volume = volume;
            this.low = low;
            this.high = high;
        }
    }

    private static FileId DirectoryId(SafeFileHandle handle) => Identity(handle, directory: true);

    private readonly record struct EntryIdentity(FileId Id, long SecondsOrTicks, uint Nanoseconds, bool ChangeTime = false);

    private EntryIdentity Stamp(SafeFileHandle handle, bool directory)
    {
        FileId fileId = Identity(handle, directory);
        if ((directory ? FolderChangeTime : FileChangeTime) is { } changeTime)
        {
            return new(fileId, changeTime(handle), 0, ChangeTime: true);
        }
        if (OperatingSystem.IsLinux())
        {
            // Older volumes may only supply ctime. A later metadata change there
            // invalidates the observation conservatively.
            const uint Birth = 0x800, Change = 0x80;
            if (Statx(handle, "", 0x1000, (int)(Birth | Change), out LinuxStat stat) != 0)
            {
                throw Ineligible();
            }
            if ((stat.Mask & Birth) != 0)
            {
                return new(fileId, stat.BirthSeconds, stat.BirthNanoseconds);
            }
            return (stat.Mask & Change) != 0 ? new(fileId, stat.ChangeSeconds, stat.ChangeNanoseconds, true) : throw Ineligible();
        }
        return new(fileId, File.GetCreationTimeUtc(handle).Ticks, 0);
    }

    private static FileId Identity(SafeFileHandle handle, bool directory)
    {
        if (handle.IsInvalid)
        {
            throw Ineligible();
        }
        if (OperatingSystem.IsWindows())
        {
            CheckWindowsType(handle, File.GetAttributes(handle), directory);
            const int FileIdInfo = 18;
            return GetFileInformationByHandleEx(handle, FileIdInfo, out FileId id, (uint)Marshal.SizeOf<FileId>()) ? id : throw Ineligible();
        }
        if (OperatingSystem.IsMacOS())
        {
            // ATTR_CMN_FSID (two 32-bit values), OBJTYPE, FILEID, in Darwin getattrlist order.
            MacAttributes attributes = new() { Count = 5, Common = 0x4 | 0x8 | 0x02000000 };
            byte[] result = new byte[24];
            if (FGetAttrList(handle, ref attributes, result, (nuint)result.Length, 0) != 0
                || BitConverter.ToUInt32(result) != result.Length || BitConverter.ToUInt32(result, 12) != (directory ? 2 : 1))
            {
                throw Ineligible();
            }
            return new FileId(BitConverter.ToUInt64(result, 4), BitConverter.ToUInt64(result, 16), 0);
        }
        LinuxStat stat = LinuxInfo(handle, directory);
        return new FileId(((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor, stat.Inode, 0);
    }

    private static string MacPath(SafeFileHandle handle, bool directory)
    {
        // Darwin packs length, vnode type, then an attrreference (offset from itself and byte count).
        MacAttributes attributes = new() { Count = 5, Common = 0x00000008 | 0x08000000 }; // OBJTYPE | FULLPATH
        byte[] result = new byte[4096];
        if (FGetAttrList(handle, ref attributes, result, (nuint)result.Length, 0) != 0)
        {
            throw Ineligible();
        }
        int size = BitConverter.ToInt32(result, 0);
        int type = BitConverter.ToInt32(result, 4);
        int offset = BitConverter.ToInt32(result, 8);
        int count = BitConverter.ToInt32(result, 12);
        if (type != (directory ? 2 : 1) || size < 16 || size > result.Length || offset < 8
            || offset > size - 8 || count < 2 || count > size - 8 - offset || result[8 + offset + count - 1] != 0)
        {
            throw Ineligible();
        }
        return Encoding.UTF8.GetString(result, 8 + offset, count - 1);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct MacAttributes
    {
        [FieldOffset(0)] public ushort Count;
        [FieldOffset(4)] public uint Common;
        [FieldOffset(16)] public uint File;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public long Size;
        [FieldOffset(80)] public long BirthSeconds;
        [FieldOffset(88)] public uint BirthNanoseconds;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtOpenFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock result, FileShare share, uint options);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileId info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetDirectoryInformation(SafeFileHandle directory, int infoClass, [Out] byte[] result, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int UnixOpenAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int UnixMakeDirectoryAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);

    [DllImport("libc", EntryPoint = "openat")]
    private static extern int UnixCreateAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, uint mode);

    // Darwin arm64 passes openat's variadic mode on the stack; its first three arguments are fixed.
    [DllImport("libc", EntryPoint = "openat")]
    private static extern int MacArmCreateAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags,
        nint unused3, nint unused4, nint unused5, nint unused6, nint unused7, uint mode);

    private static int CreateAt(SafeFileHandle parent, string name, int flags, uint mode) =>
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? MacArmCreateAt(parent, name, flags, 0, 0, 0, 0, 0, mode) : UnixCreateAt(parent, name, flags, mode);

    [DllImport("libc", EntryPoint = "unlinkat")]
    private static extern int UnixUnlinkAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("libc", EntryPoint = "renameat2")]
    private static extern int LinuxRenameAt(SafeFileHandle from, string source, SafeFileHandle to, string target, uint flags);

    [DllImport("libc", EntryPoint = "renameatx_np")]
    private static extern int MacRenameAt(SafeFileHandle from, string source, SafeFileHandle to, string target, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref int value, uint size);

    [DllImport("libc", EntryPoint = "statx")]
    private static extern int Statx(SafeFileHandle file, string path, int flags, uint mask, out LinuxStat stat);

    [DllImport("libc", EntryPoint = "fgetattrlist")]
    private static extern int FGetAttrList(SafeFileHandle file, ref MacAttributes attributes, [Out] byte[] result,
        nuint size, nuint options);

    [DllImport("libc", EntryPoint = "getattrlistbulk")]
    private static extern int GetAttrListBulk(SafeFileHandle directory, ref MacAttributes attributes, [Out] byte[] result,
        nuint size, ulong options);
}
