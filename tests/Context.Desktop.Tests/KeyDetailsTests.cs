using System.Security.Cryptography;

namespace Context.Tests;

// 138: what the settings page shows about the key of sensitive notes.
public sealed class KeyDetailsTests
{
    // AC-11: the fingerprint is the upper-case hex SHA-256 of the SubjectPublicKeyInfo, with no expiry.
    [Fact]
    public void FromPublicKey_UpperCaseSha256OfTheKey_NoExpiry()
    {
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = key.ExportSubjectPublicKeyInfo();

        KeyDetails details = KeyDetails.FromPublicKey("Context sensitive notes", spki);

        Assert.Equal("Context sensitive notes", details.Name);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(spki)), details.Fingerprint);
        Assert.Equal(64, details.Fingerprint.Length);
        Assert.Equal(details.Fingerprint.ToUpperInvariant(), details.Fingerprint);
        Assert.Equal(SealedNote.KeyId(spki), details.Fingerprint, ignoreCase: true);
        Assert.NotEqual(SealedNote.KeyId(spki), details.Fingerprint);
        Assert.Null(details.ExpiresUtc);
    }

    // AC-11: fixed bytes give a fixed fingerprint.
    [Fact]
    public void FromPublicKey_KnownBytes_KnownFingerprint()
    {
        KeyDetails details = KeyDetails.FromPublicKey("k", "abc"u8.ToArray());

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", details.Fingerprint);
    }

    // The raw name and fingerprint never leak through formatting.
    [Fact]
    public void ToString_OnlyTheTypeName()
    {
        KeyDetails details = new KeyDetails("NAME-138", "PRINT-138", new DateTime(2076, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        Assert.Equal("KeyDetails", details.ToString());
        Assert.Equal("KeyDetails", $"{details}");
        Assert.Equal("SensitiveKeyView", new SensitiveKeyView(SensitiveKeyKind.Shown, details).ToString());
    }

    // The expiry is kept in UTC: a local time (a certificate's NotAfter) is converted, a time without a kind is UTC.
    [Fact]
    public void ExpiresUtc_IsUtc()
    {
        DateTime utc = new(2076, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        KeyDetails fromUtc = new KeyDetails("k", "f", utc);
        KeyDetails fromLocal = new KeyDetails("k", "f", utc.ToLocalTime());
        KeyDetails fromUnspecified = new KeyDetails("k", "f", DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));

        Assert.All(new[] { fromUtc, fromLocal, fromUnspecified }, d =>
        {
            Assert.Equal(utc, d.ExpiresUtc);
            Assert.Equal(DateTimeKind.Utc, d.ExpiresUtc!.Value.Kind);
        });
    }
}
