using System.Net;
using System.Text;

namespace Context.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that never touches the network: it answers every request with
/// <see cref="Respond"/> and counts the requests.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    /// <summary>A unique token in the link's path. It must never show up anywhere but calendar-link.txt.</summary>
    public const string PathToken = "pathTok4f9e2b";

    /// <summary>A unique token in the link's query.</summary>
    public const string QueryToken = "queryTok7c1a8d";

    public const string Host = "outlook.example.com";

    /// <summary>A link like the ones Outlook publishes, with secrets in its path and query.</summary>
    public const string Link =
        "https://" + Host + "/owa/calendar/" + PathToken + "/reachcalendar.ics?token=" + QueryToken;

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        this.respond = respond;
    }

    private FakeHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, bool isHang)
    {
        this.respond = respond;
        IsHang = isHang;
    }

    /// <summary>True only for <see cref="Hang"/>: the handler never answers, so a test wants a short timeout.</summary>
    public bool IsHang { get; }

    public int Requests { get; private set; }

    public List<Uri?> Uris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        Uris.Add(request.RequestUri);
        return respond(request, token);
    }

    /// <summary>A 200 with <paramref name="body"/> for the request itself.</summary>
    public static FakeHttpHandler Ok(string body) =>
        new((request, _) => Task.FromResult(Response(request, HttpStatusCode.OK, new StringContent(body))));

    public static FakeHttpHandler Status(HttpStatusCode status) =>
        new((request, _) => Task.FromResult(Response(request, status, new StringContent("error page " + Link))));

    /// <summary>Waits until the request is cancelled (the feed's timeout).</summary>
    public static FakeHttpHandler Hang() =>
        new(
            async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Response(request, HttpStatusCode.OK, new StringContent(string.Empty));
            },
            isHang: true);

    /// <summary>Throws the way <c>SocketsHttpHandler</c> does, with the link in the message.</summary>
    public static FakeHttpHandler Throw() =>
        new((request, _) => throw new HttpRequestException(
            $"No such host is known ({request.RequestUri}) " + Link, new IOException(Link)));

    /// <summary>
    /// A 200 of <paramref name="bytes"/> bytes, with or without a <c>Content-Length</c>. With a
    /// <paramref name="delay"/> the handler first waits that long (or until the request is cancelled), like a
    /// slow machine, before it builds the body.
    /// </summary>
    public static FakeHttpHandler Large(long bytes, bool withLength, TimeSpan? delay = null) =>
        new(async (request, token) =>
        {
            if (delay is TimeSpan wait)
            {
                await Task.Delay(wait, token);
            }

            byte[] body = new byte[bytes];
            Encoding.ASCII.GetBytes("BEGIN:VCALENDAR\r\n").CopyTo(body, 0);
            HttpContent content = withLength
                ? new ByteArrayContent(body)
                : new StreamContent(new UnseekableStream(new MemoryStream(body)));
            if (withLength)
            {
                Assert.Equal(bytes, content.Headers.ContentLength);
            }
            else
            {
                Assert.Null(content.Headers.ContentLength);
            }

            return Response(request, HttpStatusCode.OK, content);
        });

    /// <summary>A 200 with a calendar, but the final request (after a redirect) went to an http address.</summary>
    public static FakeHttpHandler HttpRedirect(string body) =>
        new((request, _) =>
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://" + Host + "/" + PathToken),
            };
            return Task.FromResult(response);
        });

    private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status, HttpContent content) =>
        new(status) { Content = content, RequestMessage = request };

    /// <summary>A read-only stream that can't seek, so <c>StreamContent</c> has no length.</summary>
    private sealed class UnseekableStream : Stream
    {
        private readonly Stream inner;

        public UnseekableStream(Stream inner)
        {
            this.inner = inner;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
