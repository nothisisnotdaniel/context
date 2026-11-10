using System.Net;
using System.Text;

namespace Context.Tests;

public sealed class CalendarFeedTests : IDisposable
{
    internal const string Ics =
        "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Standup\r\nDTSTART:20261029T080000Z\r\n"
        + "DTEND:20261029T081500Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private const string OldIcs =
        "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Old copy\r\nDTSTART:20261029T100000Z\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OldCacheTime = new(2026, 10, 28, 17, 45, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;

    public CalendarFeedTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-feed-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string CachePath => Path.Combine(root, CalendarFeed.CacheFileName);

    private CalendarFeed Feed(FakeHttpHandler handler, bool withLink = true)
    {
        CalendarLink link = new CalendarLink(root);
        if (withLink)
        {
            link.Set(FakeHttpHandler.Link);
        }

        return new CalendarFeed(root, link, handler, () => Now, TimeoutFor(handler));
    }

    /// <summary>
    /// The download timeout for a test feed: short only for a handler that hangs (the "timed out" case), long
    /// for every handler that answers, so a slow machine never turns another result into "timed out".
    /// </summary>
    internal static TimeSpan TimeoutFor(FakeHttpHandler handler) =>
        handler.IsHang ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(30);

    /// <summary>Longer than the 100 ms every test feed once had as its timeout, far below the 30 s of now.</summary>
    internal static readonly TimeSpan SlowAnswer = TimeSpan.FromSeconds(0.3);

    /// <summary>A 6 MB answer without a <c>Content-Length</c> that takes <see cref="SlowAnswer"/> to start.</summary>
    internal static FakeHttpHandler SlowOverCap() =>
        FakeHttpHandler.Large(6L * 1024 * 1024, withLength: false, SlowAnswer);

    private void WriteCache(string text, DateTime atUtc)
    {
        File.WriteAllText(CachePath, text);
        File.SetLastWriteTimeUtc(CachePath, atUtc);
    }

    /// <summary>Every handler that makes the download fail, by the reason it should give.</summary>
    public static TheoryData<string> Failures() =>
    [
        "timed out", "network error", "HTTP 404", "HTTP 500", "HTTP 302", "not HTTPS", "over 5 MB (length)",
        "over 5 MB (stream)", "not a calendar",
    ];

    internal static FakeHttpHandler FailingHandler(string failure) => failure switch
    {
        "timed out" => FakeHttpHandler.Hang(),
        "network error" => FakeHttpHandler.Throw(),
        "HTTP 404" => FakeHttpHandler.Status(HttpStatusCode.NotFound),
        "HTTP 500" => FakeHttpHandler.Status(HttpStatusCode.InternalServerError),
        "HTTP 302" => FakeHttpHandler.Status(HttpStatusCode.Found),
        "not HTTPS" => FakeHttpHandler.HttpRedirect(Ics),
        "over 5 MB (length)" => FakeHttpHandler.Large(6L * 1024 * 1024, withLength: true),
        "over 5 MB (stream)" => FakeHttpHandler.Large(6L * 1024 * 1024, withLength: false),
        "not a calendar" => FakeHttpHandler.Ok("<html>" + FakeHttpHandler.Link + "</html>"),
        _ => throw new ArgumentException(failure, nameof(failure)),
    };

    internal static string Reason(string failure) =>
        failure.StartsWith("over 5 MB", StringComparison.Ordinal) ? "over 5 MB" : failure;

    // Every file in the root except the link file itself must be free of the link.
    private void AssertNoSecretInFiles()
    {
        foreach (string path in Directory.GetFiles(parent, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) == CalendarLink.FileName)
            {
                continue;
            }

            string text = File.ReadAllText(path);
            Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeHttpHandler.QueryToken, text, StringComparison.Ordinal);
        }
    }

    private static void AssertNoSecret(CalendarFeedResult result)
    {
        string shown = result.ToString() + result.Reason;
        Assert.DoesNotContain(FakeHttpHandler.PathToken, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, shown, StringComparison.Ordinal);
    }

    // AC-2
    [Fact]
    public void Fetch_NoLink_NotConfigured_NoRequest()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        WriteCache(Ics, Now.AddMinutes(-1));

        CalendarFeedResult result = Feed(handler, withLink: false).Fetch();

        Assert.Equal(CalendarFeedStatus.NotConfigured, result.Status);
        Assert.Null(result.Text);
        Assert.Equal(0, handler.Requests);
    }

    // AC-2
    [Fact]
    public void Fetch_Success_DownloadsOnce_WritesCache_Fresh()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Fresh, result.Status);
        Assert.Equal(Ics, result.Text);
        Assert.Equal(Now, result.CachedAtUtc);
        Assert.Null(result.Reason);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(new Uri(FakeHttpHandler.Link), handler.Uris[0]);
        Assert.Equal(Encoding.UTF8.GetBytes(Ics), File.ReadAllBytes(CachePath));
        Assert.Equal(Now, File.GetLastWriteTimeUtc(CachePath));
        Assert.Equal(
            [CalendarFeed.CacheFileName, CalendarLink.FileName],
            Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        AssertNoSecret(result);
        AssertNoSecretInFiles();
    }

    // AC-2: the cache is written with the user's permissions only.
    [Fact]
    public void Fetch_Success_CacheMode0600_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Feed(FakeHttpHandler.Ok(Ics)).Fetch();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(CachePath));
    }

    // AC-2: a webcal link is fetched over https.
    [Fact]
    public void Fetch_WebcalLink_RequestsHttps()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        CalendarLink link = new CalendarLink(root);
        link.Set("webcal://example.com/a.ics");

        new CalendarFeed(root, link, handler, () => Now).Fetch();

        Assert.Equal(new Uri("https://example.com/a.ics"), handler.Uris[0]);
    }

    // AC-2
    [Fact]
    public void Fetch_YoungCache_NoRequest_ReturnsCache()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        DateTime cachedAt = Now.AddMinutes(-4).AddSeconds(-59);
        WriteCache(OldIcs, cachedAt);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Fresh, result.Status);
        Assert.Equal(OldIcs, result.Text);
        Assert.Equal(cachedAt, result.CachedAtUtc);
        Assert.Equal(0, handler.Requests);
    }

    // AC-2
    [Fact]
    public void Fetch_CacheFiveMinutesOld_Downloads()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        WriteCache(OldIcs, Now.AddMinutes(-5));

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(1, handler.Requests);
        Assert.Equal(CalendarFeedStatus.Fresh, result.Status);
        Assert.Equal(Ics, result.Text);
        Assert.Equal(Ics, File.ReadAllText(CachePath));
        Assert.Equal(Now, File.GetLastWriteTimeUtc(CachePath));
    }

    // AC-2: a second call within 5 minutes uses the cache the first one wrote.
    [Fact]
    public void Fetch_Twice_OneRequest()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        CalendarFeed feed = Feed(handler);

        feed.Fetch();
        CalendarFeedResult second = feed.Fetch();

        Assert.Equal(1, handler.Requests);
        Assert.Equal(CalendarFeedStatus.Fresh, second.Status);
        Assert.Equal(Ics, second.Text);
    }

    // AC-2: exactly the cap is fine.
    [Fact]
    public void Fetch_ExactlyFiveMb_Accepted()
    {
        FakeHttpHandler handler = FakeHttpHandler.Large(CalendarStore.MaxFileBytes, withLength: false);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Fresh, result.Status);
    }

    // AC-2: a UTF-8 BOM is dropped.
    [Fact]
    public void Fetch_Bom_Dropped()
    {
        CalendarFeedResult result = Feed(FakeHttpHandler.Ok("﻿" + Ics)).Fetch();

        Assert.Equal(Ics, result.Text);
    }

    // AC-3, AC-6
    [Theory]
    [MemberData(nameof(Failures))]
    public void Fetch_Failure_NoCache_Failed_WithReason(string failure)
    {
        FakeHttpHandler handler = FailingHandler(failure);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Failed, result.Status);
        Assert.Equal(Reason(failure), result.Reason);
        Assert.Null(result.Text);
        Assert.Null(result.CachedAtUtc);
        Assert.Equal(1, handler.Requests);
        Assert.False(File.Exists(CachePath));
        Assert.Single(Directory.GetFiles(root));
        AssertNoSecret(result);
        AssertNoSecretInFiles();
    }

    // AC-3, AC-6
    [Theory]
    [MemberData(nameof(Failures))]
    public void Fetch_Failure_KeepsOldCache_Stale(string failure)
    {
        FakeHttpHandler handler = FailingHandler(failure);
        WriteCache(OldIcs, OldCacheTime);
        byte[] before = File.ReadAllBytes(CachePath);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Stale, result.Status);
        Assert.Equal(OldIcs, result.Text);
        Assert.Equal(OldCacheTime, result.CachedAtUtc);
        Assert.Equal(Reason(failure), result.Reason);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(before, File.ReadAllBytes(CachePath));
        Assert.Equal(OldCacheTime, File.GetLastWriteTimeUtc(CachePath));
        Assert.Equal(2, Directory.GetFiles(root).Length);
        AssertNoSecret(result);
        AssertNoSecretInFiles();
    }

    // 130 AC-1: a slow answer over the cap is still "over 5 MB", never "timed out".
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fetch_SlowOverCap_OverFiveMb_NotTimedOut(bool withCache)
    {
        FakeHttpHandler handler = SlowOverCap();
        if (withCache)
        {
            WriteCache(OldIcs, OldCacheTime);
        }

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(withCache ? CalendarFeedStatus.Stale : CalendarFeedStatus.Failed, result.Status);
        Assert.Equal("over 5 MB", result.Reason);
        Assert.Equal(1, handler.Requests);
        AssertNoSecret(result);
        AssertNoSecretInFiles();
    }

    // 130 AC-2: a slow answer of exactly the cap is accepted.
    [Fact]
    public void Fetch_SlowExactlyFiveMb_Fresh()
    {
        FakeHttpHandler handler = FakeHttpHandler.Large(CalendarStore.MaxFileBytes, withLength: false, SlowAnswer);

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Fresh, result.Status);
        Assert.Equal(1, handler.Requests);
    }

    /// <summary><see cref="Failures"/> plus the handlers that answer well.</summary>
    public static TheoryData<string> Handlers()
    {
        TheoryData<string> data = ["ok", "large", "large (slow)"];
        foreach (string failure in Failures())
        {
            data.Add(failure);
        }

        return data;
    }

    // 130 AC-4: only the handler that hangs gets the short timeout.
    [Theory]
    [MemberData(nameof(Handlers))]
    public void TimeoutFor_ShortOnlyForHang(string kind)
    {
        FakeHttpHandler handler = kind switch
        {
            "ok" => FakeHttpHandler.Ok(Ics),
            "large" => FakeHttpHandler.Large(CalendarStore.MaxFileBytes, withLength: false),
            "large (slow)" => SlowOverCap(),
            _ => FailingHandler(kind),
        };

        TimeSpan timeout = TimeoutFor(handler);

        Assert.Equal(kind == "timed out", handler.IsHang);
        if (kind == "timed out")
        {
            Assert.InRange(timeout, TimeSpan.FromTicks(1), TimeSpan.FromSeconds(0.1));
        }
        else
        {
            Assert.True(timeout >= TimeSpan.FromSeconds(10), kind);
        }
    }

    // AC-3
    [Fact]
    public void Fetch_UnreadableLinkFile_NoRequest()
    {
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        File.WriteAllText(Path.Combine(root, CalendarLink.FileName), "not a link");

        CalendarFeedResult result = new CalendarFeed(root, new CalendarLink(root), handler, () => Now).Fetch();

        Assert.Equal(CalendarFeedStatus.Failed, result.Status);
        Assert.Equal(CalendarFeed.LinkUnreadable, result.Reason);
        Assert.Equal(0, handler.Requests);
    }

    // AC-2: no exception escapes, whatever the handler throws.
    [Fact]
    public void Fetch_HandlerThrowsAnything_NetworkError()
    {
        FakeHttpHandler handler = new((_, _) => throw new InvalidOperationException(FakeHttpHandler.Link));

        CalendarFeedResult result = Feed(handler).Fetch();

        Assert.Equal(CalendarFeedStatus.Failed, result.Status);
        Assert.Equal(CalendarFeed.NetworkError, result.Reason);
    }

    // AC-1: clearing the cache.
    [Fact]
    public void ClearCache_DeletesCache()
    {
        WriteCache(Ics, Now);
        CalendarFeed feed = Feed(FakeHttpHandler.Ok(Ics));
        Assert.Equal(Now, feed.CachedAtUtc());

        feed.ClearCache();

        Assert.False(File.Exists(CachePath));
        Assert.Null(feed.CachedAtUtc());
    }

    // AC-4
    [Fact]
    public void Encrypted_CacheIsWrittenEncrypted_AndReadBack()
    {
        Encryption encryption = SampleData.On(root);
        CalendarLink link = new CalendarLink(root, encryption);
        link.Set(FakeHttpHandler.Link);
        FakeHttpHandler handler = FakeHttpHandler.Ok(Ics);
        CalendarFeed feed = new CalendarFeed(root, link, handler, () => Now, TimeSpan.FromSeconds(5), encryption);

        CalendarFeedResult first = feed.Fetch();
        CalendarFeedResult second = feed.Fetch();

        Assert.Equal(CalendarFeedStatus.Fresh, first.Status);
        Assert.Equal(Ics, second.Text);
        Assert.Equal(1, handler.Requests);
        byte[] cache = File.ReadAllBytes(CachePath);
        Assert.True(Vault.IsEncrypted(cache));
        Assert.DoesNotContain("Standup", Encoding.Latin1.GetString(cache), StringComparison.Ordinal);
    }

    // AC-7
    [Fact]
    public void Encrypted_TamperedCache_IsNotUsed_AndDownloadReplacesIt()
    {
        Encryption encryption = SampleData.On(root);
        CalendarLink link = new CalendarLink(root, encryption);
        link.Set(FakeHttpHandler.Link);
        File.WriteAllBytes(CachePath, encryption.Vault!.Encrypt(Vault.KindCalendarCache, Encoding.UTF8.GetBytes(OldIcs)));
        File.SetLastWriteTimeUtc(CachePath, Now.AddMinutes(-1));
        SampleData.Tamper(CachePath);
        File.SetLastWriteTimeUtc(CachePath, Now.AddMinutes(-1));

        CalendarFeedResult failed = new CalendarFeed(
            root, link, FakeHttpHandler.Status(System.Net.HttpStatusCode.NotFound), () => Now, null, encryption).Fetch();

        Assert.Equal(CalendarFeedStatus.Failed, failed.Status);
    }
}
