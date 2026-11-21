using System.Security.Cryptography;
using System.Text;

namespace Context.Tests;

/// <summary>
/// A P-256 key pair made for one test (job 367, as in Times), its key table (key id <see cref="KeyId"/>) and helpers
/// that sign licence tokens with it, including payloads the issuer would never write. Signing lives in the test
/// projects only; never the real key.
/// </summary>
internal sealed class TestLicences : IDisposable
{
    public const string KeyId = "test";
    public const string LicenceId = "AAECAwQFBgcICQoLDA0ODw";
    public const string Name = "Ada Lovelace";

    /// <summary>The build date the helpers check against.</summary>
    public static readonly DateOnly BuildDate = new DateOnly(2026, 10, 27);

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestLicences()
    {
        Pem = key.ExportSubjectPublicKeyInfoPem();
        Keys = LicencePublicKeys.FromTable([(KeyId, Pem)]);
    }

    public string Pem { get; }

    /// <summary>A table holding only this key, as <see cref="KeyId"/>.</summary>
    public LicencePublicKeys Keys { get; }

    public void Dispose() => key.Dispose();

    /// <summary>The JSON of a well-formed payload.</summary>
    public static string PayloadJson(string kid = KeyId, string lid = LicenceId, string name = Name,
        string until = "2027-12-31", string issued = "2026-10-27") =>
        $"{{\"v\":1,\"kid\":\"{kid}\",\"lid\":\"{lid}\",\"name\":\"{name}\",\"ed\":\"suite-pro\",\"until\":\"{until}\",\"issued\":\"{issued}\"}}";

    /// <summary>A valid token for the given fields.</summary>
    public string Token(string kid = KeyId, string lid = LicenceId, string name = Name, string until = "2027-12-31",
        string issued = "2026-10-27") =>
        Sign(PayloadJson(kid, lid, name, until, issued));

    /// <summary>Signs any payload text exactly as given.</summary>
    public string Sign(string payloadJson) => Sign(Encoding.UTF8.GetBytes(payloadJson));

    public string Sign(byte[] payload)
    {
        byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return LicenceKey.Prefix + LicenceKey.Encode(payload) + "." + LicenceKey.Encode(signature);
    }

    /// <summary>A <see cref="Context.Pro"/> on this key table, with the given revocations and build date.</summary>
    public Pro Pro(ILicenceStore store, DateOnly? buildDate, params string[] revoked) =>
        new Pro(store, Keys, new LicenceRevocations(revoked), buildDate);
}
