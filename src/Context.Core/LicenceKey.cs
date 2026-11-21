using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>
/// The contents of a verified Suite Pro licence key. Only <see cref="LicenceKey.TryVerify"/> makes one,
/// so every instance comes from a token whose signature matched a known key.
/// </summary>
internal sealed class Licence
{
    public Licence(string keyId, string licenceId, string name, DateOnly until, DateOnly issued)
    {
        KeyId = keyId;
        LicenceId = licenceId;
        Name = name;
        Until = until;
        Issued = issued;
    }

    /// <summary>The <c>kid</c>: which public key signed it.</summary>
    public string KeyId { get; }

    /// <summary>The <c>lid</c>: 22 base64url characters (16 random bytes), the id revocations name.</summary>
    public string LicenceId { get; }

    /// <summary>The licensee.</summary>
    public string Name { get; }

    /// <summary>The last build date this key covers (<c>until</c>).</summary>
    public DateOnly Until { get; }

    /// <summary>The day the key was issued.</summary>
    public DateOnly Issued { get; }

    /// <summary>True when a build of <paramref name="buildDate"/> is covered (on or before <see cref="Until"/>).</summary>
    public bool Covers(DateOnly buildDate) => buildDate <= Until;
}

/// <summary>What <see cref="LicenceKey.TryVerify"/> found: a licence, or one fixed reason (never the key text).</summary>
internal sealed class LicenceCheck
{
    private LicenceCheck(Licence? licence, string? problem)
    {
        Licence = licence;
        Problem = problem;
    }

    public Licence? Licence { get; }

    /// <summary>One of the fixed <see cref="LicenceKey"/> reasons, or <c>null</c> when it verified.</summary>
    public string? Problem { get; }

    public bool IsValid => Licence is not null;

    public static LicenceCheck Valid(Licence licence) => new(licence, null);

    public static LicenceCheck Invalid(string problem) => new(null, problem);
}

/// <summary>
/// The Suite Pro licence key format, verify only (signing lives in the issuer tool, never here):
/// <c>CTX1-&lt;base64url(payload)&gt;.&lt;base64url(signature)&gt;</c>, no padding, at most
/// <see cref="MaxLength"/> characters. The payload is UTF-8 JSON with exactly the properties
/// <c>v</c>, <c>kid</c>, <c>lid</c>, <c>name</c>, <c>ed</c>, <c>until</c> and <c>issued</c>; the signature is
/// ECDSA P-256 over SHA-256 of the exact payload bytes in IEEE P1363 form (64 bytes). The signature is
/// checked before any JSON is parsed. A port of Times' <c>src/Times/LicenceKey.cs</c> (Times#39): the constants,
/// limits and reasons are the same, so a key Times accepts Context accepts too.
/// </summary>
internal static partial class LicenceKey
{
    public const string Prefix = "CTX1-";
    public const int MaxLength = 1024;
    public const int SignatureBytes = 64;
    public const int MaxNameLength = 100;
    public const int Version = 1;
    public const string Edition = "suite-pro";
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The token is not in the <c>CTX1-</c> form (prefix, length, characters, padding or signature size).</summary>
    public const string Malformed = "it is not a licence key";

    /// <summary>No key in this build's table matches the token.</summary>
    public const string UnknownKey = "it was signed with an unknown key";

    /// <summary>The signature does not match the contents.</summary>
    public const string BadSignature = "its signature does not match";

    /// <summary>The signed contents break a format rule.</summary>
    public const string BadContents = "its contents are not valid";

    /// <summary>The licence id is in the revocation list.</summary>
    public const string Revoked = "it has been revoked";

    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Checks <paramref name="token"/> against <paramref name="keys"/> and <paramref name="revocations"/>.
    /// Never throws; a failure carries one fixed reason. The signature must verify with a key of the table
    /// before the payload is parsed, and the payload's <c>kid</c> must then name that same key.
    /// </summary>
    public static LicenceCheck TryVerify(string? token, LicencePublicKeys keys, LicenceRevocations revocations)
    {
        try
        {
            return Verify(token, keys, revocations);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException
            or JsonException or InvalidOperationException or DecoderFallbackException)
        {
            return LicenceCheck.Invalid(BadContents);
        }
    }

    private static LicenceCheck Verify(string? token, LicencePublicKeys keys, LicenceRevocations revocations)
    {
        if (!TrySplit(token, out byte[] payload, out byte[] signature))
        {
            return LicenceCheck.Invalid(Malformed);
        }

        if (keys.Count == 0)
        {
            return LicenceCheck.Invalid(UnknownKey);
        }

        string? signedBy = keys.FindSigner(payload, signature);
        if (signedBy is null)
        {
            return LicenceCheck.Invalid(BadSignature);
        }

        Licence? licence = Parse(payload);
        if (licence is null)
        {
            return LicenceCheck.Invalid(BadContents);
        }

        if (!string.Equals(licence.KeyId, signedBy, StringComparison.Ordinal))
        {
            // Signed by a key of the table, but it names another: an unknown name, or the wrong key.
            return LicenceCheck.Invalid(keys.Contains(licence.KeyId) ? BadSignature : UnknownKey);
        }

        if (revocations.Contains(licence.LicenceId))
        {
            return LicenceCheck.Invalid(Revoked);
        }

        return LicenceCheck.Valid(licence);
    }

    /// <summary>The token's payload and signature bytes, if it has the right shape (no JSON is looked at).</summary>
    internal static bool TrySplit(string? token, out byte[] payload, out byte[] signature)
    {
        payload = [];
        signature = [];
        if (token is null || token.Length > MaxLength || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string body = token[Prefix.Length..];
        int dot = body.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot != body.LastIndexOf('.'))
        {
            return false;
        }

        if (!TryDecode(body[..dot], out payload) || !TryDecode(body[(dot + 1)..], out signature))
        {
            return false;
        }

        return signature.Length == SignatureBytes && payload.Length > 0;
    }

    /// <summary>Strict unpadded base64url: only <c>A-Z a-z 0-9 - _</c>, and the canonical form of what it decodes to.</summary>
    internal static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length == 0 || text.Length % 4 == 1 || !text.All(IsBase64UrlChar))
        {
            return false;
        }

        byte[] decoded = Base64Url.DecodeFromChars(text);
        if (!string.Equals(Base64Url.EncodeToString(decoded), text, StringComparison.Ordinal))
        {
            return false;
        }

        bytes = decoded;
        return true;
    }

    /// <summary>Unpadded base64url of <paramref name="bytes"/>.</summary>
    internal static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);

    /// <summary>True for a <c>kid</c>: 1-16 characters of <c>a-z 0-9 -</c>.</summary>
    public static bool IsValidKeyId(string? keyId) => keyId is not null && KeyIdPattern().IsMatch(keyId);

    /// <summary>True for a <c>lid</c>: 22 base64url characters that are the canonical form of 16 bytes.</summary>
    public static bool IsValidLicenceId(string? licenceId) =>
        licenceId is { Length: 22 } && TryDecode(licenceId, out byte[] bytes) && bytes.Length == 16;

    /// <summary>
    /// True for a licensee: Times' project title rules (<c>ProjectList.IsValidTitle</c>) with at most 100 characters:
    /// not blank, no control character or line or paragraph separator, no invisible code point and no unpaired
    /// surrogate (U+FE0E and U+FE0F only right after a visible code point, so emoji such as <c>❤️</c> stay valid).
    /// </summary>
    public static bool IsValidName(string? name) =>
        name is not null && name.Length <= MaxNameLength && !string.IsNullOrWhiteSpace(name)
        && !HasControlCharacters(name) && !HasInvisibleOrBrokenCharacters(name);

    /// <summary>A <c>yyyy-MM-dd</c> date, exactly.</summary>
    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    // The strict payload parse: one object, each of the seven properties exactly once, nothing else.
    private static Licence? Parse(byte[] payload)
    {
        // Strings must be valid UTF-8 as a whole, not only where the reader looks.
        _ = StrictUtf8.GetString(payload);

        Utf8JsonReader reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 2 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        int? version = null;
        string? kid = null;
        string? lid = null;
        string? name = null;
        string? edition = null;
        string? until = null;
        string? issued = null;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string property = reader.GetString()!;
            if (!seen.Add(property) || !reader.Read())
            {
                return null;
            }

            if (property == "v")
            {
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int number))
                {
                    return null;
                }

                version = number;
                continue;
            }

            // Every other property is a string; anything else (a nested value included) fails at once.
            if (reader.TokenType != JsonTokenType.String)
            {
                return null;
            }

            string value = reader.GetString()!;
            switch (property)
            {
                case "kid":
                    kid = value;
                    break;
                case "lid":
                    lid = value;
                    break;
                case "name":
                    name = value;
                    break;
                case "ed":
                    edition = value;
                    break;
                case "until":
                    until = value;
                    break;
                case "issued":
                    issued = value;
                    break;
                default:
                    return null;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return null;
        }

        if (version != Version || !string.Equals(edition, Edition, StringComparison.Ordinal)
            || !IsValidKeyId(kid) || !IsValidLicenceId(lid) || !IsValidName(name)
            || !TryParseDate(until, out DateOnly untilDate) || !TryParseDate(issued, out DateOnly issuedDate))
        {
            return null;
        }

        return new Licence(kid!, lid!, name!, untilDate, issuedDate);
    }

    // Times' ProjectList.HasControlCharacters.
    private static bool HasControlCharacters(string text) =>
        text.Any(c => char.IsControl(c)
            || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    // Times' ProjectList.DefaultIgnorableRanges: the Unicode Default_Ignorable_Code_Point ranges (Unicode 16), inclusive.
    private static readonly (int First, int Last)[] DefaultIgnorableRanges =
    [
        (0x00AD, 0x00AD),
        (0x034F, 0x034F),
        (0x061C, 0x061C),
        (0x115F, 0x1160),
        (0x17B4, 0x17B5),
        (0x180B, 0x180F),
        (0x200B, 0x200F),
        (0x202A, 0x202E),
        (0x2060, 0x206F),
        (0x3164, 0x3164),
        (0xFE00, 0xFE0F),
        (0xFEFF, 0xFEFF),
        (0xFFA0, 0xFFA0),
        (0xFFF0, 0xFFF8),
        (0x1BCA0, 0x1BCA3),
        (0x1D173, 0x1D17A),
        (0xE0000, 0xE0FFF),
    ];

    private const int BrailleBlank = 0x2800;
    private const int TextPresentationSelector = 0xFE0E;
    private const int EmojiPresentationSelector = 0xFE0F;

    // Times' ProjectList.HasInvisibleOrBrokenCharacters: an unpaired surrogate, a Format (Cf) or default-ignorable
    // code point, or the braille blank; U+FE0E/U+FE0F only right after a visible code point.
    private static bool HasInvisibleOrBrokenCharacters(string text)
    {
        bool previousIsVisible = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int codePoint;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }

                codePoint = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return true;
            }
            else
            {
                codePoint = c;
            }

            if (codePoint is TextPresentationSelector or EmojiPresentationSelector)
            {
                if (!previousIsVisible)
                {
                    return true;
                }

                previousIsVisible = false;
                continue;
            }

            if (IsInvisible(codePoint))
            {
                return true;
            }

            previousIsVisible = true;
        }

        return false;
    }

    private static bool IsInvisible(int codePoint)
    {
        if (codePoint == BrailleBlank || CharUnicodeInfo.GetUnicodeCategory(codePoint) == UnicodeCategory.Format)
        {
            return true;
        }

        foreach ((int first, int last) in DefaultIgnorableRanges)
        {
            if (codePoint >= first && codePoint <= last)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBase64UrlChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    [GeneratedRegex(@"\A[a-z0-9-]{1,16}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyIdPattern();
}
