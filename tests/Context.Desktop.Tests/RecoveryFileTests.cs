using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace Context.Tests;

// 136 AC-4 / AC-8: the recovery file. One file is made for the whole class, because its key derivation is slow on
// purpose.
public sealed class RecoveryFileTests
{
    private const string Password = "correct horse battery";

    private static readonly Lazy<(byte[] File, byte[] PublicKey)> Made = new(() =>
    {
        byte[] file = RecoveryFile.Create(Password, out byte[] publicKey);
        return (file, publicKey);
    });

    // AC-4 / AC-8: the written file reads back to the same key with the password.
    [Fact]
    public void CreateThenRead_ReturnsTheKey()
    {
        using ECDiffieHellman key = RecoveryFile.Read(Made.Value.File, Password);

        Assert.Equal(Made.Value.PublicKey, key.ExportSubjectPublicKeyInfo());
        Assert.Equal(256, key.KeySize);
        using ECDiffieHellman other = SealedNote.NewKey();
        Assert.Equal(
            SealedNote.Agree(other, SealedNote.PointOf(Made.Value.PublicKey)),
            SealedNote.Agree(key, SealedNote.PointOf(other.ExportSubjectPublicKeyInfo())));
    }

    // AC-4: the key bag is protected with PBES2: AES-256 and PBKDF2-HMAC-SHA256 with at least 600,000 iterations; so
    // is the file's integrity check.
    [Fact]
    public void Create_UsesPbes2Aes256AndSlowPbkdf2()
    {
        AsnReader pfx = new AsnReader(Made.Value.File, AsnEncodingRules.DER).ReadSequence();
        Assert.True(pfx.TryReadInt32(out int version));
        Assert.Equal(3, version);
        byte[] authenticatedSafe = Data(pfx.ReadSequence());
        AsnReader macData = pfx.ReadSequence();
        pfx.ThrowIfNotEmpty();

        // The one bag: a shrouded key bag whose key is an EncryptedPrivateKeyInfo.
        AsnReader safes = new AsnReader(authenticatedSafe, AsnEncodingRules.DER).ReadSequence();
        AsnReader bags = new AsnReader(Data(safes.ReadSequence()), AsnEncodingRules.DER).ReadSequence();
        safes.ThrowIfNotEmpty();
        AsnReader bag = bags.ReadSequence();
        bags.ThrowIfNotEmpty();
        Assert.Equal("1.2.840.113549.1.12.10.1.2", bag.ReadObjectIdentifier());
        AsnReader shrouded = bag.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
        AsnReader algorithm = shrouded.ReadSequence();
        Assert.Equal("1.2.840.113549.1.5.13", algorithm.ReadObjectIdentifier());
        AsnReader pbes2 = algorithm.ReadSequence();
        AssertSlowPbkdf2(pbes2.ReadSequence());
        AsnReader cipher = pbes2.ReadSequence();
        Assert.Equal("2.16.840.1.101.3.4.1.42", cipher.ReadObjectIdentifier());
        Assert.Equal(16, cipher.ReadOctetString().Length);

        // The integrity check: PBMAC1 with the same key derivation and HMAC-SHA256.
        AsnReader digest = macData.ReadSequence();
        AsnReader macAlgorithm = digest.ReadSequence();
        Assert.Equal("1.2.840.113549.1.5.14", macAlgorithm.ReadObjectIdentifier());
        AsnReader pbmac1 = macAlgorithm.ReadSequence();
        AssertSlowPbkdf2(pbmac1.ReadSequence());
        Assert.Equal("1.2.840.113549.2.9", pbmac1.ReadSequence().ReadObjectIdentifier());
        Assert.Equal(32, digest.ReadOctetString().Length);
    }

    // AC-4: neither the password nor the public key's private half is readable in the file.
    [Fact]
    public void Create_FileDoesNotHoldThePassword()
    {
        string latin = Encoding.Latin1.GetString(Made.Value.File);

        Assert.DoesNotContain(Password, latin, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, Encoding.BigEndianUnicode.GetString(Made.Value.File), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, Encoding.Unicode.GetString(Made.Value.File), StringComparison.Ordinal);
        Assert.True(Made.Value.File.Length < 2048);
    }

    // AC-4: every file gets its own key.
    [Fact]
    public void Create_Twice_DifferentKeys()
    {
        _ = RecoveryFile.Create(Password, out byte[] second);

        Assert.NotEqual(Made.Value.PublicKey, second);
    }

    // AC-8: a wrong password is one exception without detail.
    [Theory]
    [InlineData("correct horse batterx")]
    [InlineData("")]
    public void Read_WrongPassword_Fails(string password)
    {
        RecoveryFileException failure = Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(Made.Value.File, password));

        Assert.Null(failure.InnerException);
    }

    // Round 2 (security 1): the password is used in Unicode form C, so a composed and a decomposed spelling of the
    // same visible password open the same file, in both directions.
    [Theory]
    [InlineData("caf\u00E9 au lait long", "cafe\u0301 au lait long")]
    [InlineData("cafe\u0301 au lait long", "caf\u00E9 au lait long")]
    public void Read_PasswordInAnotherUnicodeForm_OpensTheFile(string made, string typed)
    {
        Assert.NotEqual(made, typed);
        byte[] file = RecoveryFile.Create(made, out byte[] publicKey);

        using ECDiffieHellman key = RecoveryFile.Read(file, typed);

        Assert.Equal(publicKey, key.ExportSubjectPublicKeyInfo());
        Assert.Equal("caf\u00E9 au lait long", RecoveryFile.Normalized(typed));
    }

    // Round 2 (security 1): text without a normal form (a lone surrogate) is refused on both sides.
    [Fact]
    public void Password_WithALoneSurrogate_IsRefused()
    {
        string broken = "long enough \uD800 password";

        Assert.Null(RecoveryFile.Normalized(broken));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Create(broken, out _));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(Made.Value.File, broken));
    }

    // Round 2 (security 2): a file whose password protection is weaker than what Create writes is refused, even
    // with the right password; the same file at the full count opens.
    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    [InlineData(RecoveryFile.Iterations - 1)]
    public void Read_FewerIterationsThanCreateWrites_Fails(int iterations)
    {
        byte[] weak = RecoveryFile.Build(Password, iterations, out _);

        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(weak, Password));

        byte[] full = RecoveryFile.Build(Password, RecoveryFile.Iterations, out byte[] publicKey);
        using ECDiffieHellman key = RecoveryFile.Read(full, Password);
        Assert.Equal(publicKey, key.ExportSubjectPublicKeyInfo());
    }

    // AC-8: a file that is not a recovery file, was changed, or is too large fails the same way.
    [Fact]
    public void Read_WrongFile_Fails()
    {
        byte[] file = Made.Value.File;
        byte[] flipped = [.. file];
        flipped[file.Length / 2] ^= 0x01;
        byte[] large = new byte[RecoveryFile.MaxFileBytes + 1];
        file.CopyTo(large, 0);

        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(flipped, Password));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(file[..^1], Password));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read([.. file, 0x00], Password));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read([], Password));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(Encoding.UTF8.GetBytes("not a recovery file"), Password));
        Assert.Throws<RecoveryFileException>(() => RecoveryFile.Read(large, Password));
    }

    // AlgorithmIdentifier of PBKDF2: salt, at least 600,000 iterations, HMAC-SHA256.
    private static void AssertSlowPbkdf2(AsnReader derivation)
    {
        Assert.Equal("1.2.840.113549.1.5.12", derivation.ReadObjectIdentifier());
        AsnReader parameters = derivation.ReadSequence();
        Assert.True(parameters.ReadOctetString().Length >= 8);
        Assert.True(parameters.TryReadInt32(out int iterations));
        Assert.True(iterations >= 600_000, iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(RecoveryFile.Iterations, iterations);
        if (parameters.PeekTag() == Asn1Tag.Integer)
        {
            Assert.True(parameters.TryReadInt32(out int keyLength));
            Assert.Equal(32, keyLength);
        }

        Assert.Equal("1.2.840.113549.2.9", parameters.ReadSequence().ReadObjectIdentifier());
    }

    // The content of a ContentInfo of type data.
    private static byte[] Data(AsnReader contentInfo)
    {
        Assert.Equal("1.2.840.113549.1.7.1", contentInfo.ReadObjectIdentifier());
        return contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadOctetString();
    }
}
