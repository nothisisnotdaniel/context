using System.Security.Cryptography;

namespace Context;

/// <summary>
/// The public keys Suite Pro licence keys are verified with: key id (<c>kid</c>) to an SPKI PEM
/// (<c>-----BEGIN PUBLIC KEY-----</c>, P-256). Only public keys ship. The compiled table starts empty
/// until the maker pastes the PEM that Times' issuer (<c>Times.LicenceIssuer create-key</c>) prints; it must always
/// equal Times' table (<c>src/Times/LicencePublicKeys.cs</c>). With no key nothing verifies and Pro stays locked. A provider's keys can be added later next to the manual ones under
/// their own key ids. Tests build their own table with <see cref="FromTable"/>.
/// </summary>
internal sealed class LicencePublicKeys
{
    /// <summary>The OID of the NIST P-256 curve.</summary>
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private const int MaxPemChars = 16 * 1024;

    /// <summary>
    /// The text every private-key PEM label holds (<c>EC</c> and PKCS#8 alike). Spelled in two pieces so that no
    /// licence source file holds it whole: the source test fails on a file that does.
    /// </summary>
    private const string PrivateKeyLabel = "PRIVATE" + " KEY";

    /// <summary>
    /// The compiled key table. Add a line <c>("&lt;kid&gt;", """&lt;PEM&gt;"""),</c> per key; a key id is
    /// 1-16 characters of <c>a-z 0-9 -</c>.
    /// </summary>
    private static readonly (string KeyId, string Pem)[] Compiled =
    [
    ];

    private readonly Dictionary<string, ECParameters> keys;

    private LicencePublicKeys(Dictionary<string, ECParameters> keys)
    {
        this.keys = keys;
    }

    /// <summary>A table with no keys: nothing verifies.</summary>
    public static LicencePublicKeys None { get; } = new(new Dictionary<string, ECParameters>(StringComparer.Ordinal));

    /// <summary>The keys compiled into this build.</summary>
    public static LicencePublicKeys Embedded() => FromTable(Compiled);

    public int Count => keys.Count;

    /// <summary>
    /// The usable entries of <paramref name="table"/>: a valid key id and exactly one P-256
    /// <c>PUBLIC KEY</c> block each; other entries are left out. A table in which any PEM holds a
    /// private-key block (<see cref="PrivateKeyLabel"/>) gives no keys at all (a committed private key is a leaked key).
    /// </summary>
    public static LicencePublicKeys FromTable(IEnumerable<(string KeyId, string Pem)> table)
    {
        List<(string KeyId, string Pem)> entries = table.ToList();
        if (entries.Any(entry => entry.Pem is null || entry.Pem.Contains(PrivateKeyLabel, StringComparison.Ordinal)))
        {
            return None;
        }

        Dictionary<string, ECParameters> found = new Dictionary<string, ECParameters>(StringComparer.Ordinal);
        foreach ((string keyId, string pem) in entries)
        {
            if (LicenceKey.IsValidKeyId(keyId) && !found.ContainsKey(keyId) && TryImport(pem, out ECParameters key))
            {
                found.Add(keyId, key);
            }
        }

        return found.Count == 0 ? None : new LicencePublicKeys(found);
    }

    public bool Contains(string keyId) => keys.ContainsKey(keyId);

    /// <summary>The id of the key whose signature over <paramref name="payload"/> verifies, or <c>null</c>.</summary>
    public string? FindSigner(byte[] payload, byte[] signature)
    {
        foreach (KeyValuePair<string, ECParameters> entry in keys)
        {
            using ECDsa key = ECDsa.Create(entry.Value);
            try
            {
                if (key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                {
                    return entry.Key;
                }
            }
            catch (CryptographicException)
            {
                // A malformed signature verifies with no key.
            }
        }

        return null;
    }

    // Exactly one PUBLIC KEY block, on the P-256 curve.
    private static bool TryImport(string pem, out ECParameters parameters)
    {
        parameters = default;
        if (pem.Length > MaxPemChars || !PemEncoding.TryFind(pem, out PemFields fields)
            || !pem.AsSpan()[fields.Label].SequenceEqual("PUBLIC KEY")
            || PemEncoding.TryFind(pem.AsSpan()[fields.Location.End..], out _))
        {
            return false;
        }

        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(pem.AsSpan()[fields.Location]);
            ECParameters exported = key.ExportParameters(includePrivateParameters: false);
            if (exported.Curve.Oid?.Value != P256Oid && exported.Curve.Oid?.FriendlyName is not ("nistP256" or "ECDSA_P256" or "prime256v1"))
            {
                return false;
            }

            parameters = exported;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
