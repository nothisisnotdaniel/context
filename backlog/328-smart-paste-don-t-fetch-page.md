# 328 Smart paste: don't fetch page titles from local or private addresses

As a Context user who allows page-title fetching, I want the app to refuse to fetch titles from loopback, link-local and private network addresses, so that pasting a link can't make the app probe my local network.

## What

PageTitleFetcher (src/Context.Desktop, job 244) fetches any http(s) link's title when the setting allows network access. Add a SocketsHttpHandler ConnectCallback that resolves the host and refuses loopback, link-local, private (RFC 1918, ULA) and unspecified addresses, including after redirects; the link then stays as plain text, as on a timeout. Test with the FakeHttpHandler variant and an address-check unit test.

## Why

Security review of job 244: a pasted link to an internal address (router admin page, local service) is fetched silently, which leaks that the app is running and can reach internal services.

## Notes

Proposed by job 244-smart-paste (review-security 1).

Proposed by job 244-smart-paste (`factory/jobs/244-smart-paste/`).
