using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Context;

/// <summary>Where a pasted link's page title comes from (job 244); <see cref="PageTitleFetcher"/> in the app.</summary>
internal interface IPageTitleSource
{
    /// <summary>The page title of <paramref name="url"/>, or <c>null</c> when there is none. Never throws.</summary>
    public Task<string?> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the <c>&lt;title&gt;</c> of a web page: one GET without cookies or credentials (the handler is the caller's,
/// built without them), at most <see cref="MaxBodyBytes"/> of the body read, all within the timeout. The title is plain
/// text: HTML-decoded, control characters and whitespace runs made one space, trimmed, cut to
/// <see cref="MaxTitleLength"/>. Every failure gives <c>null</c>; the URL is never logged.
/// </summary>
internal sealed class PageTitleFetcher : IPageTitleSource
{
    /// <summary>The most bytes of a page that are read when looking for its title (256 KiB).</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>The most characters of a title that are kept.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The longest URL that is fetched.</summary>
    public const int MaxUrlLength = 2048;

    private static readonly Regex TitleTag = new(
        @"<title\b[^>]*>(.*?)</title\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Blank = new(
        @"[\s\p{Cc}]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Bidi marks, embeddings, overrides and isolates: removed, so a title can't make its text read differently.
    private static readonly Regex Bidi = new(
        @"[\u200E\u200F\u202A-\u202E\u2066-\u2069]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly HttpClient client;
    private readonly TimeSpan timeout;

    public PageTitleFetcher(HttpMessageHandler handler, TimeSpan timeout)
    {
        client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        this.timeout = timeout;
    }

    /// <summary>
    /// The first <c>&lt;title&gt;</c> of the page at <paramref name="url"/>, or <c>null</c>: for a URL that is not an
    /// absolute http(s) address with a host and no user info, a timeout, cancellation, a network error, a non-2xx status,
    /// a final address that is not http(s), a Content-Type other than HTML, or no non-empty title in the first
    /// <see cref="MaxBodyBytes"/>. Never throws.
    /// </summary>
    public async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        Uri? uri = Address(url);
        if (uri is null)
        {
            return null;
        }

        try
        {
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html");
            request.Headers.Accept.ParseAdd("application/xhtml+xml");
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || !IsWeb(response.RequestMessage?.RequestUri))
            {
                return null;
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            byte[] body = await ReadHead(response.Content, limit.Token).ConfigureAwait(false);
            return TitleOf(Decode(body, response.Content.Headers.ContentType?.CharSet));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The title in <paramref name="html"/> as it is inserted: the first <c>&lt;title&gt;</c>'s text, HTML-decoded,
    /// bidi controls removed, control characters and whitespace runs made one space, trimmed, cut to
    /// <see cref="MaxTitleLength"/> (never inside a surrogate pair); <c>null</c> for none or an empty one.
    /// </summary>
    public static string? TitleOf(string html)
    {
        Match match = TitleTag.Match(html);
        if (!match.Success)
        {
            return null;
        }

        string title = Blank.Replace(Bidi.Replace(WebUtility.HtmlDecode(match.Groups[1].Value), ""), " ").Trim();
        if (title.Length > MaxTitleLength)
        {
            int cut = char.IsHighSurrogate(title[MaxTitleLength - 1]) ? MaxTitleLength - 1 : MaxTitleLength;
            title = title[..cut].TrimEnd();
        }

        return title.Length == 0 ? null : title;
    }

    // An absolute http(s) URI with a host and no user info, at most MaxUrlLength characters; null otherwise.
    private static Uri? Address(string? url)
    {
        if (url is null || url.Length > MaxUrlLength || url.Trim().Length != url.Length)
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !IsWeb(uri)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0)
        {
            return null;
        }

        return uri;
    }

    private static bool IsWeb(Uri? uri) =>
        uri is not null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // At most MaxBodyBytes of the body; the rest is never read.
    private static async Task<byte[]> ReadHead(HttpContent content, CancellationToken token)
    {
        await using Stream stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[MaxBodyBytes];
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return buffer[..filled];
    }

    // The body in its declared charset when .NET knows it, else UTF-8 (bad bytes become U+FFFD).
    private static string Decode(byte[] body, string? charset)
    {
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"', ' '));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return encoding.GetString(body);
    }
}
