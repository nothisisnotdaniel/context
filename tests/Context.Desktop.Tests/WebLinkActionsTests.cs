namespace Context.Tests;

// 354 AC-8: DesktopActions.OpenWebLink and WebLink.TryParse, with FakeUrlLauncher; the note launcher is never used.
public sealed class WebLinkActionsTests : IDisposable
{
    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    public static TheoryData<string> Refused() => new(
        string.Empty,
        "https://example.com/" + new string('a', WebLink.MaxLength),
        "https://example.com/a b",
        "https://example.com/a\tb",
        "https://example.com/a\u0007b",
        "https://example.com/a\u0085b",
        "example.com/page",
        "/etc/passwd",
        "notes/a.md",
        "other.md",
        "javascript:alert(1)",
        "JavaScript:alert(1)",
        "file:///etc/passwd",
        "data:text/html,x",
        "ftp://example.com/a",
        "vbscript:msgbox(1)",
        "https://",
        "https:///path",
        "http:example.com",
        "https://a@b.com",
        "https://user:pass@example.com/",
        ".attachments/x.pdf");

    // AC-8: an http, https or mailto URL is opened once with the parsed Uri, and nothing else happens.
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("HTTP://Example.com", "http://example.com/")]
    [InlineData("mailto:a@b.c", "mailto:a@b.c")]
    public async Task OpenWebLink_WebUrl_OpensOnce(string href, string absolute)
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.OpenWebLink(href);

        Assert.Equal(ActionKind.Done, result.Kind);
        Uri opened = Assert.Single(data.UrlLauncher.Opened);
        Assert.Equal(new Uri(href), opened);
        Assert.Equal(absolute, opened.AbsoluteUri);
        Assert.Empty(data.Launcher.Launched);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-8: anything else is refused with the fixed text and never launched.
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task OpenWebLink_NotAWebUrl_RefusedNeverLaunched(string href)
    {
        await data.RefreshAsync();
        SortedDictionary<string, string> before = Files();
        int builds = data.State!.Builds;

        ActionResult result = await data.Actions!.OpenWebLink(href);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal("Can't open this link.", result.Text);
        Assert.Empty(data.UrlLauncher.Opened);
        Assert.Empty(data.Launcher.Launched);
        Assert.Equal(before, Files());
        Assert.Equal(builds, data.State.Builds);
    }

    // AC-8: null is refused too.
    [Fact]
    public async Task OpenWebLink_Null_Refused()
    {
        await data.RefreshAsync();

        ActionResult result = await data.Actions!.OpenWebLink(null);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(DesktopActions.OpenWebLinkText, result.Text);
        Assert.Empty(data.UrlLauncher.Opened);
    }

    // AC-8: a launcher that throws gives Failed with the same fixed text, not the exception's.
    [Fact]
    public async Task OpenWebLink_LauncherThrows_FailedFixedText()
    {
        await data.RefreshAsync();
        data.UrlLauncher.Throws = new InvalidOperationException("secret detail");

        ActionResult result = await data.Actions!.OpenWebLink("https://example.com/");

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't open this link.", result.Text);
        Assert.Single(data.UrlLauncher.Opened);
        Assert.Empty(data.Launcher.Launched);
    }

    // AC-8: the rule itself, at the length limit.
    [Fact]
    public void TryParse_LengthLimit()
    {
        string prefix = "https://example.com/";
        Assert.True(WebLink.TryParse(prefix + new string('a', WebLink.MaxLength - prefix.Length), out _));
        Assert.False(WebLink.TryParse(prefix + new string('a', WebLink.MaxLength - prefix.Length + 1), out _));
        Assert.False(WebLink.TryParse(null, out _));
    }

    private SortedDictionary<string, string> Files() => FileSnapshot.Take(data.Root, TagIndex.FileName, RecentNotesStore.FileName);
}
