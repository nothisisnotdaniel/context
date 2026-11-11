using System.Text;

namespace Context;

/// <summary>Where the text of a <see cref="CalendarFeedResult"/> came from.</summary>
internal enum CalendarFeedStatus
{
    /// <summary>No calendar link is set. Nothing was downloaded.</summary>
    NotConfigured,

    /// <summary>Just downloaded, or a cached copy younger than <see cref="CalendarFeed.CacheLifetime"/>.</summary>
    Fresh,

    /// <summary>The download failed; the text is the older cached copy.</summary>
    Stale,

    /// <summary>The download failed and there is no cached copy.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="CalendarFeed.Fetch"/>.</summary>
internal sealed record CalendarFeedResult
{
    public CalendarFeedResult(
        CalendarFeedStatus status, string? text = null, DateTime? cachedAtUtc = null, string? reason = null)
    {
        Status = status;
        Text = text;
        CachedAtUtc = cachedAtUtc;
        Reason = reason;
    }

    /// <summary>
    /// Where the text came from: <c>NotConfigured</c> (no text, no time, no reason), <c>Fresh</c> (text and time),
    /// <c>Stale</c> (the older cached text and its time, plus a <see cref="Reason"/>) or <c>Failed</c> (only a
    /// <see cref="Reason"/>).
    /// </summary>
    public CalendarFeedStatus Status { get; }

    /// <summary>The iCalendar text (fresh or cached); <c>null</c> for <c>NotConfigured</c> and <c>Failed</c>.</summary>
    public string? Text { get; }

    /// <summary>When the text was downloaded (UTC), or <c>null</c> when there is no text.</summary>
    public DateTime? CachedAtUtc { get; }

    /// <summary>
    /// Why the download failed (<c>Stale</c> and <c>Failed</c>), always one of the fixed texts in
    /// <see cref="CalendarFeed"/>, never an exception message; otherwise <c>null</c>.
    /// </summary>
    public string? Reason { get; }
}

/// <summary>
/// Downloads the published calendar of a <see cref="CalendarLink"/> and keeps the last good copy in
/// <c>&lt;root&gt;/calendar-link.ics</c> (outside <c>calendar/</c>, so <see cref="CalendarStore"/> doesn't read
/// it too). HTTPS only, one GET with a timeout, at most <see cref="CalendarStore.MaxFileBytes"/>. A failed or bad
/// download never replaces the cache. The link is never put into a result, a file or an exception.
/// </summary>
internal sealed class CalendarFeed
{
    /// <summary>The file name of the cached copy inside the root.</summary>
    public const string CacheFileName = "calendar-link.ics";

    public const string TimedOut = "timed out";
    public const string NetworkError = "network error";
    public const string NotHttps = "not HTTPS";
    public const string OverCap = "over 5 MB";
    public const string NotCalendar = "not a calendar";
    public const string LinkUnreadable = "can't read calendar-link.txt";

    /// <summary>A cached copy younger than this is used without downloading.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long one download may take, headers and body together.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string root;
    private readonly CalendarLink link;
    private readonly HttpClient client;
    private readonly Func<DateTime> utcNow;
    private readonly TimeSpan timeout;

    /// <param name="root">The folder the cache lives in.</param>
    /// <param name="link">Where the link comes from.</param>
    /// <param name="handler">Sends the request; the caller owns it (it isn't disposed here).</param>
    /// <param name="utcNow">The clock for the cache age and time stamp.</param>
    /// <param name="timeout">How long one download may take; <see cref="DefaultTimeout"/> if null.</param>
    public CalendarFeed(
        string root,
        CalendarLink link,
        HttpMessageHandler handler,
        Func<DateTime> utcNow,
        TimeSpan? timeout = null)
    {
        this.root = Path.GetFullPath(root);
        this.link = link;
        this.utcNow = utcNow;
        this.timeout = timeout ?? DefaultTimeout;
        client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>The full path of the cached copy.</summary>
    public string CachePath => Path.Combine(root, CacheFileName);

    /// <summary>When the cached copy was downloaded (UTC), or <c>null</c> if there is none.</summary>
    public DateTime? CachedAtUtc()
    {
        try
        {
            return File.Exists(CachePath) ? File.GetLastWriteTimeUtc(CachePath) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes the cached copy, if there is one.</summary>
    public void ClearCache()
    {
        if (File.Exists(CachePath))
        {
            File.Delete(CachePath);
        }
    }

    /// <summary>
    /// Returns the calendar text: <c>NotConfigured</c> without a link (no request), the cache when it is younger
    /// than <see cref="CacheLifetime"/> (no request), else one download (<c>Fresh</c>, and the cache is replaced).
    /// When the download fails the old cache is returned untouched (<c>Stale</c>), or <c>Failed</c> without one.
    /// Never throws.
    /// </summary>
    public CalendarFeedResult Fetch()
    {
        Uri? uri;
        try
        {
            uri = link.Get();
        }
        catch (CalendarLinkException)
        {
            return Fallback(LinkUnreadable);
        }

        if (uri is null)
        {
            return new CalendarFeedResult(CalendarFeedStatus.NotConfigured);
        }

        DateTime now = utcNow();
        DateTime? cachedAt = CachedAtUtc();
        if (cachedAt is not null)
        {
            TimeSpan age = now - cachedAt.Value;
            if (age >= TimeSpan.Zero && age < CacheLifetime)
            {
                string? cached = CalendarStore.ReadText(CachePath);
                if (cached is not null)
                {
                    return new CalendarFeedResult(CalendarFeedStatus.Fresh, cached, cachedAt);
                }
            }
        }

        (string? text, string? reason) = Download(uri);
        if (text is null)
        {
            return Fallback(reason ?? NetworkError);
        }

        try
        {
            IcsParser.Parse(text, TimeZoneInfo.Utc);
        }
        catch (FormatException)
        {
            return Fallback(NotCalendar);
        }

        WriteCache(text, now);
        return new CalendarFeedResult(CalendarFeedStatus.Fresh, text, now);
    }

    // The old cache as Stale, or Failed when there is none (or it can't be read).
    private CalendarFeedResult Fallback(string reason)
    {
        DateTime? cachedAt = CachedAtUtc();
        string? cached = cachedAt is null
            ? null
            : CalendarStore.ReadText(CachePath);
        if (cached is null)
        {
            return new CalendarFeedResult(CalendarFeedStatus.Failed, reason: reason);
        }

        return new CalendarFeedResult(CalendarFeedStatus.Stale, cached, cachedAt, reason);
    }

    // One GET. Returns the body, or null and one of the fixed reasons. Exception messages (which can hold the
    // link) are dropped here.
    private (string? Text, string? Reason) Download(Uri uri)
    {
        using CancellationTokenSource cancel = new CancellationTokenSource(timeout);
        try
        {
            return DownloadAsync(uri, cancel.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return (null, cancel.IsCancellationRequested ? TimedOut : NetworkError);
        }
        catch (Exception)
        {
            return (null, NetworkError);
        }
    }

    private async Task<(string? Text, string? Reason)> DownloadAsync(Uri uri, CancellationToken token)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        Uri? final = response.RequestMessage?.RequestUri;
        if (final is null || !final.IsAbsoluteUri || final.Scheme != Uri.UriSchemeHttps)
        {
            return (null, NotHttps);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, $"HTTP {(int)response.StatusCode}");
        }

        long? length = response.Content.Headers.ContentLength;
        if (length > CalendarStore.MaxFileBytes)
        {
            return (null, OverCap);
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream body = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (body.Length + read > CalendarStore.MaxFileBytes)
            {
                return (null, OverCap);
            }

            body.Write(buffer, 0, read);
        }

        string text = Utf8.GetString(body.GetBuffer(), 0, (int)body.Length);
        return (text.Length > 0 && text[0] == '﻿' ? text[1..] : text, null);
    }

    // Replaces the cache atomically and stamps it with the download time. A cache that can't be written is
    // skipped: the fresh text is still shown.
    private void WriteCache(string text, DateTime now)
    {
        string temp = Path.Combine(root, $".{CacheFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(root);
            CalendarLink.WritePrivate(temp, Utf8.GetBytes(text));
            File.SetLastWriteTimeUtc(temp, DateTime.SpecifyKind(now, DateTimeKind.Utc));
            File.Move(temp, CachePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keep the old cache.
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do.
            }
        }
    }
}
