using System.Text;

namespace Context;

/// <summary>Thrown when <c>calendar-link.txt</c> exists but can't be read or holds no valid link.</summary>
internal sealed class CalendarLinkException : IOException
{
    public CalendarLinkException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The published calendar link (an Outlook/Exchange ICS address), kept as one line in
/// <c>&lt;root&gt;/calendar-link.txt</c> (UTF-8, no BOM). Anyone who has the link can read the calendar,
/// so it is a secret: it is only ever written to that file (via a temp file that is then moved, Unix mode
/// <c>0600</c> when not on Windows), and only ever shown through <see cref="Mask"/>. No exception from this
/// class carries the link.
/// </summary>
internal sealed class CalendarLink
{
    /// <summary>The file name of the link inside the root.</summary>
    public const string FileName = "calendar-link.txt";

    private const string Https = "https";
    private const string Webcal = "webcal";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string root;

    /// <param name="root">The folder <c>calendar-link.txt</c> lives in.</param>
    public CalendarLink(string root)
    {
        this.root = Path.GetFullPath(root);
    }

    /// <summary>The full path of <c>calendar-link.txt</c>.</summary>
    public string FilePath => Path.Combine(root, FileName);

    /// <summary>
    /// The saved link, or <c>null</c> when there is none. Throws <see cref="CalendarLinkException"/> if the
    /// file can't be read or doesn't hold a valid link.
    /// </summary>
    public Uri? Get()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path, Utf8NoBom);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new CalendarLinkException($"Can't read {FileName}.", e);
        }

        Uri? uri = Validate(text.Trim());
        if (uri is null)
        {
            throw new CalendarLinkException($"Can't read {FileName}.");
        }

        return uri;
    }

    /// <summary>
    /// Validates <paramref name="value"/> with <see cref="Validate"/> and saves it, creating the root folder
    /// if needed. Returns the saved link. Throws <see cref="ArgumentException"/> (without the value) if it isn't
    /// valid, and <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> if it can't be written.
    /// </summary>
    public Uri Set(string value)
    {
        Uri uri = Validate(value) ?? throw new ArgumentException("Not a valid calendar link.", nameof(value));
        Directory.CreateDirectory(root);
        string temp = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            WritePrivate(temp, Utf8NoBom.GetBytes(uri.AbsoluteUri + "\n"));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return uri;
    }

    /// <summary>Deletes the link file. Returns false if there was none.</summary>
    public bool Clear()
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Returns the link as an absolute <c>https</c> URI, or <c>null</c> if <paramref name="value"/> isn't one.
    /// Accepts an absolute <c>https://</c> or <c>webcal://</c> address (any case; <c>webcal</c> becomes
    /// <c>https</c>) with a host, no user-info (not even an empty one, <c>https://@host</c>) and no whitespace
    /// or control characters. Leading and trailing whitespace is ignored.
    /// </summary>
    public static Uri? Validate(string value)
    {
        string text = value.Trim();
        if (text.Length == 0 || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }

        int colon = text.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        string scheme = text[..colon];
        if (scheme.Equals(Webcal, StringComparison.OrdinalIgnoreCase))
        {
            text = Https + text[colon..];
        }
        else if (!scheme.Equals(Https, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The authority is everything between "//" and the first "/", "?" or "#"; user-info would be before an "@".
        string rest = text[(Https.Length + 1)..];
        if (!rest.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        string authority = rest[2..];
        int authorityEnd = authority.IndexOfAny(['/', '?', '#', '\\']);
        if (authorityEnd >= 0)
        {
            authority = authority[..authorityEnd];
        }

        if (authority.Contains('@'))
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return uri;
    }

    /// <summary>The link without its path, query or port: <c>https://&lt;host&gt;/…</c>.</summary>
    public static string Mask(Uri uri) => $"https://{uri.Host}/…";

    /// <summary>Creates <paramref name="path"/> (it must not exist) readable and writable only by the user.</summary>
    internal static void WritePrivate(string path, byte[] bytes)
    {
        FileStreamOptions options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using FileStream stream = new FileStream(path, options);
        stream.Write(bytes);
    }
}
