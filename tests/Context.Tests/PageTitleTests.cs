using System.Net;
using System.Text;

namespace Context.Tests;

// 244 AC-7: PageTitleFetcher reads a pasted link's page title with one GET and never throws; FakeHttpHandler only, no
// network.
public sealed class PageTitleTests
{
    private const string Url = "https://e.x/page";

    // One GET of the URL, no Cookie or Authorization header, and the first title, cleaned.
    [Fact]
    public async Task Ok_OneGetWithoutCookies_FirstTitle()
    {
        List<HttpRequestMessage> seen = [];
        FakeHttpHandler handler = new FakeHttpHandler((request, _) =>
        {
            seen.Add(request);
            return Task.FromResult(Answer(request, Html("<html><head><TITLE lang=en>\n A &amp; B\t &lt;c&gt;  </TITLE><title>Second</title>")));
        });

        string? title = await Fetch(handler, Url);

        Assert.Equal("A & B <c>", title);
        HttpRequestMessage request = Assert.Single(seen);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri(Url), request.RequestUri);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(1, handler.Requests);
    }

    // application/xhtml+xml, a charset and no Content-Type at all are read too.
    [Theory]
    [InlineData("application/xhtml+xml")]
    [InlineData("TEXT/HTML")]
    [InlineData(null)]
    public async Task HtmlContentTypes_AreRead(string? mediaType)
    {
        FakeHttpHandler handler = Ok(Html("<title>Page</title>", mediaType));

        Assert.Equal("Page", await Fetch(handler, Url));
    }

    // The declared charset decodes the bytes.
    [Fact]
    public async Task Charset_DecodesTheBody()
    {
        ByteArrayContent content = new ByteArrayContent(Encoding.Latin1.GetBytes("<title>Café</title>"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "iso-8859-1" };

        Assert.Equal("Café", await Fetch(Ok(content), Url));
    }

    // Control characters become spaces, bidi controls are removed; a long title is cut to 200 characters, never inside a surrogate pair.
    [Fact]
    public async Task LongTitle_CutTo200()
    {
        Assert.Equal(new string('a', 200), await Fetch(Ok(Html("<title>" + new string('a', 300) + "</title>")), Url));
        string emoji = new string('b', 199) + "\U0001F600" + "c";
        Assert.Equal(new string('b', 199), await Fetch(Ok(Html("<title>" + emoji + "</title>")), Url));
        Assert.Equal("a b", await Fetch(Ok(Html("<title>a&#1;\u0007b</title>")), Url));
        Assert.Equal("ab c", await Fetch(Ok(Html("<title>a\u202Eb&#x2066; \u200Fc</title>")), Url));
    }

    // A title within the first 256 KiB of a bigger body is found; one after it is not read.
    [Fact]
    public async Task Body_OnlyTheFirst256KiB()
    {
        string padding = new string(' ', PageTitleFetcher.MaxBodyBytes);
        Assert.Equal("Early", await Fetch(Ok(Html("<title>Early</title>" + padding)), Url));
        Assert.Null(await Fetch(Ok(Html(padding + "<title>Late</title>")), Url));
        Assert.Null(await Fetch(Ok(Html(new string(' ', PageTitleFetcher.MaxBodyBytes - 10) + "<title>Cut off</title>")), Url));
    }

    // No title, an empty one or one that is only whitespace gives null.
    [Theory]
    [InlineData("<html><body>No title</body></html>")]
    [InlineData("<title></title>")]
    [InlineData("<title> \n &nbsp; </title>")]
    [InlineData("<title>Never closed")]
    [InlineData("")]
    public async Task NoTitle_Null(string body)
    {
        Assert.Null(await Fetch(Ok(Html(body)), Url));
    }

    // A non-2xx status gives null.
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task NotSuccess_Null(HttpStatusCode status)
    {
        FakeHttpHandler handler = new FakeHttpHandler(
            (request, _) => Task.FromResult(Answer(request, Html("<title>Error</title>"), status)));

        Assert.Null(await Fetch(handler, Url));
    }

    // A Content-Type that is present and not HTML gives null.
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("image/png")]
    public async Task OtherContentType_Null(string mediaType)
    {
        Assert.Null(await Fetch(Ok(Html("<title>Page</title>", mediaType)), Url));
    }

    // A final address that is not http(s) gives null; one that is still gives the title.
    [Theory]
    [InlineData("ftp://e.x/file", null)]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("http://e.x/moved", "Page")]
    public async Task FinalUri_MustBeWeb(string final, string? expected)
    {
        FakeHttpHandler handler = FakeHttpHandler.RedirectTo(final, Html("<title>Page</title>"));

        Assert.Equal(expected, await Fetch(handler, Url));
    }

    // A timeout, a network error or a cancelled token gives null.
    [Fact]
    public async Task TimeoutErrorOrCancel_Null()
    {
        Assert.Null(await Fetch(FakeHttpHandler.Hang(), Url));
        Assert.Null(await Fetch(FakeHttpHandler.Throw(), Url));

        FakeHttpHandler handler = Ok(Html("<title>Page</title>"));
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.Null(await new PageTitleFetcher(handler, TimeSpan.FromSeconds(30)).FetchAsync(Url, cancelled.Token));
    }

    // A bad URL gives null and sends nothing.
    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://e.x/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("https://user:secret@e.x/a")]
    [InlineData(" https://e.x/a")]
    public async Task BadUrl_NullAndNoRequest(string url)
    {
        FakeHttpHandler handler = Ok(Html("<title>Page</title>"));

        Assert.Null(await Fetch(handler, url));
        Assert.Equal(0, handler.Requests);
    }

    // At most 2048 characters.
    [Fact]
    public async Task LongUrl_Limit()
    {
        string longest = "https://e.x/" + new string('a', PageTitleFetcher.MaxUrlLength - 12);
        FakeHttpHandler handler = Ok(Html("<title>Page</title>"));

        Assert.Equal("Page", await Fetch(handler, longest));
        Assert.Null(await Fetch(handler, longest + "a"));
        Assert.Equal(1, handler.Requests);
    }

    private static Task<string?> Fetch(FakeHttpHandler handler, string url) =>
        new PageTitleFetcher(handler, CalendarFeedTests.TimeoutFor(handler)).FetchAsync(url, CancellationToken.None);

    private static FakeHttpHandler Ok(HttpContent content) =>
        new((request, _) => Task.FromResult(Answer(request, content)));

    private static HttpResponseMessage Answer(
        HttpRequestMessage request, HttpContent content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = content, RequestMessage = request };

    private static ByteArrayContent Html(string body, string? mediaType = "text/html")
    {
        ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (mediaType is not null)
        {
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        return content;
    }
}
