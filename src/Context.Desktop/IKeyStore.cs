namespace Context;

/// <summary>Thrown when the OS key store can't do what was asked. It carries no detail.</summary>
internal sealed class KeyStoreException : Exception
{
    public KeyStoreException()
        : base("The key store failed.")
    {
    }
}

/// <summary>
/// The keys of sensitive notes, kept by the operating system and never in the data folder: this machine's private
/// key, which can't be exported, and the public half of the recovery key. Public keys are P-256
/// SubjectPublicKeyInfo. Every member fails with <see cref="KeyStoreException"/>.
/// </summary>
internal interface IKeyStore
{
    /// <summary>False on an OS without a key store this app can use; then nothing else works.</summary>
    public bool Supported { get; }

    /// <summary>This machine's public key, or <c>null</c> when there is none yet.</summary>
    public byte[]? MachinePublicKey();

    /// <summary>The recovery public key, or <c>null</c> when recovery is not set up.</summary>
    public byte[]? RecoveryPublicKey();

    /// <summary>Creates this machine's key pair; the private key can't be exported.</summary>
    public void CreateMachineKey();

    /// <summary>Stores the recovery public key (SubjectPublicKeyInfo).</summary>
    public void StoreRecoveryPublicKey(byte[] publicKey);

    /// <summary>
    /// The one private-key call: the raw P-256 agreement (32 bytes) of this machine's private key with
    /// <paramref name="peerPublic"/> (an uncompressed point, 65 bytes). The OS may ask the user first.
    /// </summary>
    public byte[] Agree(byte[] peerPublic);
}
