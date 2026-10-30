namespace Context.Tests;

public sealed class CalendarLinkViewTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly string parent;
    private readonly string root;

    public CalendarLinkViewTests()
    {
        parent = Path.Combine(Path.GetTempPath(), "context-linkview-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(parent, "Context");
        Directory.CreateDirectory(parent);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(root))
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private string LinkPath => Path.Combine(root, CalendarLink.FileName);

    private string CachePath => Path.Combine(root, CalendarFeed.CacheFileName);

    private static string Masked => $"https://{FakeHttpHandler.Host}/…";

    private CalendarLinkView Run(string argument)
    {
        CalendarLink link = new CalendarLink(root);
        CalendarFeed feed = new CalendarFeed(root, link, FakeHttpHandler.Throw(), () => Now);
        return CalendarLinkView.Run(link, feed, argument);
    }

    private void WriteCache(DateTime atUtc)
    {
        File.WriteAllText(CachePath, CalendarFeedTests.Ics);
        File.SetLastWriteTimeUtc(CachePath, atUtc);
    }

    private static void AssertNoSecret(CalendarLinkView view, string argument)
    {
        string[] texts = typeof(CalendarLinkView).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(view) ?? string.Empty)
            .Append(view.ToString())
            .ToArray();
        foreach (string text in texts)
        {
            Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeHttpHandler.QueryToken, text, StringComparison.Ordinal);
            if (argument.Length > 0)
            {
                Assert.DoesNotContain(argument, text, StringComparison.Ordinal);
            }
        }
    }

    // AC-6
    [Fact]
    public void Show_NotSet()
    {
        CalendarLinkView view = Run(string.Empty);

        Assert.Equal(CalendarLinkViewKind.NotSet, view.Kind);
        Assert.Null(view.Masked);
        Assert.False(File.Exists(LinkPath));
    }

    // AC-6
    [Fact]
    public void Show_Masked_NotDownloaded()
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);

        CalendarLinkView view = Run("  ");

        Assert.Equal(CalendarLinkViewKind.Shown, view.Kind);
        Assert.Equal(Masked, view.Masked);
        Assert.Null(view.CachedAtUtc);
        AssertNoSecret(view, string.Empty);
    }

    // AC-6
    [Fact]
    public void Show_Masked_WithCacheTime()
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        DateTime at = new(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc);
        WriteCache(at);

        CalendarLinkView view = Run(string.Empty);

        Assert.Equal(CalendarLinkViewKind.Shown, view.Kind);
        Assert.Equal(at, view.CachedAtUtc);
        AssertNoSecret(view, string.Empty);
    }

    // AC-6: set saves the link and drops the old cache; only the mask comes back.
    [Theory]
    [InlineData(FakeHttpHandler.Link)]
    [InlineData(
        "webcal://" + FakeHttpHandler.Host + "/owa/calendar/" + FakeHttpHandler.PathToken
        + "/r.ics?token=" + FakeHttpHandler.QueryToken)]
    public void Set_Saved_ClearsCache_OnlyMask(string argument)
    {
        Directory.CreateDirectory(root);
        WriteCache(Now);

        CalendarLinkView view = Run(argument);

        Assert.Equal(CalendarLinkViewKind.Saved, view.Kind);
        Assert.Equal(Masked, view.Masked);
        Assert.Null(view.CachedAtUtc);
        Assert.True(File.Exists(LinkPath));
        Assert.False(File.Exists(CachePath));
        AssertNoSecret(view, argument);
    }

    // AC-6: invalid writes nothing and keeps the old link and cache.
    [Theory]
    [InlineData("http://")]
    [InlineData("https://user:pw@")]
    [InlineData("file:///")]
    public void Set_Invalid_WritesNothing(string prefix)
    {
        string argument =
            prefix + FakeHttpHandler.Host + "/" + FakeHttpHandler.PathToken + "?t=" + FakeHttpHandler.QueryToken;
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        WriteCache(Now);

        CalendarLinkView view = Run(argument);

        Assert.Equal(CalendarLinkViewKind.Invalid, view.Kind);
        Assert.Null(view.Masked);
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
        Assert.True(File.Exists(CachePath));
        AssertNoSecret(view, argument);
    }

    // AC-6
    [Fact]
    public void Set_Invalid_NoFolder_CreatesNothing()
    {
        CalendarLinkView view = Run("http://example.com/x.ics");

        Assert.Equal(CalendarLinkViewKind.Invalid, view.Kind);
        Assert.False(Directory.Exists(root));
    }

    // AC-6: off deletes the link and the cache.
    [Theory]
    [InlineData("off")]
    [InlineData(" OFF ")]
    public void Off_Removed_DeletesLinkAndCache(string argument)
    {
        new CalendarLink(root).Set(FakeHttpHandler.Link);
        WriteCache(Now);

        CalendarLinkView view = Run(argument);

        Assert.Equal(CalendarLinkViewKind.Removed, view.Kind);
        Assert.False(File.Exists(LinkPath));
        Assert.False(File.Exists(CachePath));
    }

    // AC-6: off without a link still drops a leftover cache.
    [Fact]
    public void Off_NoLink_NotSet_DropsCache()
    {
        Directory.CreateDirectory(root);
        WriteCache(Now);

        CalendarLinkView view = Run("off");

        Assert.Equal(CalendarLinkViewKind.NotSet, view.Kind);
        Assert.False(File.Exists(CachePath));
    }

    // AC-6
    [Fact]
    public void Show_DamagedFile_Unreadable()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(LinkPath, "http://example.com/" + FakeHttpHandler.PathToken);

        CalendarLinkView view = Run(string.Empty);

        Assert.Equal(CalendarLinkViewKind.Unreadable, view.Kind);
        AssertNoSecret(view, string.Empty);
    }

    // AC-6
    [Fact]
    public void Set_CantWrite_NotSaved()
    {
        File.WriteAllText(root, "a file where the folder should be");

        CalendarLinkView view = Run(FakeHttpHandler.Link);

        Assert.Equal(CalendarLinkViewKind.NotSaved, view.Kind);
        AssertNoSecret(view, FakeHttpHandler.Link);
    }

    // AC-6
    [Fact]
    public void Off_CantDelete_NotRemoved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        new CalendarLink(root).Set(FakeHttpHandler.Link);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        CalendarLinkView view = Run("off");

        Assert.Equal(CalendarLinkViewKind.NotRemoved, view.Kind);
        Assert.True(File.Exists(LinkPath));
    }

    // AC-7: the renderer only maps the view.
    [Fact]
    public void Render_HandBuiltViews_MatchGolden()
    {
        TimeZoneInfo zone = IcsParserTests.Zone;
        (string Golden, CalendarLinkView View)[] cases =
        [
            ("Link_NotSet", new CalendarLinkView(CalendarLinkViewKind.NotSet)),
            ("Link_Off_NotSet", new CalendarLinkView(CalendarLinkViewKind.NotSet)),
            ("Link_Shown_NotDownloaded", new CalendarLinkView(CalendarLinkViewKind.Shown, Masked)),
            (
                "Link_Shown_Downloaded",
                new CalendarLinkView(
                    CalendarLinkViewKind.Shown, Masked, new DateTime(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc))
            ),
            ("Link_Saved", new CalendarLinkView(CalendarLinkViewKind.Saved, Masked)),
            ("Link_Removed", new CalendarLinkView(CalendarLinkViewKind.Removed)),
            ("Link_Invalid", new CalendarLinkView(CalendarLinkViewKind.Invalid)),
            ("Link_Unreadable", new CalendarLinkView(CalendarLinkViewKind.Unreadable)),
            ("Link_NotSaved", new CalendarLinkView(CalendarLinkViewKind.NotSaved)),
            ("Link_NotRemoved", new CalendarLinkView(CalendarLinkViewKind.NotRemoved)),
        ];

        foreach ((string golden, CalendarLinkView view) in cases)
        {
            Assert.Equal(
                ViewParityTests.Goldens[golden],
                ViewParityTests.Render(CalendarLinkCommands.Render(view, zone), "/r"));
        }
    }
}
