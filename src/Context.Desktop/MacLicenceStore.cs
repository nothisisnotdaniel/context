using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Context;

/// <summary>
/// The Suite Pro licence on macOS: the Keychain generic password service <see cref="LicenceStoreNames.MacService"/>,
/// account <see cref="LicenceStoreNames.MacAccount"/>, the same item Times writes (a port of Times'
/// <c>MacKeychainLicenceStore</c> and <c>MacKeychainItem</c>, without unsafe code). A new item gets its own access list
/// trusting only the running apphost; through the shared <c>dotnet</c> host nothing is stored. Every message is fixed
/// and never holds the key.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacLicenceStore : ILicenceStore
{
    /// <summary>The fixed message when Context runs through the shared host.</summary>
    public const string SharedHostRefused =
        "The licence is stored only when Context runs as its own app, not through dotnet.";

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int MaxItems = 16;

    private const uint AccountAttribute = 0x61636374;
    private const uint ServiceAttribute = 0x73766365;
    private const uint LabelAttribute = 0x6c61626c;
    private const uint GenericPasswordClass = 0x67656e70;
    private const uint CFStringEncodingUtf8 = 0x08000100;

    private const string ReadFailed = "The macOS Keychain could not be read.";
    private const string SaveFailed = "The item could not be saved in the macOS Keychain.";

    private readonly string? trustedPath;

    /// <param name="processPath">The running executable (<see cref="Environment.ProcessPath"/>).</param>
    public MacLicenceStore(string? processPath)
    {
        trustedPath = TrustedApplicationPath(processPath);
    }

    public bool IsSupported => true;

    public string? WriteRefusal => trustedPath is null ? SharedHostRefused : null;

    public string? Read()
    {
        byte[]? bytes = ReadItem(LicenceStoreNames.MacService, LicenceStoreNames.MacAccount, LicenceKey.MaxLength * 4);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Write(string key)
    {
        // Checked before the keychain is touched: an item trusting the shared host is never made.
        if (trustedPath is null)
        {
            throw new LicenceStoreException(SharedHostRefused);
        }

        Replace(LicenceStoreNames.MacService, LicenceStoreNames.MacAccount, Encoding.UTF8.GetBytes(key), trustedPath);
    }

    public bool Delete() => DeleteItems(LicenceStoreNames.MacService, LicenceStoreNames.MacAccount);

    /// <summary>
    /// The path the item's access list may trust: the running executable with symlinks resolved, or <c>null</c> when it
    /// is missing, relative, not an existing file, or a shared .NET host (which would let every .NET program read it).
    /// </summary>
    internal static string? TrustedApplicationPath(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath) || !Path.IsPathFullyQualified(processPath))
        {
            return null;
        }

        string resolved;
        try
        {
            resolved = File.ResolveLinkTarget(processPath, returnFinalTarget: true)?.FullName ?? processPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!File.Exists(resolved) || IsSharedHost(processPath) || IsSharedHost(resolved))
        {
            return null;
        }

        return resolved;
    }

    /// <summary>
    /// A .NET host that runs any app: named <c>dotnet</c>, or (renamed, as some version managers do) sitting next to a
    /// <c>host/fxr</c> folder, the layout of a .NET install root. An apphost has neither.
    /// </summary>
    internal static bool IsSharedHost(string path)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? folder = Path.GetDirectoryName(path);
        return folder is not null && Directory.Exists(Path.Combine(folder, "host", "fxr"));
    }

    // The item's data, or null if there is none. More than maxBytes bytes throws.
    private static byte[]? ReadItem(string service, string account, int maxBytes)
    {
        byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
        byte[] accountBytes = Encoding.UTF8.GetBytes(account);
        int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes, out uint length, out IntPtr data, out IntPtr item);
        if (status == ErrSecItemNotFound)
        {
            return null;
        }

        if (status != ErrSecSuccess)
        {
            throw new LicenceStoreException(ReadFailed);
        }

        try
        {
            if (length > maxBytes)
            {
                throw new LicenceStoreException("The macOS Keychain item is too large.");
            }

            byte[] bytes = new byte[length];
            if (length > 0)
            {
                Marshal.Copy(data, bytes, 0, (int)length);
            }

            return bytes;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            ReleaseIfSet(item);
        }
    }

    // Stores the secret (zeroed afterwards) as the only item, trusting only trustedPath. The access object is built
    // before an old item is removed, so a failure there keeps it.
    private static void Replace(string service, string account, byte[] secret, string path)
    {
        IntPtr access = IntPtr.Zero;
        try
        {
            access = CreateAccess(path, service);
            DeleteItems(service, account);
            AddItem(service, account, secret, access);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            ReleaseIfSet(access);
        }
    }

    // Removes every item with this service and account. False if there was none.
    private static bool DeleteItems(string service, string account)
    {
        byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
        byte[] accountBytes = Encoding.UTF8.GetBytes(account);
        bool deleted = false;
        for (int i = 0; i < MaxItems; i++)
        {
            int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
                (uint)accountBytes.Length, accountBytes, out _, IntPtr.Zero, out IntPtr item);
            if (status == ErrSecItemNotFound)
            {
                return deleted;
            }

            if (status != ErrSecSuccess)
            {
                throw new LicenceStoreException(ReadFailed);
            }

            try
            {
                if (SecKeychainItemDelete(item) != ErrSecSuccess)
                {
                    throw new LicenceStoreException("The item could not be removed from the macOS Keychain.");
                }

                deleted = true;
            }
            finally
            {
                ReleaseIfSet(item);
            }
        }

        throw new LicenceStoreException("The macOS Keychain holds too many matching items.");
    }

    private static IntPtr CreateAccess(string path, string description)
    {
        IntPtr application = IntPtr.Zero;
        IntPtr list = IntPtr.Zero;
        IntPtr descriptor = IntPtr.Zero;
        try
        {
            if (SecTrustedApplicationCreateFromPath(path, out application) != ErrSecSuccess || application == IntPtr.Zero)
            {
                throw new LicenceStoreException(SaveFailed);
            }

            IntPtr library = NativeLibrary.Load(CoreFoundation);
            IntPtr callBacks = NativeLibrary.GetExport(library, "kCFTypeArrayCallBacks");
            list = CFArrayCreate(IntPtr.Zero, [application], 1, callBacks);
            descriptor = CFStringCreateWithCString(IntPtr.Zero, description, CFStringEncodingUtf8);
            if (list == IntPtr.Zero || descriptor == IntPtr.Zero)
            {
                throw new LicenceStoreException(SaveFailed);
            }

            if (SecAccessCreate(descriptor, list, out IntPtr access) != ErrSecSuccess || access == IntPtr.Zero)
            {
                throw new LicenceStoreException(SaveFailed);
            }

            return access;
        }
        finally
        {
            ReleaseIfSet(descriptor);
            ReleaseIfSet(list);
            ReleaseIfSet(application);
        }
    }

    // The service, account and label (= service) attributes point into pinned arrays for the one call.
    private static void AddItem(string service, string account, byte[] secret, IntPtr access)
    {
        byte[] serviceBytes = Encoding.UTF8.GetBytes(service);
        byte[] accountBytes = Encoding.UTF8.GetBytes(account);
        GCHandle servicePin = GCHandle.Alloc(serviceBytes, GCHandleType.Pinned);
        GCHandle accountPin = GCHandle.Alloc(accountBytes, GCHandleType.Pinned);
        GCHandle secretPin = GCHandle.Alloc(secret, GCHandleType.Pinned);
        GCHandle attributesPin = default;
        try
        {
            IntPtr servicePointer = servicePin.AddrOfPinnedObject();
            Attribute[] attributes =
            [
                new Attribute { Tag = ServiceAttribute, Length = (uint)serviceBytes.Length, Data = servicePointer },
                new Attribute { Tag = AccountAttribute, Length = (uint)accountBytes.Length, Data = accountPin.AddrOfPinnedObject() },
                new Attribute { Tag = LabelAttribute, Length = (uint)serviceBytes.Length, Data = servicePointer },
            ];
            attributesPin = GCHandle.Alloc(attributes, GCHandleType.Pinned);
            AttributeList list = new AttributeList { Count = (uint)attributes.Length, Attributes = attributesPin.AddrOfPinnedObject() };
            int status = SecKeychainItemCreateFromContent(GenericPasswordClass, ref list, (uint)secret.Length,
                secretPin.AddrOfPinnedObject(), IntPtr.Zero, access, out IntPtr item);
            if (status != ErrSecSuccess)
            {
                throw new LicenceStoreException(SaveFailed);
            }

            ReleaseIfSet(item);
        }
        finally
        {
            if (attributesPin.IsAllocated)
            {
                attributesPin.Free();
            }

            secretPin.Free();
            accountPin.Free();
            servicePin.Free();
        }
    }

    private static void ReleaseIfSet(IntPtr reference)
    {
        if (reference != IntPtr.Zero)
        {
            CFRelease(reference);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attribute
    {
        public uint Tag;
        public uint Length;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        public uint Count;
        public IntPtr Attributes;
    }

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength,
        out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength,
        IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecTrustedApplicationCreateFromPath(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr application);

    [DllImport(Security)]
    private static extern int SecAccessCreate(IntPtr descriptor, IntPtr trustedList, out IntPtr access);

    [DllImport(Security)]
    private static extern int SecKeychainItemCreateFromContent(uint itemClass, ref AttributeList attrList, uint length,
        IntPtr data, IntPtr keychain, IntPtr initialAccess, out IntPtr itemRef);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, nint count, IntPtr callBacks);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
