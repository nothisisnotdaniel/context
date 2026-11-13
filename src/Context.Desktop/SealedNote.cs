using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>Thrown when a sealed note can't be opened. It carries no detail, so nothing about the file leaks.</summary>
internal sealed class SealedNoteException : Exception
{
    public SealedNoteException()
        : base("Can't open the sealed note.")
    {
    }
}

/// <summary>A public key a note key is wrapped for: the machine key or the recovery key.</summary>
internal sealed record SealedRecipient
{
    public const string Machine = "machine";
    public const string Recovery = "recovery";

    public SealedRecipient(string kind, byte[] publicKey)
    {
        Kind = kind;
        PublicKey = publicKey;
    }

    /// <summary><see cref="Machine"/> or <see cref="Recovery"/>.</summary>
    public string Kind { get; }

    /// <summary>The P-256 public key as SubjectPublicKeyInfo.</summary>
    public byte[] PublicKey { get; }

    /// <summary>The type name only.</summary>
    public override string ToString() => nameof(SealedRecipient);
}

/// <summary>
/// The file format of a sensitive note (<c>&lt;Name&gt;.ctxs</c>, version 1, UTF-8 JSON):
/// <c>{"v":1,"nonce","tag","text","keys":[{"id","kind","epk","nonce","tag","key"}]}</c>, binary values in base64.
/// The text is sealed with AES-256-GCM under a fresh random note key; the additional data is
/// <c>context-note-v1\n&lt;Name&gt;</c>, so a file renamed on disk no longer opens. The note key is wrapped once per
/// recipient: an ephemeral P-256 key agrees with the recipient's public key, HKDF-SHA256 (salt = ephemeral public
/// point followed by the recipient's public point, info <c>context-note-key-v1</c>) gives a 32-byte key and
/// AES-256-GCM wraps the note key with the recipient's <c>id</c> as additional data. <c>id</c> is the lower-case hex
/// SHA-256 of the recipient's SubjectPublicKeyInfo. Nothing here touches a file or a key store.
/// </summary>
internal static class SealedNote
{
    /// <summary>The largest sealed file that is read (8 MB).</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>The most recipients one file can have.</summary>
    public const int MaxKeys = 8;

    private const int Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int PointSize = 65;
    private const int MaxJsonDepth = 8;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] WrapInfo = Encoding.ASCII.GetBytes("context-note-key-v1");

    /// <summary>The <c>id</c> of a recipient: lower-case hex SHA-256 of its SubjectPublicKeyInfo.</summary>
    public static string KeyId(ReadOnlySpan<byte> publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey));

    /// <summary>A new P-256 key that lives only in memory.</summary>
    public static ECDiffieHellman NewKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The uncompressed point (<c>04 || X || Y</c>, 65 bytes) of a P-256 SubjectPublicKeyInfo.</summary>
    public static byte[] PointOf(ReadOnlySpan<byte> publicKey)
    {
        using ECDiffieHellman key = ECDiffieHellman.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out int read);
        if (read != publicKey.Length)
        {
            throw new CryptographicException();
        }

        return PointOf(key);
    }

    /// <summary>The SubjectPublicKeyInfo of an uncompressed P-256 point (65 bytes).</summary>
    public static byte[] PublicKeyOf(ReadOnlySpan<byte> point)
    {
        using ECDiffieHellman key = Import(point);
        return key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The raw P-256 agreement (the shared point's X, 32 bytes) of <paramref name="own"/> with the uncompressed
    /// point <paramref name="peer"/>. A key store that holds its key in memory agrees through this.
    /// </summary>
    public static byte[] Agree(ECDiffieHellman own, ReadOnlySpan<byte> peer)
    {
        using ECDiffieHellman other = Import(peer);
        return own.DeriveRawSecretAgreement(other.PublicKey);
    }

    /// <summary>
    /// Seals <paramref name="text"/> as the note <paramref name="name"/> for <paramref name="recipients"/> (1 to
    /// <see cref="MaxKeys"/>) and returns the file's bytes. Every call uses a new note key and new nonces; no private
    /// key is needed.
    /// </summary>
    public static byte[] Seal(string name, string text, IReadOnlyList<SealedRecipient> recipients)
    {
        if (recipients.Count < 1 || recipients.Count > MaxKeys)
        {
            throw new ArgumentException("A sealed note needs 1 to 8 recipients.", nameof(recipients));
        }

        byte[] noteKey = RandomNumberGenerator.GetBytes(KeySize);
        byte[] plain = Utf8.GetBytes(text);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] tag = new byte[TagSize];
            byte[] sealedText = new byte[plain.Length];
            using (AesGcm aes = new AesGcm(noteKey, TagSize))
            {
                aes.Encrypt(nonce, plain, sealedText, tag, ContentData(name));
            }

            Wrap[] wraps = recipients.Select(r => WrapFor(r, noteKey)).ToArray();
            return Write(nonce, tag, sealedText, wraps);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(noteKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// Opens the sealed file <paramref name="file"/> as the note <paramref name="name"/> and returns its text. Only
    /// the wrap whose <c>id</c> is <see cref="KeyId"/> of <paramref name="publicKey"/> (the SubjectPublicKeyInfo of
    /// the key that <paramref name="agree"/> holds) is used; <paramref name="agree"/> gets the file's ephemeral
    /// point and returns the raw agreement, and is called at most once (never when no wrap has that id). Every
    /// failure, whatever the cause, is one <see cref="SealedNoteException"/>.
    /// </summary>
    public static string Open(string name, byte[] file, byte[] publicKey, Func<byte[], byte[]> agree)
    {
        byte[]? noteKey = null;
        byte[]? plain = null;
        try
        {
            Parsed parsed = Read(file);
            noteKey = Unwrap(parsed, publicKey, agree);
            plain = new byte[parsed.Text.Length];
            using (AesGcm aes = new AesGcm(noteKey, TagSize))
            {
                aes.Decrypt(parsed.Nonce, parsed.Text, parsed.Tag, plain, ContentData(name));
            }

            return Utf8.GetString(plain);
        }
        catch (Exception)
        {
            throw new SealedNoteException();
        }
        finally
        {
            Zero(noteKey);
            Zero(plain);
        }
    }

    /// <summary>
    /// True when <paramref name="file"/> is a readable sealed note that has a wrap for <paramref name="publicKey"/>
    /// (by id only; nothing is unwrapped). False for any other file.
    /// </summary>
    public static bool HasKey(byte[] file, byte[] publicKey)
    {
        try
        {
            string id = KeyId(publicKey);
            return Read(file).Wraps.Any(w => string.Equals(w.Id, id, StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns <paramref name="file"/> with one more wrap of its note key, for <paramref name="added"/> (replacing a
    /// wrap with the same id). The note key is unwrapped like in <see cref="Open"/>, and the text must open as
    /// <paramref name="name"/> first, so a file that is not this note is never rewritten. The sealed text itself is
    /// kept byte for byte. Fails with <see cref="SealedNoteException"/>, also when the file would get more than
    /// <see cref="MaxKeys"/> wraps.
    /// </summary>
    public static byte[] AddRecipient(
        string name, byte[] file, byte[] publicKey, Func<byte[], byte[]> agree, SealedRecipient added)
    {
        byte[]? noteKey = null;
        byte[]? plain = null;
        try
        {
            Parsed parsed = Read(file);
            noteKey = Unwrap(parsed, publicKey, agree);
            plain = new byte[parsed.Text.Length];
            using (AesGcm aes = new AesGcm(noteKey, TagSize))
            {
                aes.Decrypt(parsed.Nonce, parsed.Text, parsed.Tag, plain, ContentData(name));
            }

            Wrap wrap = WrapFor(added, noteKey);
            Wrap[] wraps = [.. parsed.Wraps.Where(w => !string.Equals(w.Id, wrap.Id, StringComparison.Ordinal)), wrap];
            if (wraps.Length > MaxKeys)
            {
                throw new SealedNoteException();
            }

            return Write(parsed.Nonce, parsed.Tag, parsed.Text, wraps);
        }
        catch (Exception)
        {
            throw new SealedNoteException();
        }
        finally
        {
            Zero(noteKey);
            Zero(plain);
        }
    }

    private static void Zero(byte[]? bytes)
    {
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static byte[] ContentData(string name) => Utf8.GetBytes("context-note-v1\n" + name);

    private static byte[] PointOf(ECDiffieHellman key)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        if (parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
        {
            throw new CryptographicException();
        }

        return [4, .. x, .. y];
    }

    // A public-only P-256 key from an uncompressed point; a point that is not on the curve is refused.
    private static ECDiffieHellman Import(ReadOnlySpan<byte> point)
    {
        if (point.Length != PointSize || point[0] != 4)
        {
            throw new CryptographicException();
        }

        ECParameters parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point.Slice(1, 32).ToArray(), Y = point.Slice(33, 32).ToArray() },
        };
        parameters.Validate();
        return ECDiffieHellman.Create(parameters);
    }

    // The key that wraps a note key: HKDF-SHA256 over the raw agreement, bound to both public points.
    private static byte[] WrapKey(byte[] agreement, byte[] ephemeralPoint, byte[] recipientPoint)
    {
        byte[] salt = [.. ephemeralPoint, .. recipientPoint];
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, agreement, KeySize, salt, WrapInfo);
    }

    private static Wrap WrapFor(SealedRecipient recipient, byte[] noteKey)
    {
        if (recipient.Kind is not (SealedRecipient.Machine or SealedRecipient.Recovery))
        {
            throw new ArgumentException("Unknown recipient kind.", nameof(recipient));
        }

        string id = KeyId(recipient.PublicKey);
        byte[] recipientPoint = PointOf(recipient.PublicKey);
        using ECDiffieHellman ephemeral = NewKey();
        byte[] ephemeralPoint = PointOf(ephemeral);
        byte[] agreement = Agree(ephemeral, recipientPoint);
        byte[] wrapKey = WrapKey(agreement, ephemeralPoint, recipientPoint);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] tag = new byte[TagSize];
            byte[] wrapped = new byte[KeySize];
            using AesGcm aes = new AesGcm(wrapKey, TagSize);
            aes.Encrypt(nonce, noteKey, wrapped, tag, Encoding.ASCII.GetBytes(id));
            return new Wrap(id, recipient.Kind, ephemeralPoint, nonce, tag, wrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(agreement);
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    // The note key from the one wrap made for publicKey. agree is called once, and not at all without such a wrap.
    private static byte[] Unwrap(Parsed parsed, byte[] publicKey, Func<byte[], byte[]> agree)
    {
        string id = KeyId(publicKey);
        Wrap wrap = parsed.Wraps.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.Ordinal))
            ?? throw new SealedNoteException();
        byte[] recipientPoint = PointOf(publicKey);
        byte[] agreement = agree([.. wrap.Point]);
        byte[] wrapKey = WrapKey(agreement, wrap.Point, recipientPoint);
        byte[] noteKey = new byte[KeySize];
        try
        {
            using AesGcm aes = new AesGcm(wrapKey, TagSize);
            aes.Decrypt(wrap.Nonce, wrap.Key, wrap.Tag, noteKey, Encoding.ASCII.GetBytes(id));
            return noteKey;
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(noteKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(agreement);
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    private static byte[] Write(byte[] nonce, byte[] tag, byte[] text, Wrap[] wraps)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("v", Version);
            json.WriteBase64String("nonce", nonce);
            json.WriteBase64String("tag", tag);
            json.WriteBase64String("text", text);
            json.WriteStartArray("keys");
            foreach (Wrap wrap in wraps)
            {
                json.WriteStartObject();
                json.WriteString("id", wrap.Id);
                json.WriteString("kind", wrap.Kind);
                json.WriteBase64String("epk", wrap.Point);
                json.WriteBase64String("nonce", wrap.Nonce);
                json.WriteBase64String("tag", wrap.Tag);
                json.WriteBase64String("key", wrap.Key);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    // Strict: version 1, every field present with its exact size, 1 to 8 wraps of a known kind.
    private static Parsed Read(byte[] file)
    {
        if (file.LongLength > MaxFileBytes)
        {
            throw new SealedNoteException();
        }

        using JsonDocument document = JsonDocument.Parse(file, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("v").ValueKind != JsonValueKind.Number
            || !root.GetProperty("v").TryGetInt32(out int version)
            || version != Version)
        {
            throw new SealedNoteException();
        }

        JsonElement keys = root.GetProperty("keys");
        if (keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() < 1 || keys.GetArrayLength() > MaxKeys)
        {
            throw new SealedNoteException();
        }

        Wrap[] wraps = keys.EnumerateArray().Select(ReadWrap).ToArray();
        return new Parsed(
            Sized(root.GetProperty("nonce"), NonceSize),
            Sized(root.GetProperty("tag"), TagSize),
            root.GetProperty("text").GetBytesFromBase64(),
            wraps);
    }

    private static Wrap ReadWrap(JsonElement element)
    {
        string id = element.GetProperty("id").GetString() ?? throw new SealedNoteException();
        string kind = element.GetProperty("kind").GetString() ?? throw new SealedNoteException();
        if (id.Length != 64 || kind is not (SealedRecipient.Machine or SealedRecipient.Recovery))
        {
            throw new SealedNoteException();
        }

        return new Wrap(
            id,
            kind,
            Sized(element.GetProperty("epk"), PointSize),
            Sized(element.GetProperty("nonce"), NonceSize),
            Sized(element.GetProperty("tag"), TagSize),
            Sized(element.GetProperty("key"), KeySize));
    }

    private static byte[] Sized(JsonElement element, int size)
    {
        byte[] bytes = element.GetBytesFromBase64();
        return bytes.Length == size ? bytes : throw new SealedNoteException();
    }

    private sealed class Wrap
    {
        public Wrap(string id, string kind, byte[] point, byte[] nonce, byte[] tag, byte[] key)
        {
            Id = id;
            Kind = kind;
            Point = point;
            Nonce = nonce;
            Tag = tag;
            Key = key;
        }

        public string Id { get; }

        public string Kind { get; }

        public byte[] Point { get; }

        public byte[] Nonce { get; }

        public byte[] Tag { get; }

        public byte[] Key { get; }
    }

    private sealed class Parsed
    {
        public Parsed(byte[] nonce, byte[] tag, byte[] text, Wrap[] wraps)
        {
            Nonce = nonce;
            Tag = tag;
            Text = text;
            Wraps = wraps;
        }

        public byte[] Nonce { get; }

        public byte[] Tag { get; }

        public byte[] Text { get; }

        public Wrap[] Wraps { get; }
    }
}
