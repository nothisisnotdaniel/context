using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Context;

/// <summary>
/// Thrown by <see cref="Vault.Unlock"/> when the passphrase doesn't unwrap the data key. Its message never holds
/// the passphrase.
/// </summary>
internal sealed class WrongPassphraseException : Exception
{
    public WrongPassphraseException()
        : base("Wrong passphrase.")
    {
    }
}

/// <summary>
/// Thrown when encrypted bytes can't be decrypted (wrong kind, changed or cut bytes, an unknown version, or no
/// unlocked vault), or when <c>encryption.json</c> can't be read. It is an <see cref="IOException"/>, so every
/// store handles it like any other unreadable file. Its message is a fixed text, never file content.
/// </summary>
internal sealed class VaultException : IOException
{
    public VaultException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Encryption at rest. <c>&lt;root&gt;/encryption.json</c> holds the KDF settings, a random salt and a random
/// 32-byte data key wrapped (AES-256-GCM) under a key derived from the passphrase (PBKDF2-HMAC-SHA256). Nothing
/// in it is secret in plain form; its presence means "encryption on".
/// <para>
/// An encrypted file is <c>00 43 54 58 45</c> (<c>\0CTXE</c>), version <c>01</c>, a 12-byte random nonce, the
/// ciphertext and a 16-byte tag. The additional data is the file's kind (<c>note</c>, <c>todos</c>, ...), so a
/// file can't be passed off as another kind. <see cref="Dispose"/> zeroes the data key.
/// </para>
/// </summary>
internal sealed class Vault : IDisposable
{
    /// <summary>The file name of the key file inside the root.</summary>
    public const string FileName = "encryption.json";

    /// <summary>The PBKDF2 iteration count new vaults use.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>
    /// The lowest iteration count <see cref="Unlock(string, string)"/> accepts from a key file, so a changed
    /// <c>encryption.json</c> can't make the passphrase cheap to guess.
    /// </summary>
    public const int MinUnlockIterations = 100_000;

    public const string KindNote = "note";
    public const string KindTodos = "todos";
    public const string KindCalendarLink = "calendar-link";
    public const string KindCalendarCache = "calendar-cache";
    public const string KindCalendar = "calendar";

    /// <summary>The number of bytes an encrypted file has on top of its plain bytes.</summary>
    public const int Overhead = HeaderLength + TagLength;

    private const byte FormatVersion = 1;
    private const int KeyFileVersion = 1;
    private const string KdfName = "PBKDF2-HMAC-SHA256";
    private const int KeyLength = 32;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 5 + 1 + NonceLength;
    private const int MinIterations = 1;
    private const int MaxIterations = 10_000_000;
    private const string WrapContext = "context-data-key-v1";

    private static readonly byte[] Magic = [0x00, 0x43, 0x54, 0x58, 0x45];

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        KindNote, KindTodos, KindCalendarLink, KindCalendarCache, KindCalendar,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly byte[] key;
    private bool disposed;

    private Vault(byte[] key)
    {
        this.key = key;
    }

    /// <summary>The full path of <c>encryption.json</c> in <paramref name="root"/>.</summary>
    public static string KeyFilePath(string root) => Path.Combine(Path.GetFullPath(root), FileName);

    /// <summary>True when <paramref name="root"/> has an <c>encryption.json</c>, that is, encryption is on.</summary>
    public static bool Exists(string root) => File.Exists(KeyFilePath(root));

    /// <summary>
    /// Makes a new random data key, wraps it under <paramref name="passphrase"/> and writes
    /// <c>encryption.json</c> (creating the root folder if needed, never replacing an existing one). Returns the
    /// unlocked vault.
    /// </summary>
    public static Vault Create(string root, string passphrase) => Create(root, passphrase, DefaultIterations);

    /// <summary>Like <see cref="Create(string, string)"/> with a given iteration count (tests use a low one).</summary>
    internal static Vault Create(string root, string passphrase, int iterations)
    {
        if (iterations < MinIterations || iterations > MaxIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] dataKey = RandomNumberGenerator.GetBytes(KeyLength);
        byte[] passKey = DeriveKey(passphrase, salt, iterations);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        byte[] wrapped = new byte[KeyLength + TagLength];
        try
        {
            using (AesGcm aes = new AesGcm(passKey, TagLength))
            {
                aes.Encrypt(
                    nonce,
                    dataKey,
                    wrapped.AsSpan(0, KeyLength),
                    wrapped.AsSpan(KeyLength),
                    Encoding.UTF8.GetBytes(WrapContext));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passKey);
        }

        KeyFile file = new KeyFile
        {
            Version = KeyFileVersion,
            Kdf = KdfName,
            Iterations = iterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            WrappedKey = Convert.ToBase64String(wrapped),
        };
        string path = KeyFilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] json = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(file, JsonOptions) + "\n");
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            CalendarLink.WritePrivate(temp, json);
            // No overwrite: an existing key file must never be replaced.
            File.Move(temp, path, overwrite: false);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return new Vault(dataKey);
    }

    /// <summary>
    /// Reads <c>encryption.json</c> and unwraps the data key with <paramref name="passphrase"/>. Throws
    /// <see cref="WrongPassphraseException"/> when it doesn't fit, and <see cref="VaultException"/> when the file
    /// is missing, not valid or asks for fewer than <see cref="MinUnlockIterations"/> iterations. Never writes
    /// anything.
    /// </summary>
    public static Vault Unlock(string root, string passphrase) => Unlock(root, passphrase, MinUnlockIterations);

    /// <summary>Like <see cref="Unlock(string, string)"/> with a given minimum iteration count (tests use a low one).</summary>
    internal static Vault Unlock(string root, string passphrase, int minIterations)
    {
        string path = KeyFilePath(root);
        KeyFile? file;
        try
        {
            file = JsonSerializer.Deserialize<KeyFile>(File.ReadAllBytes(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            throw new VaultException($"Can't read {FileName}.", ex);
        }

        byte[] salt;
        byte[] nonce;
        byte[] wrapped;
        try
        {
            if (file is null || file.Version != KeyFileVersion || file.Kdf != KdfName
                || file.Iterations is null || file.Iterations > MaxIterations
                || file.Salt is null || file.Nonce is null || file.WrappedKey is null)
            {
                throw new VaultException($"Can't read {FileName}.");
            }

            if (file.Iterations < Math.Max(MinIterations, minIterations))
            {
                throw new VaultException($"{FileName} asks for too few key iterations; it may have been changed.");
            }

            salt = Convert.FromBase64String(file.Salt);
            nonce = Convert.FromBase64String(file.Nonce);
            wrapped = Convert.FromBase64String(file.WrappedKey);
        }
        catch (FormatException ex)
        {
            throw new VaultException($"Can't read {FileName}.", ex);
        }

        if (salt.Length != SaltLength || nonce.Length != NonceLength || wrapped.Length != KeyLength + TagLength)
        {
            throw new VaultException($"Can't read {FileName}.");
        }

        byte[] passKey = DeriveKey(passphrase, salt, file.Iterations.Value);
        byte[] dataKey = new byte[KeyLength];
        try
        {
            using AesGcm aes = new AesGcm(passKey, TagLength);
            aes.Decrypt(
                nonce,
                wrapped.AsSpan(0, KeyLength),
                wrapped.AsSpan(KeyLength),
                dataKey,
                Encoding.UTF8.GetBytes(WrapContext));
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw new WrongPassphraseException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passKey);
        }

        return new Vault(dataKey);
    }

    /// <summary>True when <paramref name="bytes"/> starts with the encrypted-file magic.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);

    /// <summary>Encrypts <paramref name="plain"/> as a file of <paramref name="kind"/>, with a fresh random nonce.</summary>
    public byte[] Encrypt(string kind, ReadOnlySpan<byte> plain)
    {
        byte[] aad = KindBytes(kind);
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] output = new byte[HeaderLength + plain.Length + TagLength];
        Magic.CopyTo(output, 0);
        output[Magic.Length] = FormatVersion;
        Span<byte> nonce = output.AsSpan(Magic.Length + 1, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using AesGcm aes = new AesGcm(key, TagLength);
        aes.Encrypt(
            nonce,
            plain,
            output.AsSpan(HeaderLength, plain.Length),
            output.AsSpan(HeaderLength + plain.Length, TagLength),
            aad);
        return output;
    }

    /// <summary>
    /// Decrypts a file of <paramref name="kind"/>. Throws <see cref="VaultException"/> when it isn't an encrypted
    /// file of a known version, is too short, or fails authentication (other kind, changed bytes).
    /// </summary>
    public byte[] Decrypt(string kind, ReadOnlySpan<byte> data)
    {
        byte[] aad = KindBytes(kind);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsEncrypted(data) || data.Length < HeaderLength + TagLength || data[Magic.Length] != FormatVersion)
        {
            throw new VaultException($"Can't decrypt this {kind} file.");
        }

        int length = data.Length - HeaderLength - TagLength;
        byte[] plain = new byte[length];
        try
        {
            using AesGcm aes = new AesGcm(key, TagLength);
            aes.Decrypt(
                data.Slice(Magic.Length + 1, NonceLength),
                data.Slice(HeaderLength, length),
                data.Slice(HeaderLength + length, TagLength),
                plain,
                aad);
        }
        catch (CryptographicException ex)
        {
            throw new VaultException($"Can't decrypt this {kind} file.", ex);
        }

        return plain;
    }

    /// <summary>Zeroes the data key. The vault can't be used afterwards.</summary>
    public void Dispose()
    {
        if (!disposed)
        {
            CryptographicOperations.ZeroMemory(key);
            disposed = true;
        }
    }

    private static byte[] KindBytes(string kind)
    {
        if (!Kinds.Contains(kind))
        {
            throw new ArgumentException("Unknown file kind.", nameof(kind));
        }

        return Encoding.UTF8.GetBytes(kind);
    }

    /// <summary>
    /// The passphrase in Unicode normalization form C, so the same text typed on another OS or keyboard (composed
    /// or decomposed accents) derives the same key. <see cref="Create(string, string)"/> and
    /// <see cref="Unlock(string, string)"/> both apply it.
    /// </summary>
    public static string NormalizePassphrase(string passphrase) => passphrase.Normalize(NormalizationForm.FormC);

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            NormalizePassphrase(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeyLength);

    /// <summary>The JSON shape of <c>encryption.json</c>. Every field is nullable so a missing one is caught.</summary>
    private sealed class KeyFile
    {
        public int? Version { get; set; }

        public string? Kdf { get; set; }

        public int? Iterations { get; set; }

        public string? Salt { get; set; }

        public string? Nonce { get; set; }

        public string? WrappedKey { get; set; }
    }
}
