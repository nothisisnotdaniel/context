using System.Security.Cryptography;

namespace Context;

/// <summary>
/// What the settings page shows about the key that opens sensitive notes on this machine. Public data only: a name,
/// a fingerprint and when it expires. The name is raw outside text (a certificate's common name).
/// </summary>
internal sealed record KeyDetails
{
    public KeyDetails(string name, string fingerprint, DateTime? expiresUtc)
    {
        Name = name;
        Fingerprint = fingerprint;
        ExpiresUtc = expiresUtc is DateTime expires ? AsUtc(expires) : null;
    }

    /// <summary>The key's name: the certificate's common name on Windows, a fixed name on macOS.</summary>
    public string Name { get; }

    /// <summary>
    /// Upper-case hex: the certificate's thumbprint on Windows, else the SHA-256 of the public key
    /// (<see cref="FromPublicKey"/>).
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>When the key's certificate expires (UTC), or <c>null</c> for a key without one.</summary>
    public DateTime? ExpiresUtc { get; }

    /// <summary>
    /// The details of a key that has no certificate: the fingerprint is the upper-case hex SHA-256 of
    /// <paramref name="publicKey"/> (SubjectPublicKeyInfo; the same bytes as <see cref="SealedNote.KeyId"/>, which
    /// is lower-case), and it never expires.
    /// </summary>
    public static KeyDetails FromPublicKey(string name, byte[] publicKey) =>
        new(name, Convert.ToHexString(SHA256.HashData(publicKey)), null);

    /// <summary>Returns only the type name, so formatting the details never prints their raw text.</summary>
    public override string ToString() => nameof(KeyDetails);

    // A local time (X509Certificate2.NotAfter) is converted; a time without a kind is taken as UTC.
    private static DateTime AsUtc(DateTime time) =>
        time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
}

/// <summary>What <see cref="DesktopState.SensitiveKey"/> found.</summary>
internal enum SensitiveKeyKind
{
    /// <summary>This OS has no key store the app can use.</summary>
    Unsupported,

    /// <summary>There is no machine key yet.</summary>
    NotSetUp,

    /// <summary>The machine key exists: <see cref="SensitiveKeyView.Details"/>.</summary>
    Shown,

    /// <summary>The key store failed.</summary>
    Unreadable,
}

/// <summary>The sensitive-notes section of the settings page, with no rendering.</summary>
internal sealed record SensitiveKeyView
{
    public SensitiveKeyView(SensitiveKeyKind kind, KeyDetails? details = null)
    {
        Kind = kind;
        Details = details;
    }

    public SensitiveKeyKind Kind { get; }

    /// <summary>The machine key's details (<see cref="SensitiveKeyKind.Shown"/> only), else <c>null</c>.</summary>
    public KeyDetails? Details { get; }

    /// <summary>Returns only the type name, so formatting the view never prints its raw text.</summary>
    public override string ToString() => nameof(SensitiveKeyView);
}
