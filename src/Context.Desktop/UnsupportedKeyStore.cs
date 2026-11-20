namespace Context;

/// <summary>The key store on an OS other than macOS and Windows: it has no keys and can make none.</summary>
internal sealed class UnsupportedKeyStore : IKeyStore
{
    public bool Supported => false;

    public byte[]? MachinePublicKey() => null;

    public byte[]? RecoveryPublicKey() => null;

    public KeyDetails? MachineKeyDetails() => null;

    public void CreateMachineKey() => throw new KeyStoreException();

    public void StoreRecoveryPublicKey(byte[] publicKey) => throw new KeyStoreException();

    public byte[] Agree(byte[] peerPublic) => throw new KeyStoreException();
}
