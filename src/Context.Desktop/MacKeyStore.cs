using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Context;

/// <summary>
/// The key store on macOS: the Keychain, through Security.framework. This machine's key is a P-256 key made with
/// <c>SecKeyCreateRandomKey</c> that can't be extracted: in the Secure Enclave behind a user-presence check when
/// macOS allows that for this build, else in the login Keychain. The recovery public key is a Keychain password
/// item that holds only the public key. This is the only file with native calls.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacKeyStore : IKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string KeyTag = "context.sensitive-notes.machine-key";
    private const string KeyLabel = "Context sensitive notes";
    private const string RecoveryService = "Context sensitive notes";
    private const string RecoveryAccount = "recovery-public-key";
    private const int ItemNotFound = -25300;
    private const uint Utf8Encoding = 0x08000100;
    private const nint NumberSInt32 = 3;
    private const nuint UserPresence = 1;
    private const nuint PrivateKeyUsage = 1u << 30;

    private readonly object gate = new();
    private byte[]? machine;
    private byte[]? recovery;

    public bool Supported => true;

    public byte[]? MachinePublicKey()
    {
        lock (gate)
        {
            if (machine is not null)
            {
                return machine;
            }

            try
            {
                using Native native = new Native();
                IntPtr key = FindMachineKey(native);
                if (key == IntPtr.Zero)
                {
                    return null;
                }

                machine = SealedNote.PublicKeyOf(PublicPoint(native, key));
                return machine;
            }
            catch (Exception)
            {
                throw new KeyStoreException();
            }
        }
    }

    // The Keychain key has no certificate: the fingerprint is the SHA-256 of the public key, and it never expires.
    public KeyDetails? MachineKeyDetails()
    {
        byte[]? key = MachinePublicKey();
        return key is null ? null : KeyDetails.FromPublicKey(KeyLabel, key);
    }

    public byte[]? RecoveryPublicKey()
    {
        lock (gate)
        {
            if (recovery is not null)
            {
                return recovery;
            }

            try
            {
                using Native native = new Native();
                IntPtr query = native.Dictionary();
                Put(query, native.Constant("kSecClass"), native.Constant("kSecClassGenericPassword"));
                Put(query, native.Constant("kSecAttrService"), native.Text(RecoveryService));
                Put(query, native.Constant("kSecAttrAccount"), native.Text(RecoveryAccount));
                Put(query, native.Constant("kSecReturnData"), native.True);
                Put(query, native.Constant("kSecMatchLimit"), native.Constant("kSecMatchLimitOne"));
                int status = SecItemCopyMatching(query, out IntPtr data);
                if (status == ItemNotFound)
                {
                    return null;
                }

                if (status != 0 || data == IntPtr.Zero)
                {
                    throw new KeyStoreException();
                }

                byte[] stored = native.Bytes(native.Own(data));

                // Only a P-256 public key counts.
                _ = SealedNote.PointOf(stored);
                recovery = stored;
                return recovery;
            }
            catch (Exception)
            {
                throw new KeyStoreException();
            }
        }
    }

    public void CreateMachineKey()
    {
        lock (gate)
        {
            try
            {
                using Native native = new Native();
                IntPtr key = CreateKey(native, permanent: true, enclave: true);
                if (key == IntPtr.Zero)
                {
                    // No Secure Enclave, or this build may not use it (it is not signed with the entitlement).
                    key = CreateKey(native, permanent: true, enclave: false);
                }

                if (key == IntPtr.Zero)
                {
                    throw new KeyStoreException();
                }

                machine = null;
            }
            catch (Exception)
            {
                throw new KeyStoreException();
            }
        }
    }

    public void StoreRecoveryPublicKey(byte[] publicKey)
    {
        lock (gate)
        {
            try
            {
                using Native native = new Native();
                IntPtr item = native.Dictionary();
                Put(item, native.Constant("kSecClass"), native.Constant("kSecClassGenericPassword"));
                Put(item, native.Constant("kSecAttrService"), native.Text(RecoveryService));
                Put(item, native.Constant("kSecAttrAccount"), native.Text(RecoveryAccount));
                Put(item, native.Constant("kSecAttrLabel"), native.Text(KeyLabel + " recovery public key"));
                Put(item, native.Constant("kSecValueData"), native.Data(publicKey));
                if (SecItemAdd(item, IntPtr.Zero) != 0)
                {
                    throw new KeyStoreException();
                }

                recovery = null;
            }
            catch (Exception)
            {
                throw new KeyStoreException();
            }
        }
    }

    public byte[] Agree(byte[] peerPublic)
    {
        try
        {
            using Native native = new Native();
            IntPtr key = FindMachineKey(native);
            if (key == IntPtr.Zero)
            {
                throw new KeyStoreException();
            }

            return Exchange(native, key, peerPublic);
        }
        catch (Exception)
        {
            throw new KeyStoreException();
        }
    }

    // The machine's private key in the Keychain, or zero when there is none. Finding it asks the user nothing.
    private static IntPtr FindMachineKey(Native native)
    {
        IntPtr query = native.Dictionary();
        Put(query, native.Constant("kSecClass"), native.Constant("kSecClassKey"));
        Put(query, native.Constant("kSecAttrApplicationTag"), native.Data(Encoding.UTF8.GetBytes(KeyTag)));
        Put(query, native.Constant("kSecAttrKeyClass"), native.Constant("kSecAttrKeyClassPrivate"));
        Put(query, native.Constant("kSecReturnRef"), native.True);
        Put(query, native.Constant("kSecMatchLimit"), native.Constant("kSecMatchLimitOne"));
        int status = SecItemCopyMatching(query, out IntPtr key);
        if (status == ItemNotFound)
        {
            return IntPtr.Zero;
        }

        if (status != 0 || key == IntPtr.Zero)
        {
            throw new KeyStoreException();
        }

        return native.Own(key);
    }

    // A new P-256 private key that can't be extracted, or zero when macOS refuses. permanent: kept in the Keychain
    // under KeyTag. enclave: in the Secure Enclave, usable only after a user-presence check.
    private static IntPtr CreateKey(Native native, bool permanent, bool enclave)
    {
        IntPtr privateAttributes = native.Dictionary();
        Put(privateAttributes, native.Constant("kSecAttrIsPermanent"), permanent ? native.True : native.False);
        if (permanent)
        {
            Put(privateAttributes, native.Constant("kSecAttrApplicationTag"), native.Data(Encoding.UTF8.GetBytes(KeyTag)));
            Put(privateAttributes, native.Constant("kSecAttrLabel"), native.Text(KeyLabel));
        }

        IntPtr attributes = native.Dictionary();
        Put(attributes, native.Constant("kSecAttrKeyType"), native.Constant("kSecAttrKeyTypeECSECPrimeRandom"));
        Put(attributes, native.Constant("kSecAttrKeySizeInBits"), native.Number(256));
        if (enclave)
        {
            IntPtr access = SecAccessControlCreateWithFlags(
                IntPtr.Zero,
                native.Constant("kSecAttrAccessibleWhenUnlockedThisDeviceOnly"),
                PrivateKeyUsage | UserPresence,
                out IntPtr accessError);
            native.Own(accessError);
            if (access == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            Put(privateAttributes, native.Constant("kSecAttrAccessControl"), native.Own(access));
            Put(attributes, native.Constant("kSecAttrTokenID"), native.Constant("kSecAttrTokenIDSecureEnclave"));
        }
        else
        {
            Put(attributes, native.Constant("kSecAttrIsExtractable"), native.False);
            Put(privateAttributes, native.Constant("kSecAttrIsExtractable"), native.False);
        }

        Put(attributes, native.Constant("kSecPrivateKeyAttrs"), privateAttributes);
        IntPtr key = SecKeyCreateRandomKey(attributes, out IntPtr error);
        native.Own(error);
        return native.Own(key);
    }

    // The uncompressed public point (65 bytes) of a private key.
    private static byte[] PublicPoint(Native native, IntPtr privateKey)
    {
        IntPtr publicKey = native.Own(SecKeyCopyPublicKey(privateKey));
        if (publicKey == IntPtr.Zero)
        {
            throw new KeyStoreException();
        }

        IntPtr data = SecKeyCopyExternalRepresentation(publicKey, out IntPtr error);
        native.Own(error);
        if (data == IntPtr.Zero)
        {
            throw new KeyStoreException();
        }

        return native.Bytes(native.Own(data));
    }

    // The raw agreement (32 bytes) of a private key with an uncompressed public point. macOS may ask the user here.
    private static byte[] Exchange(Native native, IntPtr privateKey, byte[] peerPoint)
    {
        IntPtr peerAttributes = native.Dictionary();
        Put(peerAttributes, native.Constant("kSecAttrKeyType"), native.Constant("kSecAttrKeyTypeECSECPrimeRandom"));
        Put(peerAttributes, native.Constant("kSecAttrKeyClass"), native.Constant("kSecAttrKeyClassPublic"));
        IntPtr peer = SecKeyCreateWithData(native.Data(peerPoint), peerAttributes, out IntPtr peerError);
        native.Own(peerError);
        if (native.Own(peer) == IntPtr.Zero)
        {
            throw new KeyStoreException();
        }

        IntPtr shared = SecKeyCopyKeyExchangeResult(
            privateKey, native.Constant("kSecKeyAlgorithmECDHKeyExchangeStandard"), peer, native.Dictionary(), out IntPtr error);
        native.Own(error);
        if (shared == IntPtr.Zero)
        {
            throw new KeyStoreException();
        }

        return native.Bytes(native.Own(shared));
    }

    private static void Put(IntPtr dictionary, IntPtr key, IntPtr value) => CFDictionarySetValue(dictionary, key, value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte[] bytes, nint length, uint encoding, byte isExternal);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFNumberCreate(IntPtr allocator, nint type, ref int value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundation)]
    private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    [DllImport(Security)]
    private static extern IntPtr SecKeyCreateRandomKey(IntPtr parameters, out IntPtr error);

    [DllImport(Security)]
    private static extern IntPtr SecKeyCopyPublicKey(IntPtr key);

    [DllImport(Security)]
    private static extern IntPtr SecKeyCopyExternalRepresentation(IntPtr key, out IntPtr error);

    [DllImport(Security)]
    private static extern IntPtr SecKeyCreateWithData(IntPtr data, IntPtr attributes, out IntPtr error);

    [DllImport(Security)]
    private static extern IntPtr SecKeyCopyKeyExchangeResult(
        IntPtr privateKey, IntPtr algorithm, IntPtr publicKey, IntPtr parameters, out IntPtr error);

    [DllImport(Security)]
    private static extern IntPtr SecAccessControlCreateWithFlags(IntPtr allocator, IntPtr protection, nuint flags, out IntPtr error);

    [DllImport(Security)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(Security)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    // The CoreFoundation objects one call makes; all are released when it ends.
    private sealed class Native : IDisposable
    {
        private readonly Stack<IntPtr> owned = new();
        private readonly IntPtr security = NativeLibrary.Load(Security);
        private readonly IntPtr coreFoundation = NativeLibrary.Load(CoreFoundation);

        public IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(coreFoundation, "kCFBooleanTrue"));

        public IntPtr False => Marshal.ReadIntPtr(NativeLibrary.GetExport(coreFoundation, "kCFBooleanFalse"));

        // A Security.framework constant (a CFString the framework owns).
        public IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(security, name));

        // Takes ownership of a created object (zero is passed through).
        public IntPtr Own(IntPtr value)
        {
            if (value != IntPtr.Zero)
            {
                owned.Push(value);
            }

            return value;
        }

        public IntPtr Text(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return Created(CFStringCreateWithBytes(IntPtr.Zero, bytes, bytes.Length, Utf8Encoding, 0));
        }

        public IntPtr Data(byte[] bytes) => Created(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

        public IntPtr Number(int value) => Created(CFNumberCreate(IntPtr.Zero, NumberSInt32, ref value));

        public IntPtr Dictionary() => Created(CFDictionaryCreateMutable(
            IntPtr.Zero,
            0,
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks")));

        public byte[] Bytes(IntPtr data)
        {
            byte[] bytes = new byte[(int)CFDataGetLength(data)];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
            return bytes;
        }

        public void Dispose()
        {
            while (owned.Count > 0)
            {
                CFRelease(owned.Pop());
            }
        }

        private IntPtr Created(IntPtr value) => value == IntPtr.Zero ? throw new KeyStoreException() : Own(value);
    }
}
