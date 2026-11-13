using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Context;

/// <summary>
/// The key store on Windows: the current user's certificate store (<c>CurrentUser\My</c>). This machine's key is a
/// P-256 key that can't be exported, in the TPM when that works and else in the software key store behind a
/// Windows prompt, tied to the certificate <c>CN=Context sensitive notes</c>. The recovery public key is a second
/// certificate without a private key. Both certificates are self-issued and only carry the public keys; their
/// signatures come from a throw-away key and are never checked. Uses .NET's own classes only.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsKeyStore : IKeyStore
{
    private const string MachineSubject = "CN=Context sensitive notes";
    private const string RecoverySubject = "CN=Context sensitive notes recovery";
    private const string TpmProvider = "Microsoft Platform Crypto Provider";

    private readonly object gate = new();
    private byte[]? machine;
    private byte[]? recovery;

    public bool Supported => true;

    public byte[]? MachinePublicKey()
    {
        lock (gate)
        {
            machine ??= PublicKeyOf(MachineSubject, withPrivateKey: true);
            return machine;
        }
    }

    public byte[]? RecoveryPublicKey()
    {
        lock (gate)
        {
            recovery ??= PublicKeyOf(RecoverySubject, withPrivateKey: false);
            return recovery;
        }
    }

    public void CreateMachineKey()
    {
        lock (gate)
        {
            try
            {
                CngKey key = NewKey();
                try
                {
                    using ECDiffieHellmanCng agreement = new ECDiffieHellmanCng(key);
                    using X509Certificate2 bare = Certificate(MachineSubject, agreement.ExportSubjectPublicKeyInfo());
                    using X509Certificate2 withKey = bare.CopyWithPrivateKey(agreement);
                    Store(withKey);
                }
                catch (Exception)
                {
                    key.Delete();
                    throw;
                }
                finally
                {
                    key.Dispose();
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
                using X509Certificate2 certificate = Certificate(RecoverySubject, publicKey);
                Store(certificate);
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
            using X509Certificate2 certificate = Find(MachineSubject, withPrivateKey: true) ?? throw new KeyStoreException();
            using ECDiffieHellman key = certificate.GetECDiffieHellmanPrivateKey() ?? throw new KeyStoreException();
            return SealedNote.Agree(key, peerPublic);
        }
        catch (Exception)
        {
            throw new KeyStoreException();
        }
    }

    // A named P-256 agreement key that can't be exported: in the TPM when it can make and use one, else in the
    // software key store, where Windows asks the user before each use.
    private static CngKey NewKey()
    {
        string name = "Context sensitive notes " + Guid.NewGuid().ToString("N");
        try
        {
            CngKey tpm = CngKey.Create(
                CngAlgorithm.ECDiffieHellmanP256,
                name,
                new CngKeyCreationParameters
                {
                    Provider = new CngProvider(TpmProvider),
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.KeyAgreement,
                });
            try
            {
                // Some TPMs make the key but can't agree with it.
                using ECDiffieHellmanCng agreement = new ECDiffieHellmanCng(tpm);
                using ECDiffieHellman other = SealedNote.NewKey();
                _ = SealedNote.Agree(agreement, SealedNote.PointOf(other.ExportSubjectPublicKeyInfo()));
                return tpm;
            }
            catch (CryptographicException)
            {
                tpm.Delete();
                tpm.Dispose();
            }
        }
        catch (CryptographicException)
        {
            // No usable TPM: the software key store below.
        }

        return CngKey.Create(
            CngAlgorithm.ECDiffieHellmanP256,
            name,
            new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.KeyAgreement,
                UIPolicy = new CngUIPolicy(
                    CngUIProtectionLevels.ProtectKey, "Context", "This key opens your sensitive notes.", null, null),
            });
    }

    // A self-issued certificate that carries publicKey. The signature is made with a throw-away key: the agreement
    // key is not used for signing, and nothing ever checks the signature.
    private static X509Certificate2 Certificate(string subject, byte[] publicKey)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        X500DistinguishedName name = new X500DistinguishedName(subject);
        CertificateRequest request = new CertificateRequest(
            name, PublicKey.CreateFromSubjectPublicKeyInfo(publicKey, out _), HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyAgreement, critical: true));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return request.Create(
            name, X509SignatureGenerator.CreateForECDsa(signer), now.AddDays(-1), now.AddYears(50), RandomNumberGenerator.GetBytes(8));
    }

    private static void Store(X509Certificate2 certificate)
    {
        using X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    private static byte[]? PublicKeyOf(string subject, bool withPrivateKey)
    {
        try
        {
            using X509Certificate2? certificate = Find(subject, withPrivateKey);
            return certificate?.PublicKey.ExportSubjectPublicKeyInfo();
        }
        catch (Exception)
        {
            throw new KeyStoreException();
        }
    }

    // The newest certificate with exactly that subject (and a private key, when asked for); the caller disposes it.
    private static X509Certificate2? Find(string subject, bool withPrivateKey)
    {
        string wanted = new X500DistinguishedName(subject).Name;
        using X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2? found = null;
        foreach (X509Certificate2 certificate in store.Certificates)
        {
            bool matches = string.Equals(certificate.SubjectName.Name, wanted, StringComparison.Ordinal)
                && string.Equals(certificate.IssuerName.Name, wanted, StringComparison.Ordinal)
                && (!withPrivateKey || certificate.HasPrivateKey);
            if (matches && (found is null || certificate.NotBefore > found.NotBefore))
            {
                found?.Dispose();
                found = certificate;
            }
            else
            {
                certificate.Dispose();
            }
        }

        return found;
    }
}
