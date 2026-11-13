using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace Context;

/// <summary>Thrown when a recovery file can't be read: wrong password, wrong or damaged file. It carries no detail.</summary>
internal sealed class RecoveryFileException : Exception
{
    public RecoveryFileException()
        : base("Can't read the recovery file.")
    {
    }
}

/// <summary>
/// The recovery file: a PKCS #12 (<c>.pfx</c>) file that holds only the recovery private key, in one
/// PKCS #8 shrouded key bag protected with PBES2 (AES-256-CBC, PBKDF2 with HMAC-SHA256 and
/// <see cref="Iterations"/> iterations). The file's integrity check is PBMAC1 (RFC 9579) with the same PBKDF2
/// settings and HMAC-SHA256. It is written with the framework's ASN.1 writer, because the framework itself has no
/// PKCS #12 builder without an extra package. Nothing here touches a file or a key store: <see cref="Create"/>
/// returns the bytes to write, <see cref="Read"/> takes the bytes read.
/// </summary>
internal static class RecoveryFile
{
    /// <summary>The PBKDF2 iterations of both the key protection and the integrity check.</summary>
    public const int Iterations = 600_000;

    /// <summary>The shortest password a recovery file is made with.</summary>
    public const int MinPasswordLength = 12;

    /// <summary>The largest recovery file that is read (1 MB).</summary>
    public const long MaxFileBytes = 1024 * 1024;

    // A file asking for more work than this is refused before any key is derived.
    private const int MaxIterations = 10_000_000;
    private const int MacKeySize = 32;
    private const string DataOid = "1.2.840.113549.1.7.1";
    private const string ShroudedKeyBagOid = "1.2.840.113549.1.12.10.1.2";
    private const string Pbes2Oid = "1.2.840.113549.1.5.13";
    private const string Pbmac1Oid = "1.2.840.113549.1.5.14";
    private const string Pbkdf2Oid = "1.2.840.113549.1.5.12";
    private const string HmacSha256Oid = "1.2.840.113549.2.9";

    private static readonly Asn1Tag Explicit0 = new(TagClass.ContextSpecific, 0, isConstructed: true);

    /// <summary>
    /// Makes a new recovery key in memory and returns the recovery file's bytes, protected with
    /// <paramref name="password"/>; <paramref name="publicKey"/> gets the key's SubjectPublicKeyInfo. The private
    /// key exists nowhere else and is gone when this returns.
    /// </summary>
    public static byte[] Create(string password, out byte[] publicKey)
    {
        using ECDiffieHellman key = SealedNote.NewKey();
        publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] shrouded = key.ExportEncryptedPkcs8PrivateKey(
            password.AsSpan(),
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, Iterations));
        byte[] authenticatedSafe = AuthenticatedSafe(shrouded);
        byte[] salt = RandomNumberGenerator.GetBytes(MacKeySize);
        byte[] mac = Mac(password, salt, Iterations, authenticatedSafe);
        return Pfx(authenticatedSafe, salt, mac);
    }

    /// <summary>
    /// Reads the recovery key from a file <see cref="Create"/> made. The key lives only in memory; the caller
    /// disposes it. Every failure is one <see cref="RecoveryFileException"/>.
    /// </summary>
    public static ECDiffieHellman Read(byte[] file, string password)
    {
        try
        {
            if (file.LongLength > MaxFileBytes)
            {
                throw new RecoveryFileException();
            }

            AsnReader outer = new AsnReader(file, AsnEncodingRules.DER);
            AsnReader pfx = outer.ReadSequence();
            outer.ThrowIfNotEmpty();
            if (!pfx.TryReadInt32(out int version) || version != 3)
            {
                throw new RecoveryFileException();
            }

            byte[] authenticatedSafe = DataContent(pfx.ReadSequence());
            CheckMac(pfx.ReadSequence(), password, authenticatedSafe);
            pfx.ThrowIfNotEmpty();

            AsnReader safes = new AsnReader(authenticatedSafe, AsnEncodingRules.DER).ReadSequence();
            byte[] safeContents = DataContent(safes.ReadSequence());
            safes.ThrowIfNotEmpty();
            AsnReader bags = new AsnReader(safeContents, AsnEncodingRules.DER).ReadSequence();
            AsnReader bag = bags.ReadSequence();
            bags.ThrowIfNotEmpty();
            Expect(bag, ShroudedKeyBagOid);
            byte[] shrouded = bag.ReadSequence(Explicit0).ReadEncodedValue().ToArray();
            CheckKeyProtection(shrouded);

            ECDiffieHellman key = SealedNote.NewKey();
            try
            {
                key.ImportEncryptedPkcs8PrivateKey(password.AsSpan(), shrouded, out int read);
                if (read != shrouded.Length)
                {
                    throw new RecoveryFileException();
                }

                // Only a P-256 key is a recovery key.
                _ = SealedNote.PointOf(key.ExportSubjectPublicKeyInfo());
                return key;
            }
            catch (Exception)
            {
                key.Dispose();
                throw;
            }
        }
        catch (Exception)
        {
            throw new RecoveryFileException();
        }
    }

    // AuthenticatedSafe: one data ContentInfo holding SafeContents with the one shrouded key bag.
    private static byte[] AuthenticatedSafe(byte[] shrouded)
    {
        AsnWriter contents = new AsnWriter(AsnEncodingRules.DER);
        using (contents.PushSequence())
        {
            using (contents.PushSequence())
            {
                contents.WriteObjectIdentifier(ShroudedKeyBagOid);
                using (contents.PushSequence(Explicit0))
                {
                    contents.WriteEncodedValue(shrouded);
                }
            }
        }

        AsnWriter safe = new AsnWriter(AsnEncodingRules.DER);
        using (safe.PushSequence())
        {
            WriteData(safe, contents.Encode());
        }

        return safe.Encode();
    }

    private static byte[] Pfx(byte[] authenticatedSafe, byte[] salt, byte[] mac)
    {
        AsnWriter pfx = new AsnWriter(AsnEncodingRules.DER);
        using (pfx.PushSequence())
        {
            pfx.WriteInteger(3);
            WriteData(pfx, authenticatedSafe);
            using (pfx.PushSequence())
            {
                using (pfx.PushSequence())
                {
                    using (pfx.PushSequence())
                    {
                        pfx.WriteObjectIdentifier(Pbmac1Oid);
                        using (pfx.PushSequence())
                        {
                            using (pfx.PushSequence())
                            {
                                pfx.WriteObjectIdentifier(Pbkdf2Oid);
                                using (pfx.PushSequence())
                                {
                                    pfx.WriteOctetString(salt);
                                    pfx.WriteInteger(Iterations);
                                    pfx.WriteInteger(MacKeySize);
                                    WriteHmacSha256(pfx);
                                }
                            }

                            WriteHmacSha256(pfx);
                        }
                    }

                    pfx.WriteOctetString(mac);
                }

                // PBMAC1 carries its own salt and iterations; these two fields are placeholders (RFC 9579).
                pfx.WriteOctetString(Encoding.ASCII.GetBytes("NOT USED"));
                pfx.WriteInteger(1);
            }
        }

        return pfx.Encode();
    }

    // ContentInfo of type data: the content is an OCTET STRING inside [0].
    private static void WriteData(AsnWriter writer, byte[] content)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(DataOid);
            using (writer.PushSequence(Explicit0))
            {
                writer.WriteOctetString(content);
            }
        }
    }

    private static void WriteHmacSha256(AsnWriter writer)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(HmacSha256Oid);
            writer.WriteNull();
        }
    }

    private static byte[] DataContent(AsnReader contentInfo)
    {
        Expect(contentInfo, DataOid);
        AsnReader content = contentInfo.ReadSequence(Explicit0);
        contentInfo.ThrowIfNotEmpty();
        byte[] data = content.ReadOctetString();
        content.ThrowIfNotEmpty();
        return data;
    }

    private static void Expect(AsnReader reader, string oid)
    {
        if (!string.Equals(reader.ReadObjectIdentifier(), oid, StringComparison.Ordinal))
        {
            throw new RecoveryFileException();
        }
    }

    // The integrity check must be PBMAC1 with PBKDF2 and HMAC-SHA256, and must match before the key is touched.
    private static void CheckMac(AsnReader macData, string password, byte[] authenticatedSafe)
    {
        AsnReader digestInfo = macData.ReadSequence();
        AsnReader algorithm = digestInfo.ReadSequence();
        byte[] expected = digestInfo.ReadOctetString();
        digestInfo.ThrowIfNotEmpty();
        Expect(algorithm, Pbmac1Oid);
        AsnReader parameters = algorithm.ReadSequence();
        algorithm.ThrowIfNotEmpty();
        (byte[] salt, int iterations) = ReadPbkdf2(parameters.ReadSequence(), MacKeySize);
        Expect(parameters.ReadSequence(), HmacSha256Oid);
        parameters.ThrowIfNotEmpty();

        byte[] actual = Mac(password, salt, iterations, authenticatedSafe);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new RecoveryFileException();
        }
    }

    // The key bag must be PBES2 over PBKDF2 with HMAC-SHA256 and a bounded iteration count.
    private static void CheckKeyProtection(byte[] shrouded)
    {
        AsnReader info = new AsnReader(shrouded, AsnEncodingRules.DER).ReadSequence();
        AsnReader algorithm = info.ReadSequence();
        Expect(algorithm, Pbes2Oid);
        AsnReader parameters = algorithm.ReadSequence();
        _ = ReadPbkdf2(parameters.ReadSequence(), null);
    }

    // AlgorithmIdentifier of PBKDF2: salt, iterations, an optional key length and the PRF, which must be HMAC-SHA256.
    private static (byte[] Salt, int Iterations) ReadPbkdf2(AsnReader function, int? keyLength)
    {
        Expect(function, Pbkdf2Oid);
        AsnReader parameters = function.ReadSequence();
        byte[] salt = parameters.ReadOctetString();
        if (!parameters.TryReadInt32(out int iterations) || iterations < 1 || iterations > MaxIterations)
        {
            throw new RecoveryFileException();
        }

        if (parameters.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
        {
            if (!parameters.TryReadInt32(out int length) || (keyLength is { } wanted && length != wanted))
            {
                throw new RecoveryFileException();
            }
        }
        else if (keyLength is not null)
        {
            throw new RecoveryFileException();
        }

        Expect(parameters.ReadSequence(), HmacSha256Oid);
        return (salt, iterations);
    }

    // PBMAC1: HMAC-SHA256 over the AuthenticatedSafe with a PBKDF2 key from the UTF-8 password.
    private static byte[] Mac(string password, byte[] salt, int iterations, byte[] authenticatedSafe)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, MacKeySize);
        try
        {
            return HMACSHA256.HashData(key, authenticatedSafe);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
