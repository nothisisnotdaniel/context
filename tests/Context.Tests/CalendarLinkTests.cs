namespace Context.Tests;

public sealed class CalendarLinkTests : IDisposable
{
    private readonly string parent;
    private readonly string root;

    public CalendarLinkTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-link-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    // AC-1
    [Theory]
    [InlineData("https://outlook.office365.com/owa/calendar/a/b/reachcalendar.ics")]
    [InlineData("HTTPS://Outlook.Example.com/x.ics?token=1")]
    [InlineData("  https://example.com/x.ics  ")]
    [InlineData("https://example.com:8443/x.ics")]
    public void Validate_AcceptsHttps(string value)
    {
        Uri? uri = CalendarLink.Validate(value);

        Assert.NotNull(uri);
        Assert.Equal("https", uri.Scheme);
    }

    // AC-1
    [Theory]
    [InlineData("webcal://outlook.example.com/owa/x.ics?t=1", "https://outlook.example.com/owa/x.ics?t=1")]
    [InlineData("WEBCAL://outlook.example.com/x.ics", "https://outlook.example.com/x.ics")]
    public void Validate_Webcal_BecomesHttps(string value, string expected)
    {
        Uri? uri = CalendarLink.Validate(value);

        Assert.NotNull(uri);
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    // AC-1
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/x.ics")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x.ics")]
    [InlineData("/owa/calendar/x.ics")]
    [InlineData("example.com/x.ics")]
    [InlineData("https:example.com/x.ics")]
    [InlineData("https:///x.ics")]
    [InlineData("https://user:pw@example.com/x.ics")]
    [InlineData("https://user@example.com/x.ics")]
    [InlineData("https://@example.com/x.ics")]
    [InlineData("webcal://@example.com/x.ics")]
    [InlineData("https://example.com/a b.ics")]
    [InlineData("https://example.com/a\u0007.ics")]
    [InlineData("javascript:alert(1)")]
    public void Validate_RejectsOthers(string value)
    {
        Assert.Null(CalendarLink.Validate(value));
    }

    // AC-1, AC-6
    [Fact]
    public void Mask_ShowsOnlyHost()
    {
        Uri uri = CalendarLink.Validate(FakeHttpHandler.Link)!;

        string masked = CalendarLink.Mask(uri);

        Assert.Equal($"https://{FakeHttpHandler.Host}/…", masked);
        Assert.DoesNotContain(FakeHttpHandler.PathToken, masked, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, masked, StringComparison.Ordinal);
    }

    // AC-1
    [Fact]
    public void SetGetClear_RoundTrip()
    {
        CalendarLink link = new CalendarLink(root);
        Assert.Null(link.Get());

        Uri saved = link.Set("webcal://" + FakeHttpHandler.Link["https://".Length..]);

        Assert.Equal(FakeHttpHandler.Link, saved.AbsoluteUri);
        Assert.Equal(saved, link.Get());
        byte[] bytes = File.ReadAllBytes(link.FilePath);
        Assert.Equal(FakeHttpHandler.Link + "\n", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal([CalendarLink.FileName], Directory.GetFiles(root).Select(Path.GetFileName));

        Assert.True(link.Clear());
        Assert.False(File.Exists(link.FilePath));
        Assert.Null(link.Get());
        Assert.False(link.Clear());
    }

    // AC-1
    [Fact]
    public void Set_Replaces_ExistingLink()
    {
        CalendarLink link = new CalendarLink(root);
        link.Set("https://a.example.com/1.ics");

        link.Set("https://b.example.com/2.ics");

        Assert.Equal("https://b.example.com/2.ics", link.Get()!.AbsoluteUri);
    }

    // AC-1, AC-6
    [Fact]
    public void Set_Invalid_Throws_WithoutTheValue_AndWritesNothing()
    {
        CalendarLink link = new CalendarLink(root);
        string bad = "http://example.com/" + FakeHttpHandler.PathToken;

        ArgumentException e = Assert.Throws<ArgumentException>(() => link.Set(bad));

        Assert.DoesNotContain(FakeHttpHandler.PathToken, e.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    // AC-1: the secret file is readable by its owner only.
    [Fact]
    public void Set_FileMode_Is0600_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        CalendarLink link = new CalendarLink(root);
        link.Set(FakeHttpHandler.Link);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(link.FilePath));

        // Also when an existing, more open file is replaced.
        File.SetUnixFileMode(link.FilePath, (UnixFileMode)0b110_100_100);
        link.Set(FakeHttpHandler.Link);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(link.FilePath));
    }

    // AC-6: a damaged file throws without its content.
    [Fact]
    public void Get_InvalidContent_Throws_WithoutTheContent()
    {
        Directory.CreateDirectory(root);
        CalendarLink link = new CalendarLink(root);
        File.WriteAllText(link.FilePath, "http://example.com/" + FakeHttpHandler.PathToken);

        CalendarLinkException e = Assert.Throws<CalendarLinkException>(() => link.Get());

        Assert.DoesNotContain(FakeHttpHandler.PathToken, e.ToString(), StringComparison.Ordinal);
    }

    // AC-4
    [Fact]
    public void Encrypted_SetWritesCiphertext_GetReadsIt()
    {
        Encryption encryption = SampleData.On(root);
        CalendarLink link = new CalendarLink(root, encryption);

        link.Set(FakeHttpHandler.Link);

        byte[] bytes = File.ReadAllBytes(link.FilePath);
        Assert.True(Vault.IsEncrypted(bytes));
        Assert.DoesNotContain(
            FakeHttpHandler.PathToken, System.Text.Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(FakeHttpHandler.Link, link.Get()!.AbsoluteUri);
    }

    // AC-7
    [Fact]
    public void Encrypted_TamperedFile_ThrowsCalendarLinkException_WithoutTheLink()
    {
        Encryption encryption = SampleData.On(root);
        CalendarLink link = new CalendarLink(root, encryption);
        link.Set(FakeHttpHandler.Link);
        SampleData.Tamper(link.FilePath);

        CalendarLinkException ex = Assert.Throws<CalendarLinkException>(() => link.Get());

        Assert.DoesNotContain(FakeHttpHandler.PathToken, ex.ToString(), StringComparison.Ordinal);
    }
}
