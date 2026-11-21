namespace Context;

/// <summary>
/// Job 354: which link hrefs a Cmd/Ctrl+click in the editor may open in the default browser or mail app. The editor
/// checks the same rule first (<c>webHref</c> in <c>editor/src/markdown.js</c>); this is the check that counts.
/// </summary>
internal static class WebLink
{
    /// <summary>The longest href that is opened.</summary>
    public const int MaxLength = 2048;

    /// <summary>
    /// True, with the parsed <paramref name="uri"/>, for an absolute <c>http:</c> or <c>https:</c> URL with a host and
    /// no user info, or a <c>mailto:</c> URL; at most <see cref="MaxLength"/> chars, with no whitespace or control
    /// character. False for anything else: a relative URL (an attachment link), <c>javascript:</c>, <c>file:</c>,
    /// <c>data:</c>, <c>ftp:</c> and every other scheme.
    /// </summary>
    public static bool TryParse(string? href, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(href) || href.Length > MaxLength)
        {
            return false;
        }

        foreach (char c in href)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        int colon = href.IndexOf(':', StringComparison.Ordinal);
        string scheme = colon > 0 ? href[..colon].ToLowerInvariant() : string.Empty;
        if (scheme is not ("http" or "https" or "mailto"))
        {
            return false;
        }

        // An http(s) URL must name its host after "//": "http:example.com" is refused, not guessed.
        if (scheme != "mailto" && !href.AsSpan(colon + 1).StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != scheme)
        {
            return false;
        }

        if (scheme != "mailto" && (string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
