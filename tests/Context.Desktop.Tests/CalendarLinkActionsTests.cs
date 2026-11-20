using System.Text;

namespace Context.Tests;

// 138: setting, replacing and removing the Exchange calendar link through DesktopActions.
public sealed class CalendarLinkActionsTests : IDisposable
{
    private const string Masked = "https://" + FakeHttpHandler.Host + "/…";
    private const string OtherLink = "https://other.example/owa/otherTok51/calendar.ics?k=otherTok51";
    private const string InvalidText = "The calendar link must be an https:// or webcal:// address.";

    private static readonly DateTime CacheTime = new(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string LinkPath => Path.Combine(data.Root, CalendarLink.FileName);

    private string CachePath => Path.Combine(data.Root, CalendarFeed.CacheFileName);

    // AC-4: with no link stored the link is written (private off Windows), the old copy dropped, the state
    // refreshed; only the masked link comes back. The refresh's download fails (HTTP 500), so no new copy appears.
    [Fact]
    public async Task Set_NoLinkStored_WritesTheFileDropsTheCacheAndRefreshes()
    {
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        DesktopState state = data.CreateState();

        ActionResult result = await data.Actions!.SetCalendarLink(FakeHttpHandler.Link, null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal($"Calendar link saved ({Masked}).", result.Text);
        Assert.Equal(FakeHttpHandler.Link + "\n", Encoding.UTF8.GetString(File.ReadAllBytes(LinkPath)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(LinkPath));
        }

        Assert.False(File.Exists(CachePath));
        Assert.Equal(1, state.Builds);
        Assert.Equal([new Uri(FakeHttpHandler.Link)], data.Http.Uris);
        AssertNoSecret(result);
        Assert.DoesNotContain(Directory.EnumerateFiles(data.Root), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // AC-4: a webcal link is stored as https.
    [Fact]
    public async Task Set_Webcal_StoredAsHttps()
    {
        data.CreateState();

        ActionResult result = await data.Actions!.SetCalendarLink("webcal" + FakeHttpHandler.Link["https".Length..], null);

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
    }

    // AC-5: a value Validate rejects is refused with the fixed text and nothing changes; with a link stored and no
    // confirmation the answer is the same (not "not confirmed"), and "off" removes nothing.
    [Theory]
    [InlineData("off", false)]
    [InlineData("off", true)]
    [InlineData(" OFF ", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("http://outlook.example/calendar.ics", false)]
    [InlineData("http://outlook.example/calendar.ics", true)]
    [InlineData("https://user:pass@outlook.example/calendar.ics", false)]
    [InlineData("https://user@outlook.example/calendar.ics", true)]
    [InlineData("https://@outlook.example/calendar.ics", true)]
    [InlineData("ftp://outlook.example/calendar.ics", true)]
    [InlineData("https://outlook.example/a b.ics", true)]
    public async Task Set_InvalidValue_RefusedNothingChanges(string value, bool stored)
    {
        if (stored)
        {
            data.SaveLink(FakeHttpHandler.Link);
            data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        }

        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.SetCalendarLink(value, null);
        ActionResult confirmed = await data.Actions!.SetCalendarLink(value, new Confirmation());

        Assert.All(new[] { result, confirmed }, r =>
        {
            Assert.Equal(ActionKind.Refused, r.Kind);
            Assert.Equal(ActionReason.Invalid, r.Reason);
            Assert.Equal(InvalidText, r.Text);
        });
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(stored, File.Exists(LinkPath));
        Assert.Equal(0, state.Builds);
        Assert.Equal(0, data.Http.Requests);
    }

    // AC-5: 2048 characters are saved, 2049 are refused before anything is written.
    [Fact]
    public async Task Set_LengthLimit_2048Saved_2049Refused()
    {
        Assert.Equal(2048, DesktopActions.MaxCalendarLinkLength);
        const string Prefix = "https://outlook.example/";
        string longest = Prefix + new string('a', 2048 - Prefix.Length);
        string tooLong = longest + "a";
        Assert.NotNull(CalendarLink.Validate(tooLong));
        data.CreateState();

        ActionResult refused = await data.Actions!.SetCalendarLink(tooLong, null);

        Assert.Equal(ActionKind.Refused, refused.Kind);
        Assert.Equal(ActionReason.Invalid, refused.Reason);
        Assert.Equal(InvalidText, refused.Text);
        Assert.False(Directory.Exists(data.Root));

        ActionResult saved = await data.Actions!.SetCalendarLink(longest, null);

        Assert.Equal(ActionKind.Done, saved.Kind);
        Assert.Equal(longest + "\n", File.ReadAllText(LinkPath));
    }

    // AC-5: too long is refused first also when a link is stored and nothing was confirmed.
    [Fact]
    public async Task Set_TooLong_WithALinkStored_SameRefusal()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult refused = await data.Actions!.SetCalendarLink("https://outlook.example/" + new string('a', 2048), null);

        Assert.Equal(ActionReason.Invalid, refused.Reason);
        Assert.Equal(InvalidText, refused.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
    }

    // AC-5: a write error is Failed with the fixed text; no file changes.
    [Fact]
    public async Task Set_WriteFails_FailedNothingChanges()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.WriteNote("Plan", "# Plan\n");
        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        ActionResult result = await ReadOnly(() => data.Actions!.SetCalendarLink(FakeHttpHandler.Link, null));

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't save the calendar link. Check that the Context folder is writable.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, state.Builds);
        AssertNoSecret(result);
    }

    // AC-6: with a link stored, or an unreadable link file, a valid value without a confirmation changes nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Set_LinkStoredOrUnreadable_NotConfirmed_NothingChanges(bool unreadable)
    {
        if (unreadable)
        {
            data.WriteFile(CalendarLink.FileName, "not a link\n");
        }
        else
        {
            data.SaveLink(FakeHttpHandler.Link);
        }

        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.SetCalendarLink(OtherLink, null);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(DesktopActions.NotConfirmedText, result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, state.Builds);
        Assert.Equal(0, data.Http.Requests);
    }

    // AC-6: confirmed, the new link replaces the old one (or the unreadable file) and the old copy is deleted.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Set_LinkStoredOrUnreadable_Confirmed_ReplacesAndDropsTheCache(bool unreadable)
    {
        if (unreadable)
        {
            data.WriteFile(CalendarLink.FileName, "not a link\n");
        }
        else
        {
            data.SaveLink(FakeHttpHandler.Link);
        }

        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        data.CreateState();

        ActionResult result = await data.Actions!.SetCalendarLink(OtherLink, new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Calendar link saved (https://other.example/…).", result.Text);
        Assert.Equal(OtherLink + "\n", File.ReadAllText(LinkPath));
        Assert.False(File.Exists(CachePath));
        Assert.Equal([new Uri(OtherLink)], data.Http.Uris);
        Assert.DoesNotContain("otherTok51", result.Text, StringComparison.Ordinal);
    }

    // AC-7: no confirmation, nothing removed.
    [Fact]
    public async Task Remove_NotConfirmed_NothingChanges()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        DesktopState state = data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await data.Actions!.RemoveCalendarLink(null!);

        Assert.Equal(ActionKind.Refused, result.Kind);
        Assert.Equal(ActionReason.NotConfirmed, result.Reason);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, state.Builds);
    }

    // AC-7: confirmed, the link (readable or not) and the downloaded copy are deleted and the state refreshed; with
    // no link left the refresh downloads nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remove_Confirmed_DeletesLinkAndCache(bool unreadable)
    {
        if (unreadable)
        {
            data.WriteFile(CalendarLink.FileName, "not a link\n");
        }
        else
        {
            data.SaveLink(FakeHttpHandler.Link);
        }

        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        data.WriteNote("Plan", "# Plan\n");
        DesktopState state = data.CreateState();

        ActionResult result = await data.Actions!.RemoveCalendarLink(new Confirmation());

        Assert.Equal(ActionKind.Done, result.Kind);
        Assert.Equal("Calendar link removed.", result.Text);
        Assert.False(File.Exists(LinkPath));
        Assert.False(File.Exists(CachePath));
        Assert.True(File.Exists(Path.Combine(data.Root, "Plan.md")));
        Assert.Equal(1, state.Builds);
        Assert.Equal(0, data.Http.Requests);
    }

    // AC-7: a delete error is Failed with the fixed text; the link and the copy stay.
    [Fact]
    public async Task Remove_DeleteFails_FailedNothingChanges()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        data.CreateState();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        ActionResult result = await ReadOnly(() => data.Actions!.RemoveCalendarLink(new Confirmation()));

        Assert.Equal(ActionKind.Failed, result.Kind);
        Assert.Equal("Can't remove the calendar link. Check that the Context folder is writable.", result.Text);
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        AssertNoSecret(result);
    }

    // The data folder can't be written while the action runs.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private async Task<ActionResult> ReadOnly(Func<Task<ActionResult>> action)
    {
        UnixFileMode mode = File.GetUnixFileMode(data.Root);
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            return await action();
        }
        finally
        {
            File.SetUnixFileMode(data.Root, mode);
        }
    }

    private static void AssertNoSecret(ActionResult result)
    {
        foreach (string text in new[] { result.Text, result.ToString(), $"{result}" })
        {
            Assert.DoesNotContain(FakeHttpHandler.PathToken, text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeHttpHandler.QueryToken, text, StringComparison.Ordinal);
        }
    }
}
