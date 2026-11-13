using System.Security.Cryptography;

namespace Context.Tests;

/// <summary>
/// A key store in memory: P-256 keys that never leave the test process. It counts <see cref="Agree"/> calls and
/// can be told to fail; it touches no OS key store.
/// </summary>
internal sealed class FakeKeyStore : IKeyStore, IDisposable
{
    private ECDiffieHellman? machine;
    private byte[]? recovery;

    public bool Supported { get; set; } = true;

    /// <summary>How often <see cref="Agree"/> was called.</summary>
    public int Agrees { get; private set; }

    /// <summary>How often <see cref="CreateMachineKey"/> was called.</summary>
    public int MachineKeysCreated { get; private set; }

    /// <summary>When true, <see cref="Agree"/> fails (after counting the call).</summary>
    public bool AgreeThrows { get; set; }

    /// <summary>When true, <see cref="CreateMachineKey"/> fails.</summary>
    public bool CreateThrows { get; set; }

    /// <summary>When true, <see cref="StoreRecoveryPublicKey"/> fails.</summary>
    public bool StoreThrows { get; set; }

    /// <summary>A store that already has a machine key and a recovery public key (whose private half is dropped).</summary>
    public static FakeKeyStore Ready()
    {
        FakeKeyStore store = new FakeKeyStore();
        store.CreateMachineKey();
        using ECDiffieHellman recoveryKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        store.StoreRecoveryPublicKey(recoveryKey.ExportSubjectPublicKeyInfo());
        store.MachineKeysCreated = 0;
        return store;
    }

    public byte[]? MachinePublicKey() => machine?.ExportSubjectPublicKeyInfo();

    public byte[]? RecoveryPublicKey() => recovery;

    public void CreateMachineKey()
    {
        if (!Supported || CreateThrows)
        {
            throw new KeyStoreException();
        }

        machine?.Dispose();
        machine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        MachineKeysCreated++;
    }

    public void StoreRecoveryPublicKey(byte[] publicKey)
    {
        if (!Supported || StoreThrows)
        {
            throw new KeyStoreException();
        }

        recovery = publicKey;
    }

    public byte[] Agree(byte[] peerPublic)
    {
        Agrees++;
        if (AgreeThrows || machine is null)
        {
            throw new KeyStoreException();
        }

        return SealedNote.Agree(machine, peerPublic);
    }

    /// <summary>Forgets the machine key, like a new machine.</summary>
    public void LoseMachineKey()
    {
        machine?.Dispose();
        machine = null;
    }

    /// <summary>Forgets the recovery public key as well.</summary>
    public void LoseRecoveryKey() => recovery = null;

    public void Dispose() => machine?.Dispose();
}
