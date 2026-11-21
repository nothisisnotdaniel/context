using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Context.Tests;

/// <summary>Job 367 AC-5 to AC-8: the Suite Pro section of Settings, the locked hint and Main's Open Settings.</summary>
public sealed class ProSettingsTests : IDisposable
{
    // The status loads through Task.Run (a Keychain prompt may hold it), so no wait relies on bUnit's 1 s default.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly DesktopFixture data = new();

    public void Dispose() => data.Dispose();

    private string ActivationPath => Path.Combine(data.ActivationFolder, LicenceActivationFile.FileName);

    // AC-5: the heading is last, the state line a status, and with nothing stored it says so in grey.
    [Fact]
    public async Task Section_IsLast_NoLicence_GreyLine()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement heading = cut.FindAll("h3").Last();
        Assert.Equal("Suite Pro", heading.TextContent);
        Assert.Equal("pro-heading", heading.Id);
        Assert.Equal("-1", heading.GetAttribute("tabindex"));
        IElement state = cut.Find("p.pro-state");
        Assert.Equal("status", state.GetAttribute("role"));
        Assert.Equal("No Pro licence activated.", state.TextContent);
        Assert.Contains("grey", state.ClassList);
        Assert.Equal("status", cut.Find("p.pro-result").GetAttribute("role"));
        Assert.Empty(cut.FindAll("button.pro-remove"));
        Assert.Equal(1, data.LicenceStore.Reads);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("file-only")]
    [InlineData("invalid")]
    [InlineData("not-covered")]
    [InlineData("no-build-date")]
    [InlineData("unsupported")]
    [InlineData("unreadable")]
    public async Task State_ShowsExactlyOneLine(string kind)
    {
        (string text, string css, bool removable) = Arrange(kind);

        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement state = Assert.Single(cut.FindAll("p.pro-state"));
        Assert.Equal(text, state.TextContent);
        Assert.Contains(css, state.ClassList);
        Assert.Equal(removable ? 1 : 0, cut.FindAll("button.pro-remove").Count);
    }

    // AC-5: the name is text, never markup; the key never reaches the page.
    [Fact]
    public async Task Name_IsShownAsText_KeyNeverInTheMarkup()
    {
        string token = data.Licences.Token(name: "<b>Ada</b>");
        data.LicenceStore.Stored = token;

        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Equal("Suite Pro: <b>Ada</b> · updates until 2027-12-31.", cut.Find("p.pro-state").TextContent);
        Assert.Empty(cut.FindAll("p.pro-state b"));
        Assert.DoesNotContain(token[LicenceKey.Prefix.Length..][..12], cut.Markup, StringComparison.Ordinal);
    }

    // AC-5: while the store read is held, the line is a grey Loading… and the other sections are already there.
    [Fact]
    public async Task WhileTheStoreReads_GreyLoading_OtherSectionsShow()
    {
        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        using SemaphoreSlim entered = new SemaphoreSlim(0);
        data.LicenceStore.BeforeRead = () =>
        {
            entered.Release();
            release.Wait(Wait);
        };
        data.CreateState();

        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        Assert.True(await entered.WaitAsync(Wait));
        cut.WaitForAssertion(() => Assert.Equal("No calendar link set.", cut.Find(".calendar-link-state").TextContent), Wait);

        IElement loading = cut.Find("p.pro-state");
        Assert.Equal("Loading…", loading.TextContent);
        Assert.Contains("grey", loading.ClassList);

        release.Set();
        cut.WaitForAssertion(() => Assert.Equal("No Pro licence activated.", cut.Find("p.pro-state").TextContent), Wait);
    }

    // AC-6: a masked field without a value or binding, at most 1024 characters, and Activate.
    [Fact]
    public async Task Controls_MaskedFieldWithoutAValue_AndActivate()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement field = cut.Find("input.pro-key");
        Assert.Equal("password", field.GetAttribute("type"));
        Assert.Equal("1024", field.GetAttribute("maxlength"));
        Assert.Equal("off", field.GetAttribute("autocomplete"));
        Assert.Equal("Licence key", field.GetAttribute("aria-label"));
        Assert.False(field.HasAttribute("value"));
        Assert.Equal("Activate", cut.Find("button.pro-activate").TextContent);

        await TypeKey(cut, data.Licences.Token());

        Assert.False(cut.Find("input.pro-key").HasAttribute("value"));
    }

    // AC-6: a good key is stored, the file written, the result accent, the state line updates and the field is rebuilt.
    [Fact]
    public async Task Activate_Valid_StoresAndUpdatesTheState()
    {
        string token = data.Licences.Token();
        IRenderedComponent<SettingsPanel> cut = await Show();

        await TypeKey(cut, " " + token + " ");
        await cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "Suite Pro activated for Ada Lovelace · updates until 2027-12-31.", cut.Find("p.pro-result").TextContent), Wait);
        Assert.Contains("accent", cut.Find("p.pro-result").ClassList);
        cut.WaitForAssertion(() => Assert.Equal(
            "Suite Pro: Ada Lovelace · updates until 2027-12-31.", cut.Find("p.pro-state").TextContent), Wait);
        Assert.Contains("accent", cut.Find("p.pro-state").ClassList);
        Assert.Equal(token, data.LicenceStore.Stored);
        Assert.True(File.Exists(ActivationPath));
        Assert.Single(cut.FindAll("button.pro-remove"));
        Assert.DoesNotContain(token[LicenceKey.Prefix.Length..][..12], cut.Markup, StringComparison.Ordinal);

        // The typed key went with the save: Activate again finds the field empty.
        await cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Paste your licence key first.", cut.Find("p.pro-result").TextContent), Wait);
        Assert.Single(data.LicenceStore.Writes);
    }

    // AC-6: Enter in the field activates too.
    [Fact]
    public async Task Activate_EnterInTheField()
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        await TypeKey(cut, data.Licences.Token());
        await cut.Find("input.pro-key").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.StartsWith("Suite Pro activated for", cut.Find("p.pro-result").TextContent, StringComparison.Ordinal), Wait);
    }

    [Theory]
    [InlineData("", "Paste your licence key first.")]
    [InlineData("CTX1-nope", "This licence key cannot be used: it is not a licence key. Nothing was stored.")]
    public async Task Activate_Refused_RedLine_NothingStored(string key, string text)
    {
        IRenderedComponent<SettingsPanel> cut = await Show();

        await TypeKey(cut, key);
        await cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(text, cut.Find("p.pro-result").TextContent), Wait);
        Assert.Contains("red", cut.Find("p.pro-result").ClassList);
        Assert.Empty(data.LicenceStore.Writes);
        Assert.False(File.Exists(ActivationPath));
        Assert.Equal("No Pro licence activated.", cut.Find("p.pro-state").TextContent);
    }

    // AC-6: the field and both buttons are disabled while the activation runs.
    [Fact]
    public async Task Activate_WhileItRuns_ControlsDisabled()
    {
        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        using SemaphoreSlim entered = new SemaphoreSlim(0);
        data.LicenceStore.Stored = data.Licences.Token();
        data.LicenceStore.BeforeWrite = () =>
        {
            entered.Release();
            release.Wait(Wait);
        };
        IRenderedComponent<SettingsPanel> cut = await Show();

        await TypeKey(cut, data.Licences.Token(name: "Grace Hopper"));
        Task click = cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());
        Assert.True(await entered.WaitAsync(Wait));

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("input.pro-key").HasAttribute("disabled"));
            Assert.True(cut.Find("button.pro-activate").HasAttribute("disabled"));
            Assert.True(cut.Find("button.pro-remove").HasAttribute("disabled"));
        }, Wait);

        release.Set();
        await click;
        cut.WaitForAssertion(() => Assert.StartsWith("Suite Pro activated for Grace Hopper", cut.Find("p.pro-result").TextContent, StringComparison.Ordinal), Wait);
        Assert.False(cut.Find("input.pro-key").HasAttribute("disabled"));
    }

    // AC-6: a store failure and a failed file write are red, and nothing stays stored.
    [Fact]
    public async Task Activate_StoreOrFileFails_RedLine()
    {
        data.LicenceStore.WriteThrows = true;
        IRenderedComponent<SettingsPanel> cut = await Show();

        await TypeKey(cut, data.Licences.Token());
        await cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Can't save the licence. Nothing was stored.", cut.Find("p.pro-result").TextContent), Wait);

        data.LicenceStore.WriteThrows = false;
        File.WriteAllText(data.ActivationFolder, "not a folder");
        await TypeKey(cut, data.Licences.Token());
        await cut.Find("button.pro-activate").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("Can't write the activation file. Nothing was stored.", cut.Find("p.pro-result").TextContent), Wait);
        Assert.Contains("red", cut.Find("p.pro-result").ClassList);
        Assert.Null(data.LicenceStore.Stored);
        cut.WaitForAssertion(() => Assert.Equal("No Pro licence activated.", cut.Find("p.pro-state").TextContent), Wait);
    }

    // AC-7: Remove licence asks; Cancel changes nothing.
    [Fact]
    public async Task Remove_AsksFirst_CancelChangesNothing()
    {
        string token = data.Licences.Token();
        await Activated(token);
        IRenderedComponent<SettingsPanel> cut = await Show();

        IElement remove = cut.Find("button.pro-remove");
        Assert.Equal("Remove licence", remove.TextContent);
        await remove.ClickAsync(new MouseEventArgs());

        Assert.Equal("Remove the Suite Pro licence from this computer? Times uses it too.", cut.Find("[role=dialog] .question").TextContent);
        Assert.Equal("Remove", cut.Find("[role=dialog] button.confirm").TextContent);
        await cut.Find("[role=dialog] button.cancel").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Equal(0, data.LicenceStore.Deletes);
        Assert.Equal(token, data.LicenceStore.Stored);
        Assert.True(File.Exists(ActivationPath));
        Assert.Equal(string.Empty, cut.Find("p.pro-result").TextContent);
    }

    // AC-7: confirming deletes the key and the file, and the state line goes back to none.
    [Fact]
    public async Task Remove_Confirmed_RemovesKeyAndFile()
    {
        await Activated(data.Licences.Token());
        IRenderedComponent<SettingsPanel> cut = await Show();

        await cut.Find("button.pro-remove").ClickAsync(new MouseEventArgs());
        await cut.Find("[role=dialog] button.confirm").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("Suite Pro licence removed from this computer.", cut.Find("p.pro-result").TextContent), Wait);
        Assert.Contains("accent", cut.Find("p.pro-result").ClassList);
        cut.WaitForAssertion(() => Assert.Equal("No Pro licence activated.", cut.Find("p.pro-state").TextContent), Wait);
        Assert.Empty(cut.FindAll("button.pro-remove"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Null(data.LicenceStore.Stored);
        Assert.False(File.Exists(ActivationPath));
    }

    // AC-7: an invalid stored key can be removed too (the button shows whenever a key is stored).
    [Fact]
    public async Task Remove_ShowsForAnInvalidStoredKey()
    {
        data.LicenceStore.Stored = "CTX1-garbage";

        IRenderedComponent<SettingsPanel> cut = await Show();

        Assert.Single(cut.FindAll("button.pro-remove"));
    }

    // AC-8: the hint names the feature, and Open Settings calls back exactly once per click.
    [Fact]
    public void LockedHint_RendersTheSentence_AndCallsBackOnce()
    {
        int opened = 0;

        IRenderedComponent<ProLockedHint> cut = data.Bunit.Render<ProLockedHint>(p => p
            .Add(h => h.Feature, "Meeting flow")
            .Add(h => h.OnOpenSettings, () => opened++));

        IElement line = cut.Find("p.pro-locked");
        Assert.Equal("Meeting flow needs Suite Pro, the licence for Times and Context.", line.TextContent);
        Assert.Equal(Pro.LockedHint("Meeting flow"), line.TextContent);
        IElement button = cut.Find("button.pro-open-settings");
        Assert.Equal("Open Settings", button.TextContent);
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal(0, opened);

        button.Click();

        Assert.Equal(1, opened);
    }

    // AC-8: Main's callback opens Settings and moves the focus to #pro-heading.
    [Fact]
    public async Task Main_OpenProSettings_ShowsSettingsFocusedOnTheProHeading()
    {
        IRenderedComponent<Main> cut = await data.RenderAsync<Main>();
        Assert.Empty(cut.FindAll(".settings-panel"));

        await cut.InvokeAsync(() => cut.Instance.OpenProSettings());

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".settings-panel")), Wait);
        Assert.Single(cut.FindAll("h3#pro-heading"));
        // The focus call runs in OnAfterRenderAsync, after the render bUnit re-checks on, so the waits below
        // wait on the call count itself, and the heading reference is read only after the call was seen.
        Assert.True(SpinWait.SpinUntil(() => FocusIds(cut).Contains(CurrentHeadingRef(cut)), Wait),
            $"no focus call for the Pro heading after {Wait}");
        Assert.Contains(CurrentHeadingRef(cut), FocusIds(cut));

        // Opening Settings the usual way moves no focus to the heading.
        await cut.Find(".settings-panel button.close").ClickAsync(new MouseEventArgs());
        int focusCalls = FocusIds(cut).Count;
        await cut.Find("button.settings-open").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("No Pro licence activated.", cut.Find(".settings-panel p.pro-state").TextContent), Wait);
        Assert.Equal(focusCalls, FocusIds(cut).Count);

        // A second request while Settings is shown moves the focus again, once per request.
        await cut.InvokeAsync(() => cut.Instance.OpenProSettings());
        WaitForFocusCalls(cut, focusCalls + 1);
        Assert.Equal(focusCalls + 1, FocusIds(cut).Count);
        Assert.Equal(CurrentHeadingRef(cut), FocusIds(cut)[^1]);
        await cut.InvokeAsync(() => cut.Instance.OpenProSettings());
        WaitForFocusCalls(cut, focusCalls + 2);
        Assert.Equal(focusCalls + 2, FocusIds(cut).Count);
        Assert.Equal(CurrentHeadingRef(cut), FocusIds(cut)[^1]);
    }

    // Waits on the focus call count, whatever renders follow (none may follow the call).
    private void WaitForFocusCalls(IRenderedComponent<Main> cut, int count) =>
        Assert.True(SpinWait.SpinUntil(() => FocusIds(cut).Count >= count, Wait),
            $"focus calls stayed at {FocusIds(cut).Count}, expected {count}, after {Wait}");

    // The element ids of every domWrapper.focus call, read on the renderer's dispatcher so the list is not
    // read while the renderer adds to it.
    private List<string> FocusIds(IRenderedComponent<Main> cut) =>
        cut.InvokeAsync(() => data.Bunit.JSInterop.Invocations
            .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Select(i => ((ElementReference)i.Arguments[0]!).Id)
            .ToList()).GetAwaiter().GetResult();

    // The proHeading reference of the SettingsPanel shown now.
    private static string CurrentHeadingRef(IRenderedComponent<Main> cut)
    {
        string id = cut.InvokeAsync(() => ((ElementReference)typeof(SettingsPanel)
            .GetField("proHeading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.FindComponent<SettingsPanel>().Instance)!).Id).GetAwaiter().GetResult();
        Assert.False(string.IsNullOrEmpty(id));
        return id;
    }

    private (string Text, string Css, bool Removable) Arrange(string kind)
    {
        switch (kind)
        {
            case "active":
                data.LicenceStore.Stored = data.Licences.Token();
                return ("Suite Pro: Ada Lovelace · updates until 2027-12-31.", "accent", true);
            case "file-only":
                new LicenceActivationFile(data.ActivationFolder).Write(
                    new Licence(TestLicences.KeyId, TestLicences.LicenceId, "Grace Hopper", new DateOnly(2027, 12, 31), TestLicences.BuildDate));
                return ("No Pro licence activated. activation.json names Grace Hopper, but no key is stored on this computer.", "grey", false);
            case "invalid":
                data.LicenceStore.Stored = data.Licences.Token(kid: "other");
                return ("The stored licence key cannot be used: it was signed with an unknown key. Pro stays locked.", "red", true);
            case "not-covered":
                data.LicenceStore.Stored = data.Licences.Token(until: "2026-10-26");
                return ("Your licence covers updates until 2026-10-26; this build is from 2026-10-27, so Pro stays locked in it.", "yellow", true);
            case "no-build-date":
                data.LicenceStore.Stored = data.Licences.Token();
                data.ProBuildDate = null;
                return ("This build has no build date, so Pro stays locked in it.", "yellow", true);
            case "unsupported":
                data.LicenceStore.IsSupported = false;
                return ("Suite Pro needs macOS or Windows.", "grey", false);
            default:
                data.LicenceStore.Throws = true;
                return ("Can't read the licence store.", "red", false);
        }
    }

    // A licence activated through the action, so both the key and activation.json exist.
    private async Task Activated(string token)
    {
        data.CreateState();
        Assert.Equal(ActionKind.Done, (await data.Actions!.ActivatePro(token)).Kind);
    }

    private async Task<IRenderedComponent<SettingsPanel>> Show()
    {
        data.CreateState();
        IRenderedComponent<SettingsPanel> cut = data.Bunit.Render<SettingsPanel>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading…", cut.Markup, StringComparison.Ordinal), Wait);
        await Task.CompletedTask;
        return cut;
    }

    private static Task TypeKey(IRenderedComponent<SettingsPanel> cut, string value) =>
        cut.Find("input.pro-key").InputAsync(new ChangeEventArgs { Value = value });
}
