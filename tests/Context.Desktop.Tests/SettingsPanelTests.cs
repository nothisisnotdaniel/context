using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

// 138: the settings page: the Exchange calendar link and the key of sensitive notes.
public sealed class SettingsPanelTests : IDisposable
{
    private const string Marker = "MARKER-138";
    private const string MarkerLink = "https://outlook.example/owa/" + Marker + "/calendar.ics?k=" + Marker;
    private const string OtherMarkerLink = "https://other.example/owa/" + Marker + "/b.ics?k=" + Marker;
    private const string Masked = "https://" + FakeHttpHandler.Host + "/…";
    private const string OtherLink = "https://other.example/owa/otherTok51/calendar.ics?k=otherTok51";
    private const string InvalidText = "The calendar link must be an https:// or webcal:// address.";
    private const string ReplaceQuestion = "Replace the calendar link? The downloaded calendar copy is deleted too.";
    private const string RemoveQuestion = "Remove the calendar link? The downloaded calendar copy is deleted too.";

    private static readonly DateTime CacheTime = new(2026, 10, 29, 7, 30, 0, DateTimeKind.Utc);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string LinkPath => Path.Combine(data.Root, CalendarLink.FileName);

    private string CachePath => Path.Combine(data.Root, CalendarFeed.CacheFileName);

    // AC-1, AC-2, AC-3: heading, both sections, the grey empty line and the masked field; rendering changes no file
    // and sends no request.
    [Fact]
    public async Task Render_NoLink_HeadingSectionsGreyLineAndField()
    {
        data.WriteNote("Plan", "# Plan\n");
        await data.RefreshAsync();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Equal("Settings", cut.Find(".panel-bar h2").TextContent);
        Assert.Equal("Close", cut.Find(".panel-bar button.close").TextContent);
        Assert.Equal(["Exchange calendar", "Sensitive notes"], cut.FindAll("h3").Select(h => h.TextContent).ToArray());
        IElement line = cut.Find(".calendar-link-state");
        Assert.Equal("No calendar link set.", line.TextContent);
        Assert.True(line.ClassList.Contains("grey"));
        Assert.Empty(cut.FindAll("button.calendar-link-remove"));
        Assert.Equal(string.Empty, cut.Find(".settings-result").TextContent);
        Assert.Equal("status", cut.Find(".settings-result").GetAttribute("role"));
        Assert.All(cut.FindAll("button"), b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(0, data.Http.Requests);
    }

    // AC-3: one password field, labelled, no autocomplete, at most 2048 characters, never a value; Save link is
    // disabled while the field is empty.
    [Fact]
    public async Task Field_IsAMaskedInputWithoutAValue_SaveDisabledWhileEmpty()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement field = Assert.Single(cut.FindAll("input"));
        Assert.Equal("password", field.GetAttribute("type"));
        Assert.Equal("Calendar link", field.GetAttribute("aria-label"));
        Assert.Equal("off", field.GetAttribute("autocomplete"));
        Assert.Equal("2048", field.GetAttribute("maxlength"));
        Assert.False(field.HasAttribute("value"));
        Assert.Equal("Save link", cut.Find("button.calendar-link-save").TextContent);
        Assert.True(cut.Find("button.calendar-link-save").HasAttribute("disabled"));

        await Type(cut, FakeHttpHandler.Link);

        Assert.False(cut.Find("button.calendar-link-save").HasAttribute("disabled"));
        Assert.False(cut.Find("input").HasAttribute("value"));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup, StringComparison.Ordinal);

        await Type(cut, string.Empty);

        Assert.True(cut.Find("button.calendar-link-save").HasAttribute("disabled"));
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-3: while an action runs the field and the buttons are disabled.
    [Fact]
    public async Task Save_WhileTheActionRuns_FieldAndButtonsDisabled()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        await Type(cut, FakeHttpHandler.Link);

        Task click = cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        cut.WaitForAssertion(() => Assert.True(cut.Find("button.calendar-link-save").HasAttribute("disabled")));
        Assert.True(cut.Find("input").HasAttribute("disabled"));
        Assert.True(cut.Find("button.close").HasAttribute("disabled"));
        release.Set();
        await click;

        cut.WaitForAssertion(() => Assert.False(cut.Find("input").HasAttribute("disabled")));
        Assert.True(File.Exists(LinkPath));
    }

    // AC-2: a stored link is shown masked (accent) with "not downloaded yet", then with the copy's time in the
    // calendar's zone after the state changed.
    [Fact]
    public async Task Render_LinkStored_MaskedWithDownloadTime_ReloadsOnStateChange()
    {
        data.SaveLink(FakeHttpHandler.Link);
        await data.RefreshAsync();

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".calendar-link-state");
        Assert.Equal($"Calendar link: {Masked} (not downloaded yet)", line.TextContent);
        Assert.Equal(Masked, line.QuerySelector(".accent")!.TextContent);
        Assert.Equal("(not downloaded yet)", line.QuerySelector(".grey")!.TextContent);
        Assert.Equal("Remove link", cut.Find("button.calendar-link-remove").TextContent);

        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        await data.State!.Refresh();

        // 07:30 UTC is 08:30 in the fixture's zone (UTC+1).
        cut.WaitForAssertion(() => Assert.Equal(
            $"Calendar link: {Masked} (last download 2026-10-29 08:30)", cut.Find(".calendar-link-state").TextContent));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeHttpHandler.QueryToken, cut.Markup, StringComparison.Ordinal);
    }

    // AC-2: an unreadable link file gives the red line; Remove link is offered.
    [Fact]
    public async Task Render_UnreadableLinkFile_RedLineAndRemove()
    {
        data.WriteFile(CalendarLink.FileName, "not a link\n");

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".calendar-link-state");
        Assert.Equal("Can't read calendar-link.txt. Set the link again or remove it.", line.TextContent);
        Assert.True(line.ClassList.Contains("red"));
        Assert.Single(cut.FindAll("button.calendar-link-remove"));
    }

    // AC-4: Save link writes the link, shows the masked result in the status line and leaves a new, empty field.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_NoLinkStored_SavesShowsMaskedAndClearsTheField(bool enter)
    {
        IRenderedComponent<SettingsPanel> cut = await Show();
        await Type(cut, FakeHttpHandler.Link);
        string? fieldBefore = cut.Find("input").GetAttribute("blazor:oninput");

        if (enter)
        {
            await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        }

        IElement status = cut.Find(".settings-result");
        Assert.Equal($"Calendar link saved ({Masked}).", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Equal(
            $"Calendar link: {Masked} (not downloaded yet)", cut.Find(".calendar-link-state").TextContent));

        // The field is a new element (its handler id changed), without a value, and the component kept no copy:
        // Save is disabled again and Enter saves nothing more.
        IElement field = cut.Find("input");
        Assert.NotEqual(fieldBefore, field.GetAttribute("blazor:oninput"));
        Assert.False(field.HasAttribute("value"));
        Assert.True(cut.Find("button.calendar-link-save").HasAttribute("disabled"));
        Assert.Equal(string.Empty, TypedCopy(cut));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup, StringComparison.Ordinal);
    }

    // AC-5: an invalid value shows the red line; nothing is written and no dialog opens, also with a link stored.
    [Theory]
    [InlineData("off", true)]
    [InlineData("http://outlook.example/calendar.ics", false)]
    [InlineData("https://user@outlook.example/calendar.ics", true)]
    public async Task Save_InvalidValue_RedLineNoDialogNothingWritten(string value, bool stored)
    {
        if (stored)
        {
            data.SaveLink(FakeHttpHandler.Link);
            data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        }

        IRenderedComponent<SettingsPanel> cut = await Show();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        await Type(cut, value);

        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());

        IElement status = cut.Find(".settings-result");
        Assert.Equal(InvalidText, status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));

        // Typing again clears the line.
        await Type(cut, value + "x");
        Assert.Equal(string.Empty, cut.Find(".settings-result").TextContent);
    }

    // AC-6: with a link stored Save asks first; Cancel keeps the old link and its copy byte for byte.
    [Fact]
    public async Task Save_LinkStored_AsksToReplace_CancelKeepsEverything()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        IRenderedComponent<SettingsPanel> cut = await Show();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);
        await Type(cut, OtherLink);

        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());

        Assert.Equal(ReplaceQuestion, cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Replace", cut.Find("[role=dialog] button.confirm").TextContent);
        Assert.Equal(string.Empty, cut.Find(".settings-result").TextContent);
        Assert.Equal(before, FileSnapshot.Take(data.Root));

        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
        Assert.DoesNotContain("otherTok51", cut.Markup, StringComparison.Ordinal);
    }

    // AC-6: confirmed, the new link is saved and the old copy deleted.
    [Fact]
    public async Task Save_LinkStored_ConfirmReplaces()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        IRenderedComponent<SettingsPanel> cut = await Show();
        await Type(cut, OtherLink);

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal("Calendar link saved (https://other.example/…).", cut.Find(".settings-result").TextContent);
        Assert.Equal(OtherLink + "\n", File.ReadAllText(LinkPath));
        Assert.False(File.Exists(CachePath));
        cut.WaitForAssertion(() => Assert.Equal(
            "Calendar link: https://other.example/… (not downloaded yet)", cut.Find(".calendar-link-state").TextContent));
        Assert.Equal(string.Empty, TypedCopy(cut));
        Assert.DoesNotContain("otherTok51", cut.Markup, StringComparison.Ordinal);
    }

    // AC-7: Remove link asks; Cancel removes nothing, confirm deletes the link and the copy.
    [Fact]
    public async Task Remove_AsksFirst_CancelKeeps_ConfirmDeletes()
    {
        data.SaveLink(FakeHttpHandler.Link);
        data.WriteCache(DesktopFixture.Calendar(), CacheTime);
        IRenderedComponent<SettingsPanel> cut = await Show();
        SortedDictionary<string, string> before = FileSnapshot.Take(data.Root);

        await cut.Find("button.calendar-link-remove").ClickAsync(new MouseEventArgs());

        Assert.Equal(RemoveQuestion, cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Remove", cut.Find("[role=dialog] button.confirm").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(before, FileSnapshot.Take(data.Root));

        await cut.Find("button.calendar-link-remove").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        IElement status = cut.Find(".settings-result");
        Assert.Equal("Calendar link removed.", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        Assert.False(File.Exists(LinkPath));
        Assert.False(File.Exists(CachePath));
        cut.WaitForAssertion(() => Assert.Equal("No calendar link set.", cut.Find(".calendar-link-state").TextContent));
        Assert.Empty(cut.FindAll("button.calendar-link-remove"));
    }

    // AC-7: a delete that fails shows the fixed red text and keeps the files.
    [Fact]
    public async Task Remove_DeleteFails_RedLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.SaveLink(FakeHttpHandler.Link);
        IRenderedComponent<SettingsPanel> cut = await Show();
        await cut.Find("button.calendar-link-remove").ClickAsync(new MouseEventArgs());
        UnixFileMode mode = File.GetUnixFileMode(data.Root);
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        }
        finally
        {
            File.SetUnixFileMode(data.Root, mode);
        }

        IElement status = cut.Find(".settings-result");
        Assert.Equal("Can't remove the calendar link. Check that the Context folder is writable.", status.TextContent);
        Assert.True(status.ClassList.Contains("red"));
        Assert.True(File.Exists(LinkPath));
    }

    // AC-10: the sensitive section's three fixed lines.
    [Theory]
    [InlineData("unsupported", "Sensitive notes need macOS or Windows.", "grey")]
    [InlineData("none", "Sensitive notes are not set up.", "grey")]
    [InlineData("throws", "Can't read the key.", "red")]
    public async Task SensitiveSection_NoKeyToShow_FixedLine(string store, string text, string cssClass)
    {
        data.Keys = new FakeKeyStore { Supported = store != "unsupported", DetailsThrow = store == "throws" };

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".sensitive-key-state");
        Assert.Equal(text, line.TextContent);
        Assert.True(line.ClassList.Contains(cssClass));
        Assert.Empty(cut.FindAll(".key-details"));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(0, data.Keys.MachineKeysCreated);
    }

    // AC-10: name (on one line), the whole thumbprint and the expiry day in UTC; no private-key call, no new key.
    [Fact]
    public async Task SensitiveSection_Certificate_NameThumbprintAndUtcExpiry()
    {
        const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";
        data.Keys = FakeKeyStore.Ready();
        byte[] recovery = data.Keys.RecoveryPublicKey()!;
        data.Keys.Details = new KeyDetails(
            "Context\nsensitive notes", Thumbprint, new DateTime(2076, 11, 12, 23, 30, 0, DateTimeKind.Utc));

        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Equal(["Name", "Thumbprint", "Expires"], cut.FindAll(".key-details th").Select(h => h.TextContent).ToArray());
        Assert.All(cut.FindAll(".key-details th"), h => Assert.Equal("row", h.GetAttribute("scope")));
        Assert.Equal("Context sensitive notes", cut.Find(".key-name").TextContent);
        Assert.Equal(Thumbprint, cut.Find(".key-thumbprint").TextContent);
        Assert.False(cut.Find(".key-thumbprint").ClassList.Contains("cut"));
        Assert.Equal("2076-11-12", cut.Find(".key-expires").TextContent);
        Assert.Empty(cut.FindAll(".sensitive-key-state"));
        Assert.Equal(0, data.Keys.Agrees);
        Assert.Equal(0, data.Keys.MachineKeysCreated);
        Assert.Same(recovery, data.Keys.RecoveryPublicKey());
    }

    // AC-10: a key without a certificate (macOS): the 64-character fingerprint and "Does not expire"; the section
    // reloads when the state changes.
    [Fact]
    public async Task SensitiveSection_KeyWithoutCertificate_DoesNotExpire_ReloadsOnStateChange()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();
        Assert.Equal("Sensitive notes are not set up.", cut.Find(".sensitive-key-state").TextContent);

        data.Keys.CreateMachineKey();
        await data.State!.Refresh();

        cut.WaitForAssertion(() => Assert.Equal("Does not expire", cut.Find(".key-expires").TextContent));
        Assert.Equal("Context sensitive notes", cut.Find(".key-name").TextContent);
        Assert.Equal(64, cut.Find(".key-thumbprint").TextContent.Length);
        Assert.Equal(SealedNote.KeyId(data.Keys.MachinePublicKey()!), cut.Find(".key-thumbprint").TextContent, ignoreCase: true);
        Assert.Equal(0, data.Keys.Agrees);
    }

    // AC-10: the thumbprint is not cut by CSS and can be selected.
    [Fact]
    public void AppCss_ThumbprintWrapsAndIsSelectable()
    {
        string css = File.ReadAllText(Path.Combine(RepoFiles.Root(), RepoFiles.DesktopFolder, "wwwroot", "app.css"));
        int start = css.IndexOf(".key-details td {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string rule = css[start..css.IndexOf('}', start)];

        Assert.Contains("user-select: text;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", rule, StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ellipsis", rule, StringComparison.Ordinal);
    }

    // AC-1: Close reports to the parent and drops what was typed.
    [Fact]
    public async Task Close_CallsOnClose_AndDropsTheTypedLink()
    {
        int closed = 0;
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>(p => p.Add(s => s.OnClose, () => closed++));
        await Type(cut, FakeHttpHandler.Link);

        await cut.Find("button.close").ClickAsync(new MouseEventArgs());

        Assert.Equal(1, closed);
        Assert.Equal(string.Empty, TypedCopy(cut));
        Assert.False(Directory.Exists(data.Root));
    }

    // AC-8: the link's secret part is in no markup of Main at any step, in no result, snapshot or view text and in
    // no file except calendar-link.txt; the only requests are the feed's GETs to the link itself.
    [Fact]
    public async Task Secrecy_TheLinkNeverLeavesTheLinkFile()
    {
        data.WriteNote("Plan", "# Plan\n");
        List<ActionResult> results = [];
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        void Clean()
        {
            Assert.DoesNotContain(Marker, cut.Markup, StringComparison.Ordinal);
            foreach (string path in Directory.EnumerateFiles(data.Root, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) != CalendarLink.FileName)
                {
                    Assert.DoesNotContain(Marker, File.ReadAllText(path), StringComparison.Ordinal);
                    Assert.DoesNotContain(Marker, path, StringComparison.Ordinal);
                }

                Assert.False(path.EndsWith(".tmp", StringComparison.Ordinal), path);
            }

            DesktopSnapshot snapshot = data.State!.Snapshot!;
            Assert.Equal("DesktopSnapshot", snapshot.ToString());
            Assert.DoesNotContain(Marker, snapshot.Folder, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, snapshot.Current?.Reason ?? string.Empty, StringComparison.Ordinal);
        }

        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("No calendar link set.", cut.Find(".calendar-link-state").TextContent));

        // Typing and saving.
        await Type(cut, MarkerLink);
        Clean();
        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        Assert.Equal("Calendar link saved (https://outlook.example/…).", cut.Find(".settings-result").TextContent);
        Assert.Equal(MarkerLink + "\n", File.ReadAllText(LinkPath));
        cut.WaitForAssertion(() => Assert.StartsWith("Calendar link: https://outlook.example/…", cut.Find(".calendar-link-state").TextContent));
        Clean();
        await Views();

        // Refused: an invalid value that holds the marker.
        await Type(cut, "http://outlook.example/" + Marker);
        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        Assert.Equal(InvalidText, cut.Find(".settings-result").TextContent);
        Clean();

        // Replaced: asked, cancelled, asked again, confirmed.
        await Type(cut, OtherMarkerLink);
        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        Assert.Equal(ReplaceQuestion, cut.Find("[role=dialog] .question").TextContent);
        Clean();
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());
        Clean();
        await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        Assert.Equal("Calendar link saved (https://other.example/…).", cut.Find(".settings-result").TextContent);
        Assert.Equal(OtherMarkerLink + "\n", File.ReadAllText(LinkPath));
        Clean();
        await Views();

        // Failed: the folder can't be written.
        if (!OperatingSystem.IsWindows())
        {
            await Type(cut, MarkerLink);
            await cut.Find("button.calendar-link-save").ClickAsync(new MouseEventArgs());
            UnixFileMode mode = File.GetUnixFileMode(data.Root);
            File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
            }
            finally
            {
                File.SetUnixFileMode(data.Root, mode);
            }

            Assert.Equal(
                "Can't save the calendar link. Check that the Context folder is writable.",
                cut.Find(".settings-result").TextContent);
            Assert.Equal(OtherMarkerLink + "\n", File.ReadAllText(LinkPath));
            Clean();
        }

        // The actions' own answers for every step.
        results.Add(await data.Actions!.SetCalendarLink(MarkerLink, null));
        results.Add(await data.Actions!.SetCalendarLink("http://outlook.example/" + Marker, null));
        results.Add(await data.Actions!.SetCalendarLink(MarkerLink + new string('a', 2048), null));
        results.Add(await data.Actions!.SetCalendarLink(MarkerLink, new Confirmation()));
        Assert.Equal(
            [ActionKind.Refused, ActionKind.Refused, ActionKind.Refused, ActionKind.Done],
            results.Select(r => r.Kind).ToArray());
        Assert.Equal(ActionReason.NotConfirmed, results[0].Reason);
        Clean();

        // Removed.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.calendar-link-remove")));
        await cut.Find("button.calendar-link-remove").ClickAsync(new MouseEventArgs());
        Clean();
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());
        Assert.Equal("Calendar link removed.", cut.Find(".settings-result").TextContent);
        Assert.False(File.Exists(LinkPath));
        Clean();
        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());
        Clean();

        Assert.All(results, r =>
        {
            Assert.DoesNotContain(Marker, r.Text, StringComparison.Ordinal);
            Assert.Equal("ActionResult", r.ToString());
        });
        Assert.NotEmpty(data.Http.Uris);
        Assert.All(data.Http.Uris, u => Assert.Contains(u, new[] { new Uri(MarkerLink), new Uri(OtherMarkerLink) }));

        async Task Views()
        {
            CalendarLinkView view = await data.State!.CalendarLinkSetting();
            Assert.Equal(CalendarLinkViewKind.Shown, view.Kind);
            Assert.DoesNotContain(Marker, view.Masked!, StringComparison.Ordinal);
            Assert.Equal("CalendarLinkView", view.ToString());
        }
    }

    // Builds the state and renders the panel once both sections have loaded.
    private async Task<IRenderedComponent<SettingsPanel>> Show()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal));
        await Task.CompletedTask;
        return cut;
    }

    private static Task Type<T>(IRenderedComponent<T> cut, string value)
        where T : IComponent =>
        cut.Find("input.calendar-link-field").InputAsync(new ChangeEventArgs { Value = value });

    // The component's own copy of what was typed.
    private static string TypedCopy(IRenderedComponent<SettingsPanel> cut) =>
        (string)typeof(SettingsPanel)
            .GetField("typed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cut.Instance)!;
}
