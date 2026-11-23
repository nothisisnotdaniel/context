using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Context;

/// <summary>Opens a listed Markdown file and verifies the type and location of the handle that will supply its bytes.</summary>
internal static class JournalNoteFile
{
    public static FileStream Open(string root, NoteInfo note, Func<SafeFileHandle>? openFile = null,
        Func<SafeFileHandle, FileAttributes>? readDirectoryAttributes = null)
    {
        string relative = Path.GetRelativePath(root, note.Path);
        if (Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !Path.GetExtension(relative).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw Ineligible();
        }

        using SafeFileHandle folder = OpenHandle(root, directory: true);
        string expected = Path.Join(CheckedPath(folder, directory: true), relative);
        SafeFileHandle file = OpenBelow(folder, relative, openFile, readDirectoryAttributes ?? File.GetAttributes);
        try
        {
            // Both names come from live handles. A replacement at the listed path cannot vouch for another stream.
            if (!string.Equals(CheckedPath(file, directory: false), expected, StringComparison.Ordinal))
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

    private static IOException Ineligible() => new IOException("Not a regular note file.");

    // Open one component at a time from its pinned parent. Never resolve a descendant through a directory link.
    private static SafeFileHandle OpenBelow(SafeFileHandle parent, string relative, Func<SafeFileHandle>? openFile,
        Func<SafeFileHandle, FileAttributes> readDirectoryAttributes)
    {
        int separator = relative.IndexOf(Path.DirectorySeparatorChar);
        if (separator < 0)
        {
            return openFile?.Invoke() ?? OpenHandle(relative, directory: false, parent);
        }
        using SafeFileHandle child = OpenHandle(relative[..separator], directory: true, parent);
        if (child.IsInvalid)
        {
            throw Ineligible();
        }
        if (OperatingSystem.IsWindows())
        {
            CheckWindowsType(child, readDirectoryAttributes(child), directory: true);
        }
        return OpenBelow(child, relative[(separator + 1)..], openFile, readDirectoryAttributes);
    }

    private static SafeFileHandle OpenHandle(string path, bool directory, SafeFileHandle? parent = null)
    {
        if (OperatingSystem.IsWindows())
        {
            if (parent is not null)
            {
                return WindowsOpenAt(parent, path, directory);
            }
            const uint Traverse = 0x20, ReadAttributes = 0x80, OpenExisting = 3, BackupSemantics = 0x02000000;
            return CreateFileW(path, Traverse | ReadAttributes, FileShare.ReadWrite, IntPtr.Zero, OpenExisting,
                BackupSemantics, IntPtr.Zero);
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
        flags |= directory ? directoryFlag : 0;
        int fd = parent is null ? UnixOpen(path, flags) : UnixOpenAt(parent, path, flags | noFollow);
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    private static SafeFileHandle WindowsOpenAt(SafeFileHandle parent, string component, bool directory)
    {
        const uint ReadData = 1, Traverse = 0x20, ReadAttributes = 0x80, Synchronize = 0x100000;
        const uint DirectoryFile = 1, NonDirectoryFile = 0x40, SynchronousIo = 0x20, OpenReparsePoint = 0x00200000;
        const uint DontReparse = 0x1000;
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
                Attributes = DontReparse,
            };
            int status = NtOpenFile(out SafeFileHandle child, ReadAttributes | Synchronize | (directory ? Traverse : ReadData),
                ref attributes, out _, FileShare.ReadWrite, SynchronousIo | OpenReparsePoint | (directory ? DirectoryFile : NonDirectoryFile));
            if (status != 0)
            {
                child.Dispose();
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
        const int AtEmptyPath = 0x1000, StatxType = 1;
        if (Statx(handle, "", AtEmptyPath, StatxType, out LinuxStat stat) != 0 || (stat.Mask & StatxType) == 0
            || (stat.Mode & 0xf000) != (directory ? 0x4000 : 0x8000))
        {
            throw Ineligible();
        }
        string fd = handle.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
        return new FileInfo("/proc/self/fd/" + fd).LinkTarget ?? throw Ineligible();
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
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint size, uint flags);

    [DllImport("libc", EntryPoint = "open")]
    private static extern int UnixOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat")]
    private static extern int UnixOpenAt(SafeFileHandle parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("libc", EntryPoint = "statx")]
    private static extern int Statx(SafeFileHandle file, string path, int flags, uint mask, out LinuxStat stat);

    [DllImport("libc", EntryPoint = "fgetattrlist")]
    private static extern int FGetAttrList(SafeFileHandle file, ref MacAttributes attributes, [Out] byte[] result,
        nuint size, nuint options);
}
