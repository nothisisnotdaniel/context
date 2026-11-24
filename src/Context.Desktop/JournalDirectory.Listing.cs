using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Context;

internal sealed partial class JournalDirectory
{
    // Called after this directory is open and verified, immediately before enumerating its handle.
    internal Action<string>? BeforeEnumerate { get; set; }

    private Listing ListDirectory(SafeFileHandle directory, string path, bool strict = false)
    {
        BeforeEnumerate?.Invoke(path);
        Listing listing = new(strict);
        if (OperatingSystem.IsWindows())
        {
            ListWindows(directory, listing);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ListMac(directory, listing);
        }
        else
        {
            ListLinux(directory, listing);
        }
        return listing;
    }

    private sealed class Listing(bool strict)
    {
        private readonly Dictionary<string, (FileAttributes Attributes, DateTime Modified, long Length)> entries = new(StringComparer.Ordinal);

        public IEnumerable<string> Files => entries.Where(e => (e.Value.Attributes & FileAttributes.Directory) == 0).Select(e => e.Key);
        public IEnumerable<string> Folders => entries.Where(e => (e.Value.Attributes & FileAttributes.Directory) != 0).Select(e => e.Key);
        public IEnumerable<(string Name, string Path, string? Child)> Pages(string? parent) =>
            NoteStore.PagesAtLevel(parent, Files, () => Folders);
        public IEnumerable<string> Calendars => entries.Where(e =>
            (e.Value.Attributes & (FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System)) == 0
            && Path.GetExtension(e.Key).Equals(".ics", StringComparison.OrdinalIgnoreCase)).Select(e => e.Key);
        public DateTime Modified(string file) => entries[file].Modified;
        public long Length(string file) => entries[file].Length;

        public void UnreadableEntry()
        {
            if (strict)
            {
                throw Ineligible();
            }
        }

        public void RecordEntry(string name, FileAttributes attributes, DateTime modified, long length)
        {
            if (name is "" or "." or ".." || name != Path.GetFileName(name)
                || (attributes & (FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            {
                return;
            }
            entries.TryAdd(name, (attributes, modified, length));
        }
    }

    private static void ListWindows(SafeFileHandle directory, Listing listing)
    {
        // FILE_FULL_DIR_INFO. Each successful call advances the enumeration on this handle.
        const int FileFullDirectoryInfo = 14, NoMoreFiles = 18, NameOffset = 68;
        byte[] buffer = new byte[16 * 1024];
        while (GetDirectoryInformation(directory, FileFullDirectoryInfo, buffer, (uint)buffer.Length))
        {
            int start = 0;
            while (true)
            {
                ReadOnlySpan<byte> entry = buffer.AsSpan(start);
                if (entry.Length < NameOffset)
                {
                    throw Ineligible();
                }
                int next = BitConverter.ToInt32(entry);
                int size = next == 0 ? entry.Length : next;
                int length = BitConverter.ToInt32(entry[60..]);
                if (size < NameOffset || size > entry.Length || length < 0 || length > size - NameOffset || (length & 1) != 0)
                {
                    throw Ineligible();
                }
                string name = Encoding.Unicode.GetString(entry.Slice(NameOffset, length));
                FileAttributes attributes = (FileAttributes)BitConverter.ToUInt32(entry[56..]);
                long modified = Math.Clamp(BitConverter.ToInt64(entry[24..]), 0, DateTime.MaxValue.ToFileTimeUtc());
                listing.RecordEntry(name, attributes, DateTime.FromFileTime(modified), BitConverter.ToInt64(entry[40..]));
                if (next == 0)
                {
                    break;
                }
                start += next;
            }
        }
        if (Marshal.GetLastPInvokeError() != NoMoreFiles)
        {
            throw Ineligible();
        }
    }

    private static void ListMac(SafeFileHandle directory, Listing listing)
    {
        const uint Name = 1, ObjectType = 8, Modified = 0x400, Flags = 0x40000, ReturnedAttributes = 0x80000000;
        const ulong PackInvalidAttributes = 8;
        const uint DataLength = 0x200;
        MacAttributes request = new() { Count = 5, Common = ReturnedAttributes | Name | ObjectType | Modified | Flags, File = DataLength };
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = GetAttrListBulk(directory, ref request, buffer, (nuint)buffer.Length, PackInvalidAttributes)) > 0)
        {
            int start = 0;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> remaining = buffer.AsSpan(start);
                if (remaining.Length < 24)
                {
                    throw Ineligible();
                }
                int size = BitConverter.ToInt32(remaining);
                if (size < 24 || size > remaining.Length)
                {
                    throw Ineligible();
                }
                ReadOnlySpan<byte> entry = remaining[..size];
                start += size;
                // Length and returned attribute_set_t, then a name reference, vnode type, timespec and BSD flags.
                uint returned = BitConverter.ToUInt32(entry[4..]);
                if ((returned & (Name | ObjectType)) != (Name | ObjectType) || size < 56)
                {
                    listing.UnreadableEntry();
                    continue;
                }
                uint type = BitConverter.ToUInt32(entry[32..]);
                if (type is not (1 or 2))
                {
                    continue;
                }
                const int Reference = 24;
                int offset = BitConverter.ToInt32(entry[Reference..]);
                int length = BitConverter.ToInt32(entry[28..]);
                if (offset < 56 - Reference || offset > size - Reference || length < 2 || length > size - Reference - offset
                    || entry[Reference + offset + length - 1] != 0)
                {
                    throw Ineligible();
                }
                string name = Encoding.UTF8.GetString(entry.Slice(Reference + offset, length - 1));
                FileAttributes attributes = type == 2 ? FileAttributes.Directory : FileAttributes.Normal;
                if (name.StartsWith('.') || ((returned & Flags) != 0 && (BitConverter.ToUInt32(entry[52..]) & 0x8000) != 0))
                {
                    attributes |= FileAttributes.Hidden;
                }
                bool hasLength = size >= 64 && (BitConverter.ToUInt32(entry[16..]) & DataLength) != 0;
                if (type == 1 && !hasLength)
                {
                    listing.UnreadableEntry();
                }
                long dataLength = type == 1 && hasLength ? BitConverter.ToInt64(entry[56..]) : 0;
                listing.RecordEntry(name, attributes, UnixModified(BitConverter.ToInt64(entry[36..]), BitConverter.ToInt64(entry[44..])), dataLength);
            }
        }
        if (count < 0)
        {
            throw Ineligible();
        }
    }

    private static void ListLinux(SafeFileHandle directory, Listing listing)
    {
        const int AtSymlinkNoFollow = 0x100, TypeModifiedAndSize = 0x241;
        foreach (string entry in Directory.EnumerateFileSystemEntries(DescriptorPath(directory)))
        {
            string name = Path.GetFileName(entry);
            if (Statx(directory, name, AtSymlinkNoFollow, TypeModifiedAndSize, out LinuxStat stat) != 0 || (stat.Mask & 1) == 0)
            {
                listing.UnreadableEntry();
                continue;
            }
            int type = stat.Mode & 0xf000;
            if (type is 0x4000 or 0x8000)
            {
                if (type == 0x8000 && (stat.Mask & 0x200) == 0)
                {
                    listing.UnreadableEntry();
                }
                FileAttributes attributes = type == 0x4000 ? FileAttributes.Directory : FileAttributes.Normal;
                listing.RecordEntry(name, name.StartsWith('.') ? attributes | FileAttributes.Hidden : attributes,
                    UnixModified(stat.ModifiedSeconds, stat.ModifiedNanoseconds), stat.Size);
            }
        }
    }

    private static DateTime UnixModified(long seconds, long nanoseconds)
    {
        long representable = Math.Clamp(seconds, DateTimeOffset.MinValue.ToUnixTimeSeconds(), DateTimeOffset.MaxValue.ToUnixTimeSeconds());
        return DateTimeOffset.FromUnixTimeSeconds(representable).AddTicks(nanoseconds / 100).LocalDateTime;
    }
}
