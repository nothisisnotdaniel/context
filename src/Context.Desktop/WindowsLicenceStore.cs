using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Context;

/// <summary>
/// The Suite Pro licence on Windows: a generic credential in the Credential Manager (advapi32), target
/// <see cref="LicenceStoreNames.WindowsTarget"/>, user name <see cref="LicenceStoreNames.WindowsUserName"/>, a UTF-8
/// blob persisted for the local machine, the same credential Times writes (a port of Times'
/// <c>WindowsCredentialLicenceStore</c> and <c>WindowsCredential</c>, without unsafe code). Buffers from
/// <c>CredReadW</c> are freed with <c>CredFree</c>; unmanaged copies are zeroed before they are freed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsLicenceStore : ILicenceStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public bool IsSupported => true;

    public string? WriteRefusal => null;

    public string? Read()
    {
        byte[]? bytes = ReadBlob(LicenceStoreNames.WindowsTarget);
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

    public void Write(string key) =>
        WriteBlob(LicenceStoreNames.WindowsTarget, LicenceStoreNames.WindowsUserName, Encoding.UTF8.GetBytes(key));

    public bool Delete()
    {
        if (CredDelete(LicenceStoreNames.WindowsTarget, CredTypeGeneric, 0))
        {
            return true;
        }

        if (Marshal.GetLastPInvokeError() == ErrorNotFound)
        {
            return false;
        }

        throw new LicenceStoreException("The item could not be removed from the Windows Credential Manager.");
    }

    // The credential's blob, or null if there is none (capped like the Mac item).
    private static byte[]? ReadBlob(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out IntPtr pointer))
        {
            if (Marshal.GetLastPInvokeError() == ErrorNotFound)
            {
                return null;
            }

            throw new LicenceStoreException("The Windows Credential Manager could not be read.");
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize > LicenceKey.MaxLength * 4)
            {
                throw new LicenceStoreException("The Windows Credential Manager item is too large.");
            }

            byte[] blob = new byte[credential.CredentialBlobSize];
            if (blob.Length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            }

            return blob;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    // Stores the blob (zeroed afterwards), replacing an existing credential.
    private static void WriteBlob(string target, string userName, byte[] blob)
    {
        IntPtr targetPointer = Marshal.StringToHGlobalUni(target);
        IntPtr userPointer = Marshal.StringToHGlobalUni(userName);
        IntPtr data = Marshal.AllocHGlobal(Math.Max(1, blob.Length));
        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            Credential credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetPointer,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = data,
                Persist = CredPersistLocalMachine,
                UserName = userPointer,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new LicenceStoreException("The item could not be saved in the Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, data, blob.Length);
            CryptographicOperations.ZeroMemory(blob);
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(userPointer);
            Marshal.FreeHGlobal(targetPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
