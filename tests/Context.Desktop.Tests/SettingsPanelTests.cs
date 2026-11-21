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

    // 175: Load awaits two Task.Run hops (the calendar link, then the key), so no wait relies on bUnit's 1 s default.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

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
        IElement close = cut.Find(".page > .panel-bar button.close");
        Assert.Equal(string.Empty, close.TextContent);
        Assert.Equal("Close", close.GetAttribute("aria-label"));
        Assert.Equal("Close", close.GetAttribute("title"));
        Assert.NotNull(close.QuerySelector("svg"));
        Assert.Equal(["Appearance", "Exchange calendar", "Outlook", "Pasting", "Screenshots", "Command bar", "Repository folders", "Sensitive notes"], cut.FindAll("h3").Select(h => h.TextContent).ToArray());
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
        // 217: without Outlook here the Outlook section has no checkbox, so (244: besides the Pasting checkbox) the link
        // field is the only input (260: besides the screenshot folder field; 280: and the repository folder field).
        data.Outlook.Available = OutlookAvailability.Unsupported;
        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Single(cut.FindAll("input[type=checkbox]"), i => i.ClassList.Contains("fetch-titles"));
        IElement field = Assert.Single(
            cut.FindAll("input"),
            i => i.GetAttribute("type") != "checkbox" && !i.ClassList.Contains("screenshot-folder-field")
                && !i.ClassList.Contains("repository-field"));
        Assert.Equal("password", field.GetAttribute("type"));
        Assert.Equal("Calendar link", field.GetAttribute("aria-label"));
        Assert.Equal("off", field.GetAttribute("autocomplete"));
        Assert.Equal("2048", field.GetAttribute("maxlength"));
        Assert.False(field.HasAttribute("value"));
        Assert.Empty(cut.FindAll("button.calendar-link-save"));
        Assert.Equal("https:// or webcal:// link", field.GetAttribute("placeholder"));

        await Type(cut, FakeHttpHandler.Link);

        Assert.False(cut.Find("input").HasAttribute("value"));
        Assert.DoesNotContain(FakeHttpHandler.PathToken, cut.Markup, StringComparison.Ordinal);

        await Type(cut, string.Empty);

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

        Task click = cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        cut.WaitForAssertion(() => Assert.True(cut.Find("input").HasAttribute("disabled")), Wait);
        Assert.True(cut.Find("input").HasAttribute("disabled"));
        Assert.True(cut.Find("button.close").HasAttribute("disabled"));
        release.Set();
        await click;

        cut.WaitForAssertion(() => Assert.False(cut.Find("input").HasAttribute("disabled")), Wait);
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
            $"Calendar link: {Masked} (last download 2026-10-29 08:30)", cut.Find(".calendar-link-state").TextContent), Wait);
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
            await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }

        IElement status = cut.Find(".settings-result");
        Assert.Equal($"Calendar link saved ({Masked}).", status.TextContent);
        Assert.True(status.ClassList.Contains("accent"));
        Assert.Equal(FakeHttpHandler.Link + "\n", File.ReadAllText(LinkPath));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        cut.WaitForAssertion(() => Assert.Equal(
            $"Calendar link: {Masked} (not downloaded yet)", cut.Find(".calendar-link-state").TextContent), Wait);

        // The field is a new element (its handler id changed), without a value, and the component kept no copy:
        // Save is disabled again and Enter saves nothing more.
        IElement field = cut.Find("input");
        Assert.NotEqual(fieldBefore, field.GetAttribute("blazor:oninput"));
        Assert.False(field.HasAttribute("value"));
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

        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

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

        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

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
            "Calendar link: https://other.example/… (not downloaded yet)", cut.Find(".calendar-link-state").TextContent), Wait);
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
        cut.WaitForAssertion(() => Assert.Equal("No calendar link set.", cut.Find(".calendar-link-state").TextContent), Wait);
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

        cut.WaitForAssertion(() => Assert.Equal("Does not expire", cut.Find(".key-expires").TextContent), Wait);
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

        await StaleClick.ClickAsync(cut, "button.settings-open");
        cut.WaitForAssertion(() => Assert.Equal("No calendar link set.", cut.Find(".calendar-link-state").TextContent), Wait);

        // Typing and saving.
        await Type(cut, MarkerLink);
        Clean();
        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("Calendar link saved (https://outlook.example/…).", cut.Find(".settings-result").TextContent);
        Assert.Equal(MarkerLink + "\n", File.ReadAllText(LinkPath));
        cut.WaitForAssertion(() => Assert.StartsWith("Calendar link: https://outlook.example/…", cut.Find(".calendar-link-state").TextContent), Wait);
        Clean();
        await Views();

        // Refused: an invalid value that holds the marker.
        await Type(cut, "http://outlook.example/" + Marker);
        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(InvalidText, cut.Find(".settings-result").TextContent);
        Clean();

        // Replaced: asked, cancelled, asked again, confirmed.
        await Type(cut, OtherMarkerLink);
        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(ReplaceQuestion, cut.Find("[role=dialog] .question").TextContent);
        Clean();
        await StaleClick.ClickAsync(cut, "[role=dialog] button.cancel");
        Clean();
        await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");
        Assert.Equal("Calendar link saved (https://other.example/…).", cut.Find(".settings-result").TextContent);
        Assert.Equal(OtherMarkerLink + "\n", File.ReadAllText(LinkPath));
        Clean();
        await Views();

        // Failed: the folder can't be written.
        if (!OperatingSystem.IsWindows())
        {
            await Type(cut, MarkerLink);
            await cut.Find(".calendar-link-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
            UnixFileMode mode = File.GetUnixFileMode(data.Root);
            File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button.calendar-link-remove")), Wait);
        await StaleClick.ClickAsync(cut, "button.calendar-link-remove");
        Clean();
        await StaleClick.ClickAsync(cut, "[role=dialog] button.confirm");
        Assert.Equal("Calendar link removed.", cut.Find(".settings-result").TextContent);
        Assert.False(File.Exists(LinkPath));
        Clean();
        await StaleClick.ClickAsync(cut, ".settings-panel button.close");
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
    // 217 AC-2: without Outlook here the section says why, in grey, with no checkbox.
    [Theory]
    [InlineData("Unsupported", "Outlook is only available on Windows.")]
    [InlineData("NotInstalled", "Classic Outlook isn't installed. The new Outlook can't be read; use a calendar link instead.")]
    public async Task Outlook_NotAvailable_GreyLine_NoCheckbox(string availability, string text)
    {
        data.Outlook.Available = Enum.Parse<OutlookAvailability>(availability);

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".outlook-state");
        Assert.Equal(text, line.TextContent);
        Assert.Contains("grey", line.ClassList);
        Assert.Empty(cut.FindAll("input.outlook-enabled"));
        Assert.Empty(cut.FindAll(".outlook-hint"));
    }

    // 217 AC-2: with classic Outlook the checkbox shows the stored value, with the grey hint.
    [Theory]
    [InlineData(null, false)]
    [InlineData("{\"outlook\":{\"enabled\":false}}", false)]
    [InlineData("{\"outlook\":{\"enabled\":true}}", true)]
    public async Task Outlook_Available_CheckboxShowsTheStoredValue(string? settings, bool expected)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement box = cut.Find("label.outlook-line input.outlook-enabled");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.Equal(expected, box.HasAttribute("checked"));
        Assert.Equal("Read calendar from Outlook", cut.Find("label.outlook-line").TextContent.Trim());
        IElement hint = cut.Find(".outlook-hint");
        Assert.Equal("Outlook may ask whether Context may access it.", hint.TextContent);
        Assert.Contains("grey", hint.ClassList);
        Assert.Empty(cut.FindAll(".outlook-state"));
        Assert.Equal(0, data.Outlook.AppointmentCalls);
    }

    // 217 AC-2: toggling calls SetOutlookEnabled; Done shows Outlook on./off. in the accent colour and the box follows.
    [Fact]
    public async Task Outlook_Toggle_OnThenOff()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.outlook-enabled").ChangeAsync(new ChangeEventArgs { Value = true });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Outlook on.", cut.Find(".outlook-result").TextContent);
            Assert.True(cut.Find("input.outlook-enabled").HasAttribute("checked"));
        }, Wait);
        Assert.Contains("accent", cut.Find(".outlook-result").ClassList);
        Assert.Equal("status", cut.Find(".outlook-result").GetAttribute("role"));
        Assert.True(new SettingsStore(data.Root).ReadOutlook());

        await cut.Find("input.outlook-enabled").ChangeAsync(new ChangeEventArgs { Value = false });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Outlook off.", cut.Find(".outlook-result").TextContent);
            Assert.False(cut.Find("input.outlook-enabled").HasAttribute("checked"));
        }, Wait);
        Assert.False(new SettingsStore(data.Root).ReadOutlook());
    }

    // 217 AC-2: a broken settings.json shows the Failed text in red and stays as it is.
    [Fact]
    public async Task Outlook_Toggle_BrokenSettings_RedFailedText()
    {
        data.WriteFile(SettingsStore.FileName, "{\"dayEnd\":{\"moved\":5}}");
        string path = Path.Combine(data.Root, SettingsStore.FileName);
        byte[] before = File.ReadAllBytes(path);
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.outlook-enabled").ChangeAsync(new ChangeEventArgs { Value = true });

        cut.WaitForAssertion(
            () => Assert.Equal("Can't read settings.json. Nothing was saved.", cut.Find(".outlook-result").TextContent),
            Wait);
        Assert.Contains("red", cut.Find(".outlook-result").ClassList);
        Assert.False(cut.Find("input.outlook-enabled").HasAttribute("checked"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // 244 AC-9: the Pasting section: the checkbox shows the stored value (unchecked by default and for an unreadable
    // file), with its label and the grey hint; showing it asks for no page title.
    [Theory]
    [InlineData(null, false)]
    [InlineData("{\"paste\":{\"fetchTitles\":false}}", false)]
    [InlineData("{\"paste\":{\"fetchTitles\":true}}", true)]
    [InlineData("not json", false)]
    public async Task Pasting_CheckboxShowsTheStoredValue(string? settings, bool expected)
    {
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement box = cut.Find("label.fetch-titles-line input.fetch-titles");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.Equal(expected, box.HasAttribute("checked"));
        Assert.Equal("Fetch page titles for pasted links", cut.Find("label.fetch-titles-line").TextContent.Trim());
        IElement hint = cut.Find(".fetch-titles-hint");
        Assert.Equal("When on, Context downloads a pasted link's page to read its title.", hint.TextContent);
        Assert.Contains("grey", hint.ClassList);
        Assert.Equal(string.Empty, cut.Find(".fetch-titles-result").TextContent);
        Assert.Equal("status", cut.Find(".fetch-titles-result").GetAttribute("role"));
        Assert.Equal(0, data.PageTitles.Calls);
    }

    // 244 AC-9: toggling saves through SetFetchTitles (other properties kept); Done shows Page titles on./off. in the
    // accent colour and the box follows.
    [Fact]
    public async Task Pasting_Toggle_OnThenOff_KeepsOthers()
    {
        data.WriteFile(SettingsStore.FileName, "{\"theme\":\"dark\",\"outlook\":{\"enabled\":true},\"savedSearches\":[{\"name\":\"Work\",\"query\":\"#work\"}]}");
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.fetch-titles").ChangeAsync(new ChangeEventArgs { Value = true });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Page titles on.", cut.Find(".fetch-titles-result").TextContent);
            Assert.True(cut.Find("input.fetch-titles").HasAttribute("checked"));
        }, Wait);
        Assert.Contains("accent", cut.Find(".fetch-titles-result").ClassList);
        SettingsStore store = new SettingsStore(data.Root);
        Assert.True(store.ReadFetchTitles());
        Assert.True(store.ReadOutlook());
        Assert.Single(store.ReadSavedSearches());
        Assert.Contains("\"theme\": \"dark\"", File.ReadAllText(Path.Combine(data.Root, SettingsStore.FileName)), StringComparison.Ordinal);

        await cut.Find("input.fetch-titles").ChangeAsync(new ChangeEventArgs { Value = false });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Page titles off.", cut.Find(".fetch-titles-result").TextContent);
            Assert.False(cut.Find("input.fetch-titles").HasAttribute("checked"));
        }, Wait);
        Assert.False(store.ReadFetchTitles());
        Assert.True(store.ReadOutlook());
        Assert.Equal(0, data.PageTitles.Calls);
    }

    // 244 AC-9: a broken settings.json shows the red text, the box stays unchecked, and the file is not changed.
    [Fact]
    public async Task Pasting_Toggle_BrokenSettings_RedText_BoxBack()
    {
        data.WriteFile(SettingsStore.FileName, "{\"dayEnd\":{\"moved\":5}}");
        string path = Path.Combine(data.Root, SettingsStore.FileName);
        byte[] before = File.ReadAllBytes(path);
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.fetch-titles").ChangeAsync(new ChangeEventArgs { Value = true });

        cut.WaitForAssertion(
            () => Assert.Equal("Can't save settings.json.", cut.Find(".fetch-titles-result").TextContent), Wait);
        Assert.Contains("red", cut.Find(".fetch-titles-result").ClassList);
        Assert.False(cut.Find("input.fetch-titles").HasAttribute("checked"));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // 244 AC-9: a folder that can't be written: the red text, and the box goes back to the stored (checked) value.
    [Fact]
    public async Task Pasting_Toggle_ReadOnlyFolder_RedText_BoxBack()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.WriteFile(SettingsStore.FileName, "{\"paste\":{\"fetchTitles\":true}}");
        string path = Path.Combine(data.Root, SettingsStore.FileName);
        byte[] before = File.ReadAllBytes(path);
        IRenderedComponent<SettingsPanel> cut = await Show();
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanWriteIn(data.Root))
            {
                // Running as root: the folder stays writable, nothing to check.
                return;
            }

            await cut.Find("input.fetch-titles").ChangeAsync(new ChangeEventArgs { Value = false });

            cut.WaitForAssertion(
                () => Assert.Equal("Can't save settings.json.", cut.Find(".fetch-titles-result").TextContent), Wait);
            Assert.Contains("red", cut.Find(".fetch-titles-result").ClassList);
            Assert.True(cut.Find("input.fetch-titles").HasAttribute("checked"));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- 341: the theme ----

    private string SettingsPath => Path.Combine(data.Root, SettingsStore.FileName);

    private static readonly string[] ThemeButtons = ["button.theme-system", "button.theme-light", "button.theme-dark"];

    // The aria-pressed values of System, Light and Dark, in order.
    private static string[] Pressed<T>(IRenderedComponent<T> cut)
        where T : IComponent =>
        ThemeButtons.Select(b => cut.Find(b).GetAttribute("aria-pressed") ?? "missing").ToArray();

    // 341 AC-3: Appearance is the first section: its heading, then a labelled group of three plain buttons in order,
    // exactly the saved one pressed, an empty status line and no new input.
    [Theory]
    [InlineData(null, 0)]
    [InlineData("{\"appearance\":{\"theme\":\"system\"}}", 0)]
    [InlineData("{\"appearance\":{\"theme\":\"light\"}}", 1)]
    [InlineData("{\"appearance\":{\"theme\":\"dark\"}}", 2)]
    [InlineData("{\"appearance\":{\"theme\":\"Dark\"}}", 0)]
    [InlineData("not json", 0)]
    public async Task Appearance_Section_GroupButtonsAndPressedState(string? settings, int pressed)
    {
        data.Outlook.Available = OutlookAvailability.Unsupported;
        if (settings is not null)
        {
            data.WriteFile(SettingsStore.FileName, settings);
        }

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement heading = cut.FindAll("h3")[0];
        Assert.Equal("Appearance", heading.TextContent);
        IElement group = cut.Find("div.theme-choice");
        Assert.Equal("theme-choice", heading.NextElementSibling!.ClassName);
        Assert.Equal("group", group.GetAttribute("role"));
        Assert.False(string.IsNullOrEmpty(heading.Id));
        Assert.Equal(heading.Id, group.GetAttribute("aria-labelledby"));
        Assert.Single(cut.FindAll("#" + heading.Id));
        IElement[] buttons = group.Children.ToArray();
        Assert.Equal(["BUTTON", "BUTTON", "BUTTON"], buttons.Select(b => b.TagName).ToArray());
        Assert.Equal(["theme-system", "theme-light", "theme-dark"], buttons.Select(b => b.ClassName ?? string.Empty).ToArray());
        Assert.Equal(["System", "Light", "Dark"], buttons.Select(b => b.TextContent).ToArray());
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        Assert.All(buttons, b => Assert.False(b.HasAttribute("disabled")));
        string[] expected = ["false", "false", "false"];
        expected[pressed] = "true";
        Assert.Equal(expected, Pressed(cut));
        IElement result = cut.Find("p.theme-result");
        Assert.Contains("theme-result", group.NextElementSibling!.ClassList);
        Assert.Equal("status", result.GetAttribute("role"));
        Assert.Equal(string.Empty, result.TextContent);
        Assert.Empty(group.QuerySelectorAll("input"));
        Assert.Equal(
            ["calendar-link-field", "fetch-titles", "screenshot-folder-field", "repository-field"],
            cut.FindAll("input").Select(i => i.ClassName!.Split(' ')[0]).ToArray());
    }

    // 341 AC-3: while the settings load (the key read is held, so the panel's Load can't finish) the section shows grey
    // Loading… and no buttons; once it finishes the buttons replace it.
    [Fact]
    public void Appearance_WhileLoading_GreyLoadingNoButtons()
    {
        using ManualResetEventSlim hold = new ManualResetEventSlim();
        data.Keys.DetailsHold = hold;
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut;
        try
        {
            cut = data.Bunit.Render<SettingsPanel>();

            IElement state = cut.Find("p.theme-state");
            Assert.Equal("Loading…", state.TextContent);
            Assert.Contains("grey", state.ClassList);
            Assert.Equal("Appearance", state.PreviousElementSibling!.TextContent);
            Assert.Empty(cut.FindAll("div.theme-choice"));
            Assert.Empty(cut.FindAll("button.theme-system"));
        }
        finally
        {
            hold.Set();
        }

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("div.theme-choice")), Wait);
        Assert.Empty(cut.FindAll("p.theme-state"));
    }

    // 341 AC-4: clicking Light then Dark then System saves each through SetTheme (other properties kept), moves the
    // pressed state, shows the accent line and has Main call setTheme once per save with the fixed name.
    [Fact]
    public async Task Appearance_Click_SavesMovesPressedAndAppliesTheme()
    {
        data.WriteFile(SettingsStore.FileName, "{\"paste\":{\"fetchTitles\":true}}");
        IRenderedComponent<Main> cut = await OpenSettingsInMain();
        Assert.Equal(["system"], data.Themes());

        (string Button, string Name, string Text, string[] Pressed)[] steps =
        [
            ("button.theme-light", "light", "Theme: Light.", ["false", "true", "false"]),
            ("button.theme-dark", "dark", "Theme: Dark.", ["false", "false", "true"]),
            ("button.theme-system", "system", "Theme: System.", ["true", "false", "false"]),
        ];
        List<string> applied = ["system"];
        await Settled(cut);
        foreach ((string button, string name, string text, string[] pressed) in steps)
        {
            await StaleClick.ClickAsync(cut, button);

            cut.WaitForAssertion(() => Assert.Equal(text, cut.Find("p.theme-result").TextContent), Wait);
            Assert.Contains("accent", cut.Find("p.theme-result").ClassList);
            Assert.Equal(pressed, Pressed(cut));
            applied.Add(name);
            Assert.Equal(applied, data.Themes());
            Assert.Equal(Enum.Parse<AppTheme>(name, ignoreCase: true), new SettingsStore(data.Root).ReadTheme());
            Assert.True(new SettingsStore(data.Root).ReadFetchTitles());

            // 363: the save goes on to render the page again (OnThemeChanged, Load() and the state's reload), so the
            // next click waits until the page is settled.
            await Settled(cut);
        }
    }

    // 341 AC-4: clicking the pressed button does nothing: no save, no line, no setTheme.
    [Fact]
    public async Task Appearance_ClickPressed_DoesNothing()
    {
        data.WriteFile(SettingsStore.FileName, "{\"appearance\":{\"theme\":\"dark\"}}");
        IRenderedComponent<Main> cut = await OpenSettingsInMain();
        byte[] before = File.ReadAllBytes(SettingsPath);
        int builds = data.State!.Builds;

        await StaleClick.ClickAsync(cut, "button.theme-dark");

        Assert.Equal(string.Empty, cut.Find("p.theme-result").TextContent);
        Assert.Equal(["false", "false", "true"], Pressed(cut));
        Assert.Equal(["dark"], data.Themes());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal(builds, data.State.Builds);
    }

    // 341 AC-4: a broken settings.json (Failed) shows the red line, keeps the saved theme pressed, leaves the file
    // and calls no setTheme.
    [Fact]
    public async Task Appearance_BrokenSettings_RedLine_PressedStays_NoSetTheme()
    {
        data.WriteFile(SettingsStore.FileName, "{\"dayEnd\":{\"moved\":5},\"appearance\":{\"theme\":\"light\"}}");
        IRenderedComponent<Main> cut = await OpenSettingsInMain();
        byte[] before = File.ReadAllBytes(SettingsPath);

        await StaleClick.ClickAsync(cut, "button.theme-dark");

        cut.WaitForAssertion(
            () => Assert.Equal(DesktopActions.SettingsNotSavedText, cut.Find("p.theme-result").TextContent), Wait);
        Assert.Contains("red", cut.Find("p.theme-result").ClassList);
        Assert.Equal(["false", "true", "false"], Pressed(cut));
        Assert.Equal(["light"], data.Themes());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 341 AC-4: a file that would be over 64 KiB (Refused) shows the red line, keeps the pressed state, writes nothing.
    [Fact]
    public async Task Appearance_TooLarge_RedLine_PressedStays_NoSetTheme()
    {
        data.WriteFile(SettingsStore.FileName, "{\"padding\":\"" + new string('x', 65_516) + "\"}");
        IRenderedComponent<Main> cut = await OpenSettingsInMain();
        byte[] before = File.ReadAllBytes(SettingsPath);

        await StaleClick.ClickAsync(cut, "button.theme-light");

        cut.WaitForAssertion(
            () => Assert.Equal(DesktopActions.SettingsNotSavedText, cut.Find("p.theme-result").TextContent), Wait);
        Assert.Contains("red", cut.Find("p.theme-result").ClassList);
        Assert.Equal(["true", "false", "false"], Pressed(cut));
        Assert.Equal(["system"], data.Themes());
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // 341 AC-4: a folder that can't be written (Failed) shows the red line and keeps the saved theme pressed.
    [Fact]
    public async Task Appearance_ReadOnlyFolder_RedLine_PressedStays()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        data.WriteFile(SettingsStore.FileName, "{\"appearance\":{\"theme\":\"dark\"}}");
        IRenderedComponent<SettingsPanel> cut = await Show();
        byte[] before = File.ReadAllBytes(SettingsPath);
        File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            if (CanWriteIn(data.Root))
            {
                // Running as root: the folder stays writable, nothing to check.
                return;
            }

            await cut.Find("button.theme-system").ClickAsync(new MouseEventArgs());

            cut.WaitForAssertion(
                () => Assert.Equal(DesktopActions.SettingsNotSavedText, cut.Find("p.theme-result").TextContent), Wait);
            Assert.Contains("red", cut.Find("p.theme-result").ClassList);
            Assert.Equal(["false", "false", "true"], Pressed(cut));
            Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        }
        finally
        {
            File.SetUnixFileMode(data.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // 341 AC-4: while SetTheme runs all three buttons are disabled; afterwards they are enabled again.
    [Fact]
    public async Task Appearance_WhileSaving_ButtonsDisabled()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();
        using ManualResetEventSlim entered = new ManualResetEventSlim();
        using ManualResetEventSlim release = new ManualResetEventSlim();
        data.Actions!.BeforeWrite = () =>
        {
            entered.Set();
            release.Wait(Wait);
        };

        Task click = cut.Find("button.theme-dark").ClickAsync(new MouseEventArgs());
        try
        {
            Assert.True(entered.Wait(Wait));
            cut.WaitForAssertion(
                () => Assert.All(ThemeButtons, b => Assert.True(cut.Find(b).HasAttribute("disabled"), b)), Wait);
        }
        finally
        {
            release.Set();
        }

        await click;
        cut.WaitForAssertion(() => Assert.Equal("Theme: Dark.", cut.Find("p.theme-result").TextContent), Wait);
        Assert.All(ThemeButtons, b => Assert.False(cut.Find(b).HasAttribute("disabled"), b));
        Assert.Equal(["false", "false", "true"], Pressed(cut));
    }

    // 363: the theme buttons are enabled, nothing shows Loading…, every state reload has finished and the renderer has
    // nothing left queued.
    private async Task Settled(IRenderedComponent<Main> cut)
    {
        cut.WaitForAssertion(() =>
        {
            Assert.All(ThemeButtons, b => Assert.False(cut.Find(b).HasAttribute("disabled"), b));
            Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal);
        }, Wait);
        await data.State!.ReloadsSettled().WaitAsync(Wait);
        await cut.InvokeAsync(() => { });
    }

    // Renders Main, waits for its first setTheme and for every load of the page, then opens Settings and waits until
    // nothing on the page shows Loading….
    private async Task<IRenderedComponent<Main>> OpenSettingsInMain()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.True(await data.ThemeSet.WaitAsync(Wait));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        await StaleClick.ClickAsync(cut, "button.settings-open");
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".settings-panel div.theme-choice"));
            Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal);
        }, Wait);
        return cut;
    }

    private static bool CanWriteIn(string folder)
    {
        string probe = Path.Combine(folder, "probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // 260 AC-10: on macOS the section names the hotkey; elsewhere it says the hotkey works on macOS only.
    [Theory]
    [InlineData(true, "Press Control+Option+Shift+S to add a screen area to the inbox.")]
    [InlineData(false, "The screenshot hotkey works on macOS. On Windows, set the folder to the Snipping Tool's screenshots folder.")]
    public async Task Screenshots_HotkeyLineFollowsTheOs(bool isMac, string text)
    {
        data.IsMac = isMac;

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".screenshot-hotkey");
        Assert.Equal(text, line.TextContent);
        Assert.Contains("grey", line.ClassList);
    }

    // 282 AC-10: on macOS the section names the bar's hotkey; elsewhere it says the bar is only on macOS.
    [Theory]
    [InlineData(true, "Press Control+Option+Space anywhere to open the command bar.")]
    [InlineData(false, "The command bar is only on macOS.")]
    public async Task CommandBar_HotkeyLineFollowsTheOs(bool isMac, string text)
    {
        data.IsMac = isMac;

        IRenderedComponent<SettingsPanel> cut = await Show();

        cut.WaitForAssertion(() => Assert.Contains("Command bar", cut.FindAll("h3").Select(h => h.TextContent)), Wait);
        IElement line = cut.Find(".command-bar-hotkey");
        Assert.Equal(text, line.TextContent);
        Assert.Contains("grey", line.ClassList);
        Assert.Empty(cut.FindAll(".command-bar-times"));
    }

    // 282 AC-10: with no tm found, a grey line says Times commands need it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CommandBar_NoTimesCommand_GreyLine(string? found)
    {
        data.TimesCommand = found;

        IRenderedComponent<SettingsPanel> cut = await Show();

        cut.WaitForAssertion(() => Assert.Contains("Command bar", cut.FindAll("h3").Select(h => h.TextContent)), Wait);
        IElement line = cut.Find(".command-bar-times");
        Assert.Equal("Times commands need the tm command, which wasn't found.", line.TextContent);
        Assert.Contains("grey", line.ClassList);
    }

    // 260 AC-8, AC-9: no folder: the grey line, the field, Save folder and no Remove folder.
    [Fact]
    public async Task Screenshots_NoFolder_GreyLineFieldAndSave()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".screenshot-folder-state");
        Assert.Equal(SettingsPanel.ScreenshotNotSetText, line.TextContent);
        Assert.Contains("grey", line.ClassList);
        IElement field = cut.Find("input.screenshot-folder-field");
        Assert.Equal("text", field.GetAttribute("type"));
        Assert.Equal("Screenshot folder", field.GetAttribute("aria-label"));
        Assert.Equal("1024", field.GetAttribute("maxlength"));
        Assert.Equal("Save folder", cut.Find("button.screenshot-folder-save").TextContent);
        Assert.Empty(cut.FindAll("button.screenshot-folder-remove"));
        Assert.Equal("status", cut.Find(".screenshot-result").GetAttribute("role"));
    }

    // 260 AC-8: a broken screenshots value shows the red line.
    [Fact]
    public async Task Screenshots_BrokenSetting_RedLine()
    {
        data.WriteFile(SettingsStore.FileName, "{\"screenshots\":{\"folder\":42}}");

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement line = cut.Find(".screenshot-folder-state");
        Assert.Equal("Can't read the screenshot folder setting.", line.TextContent);
        Assert.Contains("red", line.ClassList);
        Assert.NotNull(cut.Find("button.screenshot-folder-remove"));
    }

    // 260 AC-9: a saved folder that is gone shows its path and the red Folder not found.; nothing creates it.
    [Fact]
    public async Task Screenshots_FolderGone_ShownNotFoundNotCreated()
    {
        string folder = data.Outside("gone");
        Directory.CreateDirectory(data.Root);
        new SettingsStore(data.Root).SaveScreenshotFolder(folder);

        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Equal("Folder: " + folder, cut.Find(".screenshot-folder-state").TextContent);
        IElement missing = cut.Find(".screenshot-folder-missing");
        Assert.Equal("Folder not found.", missing.TextContent);
        Assert.Contains("red", missing.ClassList);
        Assert.Equal("status", missing.GetAttribute("role"));
        Assert.False(Directory.Exists(folder));
    }

    // 260 AC-8, AC-9: Save (button and Enter) stores the folder, shows it and clears the field; Remove takes it out.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Screenshots_SaveThenRemove(bool enter)
    {
        string folder = data.Outside("shots");
        Directory.CreateDirectory(folder);
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.screenshot-folder-field").InputAsync(new ChangeEventArgs { Value = folder });
        if (enter)
        {
            await cut.Find("input.screenshot-folder-field").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
        else
        {
            await cut.Find("button.screenshot-folder-save").ClickAsync(new MouseEventArgs());
        }

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Screenshot folder saved.", cut.Find(".screenshot-result").TextContent);
            Assert.Equal("Folder: " + folder, cut.Find(".screenshot-folder-state").TextContent);
        }, Wait);
        Assert.Contains("accent", cut.Find(".screenshot-result").ClassList);
        Assert.Empty(cut.FindAll(".screenshot-folder-missing"));
        Assert.Equal(folder, new SettingsStore(data.Root).ReadScreenshotFolder());

        await cut.Find("button.screenshot-folder-remove").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Screenshot folder removed.", cut.Find(".screenshot-result").TextContent);
            Assert.Equal(SettingsPanel.ScreenshotNotSetText, cut.Find(".screenshot-folder-state").TextContent);
        }, Wait);
        Assert.Null(new SettingsStore(data.Root).ReadScreenshotFolder());
    }

    // 260 AC-8: a refused folder shows its text in red and writes nothing.
    [Fact]
    public async Task Screenshots_RelativeFolder_RedTextNothingWritten()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("input.screenshot-folder-field").InputAsync(new ChangeEventArgs { Value = "relative/shots" });
        await cut.Find("button.screenshot-folder-save").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(
            () => Assert.Equal("Type the folder's full path.", cut.Find(".screenshot-result").TextContent), Wait);
        Assert.Contains("red", cut.Find(".screenshot-result").ClassList);
        Assert.False(File.Exists(Path.Combine(data.Root, SettingsStore.FileName)));
    }

    private async Task<IRenderedComponent<SettingsPanel>> Show()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
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
